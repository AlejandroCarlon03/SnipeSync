@echo off
rem Builds installer\output\SnipeSync-Setup-<version>.exe.
rem   installer\build.cmd            version 1.0.0
rem   installer\build.cmd 1.2.0      explicit version
rem Needs the .NET 9 SDK, Node (for the SPA build) and Inno Setup 6 (winget install JRSoftware.InnoSetup).
setlocal
set "ROOT=%~dp0.."
set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=1.0.0"
set "PUBLISH=%ROOT%\artifacts\publish"

rem Start clean so files dropped from the app don't linger in the installer.
if exist "%PUBLISH%" rmdir /s /q "%PUBLISH%"

rem Self-contained, so the target machine needs no .NET runtime.
dotnet publish "%ROOT%\dashboard\SnipeSync.Dashboard\SnipeSync.Dashboard.csproj" -c Release -r win-x64 ^
  --self-contained true -p:Version=%VERSION% -o "%PUBLISH%" || exit /b 1

set "ISCC="
for %%P in ("%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe") do (
  if exist %%P set "ISCC=%%~P"
)
if not defined ISCC (
  echo Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup
  exit /b 1
)

"%ISCC%" /Q /DAppVersion=%VERSION% "%~dp0SnipeSync.iss" || exit /b 1
echo.
echo Built %~dp0output\SnipeSync-Setup-%VERSION%.exe
