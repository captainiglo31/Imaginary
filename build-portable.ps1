# ==============================================================================
# Imaginary – Portable Build Script
# Erstellt ein eigenstaendiges, portables Paket fuer Windows (x64)
# Keine .NET-Installation oder Administratorrechte beim Endnutzer erforderlich!
# ==============================================================================

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "dist\Imaginary-Portable"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Imaginary - Portables Paket wird erstellt ($Runtime)..." -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$rootDir = $PSScriptRoot
$projectPath = Join-Path $rootDir "src\Imaginary.Desktop\Imaginary.Desktop.csproj"
$targetDistDir = Join-Path $rootDir $OutputDir
$zipPath = Join-Path $rootDir "dist\Imaginary-Portable-$Runtime.zip"
$assetsDir = Join-Path $rootDir "portable-assets"

# Beende eventuell laufende Instanzen
Get-Process -Name "Imaginary*", "Imaginary.Desktop*" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

# Bereinige vorherige Builds
if (Test-Path $targetDistDir) {
    Write-Host "Bereinige vorheriges Ausgabe-Verzeichnis..." -ForegroundColor Yellow
    Remove-Item -Path $targetDistDir -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

$distParent = Split-Path -Path $targetDistDir -Parent
if (-not (Test-Path $distParent)) {
    New-Item -ItemType Directory -Path $distParent -Force | Out-Null
}
New-Item -ItemType Directory -Path $targetDistDir -Force | Out-Null

Write-Host "1. Kompiliere und publiziere als Self-Contained Single-File..." -ForegroundColor Green
dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $targetDistDir

# Benenne Imaginary.Desktop.exe in Imaginary.exe um
$exePath = Join-Path $targetDistDir "Imaginary.Desktop.exe"
$aliasPath = Join-Path $targetDistDir "Imaginary.exe"
if (Test-Path $exePath) {
    Move-Item $exePath $aliasPath -Force
}

# Bereinige Debug-Symbole fuer Endnutzer
Get-ChildItem -Path $targetDistDir -Filter "*.pdb" | Remove-Item -Force

# Aktualisiere auch die Imaginary.exe im Projekt-Hauptverzeichnis
Copy-Item $aliasPath (Join-Path $rootDir "Imaginary.exe") -Force

Write-Host "2. Kopiere portable Konfigurationsdateien..." -ForegroundColor Green
if (Test-Path $assetsDir) {
    Get-ChildItem -Path $assetsDir | Copy-Item -Destination $targetDistDir -Force
}

Write-Host "3. Erstelle ZIP-Archiv: $zipPath..." -ForegroundColor Green
Compress-Archive -Path "$targetDistDir\*" -DestinationPath $zipPath -Force

$zipItem = Get-Item $zipPath
$zipSizeMb = [Math]::Round($zipItem.Length / 1MB, 2)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " ERFOLG! Portables Paket fertiggestellt:" -ForegroundColor Cyan
Write-Host " Ordner: $targetDistDir" -ForegroundColor White
Write-Host " Archiv: $zipPath ($zipSizeMb MB)" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan
