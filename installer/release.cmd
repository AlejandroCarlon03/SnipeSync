@echo off
rem Builds the installer and publishes it as a GitHub release, which every installed copy offers as an
rem update (see dashboard\SnipeSync.Dashboard\Updater.cs).
rem   installer\release.cmd 1.0.1
rem   installer\release.cmd 1.0.1 "Fixes the license sync for shared mailboxes"
rem Needs everything build.cmd needs, plus the GitHub CLI signed in with push access (gh auth login).
setlocal
set "VERSION=%~1"
if "%VERSION%"=="" (
  echo Usage: installer\release.cmd ^<version^> ["release notes"]
  exit /b 1
)
set "NOTES=%~2"
set "SETUP=%~dp0output\SnipeSync-Setup-%VERSION%.exe"

gh auth status >nul 2>&1 || (
  echo The GitHub CLI isn't signed in. Run: gh auth login
  exit /b 1
)
gh release view "v%VERSION%" >nul 2>&1 && (
  echo Release v%VERSION% already exists. Pick a higher version; installed copies only update upward.
  exit /b 1
)

call "%~dp0build.cmd" %VERSION% || exit /b 1

rem The dashboard downloads the asset named SnipeSync-Setup-<version>.exe from the release tagged
rem v<version>, so keep both names as they are. The tag goes on the commit you built from, so push it first.
for /f %%C in ('git -C "%~dp0.." rev-parse HEAD') do set "COMMIT=%%C"
if defined NOTES (
  gh release create "v%VERSION%" "%SETUP%" --target %COMMIT% --title "SnipeSync %VERSION%" --notes "%NOTES%" || exit /b 1
) else (
  gh release create "v%VERSION%" "%SETUP%" --target %COMMIT% --title "SnipeSync %VERSION%" --generate-notes || exit /b 1
)
echo.
echo Published v%VERSION%. Installed copies will offer it the next time the dashboard is opened.
