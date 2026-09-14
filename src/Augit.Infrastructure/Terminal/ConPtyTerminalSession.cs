using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Augit.Infrastructure.Settings;
using Microsoft.Win32.SafeHandles;

namespace Augit.Infrastructure.Terminal;

public sealed class ConPtyTerminalSession : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SafePseudoConsoleHandle _pseudoConsole;
    private readonly SafeFileHandle _job;
    private readonly SafeFileHandle _pseudoInput;
    private readonly SafeFileHandle _pseudoOutput;
    private readonly FileStream _input;
    private readonly FileStream _output;
    private readonly Process _process;
    private readonly TerminalPromptTracker _promptTracker;
    private readonly TaskCompletionSource<int> _exitCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _outputTask;
    private readonly Queue<string> _pendingOutput = [];
    private EventHandler<string>? _outputReceived;
    private int _pendingOutputLength;
    private bool _stopping;
    private bool _disposed;

    private ConPtyTerminalSession(
        TerminalLaunchInfo launchInfo,
        SafePseudoConsoleHandle pseudoConsole,
        SafeFileHandle job,
        SafeFileHandle pseudoInput,
        SafeFileHandle pseudoOutput,
        SafeFileHandle input,
        SafeFileHandle output,
        Process process)
    {
        LaunchInfo = launchInfo;
        _pseudoConsole = pseudoConsole;
        _job = job;
        _pseudoInput = pseudoInput;
        _pseudoOutput = pseudoOutput;
        _input = new(input, FileAccess.Write, 4096, false);
        _output = new(output, FileAccess.Read, 4096, false);
        _process = process;
        _promptTracker = new(launchInfo.ShellId);
        _process.EnableRaisingEvents = true;
        _process.Exited += OnProcessExited;
        _outputTask = ReadOutputAsync();
        if (_process.HasExited)
        {
            OnProcessExited(_process, EventArgs.Empty);
        }
    }

    public event EventHandler<string>? OutputReceived
    {
        add
        {
            if (value is null)
            {
                return;
            }

            string[] pending;
            lock (_gate)
            {
                _outputReceived += value;
                pending = _pendingOutput.ToArray();
                _pendingOutput.Clear();
                _pendingOutputLength = 0;
            }

            foreach (string output in pending)
            {
                value.Invoke(this, output);
            }
        }
        remove
        {
            lock (_gate)
            {
                _outputReceived -= value;
            }
        }
    }

    public event EventHandler<int>? Exited;

    public TerminalLaunchInfo LaunchInfo { get; }

    public int ProcessId => _process.Id;

    public bool IsRunning
    {
        get
        {
            try
            {
                return !_process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public bool HasForegroundProcess => IsRunning && (ReadActiveProcessCount() > 1 || _promptTracker.MayBeRunning);

    internal uint ActiveProcessCount => ReadActiveProcessCount();

    public static ConPtyTerminalSession Start(
        TerminalLaunchInfo launchInfo,
        string workingDirectory,
        int columns,
        int rows)
    {
        ArgumentNullException.ThrowIfNull(launchInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException("终端工作目录不存在。");
        }

        ConPtyNativeMethods.Coordinate size = CreateSize(columns, rows);
        SafeFileHandle? pseudoInput = null;
        SafeFileHandle? terminalInput = null;
        SafeFileHandle? terminalOutput = null;
        SafeFileHandle? pseudoOutput = null;
        SafePseudoConsoleHandle? pseudoConsole = null;
        SafeFileHandle? job = null;
        Process? process = null;
        nint attributeList = 0;
        ConPtyNativeMethods.ProcessInformation processInformation = default;
        try
        {
            if (!ConPtyNativeMethods.CreatePipe(out pseudoInput, out terminalInput, 0, 0)
                || !ConPtyNativeMethods.CreatePipe(out terminalOutput, out pseudoOutput, 0, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建终端输入输出管道。");
            }

            int pseudoConsoleResult = ConPtyNativeMethods.CreatePseudoConsole(
                size,
                pseudoInput,
                pseudoOutput,
                0,
                out nint pseudoConsoleHandle);
            if (pseudoConsoleResult != 0)
            {
                Marshal.ThrowExceptionForHR(pseudoConsoleResult);
            }

            pseudoConsole = new(pseudoConsoleHandle);

            job = CreateKillOnCloseJob();
            ConPtyNativeMethods.StartupInfoEx startupInfo = CreateStartupInfo(pseudoConsole, out attributeList);
            string commandLine = launchInfo.CommandLine;
            uint creationFlags = ConPtyNativeMethods.ExtendedStartupInfoPresent
                | ConPtyNativeMethods.CreateSuspended;
            // 控制台宿主若不显式分离，Shell 会继承父控制台并绕过 ConPTY。
            if (ConPtyNativeMethods.GetConsoleCP() != 0)
            {
                creationFlags |= ConPtyNativeMethods.CreateNoWindow;
            }
            ConPtyNativeMethods.SecurityAttributes processAttributes = new()
            {
                Length = Marshal.SizeOf<ConPtyNativeMethods.SecurityAttributes>(),
            };
            ConPtyNativeMethods.SecurityAttributes threadAttributes = new()
            {
                Length = Marshal.SizeOf<ConPtyNativeMethods.SecurityAttributes>(),
            };
            if (!ConPtyNativeMethods.CreateProcess(
                    null,
                    commandLine,
                    ref processAttributes,
                    ref threadAttributes,
                    false,
                    creationFlags,
                    0,
                    Path.GetFullPath(workingDirectory),
                    ref startupInfo,
                    out processInformation))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法启动 {launchInfo.DisplayName}。");
            }

            if (!ConPtyNativeMethods.AssignProcessToJobObject(job, processInformation.Process))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法把终端进程加入资源回收作业。");
            }

            process = Process.GetProcessById(checked((int)processInformation.ProcessId));
            ConPtyTerminalSession session = new(
                launchInfo,
                pseudoConsole,
                job,
                pseudoInput,
                pseudoOutput,
                terminalInput,
                terminalOutput,
                process);
            pseudoConsole = null;
            job = null;
            pseudoInput = null;
            pseudoOutput = null;
            terminalInput = null;
            terminalOutput = null;
            process = null;
            if (ConPtyNativeMethods.ResumeThread(processInformation.Thread) == uint.MaxValue)
            {
                session.Dispose();
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法恢复终端 Shell 进程。");
            }

            return session;
        }
        catch
        {
            if (job is not null && !job.IsInvalid)
            {
                _ = ConPtyNativeMethods.TerminateJobObject(job, 1);
            }

            process?.Dispose();
            throw;
        }
        finally
        {
            if (processInformation.Thread != 0)
            {
                _ = ConPtyNativeMethods.CloseHandle(processInformation.Thread);
            }

            if (processInformation.Process != 0)
            {
                _ = ConPtyNativeMethods.CloseHandle(processInformation.Process);
            }

            if (attributeList != 0)
            {
                ConPtyNativeMethods.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            pseudoInput?.Dispose();
            pseudoOutput?.Dispose();
            terminalInput?.Dispose();
            terminalOutput?.Dispose();
            pseudoConsole?.Dispose();
            job?.Dispose();
        }
    }

    public async Task WriteAsync(string data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(data);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _promptTracker.OnInput(data);
            await _input.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Resize(int columns, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int result = ConPtyNativeMethods.ResizePseudoConsole(_pseudoConsole, CreateSize(columns, rows));
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        return _exitCompletion.Task.WaitAsync(cancellationToken);
    }

    public async Task StopAsync()
    {
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
        }

        if (IsRunning)
        {
            _ = ConPtyNativeMethods.TerminateJobObject(_job, 1);
        }

        _input.Dispose();
        _pseudoConsole.Dispose();
        _pseudoInput.Dispose();
        _pseudoOutput.Dispose();
        try
        {
            _ = await _exitCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }

        _output.Dispose();
        try
        {
            await _outputTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _process.Exited -= OnProcessExited;
        _process.Dispose();
        _job.Dispose();
        _writeGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ConPtyNativeMethods.Coordinate CreateSize(int columns, int rows)
    {
        return new()
        {
            X = checked((short)Math.Clamp(columns, 2, 500)),
            Y = checked((short)Math.Clamp(rows, 1, 300)),
        };
    }

    private static SafeFileHandle CreateKillOnCloseJob()
    {
        SafeFileHandle job = ConPtyNativeMethods.CreateJobObject(0, null);
        if (job.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建终端进程回收作业。");
        }

        ConPtyNativeMethods.JobObjectExtendedLimitInformation information = new();
        information.BasicLimitInformation.LimitFlags = ConPtyNativeMethods.JobObjectLimitKillOnJobClose;
        if (!ConPtyNativeMethods.SetInformationJobObject(
                job,
                ConPtyNativeMethods.JobObjectInfoExtendedLimit,
                ref information,
                checked((uint)Marshal.SizeOf<ConPtyNativeMethods.JobObjectExtendedLimitInformation>())))
        {
            int error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error, "无法配置终端进程树回收策略。");
        }

        return job;
    }

    private static ConPtyNativeMethods.StartupInfoEx CreateStartupInfo(
        SafePseudoConsoleHandle pseudoConsole,
        out nint attributeList)
    {
        nuint attributeListSize = 0;
        _ = ConPtyNativeMethods.InitializeProcThreadAttributeList(0, 1, 0, ref attributeListSize);
        int sizeError = Marshal.GetLastWin32Error();
        if (attributeListSize == 0)
        {
            throw new Win32Exception(sizeError, "无法计算终端进程启动属性大小。");
        }

        attributeList = Marshal.AllocHGlobal(checked((nint)attributeListSize));
        if (!ConPtyNativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
        {
            int error = Marshal.GetLastWin32Error();
            Marshal.FreeHGlobal(attributeList);
            attributeList = 0;
            throw new Win32Exception(error, "无法初始化终端进程启动属性。");
        }

        if (!ConPtyNativeMethods.UpdateProcThreadAttribute(
                attributeList,
                0,
                ConPtyNativeMethods.ProcThreadAttributePseudoConsole,
                pseudoConsole.DangerousGetHandle(),
                unchecked((nuint)nint.Size),
                0,
                0))
        {
            int error = Marshal.GetLastWin32Error();
            ConPtyNativeMethods.DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            attributeList = 0;
            throw new Win32Exception(error, "无法附加终端伪控制台。");
        }

        return new()
        {
            StartupInfo = new()
            {
                Size = checked((uint)Marshal.SizeOf<ConPtyNativeMethods.StartupInfoEx>()),
            },
            AttributeList = attributeList,
        };
    }

    private async Task ReadOutputAsync()
    {
        char[] buffer = new char[4096];
        using StreamReader reader = new(_output, new UTF8Encoding(false, false), false, 4096, leaveOpen: true);
        try
        {
            while (true)
            {
                int count = await reader.ReadAsync(buffer).ConfigureAwait(false);
                if (count == 0)
                {
                    return;
                }

                string text = new(buffer, 0, count);
                _promptTracker.OnOutput(text);
                EventHandler<string>? outputReceived;
                lock (_gate)
                {
                    outputReceived = _outputReceived;
                    if (outputReceived is null)
                    {
                        _pendingOutput.Enqueue(text);
                        _pendingOutputLength += text.Length;
                        while (_pendingOutputLength > 1024 * 1024 && _pendingOutput.TryDequeue(out string? removed))
                        {
                            _pendingOutputLength -= removed.Length;
                        }
                    }
                }

                outputReceived?.Invoke(this, text);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    private uint ReadActiveProcessCount()
    {
        if (_job.IsClosed || _job.IsInvalid)
        {
            return 0;
        }

        return ConPtyNativeMethods.QueryInformationJobObject(
            _job,
            ConPtyNativeMethods.JobObjectInfoBasicAccounting,
            out ConPtyNativeMethods.JobObjectBasicAccountingInformation information,
            checked((uint)Marshal.SizeOf<ConPtyNativeMethods.JobObjectBasicAccountingInformation>()),
            out _)
            ? information.ActiveProcesses
            : 0;
    }

    private void OnProcessExited(object? sender, EventArgs eventArgs)
    {
        int exitCode;
        try
        {
            exitCode = _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (_exitCompletion.TrySetResult(exitCode))
        {
            Exited?.Invoke(this, exitCode);
        }
    }
}

internal sealed class TerminalPromptTracker
{
    private const int MaximumTailLength = 1024;
    private readonly string _shellId;
    private readonly StringBuilder _plainTail = new();
    private int _escapeState;

    internal TerminalPromptTracker(string shellId)
    {
        _shellId = shellId;
    }

    internal bool MayBeRunning { get; private set; }

    internal void OnInput(string input)
    {
        if (input.IndexOfAny(['\r', '\n']) >= 0)
        {
            MayBeRunning = true;
        }
    }

    internal void OnOutput(string output)
    {
        foreach (char character in output)
        {
            if (_escapeState == 3)
            {
                if (character == '\a')
                {
                    _escapeState = 0;
                }
                else if (character == '\u001b')
                {
                    _escapeState = 4;
                }

                continue;
            }

            if (_escapeState == 4)
            {
                _escapeState = character == '\\' ? 0 : 3;
                continue;
            }

            if (_escapeState == 2)
            {
                if (character is >= '@' and <= '~')
                {
                    _escapeState = 0;
                }

                continue;
            }

            if (_escapeState == 1)
            {
                _escapeState = character switch
                {
                    '[' => 2,
                    ']' => 3,
                    _ => 0,
                };

                continue;
            }

            if (character == '\u001b')
            {
                _escapeState = 1;
                continue;
            }

            if (character != '\r' && (character >= ' ' || character is '\n' or '\t'))
            {
                _plainTail.Append(character);
            }
        }

        if (_plainTail.Length > MaximumTailLength)
        {
            _plainTail.Remove(0, _plainTail.Length - MaximumTailLength);
        }

        if (EndsWithPrompt(_plainTail.ToString()))
        {
            MayBeRunning = false;
        }
    }

    private bool EndsWithPrompt(string text)
    {
        int lineStart = text.LastIndexOf('\n');
        string line = lineStart < 0 ? text : text[(lineStart + 1)..];
        return _shellId switch
        {
            TerminalShellIds.WindowsPowerShell or TerminalShellIds.PowerShell7 =>
                line.StartsWith("PS ", StringComparison.OrdinalIgnoreCase)
                && (line.EndsWith("> ", StringComparison.Ordinal) || line.EndsWith('>')),
            TerminalShellIds.CommandPrompt => line.Contains(":\\", StringComparison.Ordinal)
                && (line.EndsWith("> ", StringComparison.Ordinal) || line.EndsWith('>')),
            TerminalShellIds.GitBash or TerminalShellIds.Wsl =>
                line.EndsWith("$ ", StringComparison.Ordinal)
                || line.EndsWith("# ", StringComparison.Ordinal),
            _ => line.EndsWith("> ", StringComparison.Ordinal)
                || line.EndsWith("$ ", StringComparison.Ordinal)
                || line.EndsWith("# ", StringComparison.Ordinal)
                || line.EndsWith("% ", StringComparison.Ordinal),
        };
    }
}
