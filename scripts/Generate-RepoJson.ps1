<#
.SYNOPSIS
    Builds the plugin and regenerates repo.json, the Dalamud custom repository file.

.DESCRIPTION
    repo.json is what Dalamud reads when someone adds this repository under
    Settings -> Experimental -> Custom Plugin Repositories. It is the packaged
    manifest plus the three download links, wrapped in an array.

    Everything except the links comes from the built manifest, which in turn comes
    from the MSBuild properties in ActionMeThis.csproj. Edit the csproj, run this,
    and the two can never disagree.

.EXAMPLE
    ./scripts/Generate-RepoJson.ps1
#>
[CmdletBinding()]
param(
    # Skip the build and reuse whatever is already in bin/Release.
    [switch] $NoBuild,

    # Owner/name of the GitHub repository releases are published to.
    [string] $Repository = 'Liquidize/ActionMeThis'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'ActionMeThis/ActionMeThis.csproj'
$manifestPath = Join-Path $root 'ActionMeThis/bin/Release/ActionMeThis/ActionMeThis.json'
$zipPath = Join-Path $root 'ActionMeThis/bin/Release/ActionMeThis/latest.zip'
$outputPath = Join-Path $root 'repo.json'

if (-not $NoBuild) {
    Write-Host 'Building Release...'
    dotnet build $project --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE."
    }
}

foreach ($required in @($manifestPath, $zipPath)) {
    if (-not (Test-Path $required)) {
        throw "Expected $required. Run a Release build first, or drop -NoBuild."
    }
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

# "releases/latest/download" always resolves to the newest published release, so the
# links never need touching again - only AssemblyVersion below tells Dalamud that an
# update exists.
$download = "https://github.com/$Repository/releases/latest/download/latest.zip"

$entry = [ordered]@{
    Author              = $manifest.Author
    Name                = $manifest.Name
    InternalName        = $manifest.InternalName
    AssemblyVersion     = $manifest.AssemblyVersion
    Description         = $manifest.Description
    Punchline           = $manifest.Punchline
    ApplicableVersion   = $manifest.ApplicableVersion
    RepoUrl             = $manifest.RepoUrl
    Tags                = $manifest.Tags
    DalamudApiLevel     = $manifest.DalamudApiLevel
    LoadPriority        = $manifest.LoadPriority
    AcceptsFeedback     = $manifest.AcceptsFeedback
    IsHide              = $false
    IsTestingExclusive  = $false
    LastUpdate          = [int64][double]::Parse((Get-Date -UFormat %s))
    DownloadCount       = 0
    DownloadLinkInstall = $download
    DownloadLinkUpdate  = $download
    DownloadLinkTesting = $download
}

# Dalamud expects an array of manifests, one per plugin in the repository.
$json = ConvertTo-Json -InputObject @($entry) -Depth 6
Set-Content -Path $outputPath -Value $json -Encoding utf8NoBOM

Write-Host "Wrote $outputPath for $($manifest.Name) $($manifest.AssemblyVersion)."
Write-Host "Zip to attach to the release: $zipPath"
