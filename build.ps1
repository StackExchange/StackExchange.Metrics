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
    # The verbosity level. Options: quiet, minimal, normal, detailed, or diagnostic. Defaults to minimal.
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


    $version = GetVersion $nugetProject $BuildNum $Stable

    # Restore
    Write-Host
    Write-Host -BackgroundColor "Cyan" -ForegroundColor "Black" "Restoring $buildProject"
    ExecuteDotNet $buildProject 'restore'

    # Build
    Write-Host
    Write-Host -BackgroundColor "Cyan" -ForegroundColor "Black" "Building $buildProject"
    ExecuteDotNet $buildProject 'build'

    # Test
    if ($testProject -and $SkipTests -ne $true)
    {
         Write-Host
         Write-Host -BackgroundColor "Cyan" -ForegroundColor "Black" "Running Tests"
         ExecuteDotNet $testProject 'test'
    }

    # Nuget Pack
    if ($Pack)
    {
        Write-Host
        Write-Host -BackgroundColor "Cyan" -ForegroundColor "Black" "Creating NuGet Package"
        Write-Host -BackgroundColor "Cyan" -ForegroundColor "Black" "Version: $version"
        ExecuteDotNet $nugetProject 'pack' $version
    }

    Write-Host
    Write-Host -ForegroundColor "Green" "Build Complete"
    Write-Host
}

function ExecuteDotNet([string] $project, [string] $cmd, [string] $version)
{
    [string[]] $dotnetArgs = $cmd, "`"$project`""

    switch ($cmd)
    {
        "restore" { }
        "build" {
            $dotnetArgs += "-c=$Configuration"
            $dotnetArgs += "--no-restore"
        }
        "test" {
            $dotnetArgs += "-c=$Configuration"
            $dotnetArgs += "--no-restore"
            $dotnetArgs += "--no-build"
        }
        "pack" {
            $dotnetArgs += "-c=$Configuration"
            $dotnetArgs += "--no-restore"
            $dotnetArgs += "/p:IncludeSymbols=true"
            $dotnetArgs += "/p:PackageVersion=$version"
            $dotnetArgs += "/p:PackageOutputPath=`"$(Join-Path $PSScriptRoot 'artifacts')`""
        }
    }

    if ($AppVeyor)
    {
        # $dotnetArgs += "/l:`"C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll`""
    }

    $dotnetArgs += "-v=$Verbosity"

    Write-Host -ForegroundColor "Cyan" "dotnet $dotnetargs"
    & dotnet $dotnetargs

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
    $nodes = $csXml.GetElementsByTagName("Version")
    if ($nodes.Count -eq 0)
    {
        Write-Error "$csproj does not contain a <Version> element."
        Exit 1
    }

    if ($nodes.Count -gt 1)
    {
        Write-Error "$csproj contains more than one instance of <Version>"
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
        $version += "$patch-unstable"
    }

    $version += "-$buildNum"

    Write-Output -NoEnumerate $version
}

Main
