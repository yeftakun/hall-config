# Publish Script for HallConfig (Windows x64 Single-File Standalone)
$ErrorActionPreference = "Stop"

$solutionDir = $PSScriptRoot
$publishDir = Join-Path $solutionDir "publish"
$projectPath = Join-Path $solutionDir "src\HallConfig.App\HallConfig.App.csproj"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "         HALLCONFIG - STANDALONE RELEASE PUBLISH         " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (Test-Path $publishDir) {
    Write-Host "Cleaning output directory: $publishDir..." -ForegroundColor Yellow
    Remove-Item -Path $publishDir -Recurse -Force | Out-Null
}

Write-Host "Publishing HallConfig.App for win-x64 (Single-File, Self-Contained)..." -ForegroundColor Green

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# Ensure native vJoy DLLs are in the publish folder
$vjoyNativeSrc = Join-Path $solutionDir "libs\x64\vJoyInterface.dll"
$vjoyWrapSrc   = Join-Path $solutionDir "libs\x64\vJoyInterfaceWrap.dll"

if (Test-Path $vjoyNativeSrc) {
    Copy-Item $vjoyNativeSrc -Destination $publishDir -Force
}
if (Test-Path $vjoyWrapSrc) {
    Copy-Item $vjoyWrapSrc -Destination $publishDir -Force
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " Publish SUCCESSFUL!" -ForegroundColor Green
Write-Host " Standalone binary output: $publishDir\HallConfig.App.exe" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green

# Check and compile Inno Setup installer if ISCC.exe is found
$isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $isccPath)) {
    $isccPath = "C:\Program Files\Inno Setup 6\ISCC.exe"
}

$issFile = Join-Path $solutionDir "installer.iss"
if ((Test-Path $isccPath) -and (Test-Path $issFile)) {
    Write-Host "`nCompiling Inno Setup Installer..." -ForegroundColor Cyan
    & $isccPath $issFile
    if ($LASTEXITCODE -eq 0) {
        $setupExe = Join-Path $solutionDir "dist\HallConfig_Setup_v1.1.0.exe"
        Write-Host " Setup Installer generated at: $setupExe" -ForegroundColor Green
    }
}

Get-ChildItem -Path $publishDir | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
