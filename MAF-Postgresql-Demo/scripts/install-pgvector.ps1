#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Builds pgvector from its official source and installs it into PostgreSQL on Windows.

.DESCRIPTION
    Clones the tagged pgvector release into a temporary folder, compiles it with the Visual Studio
    C++ tools, and copies the result into the PostgreSQL folder. It does not restart PostgreSQL,
    change its configuration, or touch any database.

    Run it from an elevated PowerShell window, because it writes to C:\Program Files.
#>
param(
    [string] $PgRoot = 'C:\Program Files\PostgreSQL\18',
    [string] $Version = 'v0.8.7'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path "$PgRoot\include\server\postgres.h")) {
    throw "PostgreSQL development headers not found under '$PgRoot'. Pass the right folder with -PgRoot."
}

$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) {
    throw 'Visual Studio C++ build tools not found. See README step 2.'
}

$vcvars = Join-Path $vsPath 'VC\Auxiliary\Build\vcvars64.bat'
$work = Join-Path $env:TEMP "pgvector-build-$([guid]::NewGuid().ToString('N'))"
$source = Join-Path $work 'pgvector'
$buildScript = Join-Path $work 'build.cmd'

New-Item -ItemType Directory -Path $work | Out-Null

try {
    Write-Host "Cloning pgvector $Version..."
    git clone --quiet --depth 1 --branch $Version https://github.com/pgvector/pgvector.git $source
    if ($LASTEXITCODE -ne 0) { throw 'git clone failed.' }

    # A .cmd file keeps the quoting simple, and vcvars only works inside cmd.exe.
    Set-Content -Path $buildScript -Encoding ascii -Value @"
@echo off
call "$vcvars" >nul || exit /b 1
set "PGROOT=$PgRoot"
cd /d "$source" || exit /b 1
nmake /nologo /F Makefile.win || exit /b 1
nmake /nologo /F Makefile.win install || exit /b 1
"@

    Write-Host "Building and installing into $PgRoot..."
    & cmd.exe /c $buildScript
    if ($LASTEXITCODE -ne 0) { throw 'The pgvector build failed. See the compiler output above.' }

    if (-not (Test-Path "$PgRoot\lib\vector.dll") -or -not (Test-Path "$PgRoot\share\extension\vector.control")) {
        throw 'The build finished, but vector.dll or vector.control is missing.'
    }

    Write-Host "pgvector $Version is installed. No restart is needed." -ForegroundColor Green
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
