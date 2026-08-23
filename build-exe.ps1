param(
    [string]$GameRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$gameRootResolved = (Resolve-Path -LiteralPath $GameRoot).Path

$pluginBuilder = Join-Path $PSScriptRoot 'build.ps1'
& powershell -NoProfile -ExecutionPolicy Bypass -File $pluginBuilder -GameRoot $gameRootResolved -SkipInstall
if ($LASTEXITCODE -ne 0) {
    throw "Plugin compilation failed with exit code $LASTEXITCODE"
}

$compiler = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$framework = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1'
$source = Join-Path $PSScriptRoot 'Launcher\Program.cs'
$plugin = Join-Path $PSScriptRoot 'bin\KotamonDevCheat.compiled.dll'
$releaseDirectory = Join-Path $PSScriptRoot 'release'
$output = Join-Path $releaseDirectory 'KotamonDevCheat.exe'
$payload = Join-Path $releaseDirectory '.KotamonDevCheat-BepInExPayload.zip'
$dotnetDirectory = Join-Path $gameRootResolved 'dotnet'
$coreDirectory = Join-Path $gameRootResolved 'BepInEx\core'
$unityLibrariesDirectory = Join-Path $gameRootResolved 'BepInEx\unity-libs'
$interopDirectory = Join-Path $gameRootResolved 'BepInEx\interop'
$thirdPartyNotices = Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.txt'
$bepInExLicense = 'C:\Program Files\Git\mingw64\share\licenses\gcc-libs\COPYING.LIB'
$loadOrderPatch = Join-Path $PSScriptRoot 'patch-bepinex-load-order.ps1'
$interopPatch = Join-Path $PSScriptRoot 'patch-unity6-interop.ps1'
$bepInExConfig = Join-Path $PSScriptRoot 'BepInEx.Kotamon.cfg'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Roslyn compiler not found: $compiler"
}
if (-not (Test-Path -LiteralPath $framework)) {
    throw ".NET Framework 4.7.1 reference assemblies not found: $framework"
}
foreach ($required in @($dotnetDirectory, $coreDirectory, $unityLibrariesDirectory, $interopDirectory,
    $thirdPartyNotices, $bepInExLicense, $loadOrderPatch, $interopPatch, $bepInExConfig)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "BepInEx payload source not found: $required"
    }
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Add-FileToArchive(
    [System.IO.Compression.ZipArchive]$Archive,
    [string]$Source,
    [string]$EntryName
) {
    $entry = $Archive.CreateEntry($EntryName, [System.IO.Compression.CompressionLevel]::Optimal)
    $entry.LastWriteTime = (Get-Item -LiteralPath $Source).LastWriteTime
    $input = [System.IO.File]::OpenRead($Source)
    $outputStream = $entry.Open()
    try {
        $input.CopyTo($outputStream)
    }
    finally {
        $outputStream.Dispose()
        $input.Dispose()
    }
}

function Add-DirectoryToArchive(
    [System.IO.Compression.ZipArchive]$Archive,
    [string]$Directory,
    [string]$EntryRoot
) {
    Get-ChildItem -LiteralPath $Directory -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($Directory.Length).TrimStart('\', '/')
        Add-FileToArchive $Archive $_.FullName ($EntryRoot.TrimEnd('/') + '/' + $relative.Replace('\', '/'))
    }
}

if (Test-Path -LiteralPath $payload) {
    Remove-Item -LiteralPath $payload -Force
}

$payloadStream = [System.IO.File]::Open($payload, [System.IO.FileMode]::CreateNew)
$payloadArchive = [System.IO.Compression.ZipArchive]::new(
    $payloadStream,
    [System.IO.Compression.ZipArchiveMode]::Create,
    $false
)
try {
    foreach ($rootFile in @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt')) {
        $sourceFile = Join-Path $gameRootResolved $rootFile
        if (Test-Path -LiteralPath $sourceFile) {
            Add-FileToArchive $payloadArchive $sourceFile $rootFile
        }
    }

    Add-DirectoryToArchive $payloadArchive $dotnetDirectory 'dotnet'
    Add-DirectoryToArchive $payloadArchive $coreDirectory 'BepInEx/core'
    Add-DirectoryToArchive $payloadArchive $unityLibrariesDirectory 'BepInEx/unity-libs'
    Add-FileToArchive $payloadArchive $bepInExConfig 'BepInEx/config/BepInEx.cfg'

    Get-ChildItem -LiteralPath $interopDirectory -Recurse -File |
        Where-Object { $_.Name -notlike '*.kotamon-original' } |
        ForEach-Object {
            $relative = $_.FullName.Substring($interopDirectory.Length).TrimStart('\', '/')
            Add-FileToArchive $payloadArchive $_.FullName ('BepInEx/interop/' + $relative.Replace('\', '/'))
        }

    Add-FileToArchive $payloadArchive $thirdPartyNotices 'BepInEx/THIRD_PARTY_NOTICES-Kotamon.txt'
    Add-FileToArchive $payloadArchive $bepInExLicense 'BepInEx/LICENSE-BepInEx-LGPL-2.1.txt'
    Add-FileToArchive $payloadArchive $loadOrderPatch 'BepInEx/Kotamon-Source/patch-bepinex-load-order.ps1'
    Add-FileToArchive $payloadArchive $interopPatch 'BepInEx/Kotamon-Source/patch-unity6-interop.ps1'
}
finally {
    $payloadArchive.Dispose()
    $payloadStream.Dispose()
}

$references = @(
    'mscorlib.dll',
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.IO.Compression.dll',
    'System.IO.Compression.FileSystem.dll',
    'System.Windows.Forms.dll'
) | ForEach-Object { Join-Path $framework $_ }

$temporaryBuildDirectory = Join-Path ([IO.Path]::GetTempPath()) ('KotamonLauncher-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryBuildDirectory -Force | Out-Null
try {
    $temporarySource = Join-Path $temporaryBuildDirectory 'Program.cs'
    $temporaryPlugin = Join-Path $temporaryBuildDirectory 'KotamonDevCheat.compiled.dll'
    $temporaryPayload = Join-Path $temporaryBuildDirectory 'BepInExPayload.zip'
    $temporaryOutput = Join-Path $temporaryBuildDirectory 'KotamonDevCheat.exe'
    Copy-Item -LiteralPath $source -Destination $temporarySource -Force
    Copy-Item -LiteralPath $plugin -Destination $temporaryPlugin -Force
    Copy-Item -LiteralPath $payload -Destination $temporaryPayload -Force

    $arguments = @(
        '/nologo',
        '/noconfig',
        '/nostdlib+',
        '/target:winexe',
        '/platform:anycpu',
        '/langversion:latest',
        '/optimize+',
        '/deterministic+',
        "/out:$temporaryOutput",
        "/resource:$temporaryPlugin,KotamonDevCheat.EmbeddedPlugin.dll",
        "/resource:$temporaryPayload,KotamonDevCheat.BepInExPayload.zip"
    )
    $arguments += $references | ForEach-Object { "/reference:$_" }
    $arguments += $temporarySource

    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Launcher compilation failed with exit code $LASTEXITCODE"
    }

    Copy-Item -LiteralPath $temporaryOutput -Destination $output -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryBuildDirectory) {
        Remove-Item -LiteralPath $temporaryBuildDirectory -Recurse -Force
    }
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $output).Hash
$payloadSize = (Get-Item -LiteralPath $payload).Length
Remove-Item -LiteralPath $payload -Force
Write-Output "Built portfolio EXE: $output"
Write-Output "Embedded BepInEx payload: $([Math]::Round($payloadSize / 1MB, 2)) MB"
Write-Output "SHA-256: $hash"
