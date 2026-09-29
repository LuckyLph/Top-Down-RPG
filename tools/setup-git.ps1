# One-time per-clone git setup for this Unity project.
#   - Installs Git LFS hooks for this repository.
#   - Registers UnityYAMLMerge as the "unityyamlmerge" merge driver referenced by .gitattributes.
#
# Usage (from the repo root):
#   powershell -ExecutionPolicy Bypass -File tools/setup-git.ps1 [-UnityEditorPath "C:\Program Files\Unity\Hub\Editor\6000.4.2f1\Editor"]

param(
    [string]$UnityEditorPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $UnityEditorPath) {
    $versionLine = Get-Content (Join-Path $repoRoot 'ProjectSettings/ProjectVersion.txt') |
        Where-Object { $_ -like 'm_EditorVersion:*' } |
        Select-Object -First 1
    $version = ($versionLine -split ':', 2)[1].Trim()
    $UnityEditorPath = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$version\Editor"
}

$yamlMerge = Join-Path $UnityEditorPath 'Data\Tools\UnityYAMLMerge.exe'
if (-not (Test-Path $yamlMerge)) {
    throw "UnityYAMLMerge not found at '$yamlMerge'. Pass -UnityEditorPath pointing at the Unity 'Editor' folder."
}

git -C $repoRoot lfs install --local
if ($LASTEXITCODE -ne 0) { throw 'git lfs install failed. Is Git LFS installed?' }

$driverPath = $yamlMerge -replace '\\', '/'
git -C $repoRoot config merge.unityyamlmerge.name 'Unity SmartMerge (UnityYAMLMerge)'
git -C $repoRoot config merge.unityyamlmerge.driver "'$driverPath' merge -h -p --force %O %B %A %A"
git -C $repoRoot config merge.unityyamlmerge.recursive binary

Write-Host "Git LFS installed and UnityYAMLMerge registered: $yamlMerge"
