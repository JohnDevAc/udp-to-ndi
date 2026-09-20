param(
    [string]$CompilerPath = (Join-Path $PSScriptRoot 'tools\inno\ISCC.exe'),
    [string]$NdiRedistributable = 'C:\Program Files\NDI\NDI 6 SDK\Redist\NDI 6 Runtime.exe'
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (!(Test-Path -LiteralPath $CompilerPath)) { throw 'Inno Setup compiler not found. Install Inno Setup 6 and supply -CompilerPath.' }
    if (!(Test-Path -LiteralPath $NdiRedistributable)) { throw 'NDI Runtime redistributable not found. Supply -NdiRedistributable.' }
    $payload = Join-Path $PSScriptRoot 'dist\installer-payload'
    dotnet publish UdpNdi.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $payload
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
    $payloadTools = Join-Path $payload 'tools'
    New-Item -ItemType Directory -Force $payloadTools | Out-Null
    Copy-Item -LiteralPath 'tools\ffmpeg.exe' -Destination (Join-Path $payloadTools 'ffmpeg.exe')
    $license = Get-ChildItem -LiteralPath 'tools\ffmpeg-package' -Filter LICENSE -Recurse | Select-Object -First 1
    if (!$license) { throw 'FFmpeg license file not found.' }
    Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $payloadTools 'FFmpeg-LICENSE.txt')
    Copy-Item -LiteralPath 'README.md','LICENSE','installer\THIRD-PARTY-NOTICES.txt' -Destination $payload
    $licenses = Join-Path $payload 'licenses'
    New-Item -ItemType Directory -Force $licenses | Out-Null
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $payload 'UDP to NDI.runtimeconfig.json') -Raw | ConvertFrom-Json
    $packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
        $package = Join-Path $packages ($framework.name.ToLowerInvariant() + '.runtime.win-x64\' + $framework.version)
        $licenseFile = Get-ChildItem -LiteralPath $package -File | Where-Object Name -Match '^LICENSE(\.TXT)?$' | Select-Object -First 1
        if (!$licenseFile) { throw "License missing for $($framework.name)." }
        Copy-Item -LiteralPath $licenseFile.FullName -Destination (Join-Path $licenses ($framework.name + '-LICENSE.txt'))
        $notices = Join-Path $package 'THIRD-PARTY-NOTICES.TXT'
        if (Test-Path -LiteralPath $notices) { Copy-Item -LiteralPath $notices -Destination (Join-Path $licenses ($framework.name + '-THIRD-PARTY-NOTICES.txt')) }
    }
    & $CompilerPath '/Q' "/DPayloadDir=$payload" "/DRedistFile=$NdiRedistributable" 'installer\Setup.iss'
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $PSScriptRoot 'dist\installer\UDP-to-NDI-Setup-1.0.1-x64.exe'
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    Set-Content -LiteralPath ($installer + '.sha256') -Value "$hash  UDP-to-NDI-Setup-1.0.1-x64.exe"
    Get-Item -LiteralPath $installer | Select-Object FullName,Length
}
finally { Pop-Location }
