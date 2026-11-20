@echo off
rem Ses Kalkani'ni derler. Windows'ta yerlesik gelen .NET Framework 4 derleyicisi yeterli, ek kurulum gerekmez.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:src\icon.ico /out:dist\SesKalkani.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if errorlevel 1 (echo Derleme basarisiz. & exit /b 1)
echo Hazir: dist\SesKalkani.exe
