@echo off
setlocal
cd /d "%~dp0.."

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK was not found. Install the .NET 9 SDK and retry.
    exit /b 1
)

echo ========================================
echo             JTG-RGB Build
echo ========================================
dotnet --info
if errorlevel 1 exit /b 1

dotnet restore JTG-RGB.sln
if errorlevel 1 (
    echo [ERROR] Restore failed.
    exit /b 1
)

dotnet build JTG-RGB.sln -c Release --no-restore
if errorlevel 1 (
    echo [ERROR] Build failed.
    exit /b 1
)

echo.
echo [OK] Build completed.
echo Output: src\JTG-RGB.App\bin\Release\net9.0-windows10.0.19041.0\
exit /b 0
