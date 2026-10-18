@echo off
echo Building LumaTray...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /win32icon:app.ico /r:System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Management.dll /out:LumaTray.exe *.cs
if %ERRORLEVEL% EQU 0 (
    echo [SUCCESS] LumaTray.exe compiled successfully with embedded icon and metadata!
) else (
    echo [ERROR] Build failed!
)
pause
