# Compile CatLock.exe et son installeur CatLockSetup.exe avec le compilateur C# fourni avec
# Windows (.NET Framework 4).
# Usage : clic droit > "Executer avec PowerShell", ou :  powershell -ExecutionPolicy Bypass -File build.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw "csc.exe introuvable (.NET Framework 4 requis)." }

$src      = Join-Path $root 'src'
$appSrc   = @((Join-Path $src 'CatLock.cs'), (Join-Path $src 'InstallInfo.cs'))
$setupSrc = @((Join-Path $src 'Setup.cs'), (Join-Path $src 'InstallInfo.cs'))
$ico      = Join-Path $src 'CatLock.ico'
$manifest = Join-Path $src 'setup.manifest'
$bin      = Join-Path $root 'bin'
$exe      = Join-Path $bin 'CatLock.exe'
$setup    = Join-Path $bin 'CatLockSetup.exe'
New-Item -ItemType Directory -Force $bin | Out-Null

# L'exe ne peut pas etre remplace s'il tourne : on arrete la copie de developpement.
Get-Process CatLock -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force
Start-Sleep -Milliseconds 300

$common = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/codepage:65001',
            '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')

# 1re passe sans icone, pour generer l'icone avec le code de dessin de l'application.
& $csc @common "/out:$exe" @appSrc
if ($LASTEXITCODE -ne 0) { throw "Echec de la compilation de CatLock." }
Start-Process -FilePath $exe -ArgumentList @('--export-icon', "`"$ico`"") -Wait

# 2e passe avec l'icone.
& $csc @common "/out:$exe" "/win32icon:$ico" @appSrc
if ($LASTEXITCODE -ne 0) { throw "Echec de la compilation de CatLock." }

# Installeur : embarque CatLock.exe et l'icone.
& $csc @common "/out:$setup" "/win32icon:$ico" "/win32manifest:$manifest" `
    "/resource:$exe,CatLock.exe" "/resource:$ico,CatLock.ico" @setupSrc
if ($LASTEXITCODE -ne 0) { throw "Echec de la compilation de l'installeur." }

Write-Host "OK -> $exe" -ForegroundColor Green
Write-Host "OK -> $setup" -ForegroundColor Green
