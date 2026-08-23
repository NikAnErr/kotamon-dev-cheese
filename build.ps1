param(
    [string]$GameRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'

$compiler = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Roslyn compiler not found: $compiler"
}

$gameRootResolved = (Resolve-Path -LiteralPath $GameRoot).Path
$source = Join-Path $PSScriptRoot 'KotamonDevCheat.cs'
$outputDirectory = Join-Path $PSScriptRoot 'bin'
$output = Join-Path $outputDirectory 'KotamonDevCheat.compiled.dll'
$pluginDirectory = Join-Path $gameRootResolved 'BepInEx\plugins\KotamonDevCheat'
$temporaryBuildDirectory = Join-Path ([IO.Path]::GetTempPath()) ('KotamonDevCheat-' + [Guid]::NewGuid().ToString('N'))

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
if (-not $SkipInstall) {
    New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
}

$references = @(
    'dotnet\System.Private.CoreLib.dll',
    'dotnet\System.Runtime.dll',
    'dotnet\netstandard.dll',
    'dotnet\System.Collections.dll',
    'dotnet\System.Console.dll',
    'dotnet\System.Linq.dll',
    'dotnet\System.ObjectModel.dll',
    'dotnet\System.Runtime.InteropServices.dll',
    'BepInEx\core\BepInEx.Core.dll',
    'BepInEx\core\BepInEx.Unity.IL2CPP.dll',
    'BepInEx\core\Il2CppInterop.Common.dll',
    'BepInEx\core\Il2CppInterop.Runtime.dll',
    'BepInEx\interop\Il2Cppmscorlib.dll',
    'BepInEx\interop\UnityEngine.CoreModule.dll',
    'BepInEx\interop\UnityEngine.IMGUIModule.dll',
    'BepInEx\interop\UnityEngine.InputLegacyModule.dll',
    'BepInEx\interop\UnityEngine.PhysicsModule.dll',
    'BepInEx\interop\UniTask.dll',
    'BepInEx\interop\Project.dll'
) | ForEach-Object { Join-Path $gameRootResolved $_ }

foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) {
        throw "Reference not found: $reference"
    }
}

New-Item -ItemType Directory -Path $temporaryBuildDirectory -Force | Out-Null
try {
    $temporarySource = Join-Path $temporaryBuildDirectory 'KotamonDevCheat.cs'
    $temporaryOutput = Join-Path $temporaryBuildDirectory 'KotamonDevCheat.compiled.dll'
    Copy-Item -LiteralPath $source -Destination $temporarySource -Force

    $temporaryReferences = foreach ($reference in $references) {
        $temporaryReference = Join-Path $temporaryBuildDirectory ([IO.Path]::GetFileName($reference))
        Copy-Item -LiteralPath $reference -Destination $temporaryReference -Force
        $temporaryReference
    }

    $arguments = @(
        '/nologo',
        '/noconfig',
        '/nostdlib+',
        '/target:library',
        '/langversion:latest',
        '/optimize+',
        '/deterministic+',
        "/out:$temporaryOutput"
    )

    $arguments += $temporaryReferences | ForEach-Object { "/reference:$_" }
    $arguments += $temporarySource

    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Compilation failed with exit code $LASTEXITCODE"
    }

    Copy-Item -LiteralPath $temporaryOutput -Destination $output -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryBuildDirectory) {
        Remove-Item -LiteralPath $temporaryBuildDirectory -Recurse -Force
    }
}

Write-Output "Built: $output"
if (-not $SkipInstall) {
    Copy-Item -LiteralPath $output -Destination (Join-Path $pluginDirectory 'KotamonDevCheat.dll') -Force
    Write-Output "Installed: $(Join-Path $pluginDirectory 'KotamonDevCheat.dll')"
}
