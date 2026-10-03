<#
.SYNOPSIS
    Builds the Tickwise native library for Windows x86_64 and copies it into the project.

.DESCRIPTION
    The Tickwise Unity package has no prebuilt binaries yet, so tickwise_ffi.dll is built
    locally from the same commit the package is pinned to in Packages/manifest.json.
    The DLL lands in Assets/Plugins/Tickwise/x86_64/, which is git-ignored.

    Close the Unity Editor first: it locks the DLL once loaded.

    Requires Rust 1.88 or newer (https://rustup.rs) and Git.

.PARAMETER InstallCli
    Also installs the tickwise command line tool from the same commit.

.EXAMPLE
    ./tools/build-tickwise.ps1 -InstallCli
#>
[CmdletBinding()]
param(
    [switch] $InstallCli
)

$ErrorActionPreference = 'Stop'

$repositoryUrl = 'https://github.com/cosgunhalil/Tickwise.git'
$projectRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $projectRoot 'Packages/manifest.json'
$workDirectory = Join-Path $projectRoot '.tickwise-build'
$pluginDirectory = Join-Path $projectRoot 'Assets/Plugins/Tickwise/x86_64'
$dllName = 'tickwise_ffi.dll'

function Invoke-Native {
    param([string] $FilePath, [string[]] $Arguments)

    # Git and cargo report progress on stderr. Windows PowerShell 5.1 turns redirected
    # stderr into error records, which 'Stop' would treat as failures; rely on the exit code.
    $ErrorActionPreference = 'Continue'
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }
}

# Read the pinned commit so the DLL always matches the installed package.
$manifest = Get-Content $manifestPath -Raw
$match = [regex]::Match($manifest, '"com\.cosgunhalil\.tickwise"\s*:\s*"[^"#]*#([0-9a-f]{40})"')
if (-not $match.Success) {
    throw "Could not find a commit-pinned com.cosgunhalil.tickwise entry in $manifestPath."
}
$commit = $match.Groups[1].Value
Write-Host "Tickwise commit: $commit"

if (-not (Test-Path (Join-Path $workDirectory '.git'))) {
    Invoke-Native git @('clone', '--filter=blob:none', '--no-checkout', $repositoryUrl, $workDirectory)
}
Invoke-Native git @('-C', $workDirectory, 'fetch', '--quiet', 'origin', $commit)
Invoke-Native git @('-C', $workDirectory, 'checkout', '--quiet', '--detach', $commit)

# Cargo reads .cargo/config.toml (static CRT) only from the working directory.
Push-Location (Join-Path $workDirectory 'bridges/tickwise-ffi')
try {
    Invoke-Native cargo @('build', '--release', '--locked')
}
finally {
    Pop-Location
}

$builtDll = Join-Path $workDirectory "bridges/tickwise-ffi/target/release/$dllName"
New-Item -ItemType Directory -Force $pluginDirectory | Out-Null
try {
    Copy-Item $builtDll (Join-Path $pluginDirectory $dllName) -Force
}
catch {
    throw "Could not replace $dllName. Close the Unity Editor (it locks loaded native plugins) and run the script again. $_"
}

# Import settings: Editor and Standalone Win64 only, x86_64. Same settings as the package's
# own .meta, with a project-specific GUID so it never collides with the package copy.
$metaPath = Join-Path $pluginDirectory "$dllName.meta"
if (-not (Test-Path $metaPath)) {
    $meta = @'
fileFormatVersion: 2
guid: 463d8590063e44168938e515eb555a7f
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      : Any
    second:
      enabled: 0
      settings:
        Exclude Android: 1
        Exclude Editor: 0
        Exclude Linux64: 1
        Exclude OSXUniversal: 1
        Exclude Win: 1
        Exclude Win64: 0
        Exclude iOS: 1
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        CPU: x86_64
        DefaultValueInitialized: true
        OS: Windows
  - first:
      Standalone: Win64
    second:
      enabled: 1
      settings:
        CPU: x86_64
  userData:
  assetBundleName:
  assetBundleVariant:
'@
    # Here-strings drop the final line break, and Unity's YAML parser rejects a .meta without one.
    [System.IO.File]::WriteAllText($metaPath, ($meta -replace "`r`n", "`n") + "`n")
}

Write-Host "Copied $dllName to $pluginDirectory"

if ($InstallCli) {
    Invoke-Native cargo @('install', '--git', $repositoryUrl, '--rev', $commit, '--locked', 'tickwise-cli')
    Write-Host 'Installed the tickwise CLI. Check with: tickwise --version'
}
