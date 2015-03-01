
# figure out the correct nuget package version (depends on whether this is a release or not)
$version = "$env:NUGET_RELEASE_VERSION"
if ("$env:APPVEYOR_REPO_TAG" -ne "true") # non-tagged (pre-release build)
{
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
