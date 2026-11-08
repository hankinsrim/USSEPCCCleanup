<#
.SYNOPSIS
    Compile the ccfree tool, build USSEP CC Cleanup.esp and the dummy masters from an MO2 profile, and stage them in .\dist.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Instance C:\Modding\MO2 -ProfileName Default
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Instance C:\Modding\MO2 -ProfileName Default -Zip
#>
param(
    [Parameter(Mandatory)] [string] $Instance,
    [Parameter(Mandatory)] [string] $ProfileName,
    [switch] $Zip
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$version = '1.0'
$ussepVersion = '4.3.9c'

$installer = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
$vs = & "$installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if (-not $vs) { throw 'Visual Studio with MSBuild was not found.' }
$csc = "$vs\MSBuild\Current\Bin\Roslyn\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe was not found at $csc" }

New-Item -ItemType Directory -Force build | Out-Null
& $csc -nologo -langversion:latest -out:build\ccfree.exe tool\*.cs
if ($LASTEXITCODE) { throw 'Compiling ccfree failed' }

Remove-Item build\out -Recurse -Force -ErrorAction SilentlyContinue
& build\ccfree.exe build $Instance $ProfileName build\out
if ($LASTEXITCODE) { throw 'ccfree build failed' }

Remove-Item dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force dist | Out-Null
Copy-Item 'build\out\CC Dummy Masters\*', 'build\out\USSEP CC Cleanup\*' dist

Write-Host "`nMod folder ready: $PSScriptRoot\dist (build log: build\out\build-log.txt)"
Get-ChildItem dist -File | ForEach-Object { Write-Host "  $($_.Name)" }

if ($Zip) {
    # MO2 reads the mod version from fomod\info.xml, otherwise it falls back to the file date.
    $stage = "$PSScriptRoot\build\package"
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force "$stage\fomod" | Out-Null
    Copy-Item dist\* $stage
    Copy-Item fomod\ModuleConfig.xml "$stage\fomod"
    Copy-Item LICENSE "$stage\LICENSE.txt"
    @"
USSEP CC Cleanup $version, for USSEP $ussepVersion
Copyright (c) 2026 hankinsrim, released under the MIT License (LICENSE.txt).

Load USSEP CC Cleanup.esp directly after USSEP. Do not install the real free Creation Club
plugins alongside the dummy masters.

Source code: https://github.com/hankinsrim/USSEPCCCleanup
"@ | Out-File "$stage\README.txt" -Encoding utf8
    @"
<?xml version="1.0" encoding="UTF-8"?>
<fomod>
	<Name>USSEP CC Cleanup</Name>
	<Author>hankinsrim</Author>
	<Version>$version</Version>
	<Website>https://github.com/hankinsrim/USSEPCCCleanup</Website>
	<Description>Run USSEP without the free Creation Club content: dummy masters plus a cleanup plugin for USSEP $ussepVersion.</Description>
</fomod>
"@ | Out-File "$stage\fomod\info.xml" -Encoding utf8
    $zipPath = "$PSScriptRoot\build\USSEPCCCleanup-$version.zip"
    Compress-Archive -Path "$stage\*" -DestinationPath $zipPath -Force
    Write-Host "Zip: $zipPath"
}
