using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Augit.Infrastructure.Interop;

public sealed class WorkspaceInstanceCoordinator : IAsyncDisposable
{
    private static readonly byte[] ActivationSignal = [1];
    private readonly string _pipeName;
    private readonly Semaphore _semaphore;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listenerTask;
    private bool _ownsWorkspace;
    private bool _disposed;

    public WorkspaceInstanceCoordinator(string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        string normalizedPath = Path.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
        _pipeName = $"Augit.Workspace.{key}";
        _semaphore = new(1, 1, $"Local\\Augit.Workspace.{key}");
    }

    public event EventHandler? ActivationRequested;

    public async Task<bool> TryBecomeOwnerAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_ownsWorkspace)
        {
            return true;
        }

        if (_semaphore.WaitOne(0))
        {
            _ownsWorkspace = true;
            _listenerTask = ListenAsync(_shutdown.Token);
            return true;
        }

        try
        {
            using NamedPipeClientStream client = new(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(900));
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await client.ConnectAsync(linked.Token).ConfigureAwait(false);
            await client.WriteAsync(ActivationSignal.AsMemory(), linked.Token).ConfigureAwait(false);
            await client.FlushAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _shutdown.CancelAsync().ConfigureAwait(false);
        if (_listenerTask is not null)
        {
            try
            {
                await _listenerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _shutdown.Dispose();
        if (_ownsWorkspace)
        {
            _semaphore.Release();
        }

        _semaphore.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using NamedPipeServerStream server = new(
                _pipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                byte[] signal = new byte[1];
                int received = await server.ReadAsync(signal, cancellationToken).ConfigureAwait(false);
                if (received > 0)
                {
                    ActivationRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
            }
        }
    }
}
