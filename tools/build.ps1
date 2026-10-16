# Publishes the self-contained single-file executable into .\dist
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1
param(
    [string]$Configuration = 'Release',
    [string]$Output = 'dist'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\TodoWidget\TodoWidget.csproj'

# Publishing notes (measured on Windows 11 26200, 150% scaling):
#   EnableCompressionInSingleFile=true  -> exe 70 MB but 264 MB private memory,
#                                          because the runtime decompresses the
#                                          assemblies into private pages.
#   compression off, ReadyToRun on      -> exe 162 MB, 147 MB private.
#   compression off, ReadyToRun off     -> exe 145 MB, 144 MB private.  <- chosen
# A desktop widget is resident all day, so RAM wins over download size.
Write-Output "publishing $project -> $Output ..."

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=none `
    -o (Join-Path $root $Output) `
    --nologo

if ($LASTEXITCODE -ne 0) { throw "publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $root "$Output\TodoWidget.exe"
if (-not (Test-Path $exe)) { throw "expected $exe" }

# The product name is Chinese; the assembly keeps an ASCII name so that
# single-file publishing and debugging stay free of encoding surprises.
$renamed = Join-Path $root "$Output\待办清单.exe"
Copy-Item $exe $renamed -Force
Remove-Item $exe -Force

Write-Output ''
Write-Output "done -> $renamed"
Get-ChildItem (Join-Path $root $Output) -File |
    Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } } |
    Format-Table -AutoSize
