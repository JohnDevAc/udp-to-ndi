param([string]$OutputDirectory = 'dist/portable-1.0.2')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish UdpNdi.csproj -c Release -r win-x64 --self-contained false -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if (Test-Path -LiteralPath (Join-Path $OutputDirectory 'tools\ffmpeg.exe')) { throw 'Release payload must not contain FFmpeg. Use a clean output directory.' }
    Copy-Item -LiteralPath 'README.md','LICENSE','installer\THIRD-PARTY-NOTICES.txt','installer\THIRD-PARTY-TERMS.txt' -Destination $OutputDirectory
    $licenses = Join-Path $OutputDirectory 'licenses'
    New-Item -ItemType Directory -Force $licenses | Out-Null
    Copy-Item -LiteralPath 'licenses\FFmpeg-GPL-3.0.txt' -Destination $licenses
    Write-Output "Ready: $OutputDirectory\UDP to NDI.exe"
}
finally { Pop-Location }
