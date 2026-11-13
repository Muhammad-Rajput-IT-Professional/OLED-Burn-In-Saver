@echo off
echo ===================================================
echo   Building OLED Blackout Saver (Native Windows)
echo ===================================================

set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC% (
    echo Error: C# compiler not found at %CSC%
    pause
    exit /b 1
)

%CSC% /target:winexe /out:OLEDSaver.exe /win32icon:app.ico /win32manifest:app.manifest /platform:anycpu /optimize+ Program.cs MainForm.cs Settings.cs DisplayHelper.cs InputWatcher.cs BlackoutForm.cs BlackoutManager.cs TeamsWatcher.cs MediaPlaybackWatcher.cs

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ===================================================
    echo   BUILD SUCCESSFUL: OLEDSaver.exe created!
    echo ===================================================
) else (
    echo.
    echo BUILD FAILED!
)
