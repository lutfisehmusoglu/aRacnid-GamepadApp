param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.1',
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$projectFile = Join-Path $projectRoot 'GamepadApp.csproj'
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$publishDirectory = Join-Path $artifactsRoot 'publish\win-x64'
$installerDirectory = Join-Path $artifactsRoot 'installer'
$installerScript = Join-Path $projectRoot 'installer\aRacnid-GamepadApp.iss'

function Assert-WorkspaceChild([string]$Path) {
    $resolved = [System.IO.Path]::GetFullPath($Path)
    $rootWithSeparator = $projectRoot.TrimEnd('\') + '\'
    if (-not $resolved.StartsWith(
        $rootWithSeparator,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Güvenli çalışma alanı dışında işlem reddedildi: $resolved"
    }
}

Assert-WorkspaceChild $artifactsRoot
if (Test-Path -LiteralPath $artifactsRoot) {
    Remove-Item -LiteralPath $artifactsRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null

& dotnet restore $projectFile -r win-x64
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore başarısız oldu: $LASTEXITCODE"
}

& dotnet publish $projectFile `
    -c Release `
    -r win-x64 `
    --self-contained true `
    --no-restore `
    -p:Version=$Version `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish başarısız oldu: $LASTEXITCODE"
}

$requiredFiles = @(
    'GamepadApp.exe',
    'GamepadApp.dll',
    'GamepadApp.runtimeconfig.json',
    'SDL3.dll',
    'SDL3-LICENSE.txt',
    'THIRD-PARTY-NOTICES.txt',
    'README.md'
)

foreach ($file in $requiredFiles) {
    $candidate = Join-Path $publishDirectory $file
    if (-not (Test-Path -LiteralPath $candidate)) {
        throw "Yayın dosyası eksik: $file"
    }
}

$portableZip = Join-Path $artifactsRoot (
    "aRacnid-GamepadApp-$Version-win-x64-portable.zip")

if (Test-Path -LiteralPath $portableZip) {
    Remove-Item -LiteralPath $portableZip -Force
}

$zipCreated = $false
for ($attempt = 1; $attempt -le 10; $attempt++) {
    try {
        Compress-Archive `
            -Path (Join-Path $publishDirectory '*') `
            -DestinationPath $portableZip `
            -CompressionLevel Optimal `
            -Force
        $zipCreated = $true
        break
    }
    catch {
        if ($attempt -eq 10) {
            throw
        }

        Start-Sleep -Milliseconds 500
    }
}

if (-not $zipCreated -or -not (Test-Path -LiteralPath $portableZip)) {
    throw "Portable ZIP oluşturulamadı: $portableZip"
}

if (-not $SkipInstaller) {
    $isccCandidates = @(
        $env:ISCC_PATH,
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $iscc = $isccCandidates |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1

    if (-not $iscc) {
        throw 'Inno Setup 6 bulunamadı. https://jrsoftware.org/isdl.php adresinden kurun veya -SkipInstaller kullanın.'
    }

    & $iscc `
        "/DMyAppVersion=$Version" `
        "/DMyPublishDir=$publishDirectory" `
        $installerScript

    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup derlemesi başarısız oldu: $LASTEXITCODE"
    }
}

$releaseFiles = @()

if (Test-Path -LiteralPath $portableZip) {
    $releaseFiles += $portableZip
}
$installerFile = Join-Path $installerDirectory (
    "aRacnid-GamepadApp-Setup-$Version-x64.exe")
if (Test-Path -LiteralPath $installerFile) {
    $releaseFiles += $installerFile
}
$hashFile = Join-Path $artifactsRoot 'SHA256SUMS.txt'
$hashLines = foreach ($file in $releaseFiles) {
    $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256
    '{0} *{1}' -f $hash.Hash, [System.IO.Path]::GetFileName($file)
}
$hashLines | Set-Content -LiteralPath $hashFile -Encoding ascii

Write-Host "Yayın tamamlandı: $artifactsRoot"
