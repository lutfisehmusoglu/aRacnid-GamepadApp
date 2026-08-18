#ifndef MyAppVersion
  #define MyAppVersion "1.0.1"
#endif

#ifndef MyPublishDir
  #define MyPublishDir "..\artifacts\publish\win-x64"
#endif

#define MyAppName "aRacnid GamepadApp"
#define MyAppExeName "GamepadApp.exe"
#define MyPublisher "aRacnid"

[Setup]
AppId={{B0341BB9-A5A7-4E53-96A6-C89591349B1E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts\installer
OutputBaseFilename=aRacnid-GamepadApp-Setup-{#MyAppVersion}-x64
SetupIconFile=..\assets\appicon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
AppMutex=GamepadApp_SingleInstance
CloseApplications=yes
RestartApplications=no
CloseApplicationsFilter={#MyAppExeName}
SetupLogging=yes
UsedUserAreasWarning=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{code:GetHidHideCliPath}"; Parameters: "--app-reg ""{app}\{#MyAppExeName}"""; Flags: runhidden waituntilterminated; Check: HidHideCliExists
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\appicon.ico"
Type: dirifempty; Name: "{app}"

[Code]

function GetHidHideCliPath(Param: String): String;
begin
  Result :=
    ExpandConstant(
      '{pf}\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe');
end;

function HidHideCliExists: Boolean;
begin
  Result := FileExists(GetHidHideCliPath(''));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Uygulamanın kendi HidHide whitelist kaydını kaldır. }
    if HidHideCliExists then
    begin
      Exec(
        GetHidHideCliPath(''),
        '--app-unreg "' + ExpandConstant('{app}\{#MyAppExeName}') + '"',
        '',
        SW_HIDE,
        ewWaitUntilTerminated,
        ResultCode);
    end;

    { Windows başlangıç kaydını temizle. }
    RegDeleteValue(
      HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      'GamepadApp');
  end;
end;
