<#
.SYNOPSIS
    Gera o executável portátil do Peripheral Asset Tracker para Windows 10/11 (x64).

.DESCRIPTION
    1. Executa os testes automatizados (use -SkipTests para pular).
    2. Publica o aplicativo desktop como um único .exe self-contained:
       o usuário final não precisa instalar .NET, Python ou Node.js.
    3. Grava em artifacts\release\:
         Peripheral-Asset-Tracker-v<versão>.exe
         Peripheral-Asset-Tracker-v<versão>.exe.sha256

    A versão vem de <Version> em Directory.Build.props.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1
#>
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\PeripheralAssetTracker.Desktop\PeripheralAssetTracker.Desktop.csproj'
$publishDir = Join-Path $root 'artifacts\publish'
$releaseDir = Join-Path $root 'artifacts\release'

$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Versão não encontrada em Directory.Build.props.' }

function Invoke-Step([string]$name, [scriptblock]$command) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE -ne 0) { throw "Falha na etapa: $name (código $LASTEXITCODE)." }
}

if (-not $SkipTests) {
    Invoke-Step 'Testes' { dotnet test (Join-Path $root 'tests\PeripheralAssetTracker.Tests') -c Release --nologo -v q }
}

Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
Invoke-Step "Publicação v$version (win-x64, arquivo único)" {
    dotnet publish $project -c Release -r win-x64 --self-contained true --nologo -v q `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publishDir
}

New-Item -ItemType Directory -Force $releaseDir | Out-Null
$exe = Join-Path $releaseDir "Peripheral-Asset-Tracker-v$version.exe"
Copy-Item (Join-Path $publishDir 'PeripheralAssetTracker.exe') $exe -Force

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $exe -Leaf)" | Set-Content "$exe.sha256" -Encoding ascii

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ''
Write-Host "Executável: $exe ($sizeMb MB)" -ForegroundColor Green
Write-Host "SHA-256:    $hash"
