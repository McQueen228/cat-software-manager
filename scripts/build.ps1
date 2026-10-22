param(
    [string]$Version = "1.0.0",
    [switch]$SkipInstaller,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\CatSoftwareManager\CatSoftwareManager.csproj"
$publishDir = Join-Path $root "artifacts\publish"
$installerDir = Join-Path $root "artifacts\installer"
$portableDir = Join-Path $root "artifacts\portable"

if (-not $SkipTests) {
    dotnet run --project (Join-Path $root "tests\CatSoftwareManager.SmokeTests\CatSoftwareManager.SmokeTests.csproj") -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Smoke tests failed."
    }
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $installerDir -Force | Out-Null
New-Item -ItemType Directory -Path $portableDir -Force | Out-Null

$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts"))
foreach ($target in @($publishDir, $installerDir, $portableDir)) {
    $resolvedTarget = [System.IO.Path]::GetFullPath($target)
    if (-not $resolvedTarget.StartsWith(
            $artifactsRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside the artifacts directory: $resolvedTarget"
    }
}

Get-ChildItem -LiteralPath $publishDir -Force | Remove-Item -Recurse -Force
Get-ChildItem -LiteralPath $installerDir -Force | Remove-Item -Recurse -Force
Get-ChildItem -LiteralPath $portableDir -Force | Remove-Item -Recurse -Force

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -p:FileVersion="$Version.0" `
    -p:AssemblyVersion="$Version.0" `
    -p:InformationalVersion=$Version `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$portableArchive = Join-Path $root "artifacts\CatSoftwareManager-Portable-v$Version-x64.zip"
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $portableArchive -CompressionLevel Optimal -Force

if (-not $SkipInstaller) {
    $compilerCandidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    $compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $compiler) {
        throw "Inno Setup 6 is required. Install it with: winget install JRSoftware.InnoSetup"
    }

    $publishRelative = "..\artifacts\publish"
    & $compiler "/DMyAppVersion=$Version" "/DPublishDir=$publishRelative" (Join-Path $root "installer\CatSoftwareManager.iss")
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed."
    }
}

Write-Host ""
Write-Host "Build complete."
Write-Host "Portable: $portableArchive"
if (-not $SkipInstaller) {
    Write-Host "Installer: $(Join-Path $installerDir "CatSoftwareManager-Setup-v$Version-x64.exe")"
}
