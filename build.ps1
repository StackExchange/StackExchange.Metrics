param (
    # Specifies a pre-release build number. This is ignored if -Stable is used.
    [int] $BuildNum,
    # Indicates this is a Stable release. If set, the nuget version number is left unaltered.
    [switch] $Stable,
    # The configuration target for the build (e.g. Debug or Release)
    [string] $Configuration = "Release",
    # Skips tests.
    [switch] $SkipTests,
    # Creates nuget packages.
    [switch] $Pack,
    # The MSBuild verbosity level. Options: quiet, minimal, normal, detailed, or diagnostic. Defaults to minimal.
    [string] $Verbosity = "minimal",
    # Enables Powershell debugging output
    [switch] $PsDebug
)

$AppVeyor = $env:APPVEYOR -eq 'true'

function Main()
{
    # project config
    $buildProject = 'BosunReporter.sln'
    $nugetProject = 'BosunReporter\BosunReporter.csproj'
    $testProject = $null

    # build script config
    if ($PsDebug)
    {
        $DebugPreference = "Continue"
    }

    if ($AppVeyor)
    {
        Write-Debug 'Using AppVeyor build mode. -BuildNum and -Stable parameters are ignored.'
        $BuildNum = [int]::Parse("$env:APPVEYOR_BUILD_NUMBER")
        $Stable = [bool]::Parse("$env:APPVEYOR_REPO_TAG")
    }

    $msbuildExe = GetMsBuildExe

    $version = GetVersion $nugetProject $BuildNum $Stable

    # Restore
    Write-Host
    Write-Host -BackgroundColor "Cyan" -ForegroundColor "Black" "Restoring $buildProject"
    ExecuteMsBuild $msbuildExe $buildProject 'restore'

    # Build
    Write-Host
    Write-Host -BackgroundColor "Cyan" -ForegroundColor "Black" "Building $buildProject"
    ExecuteMsBuild $msbuildExe $buildProject

    # Test
    if ($testProject -and $SkipTests -ne $true)
    {
         Write-Host
         Write-Host -BackgroundColor "Cyan" -ForegroundColor "Black" "Running Tests"
         ExecuteMsBuild $msbuildExe $testProject 'Tests'
    }

    # Nuget Pack
    if ($Pack)
    {
        Write-Host
        Write-Host -BackgroundColor "Cyan" -ForegroundColor "Black" "Creating NuGet Package"
        Write-Host -BackgroundColor "Cyan" -ForegroundColor "Black" "Version: $version"
        ExecuteMsBuild $msbuildExe $nugetProject 'pack' $version
    }

    Write-Host
    Write-Host -ForegroundColor "Green" "Build Complete"
    Write-Host
}

function GetMsBuildExe()
{
    # We want to find the MSBuild associated with Visual Studio 2017 (MSBuild 15). This could probably use some improvement.
    $regKey = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\VisualStudio\SxS\VS7'
    $vsLocation = Get-ItemProperty $regKey -ErrorAction SilentlyContinue | Select-Object -ExpandProperty '15.0'

    if ($vsLocation -eq $null)
    {
        Write-Error 'Visual Studio 2017 is required, but does not appear to be installed.'
        Exit 1
    }

    # combine the VS install path with the MSBuild location
    $msbuildExe = Join-Path $vsLocation 'MSBuild\15.0\bin\MSBuild.exe'
    Write-Debug "MSBuild location: $msbuildExe"
    return $msbuildExe
}

function ExecuteMsBuild([string] $msbuildExe, [string] $project, [string] $target, [string] $version)
{
    [string[]] $msBuildArgs = "`"$project`"", "/p:Configuration=$Configuration"

    if ($target -ne "")
    {
        $msBuildArgs += "/t:$target"

        if ($target -eq 'pack')
        {
            $msBuildArgs += "/p:IncludeSymbols=true"
            $msBuildArgs += "/p:PackageVersion=$version"
            $msBuildArgs += "/p:PackageOutputPath=`"$(Join-Path $PSScriptRoot 'artifacts')`""
        }
    }

    if ($AppVeyor)
    {
        $msBuildArgs += "/logger:`"C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll`""
    }

    $msBuildArgs += "/verbosity:$Verbosity"

    Write-Host -ForegroundColor "Cyan" "'$msbuildExe' $msBuildArgs"
    & $msbuildExe $msBuildArgs

    if ($LASTEXITCODE -ne 0)
    {
        Write-Host
        Write-Host -ForegroundColor "Red" 'Build Failed'
        Write-Host

        Exit 1
    }
}

function GetVersion([string] $nugetProject, [int] $buildNum, [bool] $stable)
{
    $fileName = Join-Path -Path (Get-Location) -ChildPath $nugetProject
    $csXml = New-Object XML
    $csXml.Load($fileName)

    $packageVersionNode = GetPackageVersionNode $csXml
    $version = $packageVersionNode.InnerText

    $versionGroups = GetVersionGroups $version

    if (!$stable)
    {
        $version = GeneratePrereleaseVersion $versionGroups $buildNum
    }

    Write-Output -NoEnumerate $version
}

function GetPackageVersionNode ([xml] $csXml)
{
    # find the PackageVersion element
    $nodes = $csXml.GetElementsByTagName("PackageVersion")
    if ($nodes.Count -eq 0)
    {
        Write-Error "$csproj does not contain a <PackageVersion> element."
        Exit 1
    }

    if ($nodes.Count -gt 1)
    {
        Write-Error "$csproj contains more than one instance of <PackageVersion>"
        Exit 1
    }

    Write-Output -NoEnumerate $nodes[0]
}

function GetVersionGroups ([string] $version)
{
    # validate the package version and extract major version
    $versionMatch = [Regex]::Match($version, '^(?<Major>\d+)\.(?<Minor>\d+)\.(?<Patch>\d+)(?<PreRelease>-\S+)?$')
    if (!$versionMatch.Success)
    {
        Write-Error "Invalid PackageVersion: $version. It must follow the MAJOR.MINOR.PATCH format."
        Exit 1
    }

    Write-Output -NoEnumerate $versionMatch.Groups
}

function GeneratePrereleaseVersion ([System.Text.RegularExpressions.GroupCollection] $versionGroups, [int] $buildNum)
{
    $version = $versionGroups["Major"].Value + "." + $versionGroups["Minor"].Value + "."

    if ($versionGroups["PreRelease"].Success)
    {
        # the version is already marked as a pre-release, so we don't need to increment the patch number
        $version += $versionGroups["Patch"].Value + $versionGroups["PreRelease"].Value
    }
    else
    {
        $patch = [int]::Parse($versionGroups["Patch"]) + 1
        $version += "$patch-unStable"
    }

    $version += "-$buildNum"

    Write-Output -NoEnumerate $version
}

Main
