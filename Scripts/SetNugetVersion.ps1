
# figure out the correct nuget package version (depends on whether this is a release or not)
$version = "$env:nuget_version"
if ("$env:APPVEYOR_REPO_TAG" -ne "true") # non-tagged (pre-release build)
{
  $version += "-unstable$env:APPVEYOR_BUILD_NUMBER"
}

# grab .nuspec file contents
$file = "$PSScriptRoot\..\BosunReporter\BosunReporter.nuspec"
$contents = (Get-Content $file) | Out-String

# replace NUGET_VERSION with the the actual version
$count = 0
$replacer = [System.Text.RegularExpressions.MatchEvaluator]{
  $script:count++
  "$version"
}
$contents = [Regex]::Replace($contents, "NUGET_VERSION", $replacer)

# make sure NUGET_VERSION existed exactly once
if ($count -ne 1)
{
  Write-Error "NUGET_VERSION was found $count times in the nuspec file. If should have been found exactly once."
  Exit 1
}


Write-Host "Nuget version set as $version"
$contents | Set-Content $file
