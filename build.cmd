@echo off
rem Builds dist\KeyboardToggler-Setup.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll"
if not exist build mkdir build
if not exist dist mkdir dist

rem 1. App icon
"%CSC%" /nologo /out:build\MakeIcon.exe /r:System.Drawing.dll tools\MakeIcon.cs || exit /b 1
build\MakeIcon.exe build\app.ico || exit /b 1

rem 2. The tray app
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /win32icon:build\app.ico %REFS% ^
  /out:build\KeyboardToggler.exe src\*.cs || exit /b 1

rem 3. The installer, with the app and the Interception driver files embedded
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /win32icon:build\app.ico /win32manifest:setup\setup.manifest %REFS% ^
  /resource:build\KeyboardToggler.exe,KeyboardToggler.exe ^
  /resource:vendor\interception.dll,interception.dll ^
  /resource:vendor\install-interception.exe,install-interception.exe ^
  /resource:vendor\Interception-LGPL-3.0.txt,Interception-LGPL-3.0.txt ^
  /out:dist\KeyboardToggler-Setup.exe setup\Setup.cs || exit /b 1

echo Built dist\KeyboardToggler-Setup.exe
