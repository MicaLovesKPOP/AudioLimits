#define MyAppName "Audio Limits"
#define MyAppVersion "1.0.0-rc.2"
#define MyAppPublisher "MicaLovesKPOP"
#define MyAppURL "https://github.com/MicaLovesKPOP/AudioLimits"
#define MyLauncherExeName "AudioLimits.exe"
#define MyAppHostExeName "AudioLimits.App.exe"

[Setup]
AppId={{69677083-D9A2-434C-B865-ABF393073727}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\Audio Limits
DefaultGroupName=Audio Limits
DisableProgramGroupPage=yes
OutputDir=..\release
OutputBaseFilename=AudioLimits-Setup
SetupIconFile=..\src\AudioLimits.App\Assets\AudioLimits.ico
UninstallDisplayIcon={app}\{#MyLauncherExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic
WizardImageFile=..\src\AudioLimits.App\Assets\AudioLimits-WizardLarge.png
WizardImageFileDynamicDark=..\src\AudioLimits.App\Assets\AudioLimits-WizardLarge.png
WizardImageBackColor=none
WizardImageBackColorDynamicDark=none
WizardSmallImageFile=..\src\AudioLimits.App\Assets\AudioLimits-WizardSmall.png
WizardSmallImageFileDynamicDark=..\src\AudioLimits.App\Assets\AudioLimits-WizardSmall.png
WizardSmallImageBackColor=none
WizardSmallImageBackColorDynamicDark=none
DisableReadyPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=no
SetupLogging=yes

[Files]
Source: "..\artifacts\distribution\Audio Limits\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; rc.2 moves the framework-dependent WinUI payload from the install root into
; {app}\app. Clean the known pre.30 root payload while preserving the launcher,
; Inno uninstaller, and any non-application files a user may have placed there.
Type: files; Name: "{app}\AudioLimits.App.exe"
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\AudioLimits.deps.json"
Type: files; Name: "{app}\AudioLimits.runtimeconfig.json"
Type: files; Name: "{app}\AudioLimits.App.deps.json"
Type: files; Name: "{app}\AudioLimits.App.runtimeconfig.json"
Type: files; Name: "{app}\*.pri"
Type: files; Name: "{app}\*.winmd"
Type: files; Name: "{app}\*.xbf"
Type: files; Name: "{app}\*.pdb"
Type: files; Name: "{app}\AudioLimits.ico"
Type: filesandordirs; Name: "{app}\Assets"
Type: filesandordirs; Name: "{app}\runtimes"

[Icons]
Name: "{autoprograms}\Audio Limits"; Filename: "{app}\app\{#MyAppHostExeName}"; WorkingDir: "{app}\app"
Name: "{autodesktop}\Audio Limits"; Filename: "{app}\app\{#MyAppHostExeName}"; WorkingDir: "{app}\app"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\app\{#MyAppHostExeName}"; Description: "Launch Audio Limits"; Flags: nowait postinstall skipifsilent; Check: CanLaunchAfterInstall

[Code]
const
  DotNetRegistryKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  VCRuntimeRegistryKey = 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64';
  DotNetUrl = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';
  VCRuntimeUrl = 'https://aka.ms/vc14/vc_redist.x64.exe';
  WindowsAppRuntimeUrl = 'https://aka.ms/windowsappsdk/2.3/2.3.1/windowsappruntimeinstall-x64.exe';
  WindowsAppRuntimePackage = 'Microsoft.WindowsAppRuntime.2';
  MinimumWindowsAppRuntimeVersion = '2.3.1.0';
  MinimumVCRuntimeVersion = '14.50.0.0';
  AudioLimitsUninstallRegistryKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{69677083-D9A2-434C-B865-ABF393073727}_is1';
  InstallStateFresh = 0;
  InstallStateUpdate = 1;
  InstallStateRepair = 2;

var
  DownloadPage: TDownloadWizardPage;
  InstallProgressPage: TOutputProgressWizardPage;
  MissingDotNet: Boolean;
  MissingVCRuntime: Boolean;
  MissingWindowsAppRuntime: Boolean;
  PrerequisiteRestartRequired: Boolean;
  MaintenancePage: TOutputMsgWizardPage;
  ExistingInstallState: Integer;
  InstalledVersion: String;
  InstalledLocation: String;
  InstalledScope: String;

function VersionAtLeast(const ActualVersion, MinimumVersion: String): Boolean;
var
  Actual, Minimum: Int64;
  CleanActual: String;
begin
  CleanActual := ActualVersion;
  if (Length(CleanActual) > 0) and ((CleanActual[1] = 'v') or (CleanActual[1] = 'V')) then
    Delete(CleanActual, 1, 1);

  Result := StrToVersion(CleanActual, Actual) and
            StrToVersion(MinimumVersion, Minimum) and
            (Actual >= Minimum);
end;

function QueryInstalledAudioLimitsMachine64(var Version, Location: String): Boolean;
begin
  Result := RegQueryStringValue(HKEY_LOCAL_MACHINE_64, AudioLimitsUninstallRegistryKey,
    'DisplayVersion', Version);
  if Result then
  begin
    RegQueryStringValue(HKEY_LOCAL_MACHINE_64, AudioLimitsUninstallRegistryKey,
      'InstallLocation', Location);
    InstalledScope := 'all users';
  end;
end;

function QueryInstalledAudioLimitsUser64(var Version, Location: String): Boolean;
begin
  Result := RegQueryStringValue(HKEY_CURRENT_USER_64, AudioLimitsUninstallRegistryKey,
    'DisplayVersion', Version);
  if Result then
  begin
    RegQueryStringValue(HKEY_CURRENT_USER_64, AudioLimitsUninstallRegistryKey,
      'InstallLocation', Location);
    InstalledScope := 'current user';
  end;
end;

function QueryInstalledAudioLimitsMachine32(var Version, Location: String): Boolean;
begin
  Result := RegQueryStringValue(HKEY_LOCAL_MACHINE_32, AudioLimitsUninstallRegistryKey,
    'DisplayVersion', Version);
  if Result then
  begin
    RegQueryStringValue(HKEY_LOCAL_MACHINE_32, AudioLimitsUninstallRegistryKey,
      'InstallLocation', Location);
    InstalledScope := 'all users';
  end;
end;

function QueryInstalledAudioLimitsUser32(var Version, Location: String): Boolean;
begin
  Result := RegQueryStringValue(HKEY_CURRENT_USER_32, AudioLimitsUninstallRegistryKey,
    'DisplayVersion', Version);
  if Result then
  begin
    RegQueryStringValue(HKEY_CURRENT_USER_32, AudioLimitsUninstallRegistryKey,
      'InstallLocation', Location);
    InstalledScope := 'current user';
  end;
end;

function DetectInstalledAudioLimits: Boolean;
begin
  InstalledVersion := '';
  InstalledLocation := '';
  InstalledScope := '';

  if IsWin64 then
  begin
    if QueryInstalledAudioLimitsMachine64(InstalledVersion, InstalledLocation) then
    begin
      Result := True;
      Exit;
    end;

    if QueryInstalledAudioLimitsUser64(InstalledVersion, InstalledLocation) then
    begin
      Result := True;
      Exit;
    end;
  end;

  if QueryInstalledAudioLimitsMachine32(InstalledVersion, InstalledLocation) then
  begin
    Result := True;
    Exit;
  end;

  Result := QueryInstalledAudioLimitsUser32(InstalledVersion, InstalledLocation);
end;

function ParseThreePartVersion(const Version: String; var Major, Minor, Patch: Integer): Boolean;
var
  Dot1: Integer;
  Dot2: Integer;
  Remainder: String;
begin
  Result := False;
  Dot1 := Pos('.', Version);
  if Dot1 = 0 then
    Exit;

  Remainder := Copy(Version, Dot1 + 1, Length(Version));
  Dot2 := Pos('.', Remainder);
  if Dot2 = 0 then
    Exit;

  Major := StrToIntDef(Copy(Version, 1, Dot1 - 1), -1);
  Minor := StrToIntDef(Copy(Remainder, 1, Dot2 - 1), -1);
  Patch := StrToIntDef(Copy(Remainder, Dot2 + 1, Length(Remainder)), -1);

  Result := (Major >= 0) and (Major <= 65535) and
            (Minor >= 0) and (Minor <= 65535) and
            (Patch >= 0) and (Patch <= 65535);
end;

function TryPackAudioLimitsVersion(const Version: String; var Packed: Int64): Boolean;
var
  SuffixPosition: Integer;
  BaseVersion: String;
  SequenceNumber: Integer;
  ChannelBuild: Integer;
  Major: Integer;
  Minor: Integer;
  Patch: Integer;
  NormalizedVersion: String;
begin
  Result := False;

  SuffixPosition := Pos('-pre.', Version);
  if SuffixPosition > 0 then
  begin
    BaseVersion := Copy(Version, 1, SuffixPosition - 1);
    SequenceNumber := StrToIntDef(Copy(Version, SuffixPosition + 5, Length(Version)), -1);
    if (SequenceNumber < 0) or (SequenceNumber >= 50000) then
      Exit;
    ChannelBuild := SequenceNumber;
  end
  else
  begin
    SuffixPosition := Pos('-rc.', Version);
    if SuffixPosition > 0 then
    begin
      BaseVersion := Copy(Version, 1, SuffixPosition - 1);
      SequenceNumber := StrToIntDef(Copy(Version, SuffixPosition + 4, Length(Version)), -1);
      if (SequenceNumber < 0) or (SequenceNumber >= 15000) then
        Exit;
      { Release candidates sort after pre-releases but before the final release. }
      ChannelBuild := 50000 + SequenceNumber;
    end
    else
    begin
      BaseVersion := Version;
      { A final release sorts after every pre-release/RC of the same base version. }
      ChannelBuild := 65535;
    end;
  end;

  if not ParseThreePartVersion(BaseVersion, Major, Minor, Patch) then
    Exit;

  NormalizedVersion := IntToStr(Major) + '.' + IntToStr(Minor) + '.' +
    IntToStr(Patch) + '.' + IntToStr(ChannelBuild);
  Result := StrToVersion(NormalizedVersion, Packed);
end;

function TryCompareAudioLimitsVersions(const LeftVersion, RightVersion: String;
  var Comparison: Integer): Boolean;
var
  LeftPacked: Int64;
  RightPacked: Int64;
begin
  Result := TryPackAudioLimitsVersion(LeftVersion, LeftPacked) and
            TryPackAudioLimitsVersion(RightVersion, RightPacked);
  if Result then
    Comparison := ComparePackedVersion(LeftPacked, RightPacked);
end;

function InitializeSetup: Boolean;
var
  Comparison: Integer;
begin
  Result := True;
  ExistingInstallState := InstallStateFresh;

  if not DetectInstalledAudioLimits then
  begin
    Log('No previous Audio Limits installation detected.');
    Exit;
  end;

  Log('Detected Audio Limits ' + InstalledVersion + ' (' + InstalledScope + ').');
  if InstalledLocation <> '' then
    Log('Previous installation location: ' + InstalledLocation);

  if not TryCompareAudioLimitsVersions(InstalledVersion, '{#MyAppVersion}', Comparison) then
  begin
    SuppressibleMsgBox(
      'Setup found an existing Audio Limits installation (' + InstalledVersion +
      '), but its version could not be compared safely.' + #13#10 + #13#10 +
      'To avoid an accidental downgrade, Setup will close. Use a current installer, or remove the existing installation from Windows Settings first.',
      mbInformation, MB_OK, IDOK);
    Result := False;
    Exit;
  end;

  if Comparison > 0 then
  begin
    SuppressibleMsgBox(
      'Audio Limits ' + InstalledVersion + ' is already installed.' + #13#10 + #13#10 +
      'This Setup contains the older version {#MyAppVersion}. Setup will close to avoid an accidental downgrade.',
      mbInformation, MB_OK, IDOK);
    Result := False;
    Exit;
  end;

  if Comparison = 0 then
    ExistingInstallState := InstallStateRepair
  else
    ExistingInstallState := InstallStateUpdate;
end;

function HasDotNet8DesktopIn32BitRegistryView: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  Installed: Cardinal;
begin
  Result := False;
  if not RegGetValueNames(HKEY_LOCAL_MACHINE_32, DotNetRegistryKey, Names) then
    Exit;

  for I := 0 to GetArrayLength(Names) - 1 do
  begin
    if (Pos('8.', Names[I]) = 1) and
       RegQueryDWordValue(HKEY_LOCAL_MACHINE_32, DotNetRegistryKey, Names[I], Installed) and
       (Installed = 1) then
    begin
      Log('Detected .NET 8 Desktop Runtime in 32-bit registry view: ' + Names[I]);
      Result := True;
      Exit;
    end;
  end;
end;

function HasDotNet8DesktopIn64BitRegistryView: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  Installed: Cardinal;
begin
  Result := False;
  if not RegGetValueNames(HKEY_LOCAL_MACHINE_64, DotNetRegistryKey, Names) then
    Exit;

  for I := 0 to GetArrayLength(Names) - 1 do
  begin
    if (Pos('8.', Names[I]) = 1) and
       RegQueryDWordValue(HKEY_LOCAL_MACHINE_64, DotNetRegistryKey, Names[I], Installed) and
       (Installed = 1) then
    begin
      Log('Detected .NET 8 Desktop Runtime in 64-bit registry view: ' + Names[I]);
      Result := True;
      Exit;
    end;
  end;
end;

function IsDotNet8DesktopInstalled: Boolean;
begin
  { .NET's global install-location registry contract is written in the 32-bit
    registry view even for x64 runtimes. Check that view explicitly, then also
    accept the 64-bit view as a defensive fallback for nonstandard installs.
    Keep the registry roots directly in the Reg* calls instead of passing an
    HKEY through a user-defined Pascal Script function parameter; ISCC 6.7.3
    rejects HKEY as a user-declared parameter type even though Reg* built-ins
    expose it in their documented signatures. }
  Result := HasDotNet8DesktopIn32BitRegistryView;
  if (not Result) and IsWin64 then
    Result := HasDotNet8DesktopIn64BitRegistryView;
end;

function IsVCRuntimeInstalled: Boolean;
var
  Installed: Cardinal;
  Version: String;
begin
  Result := RegQueryDWordValue(HKEY_LOCAL_MACHINE_64, VCRuntimeRegistryKey, 'Installed', Installed) and
            (Installed = 1) and
            RegQueryStringValue(HKEY_LOCAL_MACHINE_64, VCRuntimeRegistryKey, 'Version', Version) and
            VersionAtLeast(Version, MinimumVCRuntimeVersion);
end;

function IsWindowsAppRuntimeInstalled: Boolean;
var
  PowerShellExe: String;
  Command: String;
  ResultCode: Integer;
begin
  PowerShellExe := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Command :=
    '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ' +
    '"$p = @(Get-AppxPackage); $min = [version]''' + MinimumWindowsAppRuntimeVersion + '''; ' +
    '$fw64 = @($p | Where-Object { $_.Name -eq ''' + WindowsAppRuntimePackage + ''' -and $_.Architecture -eq ''X64'' -and [version]$_.Version -ge $min }).Count -gt 0; ' +
    '$fw86 = @($p | Where-Object { $_.Name -eq ''' + WindowsAppRuntimePackage + ''' -and $_.Architecture -eq ''X86'' -and [version]$_.Version -ge $min }).Count -gt 0; ' +
    '$main = @($p | Where-Object { $_.Name -eq ''MicrosoftCorporationII.WinAppRuntime.Main.2'' -and $_.Architecture -eq ''X64'' -and [version]$_.Version -ge $min }).Count -gt 0; ' +
    '$singleton = @($p | Where-Object { $_.Name -eq ''MicrosoftCorporationII.WinAppRuntime.Singleton'' -and $_.Architecture -eq ''X64'' -and ([version]$_.Version).Major -ge 8000 }).Count -gt 0; ' +
    '$ddlm64 = @($p | Where-Object { $_.Name -like ''Microsoft.WinAppRuntime.DDLM.2.3.*-x6'' -and $_.Architecture -eq ''X64'' -and [version]$_.Version -ge $min }).Count -gt 0; ' +
    '$ddlm86 = @($p | Where-Object { $_.Name -like ''Microsoft.WinAppRuntime.DDLM.2.3.*-x8'' -and $_.Architecture -eq ''X86'' -and [version]$_.Version -ge $min }).Count -gt 0; ' +
    'if ($fw64 -and $fw86 -and $main -and $singleton -and $ddlm64 -and $ddlm86) { exit 0 } else { exit 1 }"';

  Result := Exec(PowerShellExe, Command, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and
            (ResultCode = 0);
end;

procedure DetectPrerequisites;
begin
  MissingDotNet := not IsDotNet8DesktopInstalled;
  MissingVCRuntime := not IsVCRuntimeInstalled;
  MissingWindowsAppRuntime := not IsWindowsAppRuntimeInstalled;

  if MissingDotNet then
    Log('Prerequisite missing: .NET 8 Desktop Runtime x64')
  else
    Log('Prerequisite present: .NET 8 Desktop Runtime x64');

  if MissingVCRuntime then
    Log('Prerequisite missing/outdated: Microsoft Visual C++ Runtime x64')
  else
    Log('Prerequisite present: Microsoft Visual C++ Runtime x64');

  if MissingWindowsAppRuntime then
    Log('Prerequisite missing/incomplete: Windows App Runtime 2.3.1')
  else
    Log('Prerequisite present: Windows App Runtime 2.3.1');
end;

function LooksLikeAudioLimitsSourceTree(const Path: String): Boolean;
begin
  Result := FileExists(AddBackslash(Path) + 'AudioLimits.sln') and
            DirExists(AddBackslash(Path) + 'src\AudioLimits.App');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectDir) and LooksLikeAudioLimitsSourceTree(WizardDirValue) then
  begin
    MsgBox('That folder appears to contain the Audio Limits source project.' + #13#10 + #13#10 +
      'Choose a different installation folder so Setup does not overwrite development files.',
      mbError, MB_OK);
    Result := False;
  end;
end;

function RunPrerequisite(const FileName, Parameters, DisplayName: String; var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  InstallProgressPage.SetText('Installing ' + DisplayName + '...', '');

  if not Exec(FileName, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := 'Setup could not start the ' + DisplayName + ' installer.';
    Exit;
  end;

  if ResultCode = 0 then
    Exit;

  if (ResultCode = 3010) or (ResultCode = 1641) then
  begin
    NeedsRestart := True;
    Exit;
  end;

  Result := DisplayName + ' installation failed with exit code ' + IntToStr(ResultCode) + '.';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  DownloadedAnything: Boolean;
  ErrorText: String;
  DotNetPath: String;
  VCRuntimePath: String;
  WindowsAppRuntimePath: String;
begin
  Result := '';
  PrerequisiteRestartRequired := False;

  if LooksLikeAudioLimitsSourceTree(WizardDirValue) then
  begin
    Result := 'The selected installation folder contains the Audio Limits source project. Choose a different folder.';
    Exit;
  end;

  DetectPrerequisites;

  if not (MissingDotNet or MissingVCRuntime or MissingWindowsAppRuntime) then
    Exit;

  DotNetPath := ExpandConstant('{tmp}\AudioLimits-dotnet8-desktop-x64.exe');
  VCRuntimePath := ExpandConstant('{tmp}\AudioLimits-vc-redist-x64.exe');
  WindowsAppRuntimePath := ExpandConstant('{tmp}\AudioLimits-WindowsAppRuntime-2.3.1-x64.exe');

  DownloadPage.Clear;
  DownloadedAnything := False;

  if MissingVCRuntime then
  begin
    DownloadPage.Add(VCRuntimeUrl, ExtractFileName(VCRuntimePath), '');
    DownloadedAnything := True;
  end;

  if MissingDotNet then
  begin
    DownloadPage.Add(DotNetUrl, ExtractFileName(DotNetPath), '');
    DownloadedAnything := True;
  end;

  if MissingWindowsAppRuntime then
  begin
    DownloadPage.Add(WindowsAppRuntimeUrl, ExtractFileName(WindowsAppRuntimePath), '');
    DownloadedAnything := True;
  end;

  if DownloadedAnything then
  begin
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        Result := 'A required Microsoft runtime could not be downloaded.' + #13#10 + GetExceptionMessage;
        Exit;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;

  InstallProgressPage.Show;
  try
    if MissingVCRuntime then
    begin
      ErrorText := RunPrerequisite(VCRuntimePath, '/install /quiet /norestart',
        'Microsoft Visual C++ Runtime', PrerequisiteRestartRequired);
      if ErrorText <> '' then
      begin
        Result := ErrorText;
        Exit;
      end;
    end;

    if MissingDotNet then
    begin
      ErrorText := RunPrerequisite(DotNetPath, '/install /quiet /norestart',
        '.NET 8 Desktop Runtime', PrerequisiteRestartRequired);
      if ErrorText <> '' then
      begin
        Result := ErrorText;
        Exit;
      end;
    end;

    if MissingWindowsAppRuntime then
    begin
      ErrorText := RunPrerequisite(WindowsAppRuntimePath, '--quiet',
        'Windows App Runtime 2.3.1', PrerequisiteRestartRequired);
      if ErrorText <> '' then
      begin
        Result := ErrorText;
        Exit;
      end;
    end;
  finally
    InstallProgressPage.Hide;
  end;

  DetectPrerequisites;
  if MissingVCRuntime then
    Result := 'The Microsoft Visual C++ Runtime is still unavailable after setup.'
  else if MissingDotNet then
    Result := 'The .NET 8 Desktop Runtime is still unavailable after setup.'
  else if MissingWindowsAppRuntime then
    Result := 'The Windows App Runtime 2.3.1 is still unavailable after setup.';

  if PrerequisiteRestartRequired then
    NeedsRestart := True;
end;

function CanLaunchAfterInstall: Boolean;
begin
  Result := not PrerequisiteRestartRequired;
end;

procedure InitializeWizard;
var
  MaintenanceMessage: String;
begin
  DownloadPage := CreateDownloadPage('Preparing Audio Limits',
    'Setup is downloading required Microsoft components.', nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;

  InstallProgressPage := CreateOutputProgressPage('Preparing Audio Limits',
    'Setup is installing required Microsoft components.');

  if ExistingInstallState = InstallStateUpdate then
  begin
    MaintenanceMessage :=
      'Audio Limits ' + InstalledVersion + ' is already installed.' + #13#10 + #13#10 +
      'Setup will update it to {#MyAppVersion} using the existing install location and install scope.' + #13#10 + #13#10 +
      'Your limits, settings, and existing shortcut choices will be kept. Required Microsoft components will be checked again before the update.';
    MaintenancePage := CreateOutputMsgPage(wpWelcome, 'Update Audio Limits',
      'A previous version is already installed.', MaintenanceMessage);
  end
  else if ExistingInstallState = InstallStateRepair then
  begin
    MaintenanceMessage :=
      'Audio Limits {#MyAppVersion} is already installed.' + #13#10 + #13#10 +
      'Setup can repair the installation by reinstalling the application files and rechecking required Microsoft components.' + #13#10 + #13#10 +
      'Your limits and settings will be kept.' + #13#10 + #13#10 +
      'To uninstall Audio Limits, use Windows Settings > Apps.';
    MaintenancePage := CreateOutputMsgPage(wpWelcome, 'Repair Audio Limits',
      'This version is already installed.', MaintenanceMessage);
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  if ExistingInstallState <> InstallStateFresh then
  begin
    { An update/repair keeps the previous scope, location and task choices. }
    if (PageID = wpSelectDir) or (PageID = wpSelectTasks) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    WizardForm.FinishedHeadingLabel.Caption := 'Audio Limits is ready';
    if ExistingInstallState = InstallStateUpdate then
      WizardForm.FinishedLabel.Caption := 'Audio Limits has been updated successfully.'
    else if ExistingInstallState = InstallStateRepair then
      WizardForm.FinishedLabel.Caption := 'Audio Limits has been repaired successfully.'
    else
      WizardForm.FinishedLabel.Caption := 'Audio Limits has been installed successfully.';

    WizardForm.NextButton.Caption := SetupMessage(msgButtonFinish);
  end
  else if (MaintenancePage <> nil) and (CurPageID = MaintenancePage.ID) then
  begin
    if ExistingInstallState = InstallStateUpdate then
      WizardForm.NextButton.Caption := '&Update'
    else
      WizardForm.NextButton.Caption := '&Repair';
  end
  else if CurPageID = wpSelectTasks then
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall)
  else
    WizardForm.NextButton.Caption := SetupMessage(msgButtonNext);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'Audio Limits');
end;
