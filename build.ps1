param(
    [Parameter(Mandatory = $false)]
    [string]$AcadVersion = "2026",

    [Parameter(Mandatory = $false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [switch]$PackageOnly,

    [Parameter(Mandatory = $false)]
    [switch]$CreateInstaller
)

$ErrorActionPreference = "Stop"
$SolutionRoot = Split-Path -Parent $PSCommandPath
$ProjectDir = Join-Path $SolutionRoot "src\AcLayerStandardizer"
$DistDir = Join-Path $SolutionRoot "dist"
$BundleName = "AcLayerStandardizer.bundle"
$BundleDir = Join-Path $SolutionRoot $BundleName

# Validate AutoCAD version
if ($AcadVersion -ne "2026" -and $AcadVersion -ne "2027")
{
    Write-Error "AcadVersion must be 2026 or 2027. Got: $AcadVersion"
    exit 1
}

# Determine target framework
$Tfm = if ($AcadVersion -eq "2027") { "net10.0-windows" } else { "net8.0-windows" }

Write-Host "=== ACAD Layer Standardizer - Build & Package ===" -ForegroundColor Cyan
Write-Host "  AutoCAD version: $AcadVersion"
Write-Host "  Target framework: $Tfm"
Write-Host "  Configuration:    $Configuration"
Write-Host ""

if (-not $PackageOnly)
{
    # Restore
    Write-Host ">> Restoring packages..." -ForegroundColor Yellow
    dotnet restore $ProjectDir\AcLayerStandardizer.csproj -p:AcadVersion=$AcadVersion
    if ($LASTEXITCODE -ne 0) { exit 1 }

    # Build
    Write-Host ">> Building..." -ForegroundColor Yellow
    dotnet build $ProjectDir\AcLayerStandardizer.csproj `
        -p:AcadVersion=$AcadVersion `
        -p:Configuration=$Configuration `
        --no-restore
    if ($LASTEXITCODE -ne 0) { exit 1 }

    # Run tests
    $TestProject = Join-Path $SolutionRoot "tests\AcLayerStandardizer.Tests\AcLayerStandardizer.Tests.csproj"
    if (Test-Path $TestProject)
    {
        Write-Host ">> Running tests..." -ForegroundColor Yellow
        dotnet test $TestProject -p:AcadVersion=$AcadVersion --no-restore
        if ($LASTEXITCODE -ne 0) { exit 1 }
    }
}

# Build installer if requested
if ($CreateInstaller)
{
    Write-Host ">> Building installer..." -ForegroundColor Yellow
    
    # Find Inno Setup compiler
    $IsccPath = $null
    $PossiblePaths = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($Path in $PossiblePaths)
    {
        if (Test-Path $Path)
        {
            $IsccPath = $Path
            break
        }
    }
    
    if (-not $IsccPath)
    {
        Write-Error "Inno Setup not found. Install it from https://jrsoftware.org/isinfo.php"
        exit 1
    }
    
    # Set environment variables for Inno Setup script
    $env:MYAPPVERSION = "ALPHA/0.1"
    $env:MYAPPACADVERSION = $AcadVersion
    
    $IssScript = Join-Path $SolutionRoot "installer\ACADLayerStandardizer.iss"
    & $IsccPath $IssScript
    if ($LASTEXITCODE -ne 0) { exit 1 }
    
    $InstallerDest = Join-Path $DistDir "AcLayerStandardizer_$AcadVersion.exe"
    Write-Host "  Installer: $InstallerDest" -ForegroundColor Green
}

# Package
$OutputDir = Join-Path $ProjectDir "bin\$Configuration\$Tfm"
Write-Host ">> Packaging from: $OutputDir" -ForegroundColor Yellow

# Clean previous bundle
if (Test-Path $BundleDir)
{
    Remove-Item -Path $BundleDir -Recurse -Force
}

# Create bundle structure
$TargetDir = Join-Path $BundleDir "Contents\Windows\64-bit"
New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null

# Copy DLLs
Copy-Item -Path (Join-Path $OutputDir "AcLayerStandardizer.dll") -Destination $TargetDir
Copy-Item -Path (Join-Path $OutputDir "Nodify.dll") -Destination $TargetDir

# Copy build output (optional, for debugging)
New-Item -ItemType Directory -Path (Join-Path $BundleDir "build-output") -Force | Out-Null
Copy-Item -Path "$OutputDir\*" -Destination (Join-Path $BundleDir "build-output") -Include "*.pdb", "*.deps.json" -ErrorAction SilentlyContinue

# Copy manifest
Copy-Item -Path (Join-Path $DistDir "PackageContents.xml") -Destination $BundleDir

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Green
Write-Host "  Bundle:  $BundleDir"
if ($CreateInstaller)
{
    Write-Host "  Installer: $DistDir\AcLayerStandardizer_$AcadVersion.exe" -ForegroundColor Green
}
Write-Host ""
Write-Host "Installation:" -ForegroundColor Cyan
Write-Host "  Option 1: Run the installer (AcLayerStandardizer_$AcadVersion.exe)"
Write-Host "  Option 2: Copy '$BundleName' folder to:"
Write-Host "     %APPDATA%\Autodesk\ApplicationPlugins\"
Write-Host "  Restart AutoCAD $AcadVersion"
Write-Host ""
