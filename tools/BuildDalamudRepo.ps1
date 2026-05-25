param(
    [Parameter(Mandatory = $true)]
    [string] $GitHubOwner,

    [Parameter(Mandatory = $true)]
    [string] $GitHubRepo,

    [string] $Branch = "main"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$pluginProject = Join-Path $root "src\GoldSaucerCasino.Plugin\GoldSaucerCasino.Plugin.csproj"
$pluginOutput = Join-Path $root "src\GoldSaucerCasino.Plugin\bin\Release"
$pluginPackageOutput = Join-Path $pluginOutput "GoldSaucerCasino"
$repoRoot = Join-Path $root "dist\dalamud-repo"
$pluginRepoDir = Join-Path $repoRoot "plugins\GoldSaucerCasino"
$zipPath = Join-Path $pluginRepoDir "latest.zip"
$pluginMasterPath = Join-Path $repoRoot "pluginmaster.json"

dotnet build $pluginProject -c Release

New-Item -ItemType Directory -Path $pluginRepoDir -Force | Out-Null

$packagerZip = Join-Path $pluginPackageOutput "latest.zip"
$packagerManifest = Join-Path $pluginPackageOutput "GoldSaucerCasino.json"
if (!(Test-Path $packagerZip)) {
    throw "DalamudPackager did not create $packagerZip"
}
if (!(Test-Path $packagerManifest)) {
    throw "DalamudPackager did not create $packagerManifest"
}

Copy-Item -LiteralPath $packagerZip -Destination $zipPath -Force

$entry = Get-Content $packagerManifest | ConvertFrom-Json

$rawBase = "https://raw.githubusercontent.com/$GitHubOwner/$GitHubRepo/$Branch"
$zipUrl = "$rawBase/plugins/GoldSaucerCasino/latest.zip"
$unixTime = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()

$entry | Add-Member -NotePropertyName RepoUrl -NotePropertyValue "https://github.com/$GitHubOwner/$GitHubRepo" -Force
$entry | Add-Member -NotePropertyName DownloadLinkInstall -NotePropertyValue $zipUrl -Force
$entry | Add-Member -NotePropertyName DownloadLinkTesting -NotePropertyValue $zipUrl -Force
$entry | Add-Member -NotePropertyName DownloadLinkUpdate -NotePropertyValue $zipUrl -Force
$entry | Add-Member -NotePropertyName DownloadCount -NotePropertyValue 0 -Force
$entry | Add-Member -NotePropertyName LastUpdate -NotePropertyValue $unixTime -Force
$entry | Add-Member -NotePropertyName IsHide -NotePropertyValue $false -Force
$entry | Add-Member -NotePropertyName IsTestingExclusive -NotePropertyValue $false -Force

$pluginMasterJson = ConvertTo-Json -InputObject @($entry) -Depth 8
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($pluginMasterPath, $pluginMasterJson, $utf8NoBom)

Write-Host "Built Dalamud custom repository files:"
Write-Host "  $pluginMasterPath"
Write-Host "  $zipPath"
Write-Host ""
Write-Host "Push the contents of this folder to GitHub:"
Write-Host "  $repoRoot"
Write-Host ""
Write-Host "Custom Plugin Repository URL:"
Write-Host "  $rawBase/pluginmaster.json"
Write-Host ""
Write-Host "If this source repo is also the GitHub repository, copy the generated repo files to the root before pushing:"
Write-Host "  Copy-Item dist\dalamud-repo\pluginmaster.json pluginmaster.json -Force"
Write-Host "  New-Item -ItemType Directory -Path plugins\GoldSaucerCasino -Force"
Write-Host "  Copy-Item dist\dalamud-repo\plugins\GoldSaucerCasino\latest.zip plugins\GoldSaucerCasino\latest.zip -Force"
