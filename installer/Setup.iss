#ifndef PayloadDir
  #define PayloadDir "..\dist\installer-payload"
#endif
#ifndef RedistFile
  #define RedistFile "C:\Program Files\NDI\NDI 6 SDK\Redist\NDI 6 Runtime.exe"
#endif

[Setup]
AppId={{270E6A76-82C3-4376-8CAA-5A1E759092E0}
AppName=UDP to NDI
AppVersion=1.0.1
AppVerName=UDP to NDI 1.0.1
AppPublisher=John Lightfoot
DefaultDirName={localappdata}\Programs\UDP to NDI
DefaultGroupName=UDP to NDI
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist\installer
OutputBaseFilename=UDP-to-NDI-Setup-1.0.1-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\app.ico
SetupLogging=yes
UninstallDisplayIcon={app}\UDP to NDI.exe
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=1.0.1.0
VersionInfoDescription=UDP to NDI Installer

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RedistFile}"; DestName: "NDI Runtime Setup.exe"; Flags: dontcopy

[Icons]
Name: "{group}\UDP to NDI"; Filename: "{app}\UDP to NDI.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\UDP to NDI"; Filename: "{app}\UDP to NDI.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\UDP to NDI.exe"; Description: "Open UDP to NDI"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function NdiInstalled: Boolean;
begin
  Result := FileExists(AddBackslash(GetEnv('NDI_RUNTIME_DIR_V6')) + 'Processing.NDI.Lib.x64.dll') or
    FileExists(AddBackslash(GetEnv('NDI_RUNTIME_DIR_V5')) + 'Processing.NDI.Lib.x64.dll') or
    FileExists(ExpandConstant('{commonpf64}\NDI\NDI 6 Runtime\v6\Processing.NDI.Lib.x64.dll')) or
    FileExists(ExpandConstant('{commonpf64}\NDI\NDI 6 Tools\Runtime\Processing.NDI.Lib.x64.dll')) or
    FileExists(ExpandConstant('{commonpf64}\NDI\NDI 6 SDK\Bin\x64\Processing.NDI.Lib.x64.dll'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if NdiInstalled then
  begin
    Log('Existing NDI runtime detected; leaving it and its settings unchanged.');
    exit;
  end;
  if WizardSilent then
  begin
    Result := 'NDI Runtime is required. Run this installer interactively to install the bundled official NDI Runtime, or install NDI Runtime first.';
    exit;
  end;
  MsgBox('NDI Runtime is required. Its official installer will now open. Complete it, then this installation will continue.', mbInformation, MB_OK);
  ExtractTemporaryFile('NDI Runtime Setup.exe');
  if not ShellExec('', ExpandConstant('{tmp}\NDI Runtime Setup.exe'), '/NORESTART', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
    Result := 'Unable to start NDI Runtime Setup. Install NDI Runtime and try again.'
  else if (ResultCode <> 0) and (ResultCode <> 3010) then
    Result := 'NDI Runtime installation was cancelled or failed. Complete its installation and try again.'
  else if not NdiInstalled then
    Result := 'NDI Runtime was not detected. Restart this installer after completing NDI Runtime installation.';
  if ResultCode = 3010 then NeedsRestart := True;
end;
