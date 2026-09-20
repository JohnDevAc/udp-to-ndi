$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$results = Join-Path $workspace 'test-results'
$target = Join-Path $results 'installer-smoke-app'
$setupFile = Join-Path $workspace 'dist\installer\UDP-to-NDI-Setup-1.0.1-x64.exe'
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{270E6A76-82C3-4376-8CAA-5A1E759092E0}_is1'
if (Test-Path -LiteralPath $registration) { throw 'An installed copy already exists. Use an isolated Windows account for this smoke test.' }
if (Test-Path -LiteralPath $target) { throw 'The smoke-test destination already exists; inspect it before testing again.' }
New-Item -ItemType Directory -Force $results | Out-Null
$settingsFile = Join-Path $env:LOCALAPPDATA 'UdpToNdi\settings.json'
$ndiFile = Join-Path $env:PROGRAMDATA 'NDI\ndi-config.v1.json'
function FileHash([string]$path) { if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path).Hash } else { 'absent' } }
$settingsBefore = FileHash $settingsFile
$ndiBefore = FileHash $ndiFile
$installLog = Join-Path $results 'installer-install.log'
$summary = Join-Path $results 'installer-smoke.txt'
$installed = $false
try {
    $setupArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=', ('/DIR="' + $target + '"'), '/GROUP="UDP to NDI Installer Verification"', ('/LOG="' + $installLog + '"'))
    $setup = Start-Process -FilePath $setupFile -ArgumentList $setupArgs -PassThru -WindowStyle Hidden
    $setup.WaitForExit()
    if ($setup.ExitCode -ne 0) { throw "Installation failed: $($setup.ExitCode)" }
    $installed = $true
    Set-Content -LiteralPath $summary -Value 'PASS: silent per-user installation completed.'
    foreach ($required in @('UDP to NDI.exe', 'coreclr.dll', 'hostfxr.dll', 'tools\ffmpeg.exe', 'tools\FFmpeg-LICENSE.txt', 'licenses\Microsoft.NETCore.App-LICENSE.txt', 'unins000.exe')) {
        if (!(Test-Path -LiteralPath (Join-Path $target $required))) { throw "Installed file missing: $required" }
    }
    Add-Content -LiteralPath $summary -Value 'PASS: app, local .NET runtime, FFmpeg, licenses and uninstaller are present.'
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'UDP to NDI\UDP to NDI.lnk'
    if (!(Test-Path -LiteralPath $shortcut)) { throw 'Start menu shortcut missing.' }
    Add-Content -LiteralPath $summary -Value 'PASS: Start menu shortcut created.'
    $app = Start-Process -FilePath (Join-Path $target 'UDP to NDI.exe') -ArgumentList '--render-ui' -WorkingDirectory $workspace -PassThru -WindowStyle Hidden
    if (!$app.WaitForExit(15000)) { $app.Kill(); throw 'Installed application did not complete its UI smoke test.' }
    if ($app.ExitCode -ne 0) { throw "Installed application failed: $($app.ExitCode)" }
    Add-Content -LiteralPath $summary -Value 'PASS: installed application launches and renders both windows.'
    & (Join-Path $target 'tools\ffmpeg.exe') -version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Installed FFmpeg failed.' }
    Add-Content -LiteralPath $summary -Value 'PASS: installed FFmpeg runs.'
}
finally {
    if ($installed) {
        $uninstallLog = Join-Path $results 'installer-uninstall.log'
        $remove = Start-Process -FilePath (Join-Path $target 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + $uninstallLog + '"')) -PassThru -WindowStyle Hidden
        $remove.WaitForExit()
        if ($remove.ExitCode -ne 0) { throw "Uninstall failed: $($remove.ExitCode)" }
        if ((Test-Path -LiteralPath (Join-Path $target 'UDP to NDI.exe')) -or (Test-Path -LiteralPath $registration)) { throw 'Uninstall left application files or registration behind.' }
        Add-Content -LiteralPath $summary -Value 'PASS: uninstaller removes application and registration.'
    }
    if ((FileHash $settingsFile) -ne $settingsBefore -or (FileHash $ndiFile) -ne $ndiBefore) { throw 'Saved slot settings or Windows NDI settings changed.' }
    Add-Content -LiteralPath $summary -Value 'PASS: saved slots and Windows NDI settings are unchanged.'
}
Get-Content -LiteralPath $summary
