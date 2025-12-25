<#
PowerShell script to publish and package PhotoSorterApp and (optionally) create a Git tag and GitHub release.
Usage examples:
  # Quick publish with defaults
  .\build\publish.ps1 -Version v1.0.1

  # Custom runtime and do not create remote tag or release
  .\build\publish.ps1 -Version v1.0.1 -Runtime win-x86 -CreateTag:$false -CreateRelease:$false
#>

param(
    [string]$Version = "v1.0.1",
    [string]$Project = "PhotoSorterApp/PhotoSorterApp.csproj",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$ArtifactsDir = ".\\artifacts",
    [switch]$CreateTag = $true,
    [switch]$CreateRelease = $true,
    [string]$ZipName = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-CommandExists {
    param([string]$cmd)
    return (Get-Command $cmd -ErrorAction SilentlyContinue) -ne $null
}

if (-not (Test-CommandExists dotnet)) {
    Write-Error "dotnet CLI not found. Install .NET SDK to continue."
    exit 1
}

# Normalize paths
$projectPath = Resolve-Path -LiteralPath $Project -ErrorAction SilentlyContinue
if (-not $projectPath) {
    # try relative to repo root
    $projectPath = Join-Path (Get-Location) $Project
}

$ArtifactsDir = Resolve-Path -LiteralPath $ArtifactsDir -ErrorAction SilentlyContinue
if (-not $ArtifactsDir) {
    New-Item -ItemType Directory -Path (Join-Path (Get-Location) '.\\artifacts') -Force | Out-Null
    $ArtifactsDir = Resolve-Path -LiteralPath '.\\artifacts'
}

$ArtifactsDir = $ArtifactsDir.Path
$PublishDir = Join-Path $ArtifactsDir 'publish'

if ([string]::IsNullOrWhiteSpace($ZipName)) {
    # remove leading 'v' from version when constructing filename for clarity, but keep as-is otherwise
    $verForName = $Version.TrimStart('v','V')
    $ZipName = "PhotoSorterApp-$verForName-$Runtime.zip"
}

$ZipPath = Join-Path $ArtifactsDir $ZipName

Write-Host "Project: $Project"
Write-Host "Configuration: $Configuration"
Write-Host "Runtime: $Runtime"
Write-Host "Artifacts dir: $ArtifactsDir"
Write-Host "Publish dir: $PublishDir"
Write-Host "Zip: $ZipPath"

# 1) dotnet publish
Write-Host "\n-> Running dotnet publish..."
if (Test-CommandExists dotnet) {
    dotnet publish $Project -c $Configuration -r $Runtime --self-contained false -o $PublishDir
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed."
        exit 2
    }
} else {
    Write-Error "dotnet CLI is not available."
    exit 1
}

# 2) create artifacts and zip
Write-Host "\n-> Creating zip archive..."
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $ZipPath -Force
Write-Host "Archive created: $ZipPath"

# 3) create git tag and push (optional)
if ($CreateTag) {
    if (-not (Test-CommandExists git)) {
        Write-Warning "git not found — skipping tag creation."
    } else {
        Write-Host "\n-> Creating git tag $Version..."
        git tag -a $Version -m "Release $Version" 2>$null
        git push origin $Version
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Failed to push tag $Version. You may need to push manually."
        } else {
            Write-Host "Tag pushed: $Version"
        }
    }
}

# 4) create GitHub release and upload asset (optional)
if ($CreateRelease) {
    if (-not (Test-CommandExists gh)) {
        Write-Warning "gh CLI not found — skipping GitHub release creation. Install GitHub CLI (gh) to enable this step."
    } else {
        Write-Host "\n-> Creating GitHub release $Version..."
        try {
            # create or update release; --clobber will replace assets
            gh release create $Version $ZipPath --title $Version --notes "Release $Version" --clobber
            Write-Host "GitHub release created/updated: $Version"
        }
        catch {
            Write-Warning "gh release command failed: $_"
        }
    }
}

Write-Host "\nDone. Artifacts available in: $ArtifactsDir"
exit 0
