param(
    [string]$Configuration = "Release",
    [string]$ModName = "YATMMedvedMoreBots",
    [string]$ServerModFolderName = "YATMMedved",
    [string]$ServerModRoot = "SPT\user\mods",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

function Find-RepoRoot {
    $Dir = (Resolve-Path $PSScriptRoot).Path

    while ($true) {
        if (Test-Path (Join-Path $Dir "YATMMedvedMoreBots.sln")) {
            return $Dir
        }

        if ((Test-Path (Join-Path $Dir "Plugin\Plugin.csproj")) -and
            (Test-Path (Join-Path $Dir "Prepatch\Prepatch.csproj")) -and
            (Test-Path (Join-Path $Dir "YetAnotherTraderMod\yatm-medved-server.csproj"))) {
            return $Dir
        }

        $Parent = Split-Path $Dir -Parent
        if ([string]::IsNullOrWhiteSpace($Parent) -or $Parent -eq $Dir) {
            break
        }

        $Dir = $Parent
    }

    throw "Could not find repo root. Put tools\package-mod.ps1 anywhere under the YATMMedvedMoreBots repo."
}

function Find-OutputDll {
    param(
        [string]$Label,
        [string]$DllName,
        [string[]]$SearchRoots
    )

    $ExistingRoots = @($SearchRoots | Where-Object { Test-Path $_ })

    foreach ($SearchRoot in $ExistingRoots) {
        $Match = Get-ChildItem -Path $SearchRoot -Filter $DllName -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch "\\obj\\" -and $_.FullName -notmatch "\\ref\\" -and $_.FullName -notmatch "\\refint\\" } |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1

        if ($null -ne $Match) {
            return $Match
        }
    }

    $Checked = if ($SearchRoots.Count -gt 0) { $SearchRoots -join "`n  - " } else { "none" }
    throw "Could not find $Label output DLL '$DllName'. Checked:`n  - $Checked`nBuild the project first or run this script without -SkipBuild."
}

function Copy-DllPair {
    param(
        [System.IO.FileInfo]$Dll,
        [string]$Destination
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item $Dll.FullName $Destination -Force

    $Pdb = [System.IO.Path]::ChangeExtension($Dll.FullName, ".pdb")
    if (Test-Path $Pdb) {
        Copy-Item $Pdb $Destination -Force
    }
}

$Root = Find-RepoRoot
$StageRoot = Join-Path $Root "dist\stage\$ModName"
$ZipPath = Join-Path $Root "dist\$ModName.zip"

Write-Host "[Package] Repo root: $Root"

if (-not $SkipBuild) {
    $SolutionPath = Join-Path $Root "YATMMedvedMoreBots.sln"
    $BuildProjectPath = Join-Path $Root "Build\Build.csproj"

    if (Test-Path $SolutionPath) {
        Write-Host "[Package] Building solution: $SolutionPath"
        dotnet build $SolutionPath -c $Configuration
    }
    elseif (Test-Path $BuildProjectPath) {
        Write-Host "[Package] Building Build project: $BuildProjectPath"
        dotnet build $BuildProjectPath -c $Configuration
    }
    else {
        throw "No solution or Build\Build.csproj found under $Root"
    }
}
else {
    Write-Host "[Package] SkipBuild enabled; packaging existing $Configuration outputs."
}

if (Test-Path $StageRoot) {
    Remove-Item $StageRoot -Recurse -Force
}

$PluginInstallDir = Join-Path $StageRoot "BepInEx\plugins\$ModName"
$PrepatchInstallDir = Join-Path $StageRoot "BepInEx\patchers\$ModName"
$ServerInstallDir = Join-Path $StageRoot "$ServerModRoot\$ServerModFolderName"

New-Item -ItemType Directory -Force -Path $PluginInstallDir, $PrepatchInstallDir, $ServerInstallDir | Out-Null

$PluginDll = Find-OutputDll `
    -Label "plugin" `
    -DllName "YATMMedvedPlugin.dll" `
    -SearchRoots @(
        (Join-Path $Root "Plugin\bin\$Configuration"),
        (Join-Path $Root "Plugin\build\bin\$Configuration")
    )

Write-Host "[Package] Plugin: $($PluginDll.FullName)"
Copy-DllPair -Dll $PluginDll -Destination $PluginInstallDir

$PrepatchDll = Find-OutputDll `
    -Label "prepatch" `
    -DllName "YATMMedvedPrepatch.dll" `
    -SearchRoots @(
        (Join-Path $Root "Prepatch\bin\$Configuration"),
        (Join-Path $Root "Prepatch\build\bin\$Configuration")
    )

Write-Host "[Package] Prepatch: $($PrepatchDll.FullName)"
Copy-DllPair -Dll $PrepatchDll -Destination $PrepatchInstallDir

$ServerDll = Find-OutputDll `
    -Label "server mod" `
    -DllName "yatm-medved-server.dll" `
    -SearchRoots @(
        (Join-Path $Root "YetAnotherTraderMod\bin\$Configuration"),
        (Join-Path $Root "YetAnotherTraderMod\build\bin\$Configuration"),
        (Join-Path $Root "build\bin\$Configuration"),
        (Join-Path $Root "Build\bin\$Configuration")
    )

$ServerOutputDir = Split-Path $ServerDll.FullName -Parent
Write-Host "[Package] Server output: $ServerOutputDir"
Copy-Item (Join-Path $ServerOutputDir "*") $ServerInstallDir -Recurse -Force

Write-Host "[Package] Copying server source data, if present..."
$ServerSource = Join-Path $Root "YetAnotherTraderMod"
$ServerDataNames = @(
    "package.json",
    "config.jsonc",
    "config.json",
    "config",
    "db",
    "data",
    "bundles",
    "res",
    "locales",
    "assets"
)

foreach ($Name in $ServerDataNames) {
    $Source = Join-Path $ServerSource $Name
    if (Test-Path $Source) {
        Copy-Item $Source $ServerInstallDir -Recurse -Force
    }
}

foreach ($Doc in @("README.md", "LICENSE", "LICENSE.md", "CHANGELOG.md")) {
    $Source = Join-Path $Root $Doc
    if (Test-Path $Source) {
        Copy-Item $Source $StageRoot -Force
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path $ZipPath) | Out-Null
if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Write-Host "[Package] Creating zip: $ZipPath"
Compress-Archive -Path (Join-Path $StageRoot "*") -DestinationPath $ZipPath -Force

Write-Host "[Package] Done. Zip layout:"
Write-Host "  BepInEx\plugins\$ModName\"
Write-Host "  BepInEx\patchers\$ModName\"
Write-Host "  $ServerModRoot\$ServerModFolderName\"
Write-Host "[Package] Install by extracting $ZipPath into your SPT root folder."
