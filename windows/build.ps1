<#
.SYNOPSIS
    Builds, tests, and runs the Windows port of macshot.

.DESCRIPTION
    macshot for Windows is an unpackaged WinUI 3 app, so there is nothing to
    install: publishing produces a folder with the executable in it, and running
    that puts the icon in the notification area. There is no MSI or MSIX yet; that
    belongs to the distribution milestone.

    Visual Studio is not required. The .NET SDK restores the Windows App SDK and
    the Windows SDK projection from NuGet.

.PARAMETER Configuration
    Debug or Release. Defaults to Release, which is what CI builds and the only
    configuration where the warning-as-error settings match a release run.

.PARAMETER Test
    Runs the Core unit tests after building.

.PARAMETER Publish
    Produces a runnable folder under windows/dist (override with -OutputPath).

.PARAMETER Run
    Publishes and then starts macshot. Implies -Publish.

.PARAMETER FrameworkDependent
    Publishes against an installed .NET 8 Desktop Runtime instead of bundling one.
    Much smaller output, but the machine running it needs that runtime.

.EXAMPLE
    .\build.ps1 -Test
    Builds Release and runs the unit tests. This is the CI equivalent.

.EXAMPLE
    .\build.ps1 -Run
    Builds, publishes a self-contained copy, and starts it. Look for the icon in
    the notification area, then press Ctrl+Shift+X to capture.
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$Test,
    [switch]$Publish,
    [switch]$Run,
    [switch]$FrameworkDependent,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$solution = Join-Path $root 'Macshot.Windows.sln'
$app = Join-Path $root 'src/Macshot.Windows/Macshot.Windows.csproj'
$coreTests = Join-Path $root 'tests/Macshot.Windows.Core.Tests/Macshot.Windows.Core.Tests.csproj'

function Invoke-Step {
    param([string]$Description, [scriptblock]$Action)

    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Action

    # dotnet reports failure through the exit code rather than by throwing, so
    # without this a failed build would be followed by a cheerful "done".
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) {
    throw 'The WinUI app only builds on Windows. On macOS or Linux build windows/src/Macshot.Windows.Core instead.'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK was not found. Install .NET SDK 8.0 or newer from https://dotnet.microsoft.com/download and reopen the terminal.'
}

$sdkMajor = [int]((& dotnet --version).Split('.')[0])
if ($sdkMajor -lt 8) {
    throw "The .NET SDK is $sdkMajor.x; macshot needs 8.0 or newer."
}

Invoke-Step "Building $Configuration" {
    # --warnaserror matches CI. A warning that only fails in CI is a warning found
    # after the push rather than before it.
    dotnet build $solution --configuration $Configuration --warnaserror
}

if ($Test) {
    Invoke-Step 'Running Core tests' {
        dotnet test $coreTests --configuration $Configuration --no-build
    }
}

if ($Run -or $Publish) {
    if (-not $OutputPath) {
        $OutputPath = Join-Path $root "dist/$Configuration"
    }

    $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
    Invoke-Step "Publishing to $OutputPath" {
        dotnet publish $app `
            --configuration $Configuration `
            --self-contained $selfContained `
            --output $OutputPath
    }

    $executable = Join-Path $OutputPath 'Macshot.Windows.exe'
    if (-not (Test-Path $executable)) {
        throw "Publish finished but $executable is missing."
    }

    Write-Host ''
    Write-Host "macshot published to $OutputPath" -ForegroundColor Green
    Write-Host 'Nothing needs installing: copy that folder anywhere and run the executable.'

    if ($Run) {
        Write-Host '==> Starting macshot' -ForegroundColor Cyan
        Start-Process -FilePath $executable

        Write-Host ''
        Write-Host 'macshot runs with no window: look for its icon in the notification area.' -ForegroundColor Green
        Write-Host '  Ctrl+Shift+X  capture an area'
        Write-Host '  Ctrl+Shift+F  capture every screen'
        Write-Host '  right-click the icon for Preferences and Quit'
    }
}
