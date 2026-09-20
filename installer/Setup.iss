#ifndef PayloadDir
  #define PayloadDir "..\dist\installer-payload"
#endif
#ifndef RedistFile
  #define RedistFile "C:\Program Files\NDI\NDI 6 SDK\Redist\NDI 6 Runtime.exe"
#endif
#ifndef FfmpegUrl
  #define FfmpegUrl "https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip"
#endif
#ifndef SetupName
  #define SetupName "UDP-to-NDI-Setup-1.0.2-x64"
#endif

[Setup]
AppId={{270E6A76-82C3-4376-8CAA-5A1E759092E0}
AppName=UDP to NDI
AppVersion=1.0.2
AppVerName=UDP to NDI 1.0.2
AppPublisher=John Lightfoot
DefaultDirName={localappdata}\Programs\UDP to NDI
DefaultGroupName=UDP to NDI
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist\installer
OutputBaseFilename={#SetupName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\app.ico
SetupLogging=yes
UninstallDisplayIcon={app}\UDP to NDI.exe
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=1.0.2.0
VersionInfoDescription=UDP to NDI Installer
LicenseFile=THIRD-PARTY-TERMS.txt
ArchiveExtraction=full
ExtraDiskSpaceRequired=450000000

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,\tools\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RedistFile}"; DestName: "NDI Runtime Setup.exe"; Flags: dontcopy
Source: "{tmp}\ffmpeg-download\ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe"; DestDir: "{app}\tools"; ExternalSize: 105423872; Flags: external ignoreversion; Check: ShouldInstallFfmpeg
Source: "{tmp}\ffmpeg-download\ffmpeg-9.0.2-essentials_build\LICENSE"; DestDir: "{app}\tools"; DestName: "FFmpeg-LICENSE.txt"; ExternalSize: 35149; Flags: external ignoreversion; Check: ShouldInstallFfmpeg
Source: "{tmp}\ffmpeg-download\ffmpeg-9.0.2-essentials_build\README.txt"; DestDir: "{app}\tools"; DestName: "FFmpeg-README.txt"; ExternalSize: 45000; Flags: external ignoreversion; Check: ShouldInstallFfmpeg

[Icons]
Name: "{group}\UDP to NDI"; Filename: "{app}\UDP to NDI.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\UDP to NDI"; Filename: "{app}\UDP to NDI.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\UDP to NDI.exe"; Description: "Open UDP to NDI"; Flags: nowait postinstall skipifsilent unchecked
Filename: "https://www.gyan.dev/ffmpeg/builds/"; Description: "Open FFmpeg download page (manual setup needed)"; Flags: shellexec postinstall skipifsilent unchecked; Check: NeedsManualFfmpeg

[Code]
var
  DownloadPage: TDownloadWizardPage;
  FfmpegReady: Boolean;

function ShouldInstallFfmpeg: Boolean;
begin
  Result := FfmpegReady;
end;

function NeedsManualFfmpeg: Boolean;
begin
  Result := not FfmpegReady and not FileExists(ExpandConstant('{app}\tools\ffmpeg.exe'));
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('Downloading FFmpeg', 'FFmpeg is downloaded directly from Gyan.dev''s GitHub release. Internet access is required.', nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and NeedsManualFfmpeg then
    WizardForm.FinishedLabel.Caption := 'UDP to NDI is installed. FFmpeg still needs to be set up before streaming.' + #13#10#13#10 +
      'Download the Windows essentials ZIP from https://www.gyan.dev/ffmpeg/builds/ and extract it. In the app, choose More > Choose FFmpeg and select bin\ffmpeg.exe.';
end;

function PrepareFfmpeg: String;
begin
  Result := '';
  if FfmpegReady then exit;
  if ExpandConstant('{param:SKIPFFMPEGDOWNLOAD|0}') = '1' then exit;
  DownloadPage.Clear;
  DownloadPage.Add('{#FfmpegUrl}',
    'ffmpeg.zip', '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      ExtractArchive(ExpandConstant('{tmp}\ffmpeg.zip'), ExpandConstant('{tmp}\ffmpeg-download'), '', True, nil);
      if not FileExists(ExpandConstant('{tmp}\ffmpeg-download\ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe')) then
        RaiseException('The downloaded archive does not contain the expected FFmpeg executable.');
      FfmpegReady := True;
    except
      Log('FFmpeg download unavailable: ' + GetExceptionMessage);
      SuppressibleMsgBox('FFmpeg could not be downloaded or verified. Setup will continue without it.' + #13#10#13#10 +
        'To enable streaming, download the Windows essentials ZIP from https://www.gyan.dev/ffmpeg/builds/ and extract it.' + #13#10#13#10 +
        'Open UDP to NDI, choose More > Choose FFmpeg, then select bin\ffmpeg.exe from the extracted folder.' + #13#10#13#10 +
        'An existing FFmpeg installation will be kept. You can also rerun Setup when your connection is working.', mbInformation, MB_OK, IDOK);
    end;
  finally
    DownloadPage.Hide;
  end;
end;

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
  Result := PrepareFfmpeg;
  if Result <> '' then exit;
  ResultCode := 0;
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
