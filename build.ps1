param(
    [Parameter(Mandatory = $false)]
    [string]$AcadVersion = "2027",

    [Parameter(Mandatory = $false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [switch]$PackageOnly,

    [Parameter(Mandatory = $false)]
    [switch]$CreateInstaller = $true,

    [Parameter(Mandatory = $false)]
    [switch]$SkipInstaller
)

if ($SkipInstaller) { $CreateInstaller = $false }

$ErrorActionPreference = "Stop"
$SolutionRoot = Split-Path -Parent $PSCommandPath
$ProjectDir = Join-Path $SolutionRoot "src\AcLayerStandardizer"
$DistDir = Join-Path $SolutionRoot "dist"
$BundleName = "AcLayerStandardizer.bundle"
$BundleDir = Join-Path $SolutionRoot $BundleName

# Single source of truth for the app version -- keep in sync with the
# installer's #define MyAppVersion (installer/ACADLayerStandardizer.iss),
# which reads this same string via the MYAPPVERSION env var below.
$AppVersion = "ALPHA/0.3"
$AppVersionSafe = $AppVersion -replace "/", "-"

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
        -p:InformationalVersion=$AppVersion `
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

# Build installer (on by default, every build, so the installer never drifts
# from the app -- pass -SkipInstaller or -CreateInstaller:$false to opt out)
$InstallerBuilt = $false
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
        Write-Warning "Inno Setup not found -- skipping installer build. Install from https://jrsoftware.org/isinfo.php to have build.ps1 keep the installer current automatically."
    }
    else
    {
        # Set environment variables for Inno Setup script
        $env:MYAPPVERSION = $AppVersion
        $env:MYAPPTFM = $Tfm

        $IssScript = Join-Path $SolutionRoot "installer\ACADLayerStandardizer.iss"
        & $IsccPath $IssScript
        if ($LASTEXITCODE -ne 0) { exit 1 }

        $InstallerBuilt = $true
        $InstallerDest = Join-Path $DistDir "AcLayerStandardizer_$AppVersionSafe.exe"
        Write-Host "  Installer: $InstallerDest" -ForegroundColor Green
    }
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
if ($InstallerBuilt)
{
    Write-Host "  Installer: $DistDir\AcLayerStandardizer_$AppVersionSafe.exe" -ForegroundColor Green
}
Write-Host ""
Write-Host "Installation:" -ForegroundColor Cyan
Write-Host "  Option 1: Run the installer (AcLayerStandardizer_$AppVersionSafe.exe)"
Write-Host "  Option 2: Copy '$BundleName' folder to:"
Write-Host "     %APPDATA%\Autodesk\ApplicationPlugins\"
Write-Host "  Restart AutoCAD $AcadVersion"
Write-Host ""
