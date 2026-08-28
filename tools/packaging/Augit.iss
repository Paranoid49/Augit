#ifndef PublishDir
  #error 必须定义 PublishDir
#endif

#ifndef OutputDir
  #error 必须定义 OutputDir
#endif

#ifndef AppVersion
  #error 必须定义 AppVersion
#endif

#define DotNetUrl "https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.11/dotnet-runtime-10.0.11-win-x64.exe"
#define DotNetFile "dotnet-runtime-10.0.11-win-x64.exe"
#define DotNetHash "694e0e0af26b2b8949b8eda8a3831ab31aeac79797d43d6ff8c8798eae642c0904852e641c47329d7d893408f25feab1530ca2b7a0c6ed0d991e0113466a4bf9"
#define WebViewUrl "https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/89620190-81af-46a2-bb59-6228918a312e/MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
#define WebViewFile "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
#define WebViewSize "213044432"
#define WebViewHash "358a11cff88ce519301c3b60bcefe848f922688ab0a333fc0f18ddf83bb3b4f3"

[Setup]
AppId={{BC6B9385-88A8-4B7D-9F16-4CD4379F1B13}
AppName=Augit
AppVersion={#AppVersion}
AppPublisher=Augit
DefaultDirName={autopf}\Augit
DefaultGroupName=Augit
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=Augit-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dynamic
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
UninstallDisplayIcon={app}\Augit.exe
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=yes
SetupLogging=yes
UsePreviousTasks=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "addtopath"; Description: "将 Augit 加入系统 PATH"; Flags: unchecked
Name: "explorercontext"; Description: "在资源管理器中增加“使用 Augit 打开”"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "VerifyRuntime.ps1"; Flags: dontcopy

[Icons]
Name: "{group}\Augit"; Filename: "{app}\Augit.exe"; WorkingDir: "{userdocs}"

[Registry]
Root: HKCR; Subkey: "Directory\shell\Augit"; ValueType: string; ValueName: ""; ValueData: "使用 Augit 打开"; Tasks: explorercontext; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\shell\Augit"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\Augit.exe,0"; Tasks: explorercontext
Root: HKCR; Subkey: "Directory\shell\Augit\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Augit.exe"" ""%1"""; Tasks: explorercontext
Root: HKCR; Subkey: "Directory\Background\shell\Augit"; ValueType: string; ValueName: ""; ValueData: "使用 Augit 打开"; Tasks: explorercontext; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\Background\shell\Augit"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\Augit.exe,0"; Tasks: explorercontext
Root: HKCR; Subkey: "Directory\Background\shell\Augit\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Augit.exe"" ""%V"""; Tasks: explorercontext

[Run]
Filename: "{app}\Augit.exe"; Description: "启动 Augit"; Flags: postinstall nowait skipifsilent

[Code]
var
  DependencyPage: TOutputMsgWizardPage;
  DownloadPage: TDownloadWizardPage;
  NeedDotNetRuntime: Boolean;
  NeedWebViewRuntime: Boolean;
  RuntimePackagesReady: Boolean;

function TakeDelimitedValue(var Values: String; const Delimiter: String): String;
var
  DelimiterIndex: Integer;
begin
  DelimiterIndex := Pos(Delimiter, Values);
  if DelimiterIndex = 0 then begin
    Result := Values;
    Values := '';
    Exit;
  end;

  Result := Copy(Values, 1, DelimiterIndex - 1);
  Delete(Values, 1, DelimiterIndex + Length(Delimiter) - 1);
end;

function VersionAtLeast(
  const ActualVersion: String;
  RequiredMajor, RequiredMinor, RequiredBuild, RequiredRevision: Integer): Boolean;
var
  Remaining: String;
  MajorText: String;
  MinorText: String;
  BuildText: String;
  RevisionText: String;
  Major: Integer;
  Minor: Integer;
  Build: Integer;
  Revision: Integer;
begin
  Remaining := ActualVersion;
  MajorText := TakeDelimitedValue(Remaining, '.');
  MinorText := TakeDelimitedValue(Remaining, '.');
  BuildText := TakeDelimitedValue(Remaining, '.');
  RevisionText := TakeDelimitedValue(Remaining, '.');
  if (MajorText = '') or (MinorText = '') or (BuildText = '') then begin
    Result := False;
    Exit;
  end;

  Major := StrToIntDef(MajorText, -1);
  Minor := StrToIntDef(MinorText, -1);
  Build := StrToIntDef(BuildText, -1);
  if RevisionText = '' then
    Revision := 0
  else
    Revision := StrToIntDef(RevisionText, -1);
  Result := (Major > RequiredMajor) or
    ((Major = RequiredMajor) and (Minor > RequiredMinor)) or
    ((Major = RequiredMajor) and (Minor = RequiredMinor) and (Build > RequiredBuild)) or
    ((Major = RequiredMajor) and (Minor = RequiredMinor) and (Build = RequiredBuild) and
      (Revision >= RequiredRevision));
end;

function HasDotNetRuntime: Boolean;
var
  RuntimeDirectory: String;
  FindRec: TFindRec;
begin
  Result := False;
  RuntimeDirectory := ExpandConstant('{pf64}\dotnet\shared\Microsoft.NETCore.App');
  if not DirExists(RuntimeDirectory) then
    Exit;

  if FindFirst(RuntimeDirectory + '\10.*', FindRec) then begin
    try
      repeat
        if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
          VersionAtLeast(FindRec.Name, 10, 0, 0, 0) then begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function HasWebViewRuntime: Boolean;
var
  Version: String;
  ClientKey: String;
begin
  ClientKey := 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result := (RegQueryStringValue(HKLM32, ClientKey, 'pv', Version) or
      RegQueryStringValue(HKCU, ClientKey, 'pv', Version)) and
    VersionAtLeast(Version, 151, 0, 4129, 107);
end;

function MissingRuntimeText: String;
begin
  Result := '';
  if NeedDotNetRuntime then
    Result := Result + '• .NET 10 Runtime（Windows x64；将安装锁定版 10.0.11）' + #13#10;
  if NeedWebViewRuntime then
    Result := Result + '• Microsoft Edge WebView2 Runtime 151.0.4129.107（Windows x64）' + #13#10;
end;

function InitializeSetup: Boolean;
begin
  if IsArm64 then begin
    MsgBox('Augit 首版不支持 ARM64，请在 Windows x64 设备上安装。', mbCriticalError, MB_OK);
    Result := False;
    Exit;
  end;

  Result := True;
end;

procedure InitializeWizard;
begin
  NeedDotNetRuntime := not HasDotNetRuntime;
  NeedWebViewRuntime := not HasWebViewRuntime;
  RuntimePackagesReady := not (NeedDotNetRuntime or NeedWebViewRuntime);
  DependencyPage := CreateOutputMsgPage(
    wpSelectTasks,
    '运行时依赖',
    '安装器会先说明缺失项，再由用户确认是否联网安装。',
    '当前缺少以下运行时：' + #13#10#13#10 + MissingRuntimeText + #13#10 +
    '确认后只从项目锁定的微软官方地址下载，文件通过固定哈希和 Microsoft Corporation 数字签名校验后才会执行。');
  DownloadPage := CreateDownloadPage('正在下载微软运行时', '可以取消下载；取消后不会安装 Augit。', nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = DependencyPage.ID) and not (NeedDotNetRuntime or NeedWebViewRuntime);
end;

function VerifyRuntimePackage(const FileName, Algorithm, ExpectedHash, ExpectedSize: String): Boolean;
var
  ResultCode: Integer;
  PowerShell: String;
  ScriptPath: String;
  PackagePath: String;
  Parameters: String;
begin
  ExtractTemporaryFile('VerifyRuntime.ps1');
  PowerShell := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  ScriptPath := ExpandConstant('{tmp}\VerifyRuntime.ps1');
  PackagePath := ExpandConstant('{tmp}\') + FileName;
  Parameters := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' +
    AddQuotes(ScriptPath) + ' -Path ' + AddQuotes(PackagePath) +
    ' -Algorithm ' + Algorithm + ' -ExpectedHash ' + ExpectedHash;
  if ExpectedSize <> '' then
    Parameters := Parameters + ' -ExpectedSize ' + ExpectedSize;
  Result := Exec(PowerShell, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and
    (ResultCode = 0);
end;

function DownloadRuntimePackages: Boolean;
var
  ErrorText: String;
begin
  Result := False;
  ErrorText := '';
  DownloadPage.Clear;
  if NeedDotNetRuntime then
    DownloadPage.Add('{#DotNetUrl}', '{#DotNetFile}', '');
  if NeedWebViewRuntime then
    DownloadPage.Add('{#WebViewUrl}', '{#WebViewFile}', '{#WebViewHash}');

  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      if NeedDotNetRuntime and
        not VerifyRuntimePackage('{#DotNetFile}', 'SHA512', '{#DotNetHash}', '') then
        ErrorText := '.NET Runtime 文件的哈希或微软数字签名校验失败。'
      else if NeedWebViewRuntime and
        not VerifyRuntimePackage('{#WebViewFile}', 'SHA256', '{#WebViewHash}', '{#WebViewSize}') then
        ErrorText := 'WebView2 Runtime 文件的哈希或微软数字签名校验失败。'
      else
        Result := True;
      if ErrorText <> '' then
        SuppressibleMsgBox(ErrorText, mbCriticalError, MB_OK, IDOK);
    except
      if DownloadPage.AbortedByUser then
        ErrorText := '运行时下载已取消。'
      else
        ErrorText := GetExceptionMessage;
      SuppressibleMsgBox(ErrorText, mbCriticalError, MB_OK, IDOK);
      Result := False;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID <> wpReady) or not (NeedDotNetRuntime or NeedWebViewRuntime) then
    Exit;

  if MsgBox(
      '安装 Augit 前需要联网下载以下微软运行时：' + #13#10#13#10 + MissingRuntimeText +
      (#13#10) + '是否继续下载、校验并安装？',
      mbConfirmation,
      MB_YESNO) <> IDYES then begin
    Result := False;
    Exit;
  end;

  RuntimePackagesReady := DownloadRuntimePackages;
  Result := RuntimePackagesReady;
end;

function RunRuntimeInstaller(const FileName, Parameters: String; var NeedsRestart: Boolean): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(
    ExpandConstant('{tmp}\') + FileName,
    Parameters,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);
  if Result and (ResultCode = 3010) then
    NeedsRestart := True;
  Result := Result and ((ResultCode = 0) or (ResultCode = 3010));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not RuntimePackagesReady then begin
    Result := '运行时尚未下载并通过校验。';
    Exit;
  end;

  if NeedDotNetRuntime and
    not RunRuntimeInstaller('{#DotNetFile}', '/install /quiet /norestart', NeedsRestart) then begin
    Result := '.NET 10 Runtime 安装失败，Augit 尚未安装。';
    Exit;
  end;
  if NeedDotNetRuntime and not HasDotNetRuntime then begin
    Result := '.NET 10 Runtime 安装完成后仍未检测到兼容版本。';
    Exit;
  end;

  if NeedWebViewRuntime and
    not RunRuntimeInstaller('{#WebViewFile}', '/silent /install', NeedsRestart) then begin
    Result := 'WebView2 Runtime 安装失败，Augit 尚未安装。';
    Exit;
  end;
  if NeedWebViewRuntime and not HasWebViewRuntime then
    Result := 'WebView2 Runtime 安装完成后仍未检测到兼容版本。';
end;

function NormalizePathEntry(const Value: String): String;
begin
  Result := RemoveBackslashUnlessRoot(Trim(Value));
end;

function PathContainsEntry(const PathValue, Entry: String): Boolean;
var
  Remaining: String;
  Part: String;
begin
  Result := False;
  Remaining := PathValue;
  while Remaining <> '' do begin
    Part := TakeDelimitedValue(Remaining, ';');
    if CompareText(NormalizePathEntry(Part), NormalizePathEntry(Entry)) = 0 then begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure AddApplicationPath;
var
  PathValue: String;
begin
  if not RegQueryStringValue(
      HKLM64,
      'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
      'Path',
      PathValue) then
    PathValue := '';
  if PathContainsEntry(PathValue, ExpandConstant('{app}')) then
    Exit;
  if (PathValue <> '') and (PathValue[Length(PathValue)] <> ';') then
    PathValue := PathValue + ';';
  if not RegWriteDWordValue(
      HKLM64,
      'Software\Augit',
      'InstallerAddedPath',
      1) then
    RaiseException('无法记录 Augit PATH 项的安装器所有权。');
  if not RegWriteExpandStringValue(
      HKLM64,
      'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
      'Path',
      PathValue + ExpandConstant('{app}')) then begin
    RegDeleteValue(HKLM64, 'Software\Augit', 'InstallerAddedPath');
    RegDeleteKeyIfEmpty(HKLM64, 'Software\Augit');
    RaiseException('无法将 Augit 加入系统 PATH。');
  end;
end;

function InstallerOwnsApplicationPath: Boolean;
var
  InstallerAddedPath: Cardinal;
begin
  Result := RegQueryDWordValue(
      HKLM64,
      'Software\Augit',
      'InstallerAddedPath',
      InstallerAddedPath) and
    (InstallerAddedPath = 1);
end;

procedure ClearApplicationPathOwnership;
begin
  RegDeleteValue(HKLM64, 'Software\Augit', 'InstallerAddedPath');
  RegDeleteKeyIfEmpty(HKLM64, 'Software\Augit');
end;

procedure RemoveApplicationPath;
var
  PathValue: String;
  Remaining: String;
  Part: String;
  Updated: String;
begin
  if not InstallerOwnsApplicationPath then
    Exit;
  if not RegQueryStringValue(
      HKLM64,
      'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
      'Path',
      PathValue) then begin
    ClearApplicationPathOwnership;
    Exit;
  end;
  Remaining := PathValue;
  Updated := '';
  while Remaining <> '' do begin
    Part := TakeDelimitedValue(Remaining, ';');
    if (Trim(Part) <> '') and
      (CompareText(NormalizePathEntry(Part), NormalizePathEntry(ExpandConstant('{app}'))) <> 0) then begin
      if Updated <> '' then
        Updated := Updated + ';';
      Updated := Updated + Trim(Part);
    end;
  end;
  if RegWriteExpandStringValue(
      HKLM64,
      'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
      'Path',
      Updated) then
    ClearApplicationPathOwnership;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then
    Exit;
  if WizardIsTaskSelected('addtopath') then
    AddApplicationPath
  else
    RemoveApplicationPath;
  if not WizardIsTaskSelected('explorercontext') then begin
    RegDeleteKeyIncludingSubkeys(HKCR, 'Directory\shell\Augit');
    RegDeleteKeyIncludingSubkeys(HKCR, 'Directory\Background\shell\Augit');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemoveApplicationPath;
end;
