# Builds the distributable: publishes the app, then compiles the single-file installer.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-installer.ps1
#
# Inno Setup is not installed on this machine; the script bootstraps a portable copy
# into %TEMP% (verified against the official release's Authenticode signature) and
# leaves no registry entries or Program Files footprint behind.
param(
    [string]$InnoPath = '',
    [switch]$SkipAppPublish
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $PSScriptRoot
$iss = Join-Path $root 'installer\setup.iss'
$payload = Join-Path $root 'dist\待办清单.exe'
$innoWork = Join-Path $env:TEMP 'dsh-innosetup'

function Write-Step([string]$text) { Write-Output "`n=== $text ===" }

# ---------------------------------------------------------------- app payload

if (-not $SkipAppPublish) {
    Write-Step 'publishing the app'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw "app publish failed ($LASTEXITCODE)" }
}

if (-not (Test-Path $payload)) { throw "payload missing: $payload" }
Write-Output ("payload : {0}  ({1:N2} MB)" -f $payload, ((Get-Item $payload).Length / 1MB))

# ---------------------------------------------------------------- Inno Setup

function Get-InnoCompiler {
    if ($InnoPath -and (Test-Path $InnoPath)) { return $InnoPath }

    foreach ($p in @(
            "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            (Join-Path $innoWork 'app\ISCC.exe'))) {
        if (Test-Path $p) { return $p }
    }

    Write-Output 'ISCC.exe not found — bootstrapping a portable Inno Setup ...'
    New-Item -ItemType Directory -Force -Path $innoWork | Out-Null

    $installer = Join-Path $innoWork 'innosetup.exe'
    if (-not (Test-Path $installer) -or (Get-Item $installer).Length -lt 1MB) {
        # github.com/.../releases/download is unreachable from here, but the API host
        # is not, and its asset endpoint redirects to the same CDN file.
        $hdr = @{ 'User-Agent' = 'dsh-build'; 'Accept' = 'application/vnd.github+json' }
        $rel = Invoke-RestMethod 'https://api.github.com/repos/jrsoftware/issrc/releases/tags/is-6_7_3' -Headers $hdr -TimeoutSec 60
        $asset = $rel.assets | Where-Object { $_.name -eq 'innosetup-6.7.3.exe' } | Select-Object -First 1
        if (-not $asset) { throw 'could not locate the Inno Setup 6.7.3 release asset' }

        Invoke-WebRequest -Uri $asset.url -Headers @{ 'User-Agent' = 'dsh-build'; 'Accept' = 'application/octet-stream' } `
            -OutFile $installer -TimeoutSec 900 -UseBasicParsing
    }

    $sig = Get-AuthenticodeSignature $installer
    if ($sig.Status -ne 'Valid') { throw "Inno Setup download is not validly signed (status: $($sig.Status))" }
    Write-Output "  signature OK: $($sig.SignerCertificate.Subject)"

    # /PORTABLE=1 extracts the files without touching Program Files or the registry.
    $appDir = Join-Path $innoWork 'app'
    if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
    Start-Process -FilePath $installer `
        -ArgumentList '/PORTABLE=1', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$appDir" `
        -Wait | Out-Null

    $iscc = Join-Path $appDir 'ISCC.exe'
    if (-not (Test-Path $iscc)) { throw "portable extraction produced no ISCC.exe under $appDir" }
    return $iscc
}

$iscc = Get-InnoCompiler
Write-Output "ISCC    : $iscc"

# Inno Setup ships Japanese/Korean/Thai but NO Simplified Chinese — that one is an
# unofficial translation kept in the official repository. Without it the wizard would
# be English for a Chinese-speaking friend.
$langDir = Join-Path (Split-Path -Parent $iscc) 'Languages'
$isl = Join-Path $langDir 'ChineseSimplified.isl'
if (-not (Test-Path $isl)) {
    Write-Output 'fetching ChineseSimplified.isl ...'
    $hdr = @{ 'User-Agent' = 'dsh-build'; 'Accept' = 'application/vnd.github+json' }
    $meta = Invoke-RestMethod 'https://api.github.com/repos/jrsoftware/issrc/contents/Files/Languages/ChineseSimplified.isl' `
        -Headers $hdr -TimeoutSec 60
    $b64 = $meta.content -replace '\s', ''
    New-Item -ItemType Directory -Force -Path $langDir | Out-Null
    [System.IO.File]::WriteAllBytes($isl, [Convert]::FromBase64String($b64))
    Write-Output "  saved $isl ($((Get-Item $isl).Length) bytes)"
}

# ---------------------------------------------------------------- compile

# Inno Setup 6 reads a BOM-less .iss as ANSI, which mangles every Chinese string.
$text = [System.IO.File]::ReadAllText($iss)
if (-not ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF)) {
    Write-Output 'adding UTF-8 BOM to setup.iss'
    [System.IO.File]::WriteAllText($iss, $text, (New-Object System.Text.UTF8Encoding($true)))
}

$outDir = Join-Path $root 'installer\out'
Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Step 'compiling installer'
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

Write-Step 'result'
Get-ChildItem $outDir -File |
    Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } },
                  @{ n = 'SHA256'; e = { (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0, 16) } } |
    Format-Table -AutoSize
