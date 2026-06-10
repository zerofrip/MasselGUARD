@echo off
title MasselGUARD -- .NET 10 Desktop Runtime Check
setlocal enabledelayedexpansion

echo.
echo  ====================================
echo       MasselGUARD
echo       .NET 10 Runtime Check
echo  ====================================
echo.

rem ── Check if dotnet.exe is available ─────────────────────────────────────────
where dotnet >nul 2>&1
if errorlevel 1 (
    echo  [NOT FOUND] dotnet.exe was not found on this machine.
    echo.
    echo  .NET 10 Desktop Runtime is required to run MasselGUARD.
    goto :install_prompt
)

rem ── Check for Microsoft.WindowsDesktop.App 10.x ──────────────────────────────
set FOUND=0
for /f "tokens=2" %%v in ('dotnet --list-runtimes 2^>nul ^| findstr /i "Microsoft.WindowsDesktop.App 10\."') do (
    set FOUND=1
    set RT_VER=%%v
)

if "!FOUND!"=="1" (
    echo  [OK] .NET 10 Desktop Runtime is installed.
    echo       Version: !RT_VER!
    echo.
    echo  MasselGUARD should start without issues.
    echo.
    pause
    exit /b 0
)

rem ── Not installed ─────────────────────────────────────────────────────────────
echo  [NOT FOUND] .NET 10 Desktop Runtime is not installed.
echo.
echo  MasselGUARD requires:
echo    Microsoft.WindowsDesktop.App 10.x (x64)
echo.

:install_prompt
echo  Would you like to open the download page now?
echo.
echo    [1] Yes -- open https://dotnet.microsoft.com/download/dotnet/10.0
echo    [2] No  -- exit
echo.
set /p CHOICE="  Enter choice (1 or 2): "

if "!CHOICE!"=="1" (
    echo.
    echo  Opening download page...
    start "" "https://dotnet.microsoft.com/download/dotnet/10.0"
    echo.
    echo  Download and run the installer, then start MasselGUARD.exe.
    echo.
) else (
    echo.
    echo  No changes made.
    echo.
)

pause
exit /b 0
