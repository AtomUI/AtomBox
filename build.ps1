param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$SkipTests,

    [switch]$Clean,

    [switch]$NoRestore,

    [switch]$IgnoreFailedSources,

    [switch]$RunDesktop
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $repoRoot "AtomBox.slnx"
$artifacts = Join-Path $repoRoot ".artifacts"
$desktopProject = Join-Path $repoRoot "src\AtomBox.Desktop\AtomBox.Desktop.csproj"

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:AVALONIA_TELEMETRY_OPTOUT = "1"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet CLI was not found. Install the .NET SDK first."
}

if (-not (Test-Path -LiteralPath $solution)) {
    throw "Solution file was not found: $solution"
}

if ($Clean) {
    $resolvedRoot = (Resolve-Path -LiteralPath $repoRoot).Path
    if (Test-Path -LiteralPath $artifacts) {
        $resolvedArtifacts = (Resolve-Path -LiteralPath $artifacts).Path
        if (-not $resolvedArtifacts.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean artifacts outside repository: $resolvedArtifacts"
        }

        Write-Step "Cleaning .artifacts"
        Remove-Item -LiteralPath $resolvedArtifacts -Recurse -Force
    }
}

if (-not $NoRestore) {
    Write-Step "Restoring packages"
    $restoreArguments = @("restore", $solution)
    if ($IgnoreFailedSources) {
        $restoreArguments += "--ignore-failed-sources"
    }

    Invoke-DotNet $restoreArguments
}

Write-Step "Building $Configuration"
Invoke-DotNet @("build", $solution, "--configuration", $Configuration, "--no-restore")

if (-not $SkipTests) {
    Write-Step "Running tests"
    Invoke-DotNet @("test", $solution, "--configuration", $Configuration, "--no-restore", "--no-build")
}

if ($RunDesktop) {
    Write-Step "Starting AtomBox Desktop"
    Invoke-DotNet @("run", "--project", $desktopProject, "--configuration", $Configuration, "--no-build")
}

Write-Host ""
Write-Host "Build completed. Outputs are under: $artifacts\bin" -ForegroundColor Green
