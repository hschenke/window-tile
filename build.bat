@echo off
setlocal
echo Compiling WindowTileHotkeys.exe...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize+ /out:"%~dp0WindowTileHotkeys.exe" /reference:System.Windows.Forms.dll,System.Drawing.dll "%~dp0WindowTileHotkeys.cs"
if %ERRORLEVEL% equ 0 (
    echo Compilation successful!
) else (
    echo Compilation failed.
    pause
)
