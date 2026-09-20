param([string]$OutputDirectory = 'dist')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish UdpNdi.csproj -c Release -r win-x64 --self-contained false -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if (!(Test-Path -LiteralPath 'tools\ffmpeg.exe')) { throw 'Place ffmpeg.exe in tools before packaging. See README.md.' }
    $outputTools = Join-Path $OutputDirectory 'tools'
    New-Item -ItemType Directory -Force $outputTools | Out-Null
    Copy-Item -LiteralPath 'tools\ffmpeg.exe' -Destination (Join-Path $outputTools 'ffmpeg.exe')
    $license = Get-ChildItem -LiteralPath 'tools\ffmpeg-package' -Filter LICENSE -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($license) { Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $outputTools 'FFmpeg-LICENSE.txt') }
    Copy-Item -LiteralPath 'README.md','LICENSE','installer\THIRD-PARTY-NOTICES.txt' -Destination $OutputDirectory
    Write-Output "Ready: $OutputDirectory\UDP to NDI.exe"
}
finally { Pop-Location }
