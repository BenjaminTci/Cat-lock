# Compile CatLock.exe avec le compilateur C# fourni avec Windows (.NET Framework 4).
# Usage : clic droit > "Executer avec PowerShell", ou :  powershell -ExecutionPolicy Bypass -File build.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw "csc.exe introuvable (.NET Framework 4 requis)." }

$src = Join-Path $root 'src\CatLock.cs'
$ico = Join-Path $root 'src\CatLock.ico'
$bin = Join-Path $root 'bin'
$exe = Join-Path $bin 'CatLock.exe'
New-Item -ItemType Directory -Force $bin | Out-Null

# L'exe ne peut pas etre remplace s'il tourne : on l'arrete.
Get-Process CatLock -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

$common = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/codepage:65001',
            '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')

# 1re passe sans icone, pour generer l'icone avec le code de dessin de l'application.
& $csc @common "/out:$exe" $src
if ($LASTEXITCODE -ne 0) { throw "Echec de la compilation." }
Start-Process -FilePath $exe -ArgumentList @('--export-icon', "`"$ico`"") -Wait

# 2e passe avec l'icone.
& $csc @common "/out:$exe" "/win32icon:$ico" $src
if ($LASTEXITCODE -ne 0) { throw "Echec de la compilation." }

Write-Host "OK -> $exe" -ForegroundColor Green
