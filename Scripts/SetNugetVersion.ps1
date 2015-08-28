# make sure AssemblyInfo.cs has 
$asmInfo = (Get-Content "$PSScriptRoot\..\$env:ASSEMBLY_FILE") | Out-String
$pattern = '^\s*\[\s*assembly\s*:\s*AssemblyInformationalVersion\s*\(\s*"[^"]*"\s*\)\]\s*$'
if (-not [Regex]::IsMatch($asmInfo, $pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline))
{
    Write-Error "AssemblyInformationalVersion was not found in $env:ASSEMBLY_FILE"
    Exit 1
}

# get current release version
$version = "$env:NUGET_RELEASE_VERSION"

# make sure version follows the 0.0.0 format
if (![Regex]::IsMatch($version, '^\d+\.\d+\.\d+$'))
{
	Write-Error "Invalid NUGET_RELEASE_VERSION: $version"
	Exit 1
}

# set the correct nuget package version (depends on whether this is a release or not)
if ("$env:APPVEYOR_REPO_TAG" -ne "true") # non-tagged (pre-release build)
{
	# we want to increment the patch number for unstable builds
	$version = [Regex]::Replace($version, '^(\d+\.\d+\.)(\d+)$', {
		param([System.Text.RegularExpressions.Match] $match)
		$val = [int]::Parse($match.Groups[2].Value)
		$val++
		$match.Groups[1].Value + $val
	})

	$version += "-unstable$env:APPVEYOR_BUILD_NUMBER"
}

# grab .nuspec file contents
$file = "$PSScriptRoot\..\$env:NUGET_FILE"
$contents = (Get-Content $file) | Out-String

# make sure there is exactly one occurance of <version>$version$</version> in the file
$pattern = '<version>\$version\$</version>'
$matches = [Regex]::Matches("$contents", "$pattern")
if ($matches.Count -ne 1)
{
	$count = $matches.Count
	Write-Error "<version>`$version`$</version> was found $count times in the nuspec file. If should have been found exactly once."
	Exit 1
}

# set the NUGET_VERSION env variable
[Environment]::SetEnvironmentVariable("NUGET_VERSION", "$version", "Process")
Write-Host "NUGET_VERSION set as $version"
