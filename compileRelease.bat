@echo off
setlocal EnableExtensions

rem Build launcher, generate update metadata, and refresh the tracked release feed.
rem GitHub Actions handles tagged public releases automatically.

set "ROOT=%~dp0"
set "HOST_PROJECT=%ROOT%src\FocLauncherHost\FocLauncherHost.csproj"
set "METADATA_PROJECT=%ROOT%tools\MetadataCreator\MetadataCreator.csproj"
set "RELEASE_FEED=%ROOT%releases"
set "METADATA_CREATOR=%ROOT%tools\MetadataCreator\bin\Release\MetadataCreator.exe"
set "RELEASE_OUTPUT=%ROOT%src\FocLauncherHost\bin\Release"
set "RELEASE_SOURCE=%ROOT%artifacts\release-source"
set "RELEASE_SOURCE_BUILD=%ROOT%artifacts\release-source\Release"
set "APP_UPDATER_OUTPUT=%ROOT%src\FocLauncher.AppUpdater\bin\Release"
set "LAUNCHER_OUTPUT=%ROOT%src\FocLauncher\bin\Release"
set "THEMING_OUTPUT=%ROOT%src\FocLauncher.Theming\bin\Release"
set "THREADING_OUTPUT=%ROOT%src\FocLauncher.Threading\bin\Release"
set "UPDATE_METADATA_URL=https://raw.githubusercontent.com/FuriosGuy/EAW-Launcher/master/releases/LauncherUpdateData.xml"
set "UPDATE_FILE_ROOT=https://raw.githubusercontent.com/FuriosGuy/EAW-Launcher/master/releases"
set "BUILD_PLATFORM=AnyCPU"
set "MSBUILD_PATH="

if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" (
    for /f "usebackq delims=" %%I in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do if not defined MSBUILD_PATH set "MSBUILD_PATH=%%I"
)

if not defined MSBUILD_PATH if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD_PATH=%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD_PATH if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD_PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"

if not defined MSBUILD_PATH (
    echo Could not find MSBuild. Install Visual Studio with the .NET desktop build tools.
    exit /b 1
)

pushd "%ROOT%"

echo Select [B] to build or [R] to rebuild the solution.
choice /C BR /N
if errorlevel 2 (set "BUILD_TARGET=Rebuild") else set "BUILD_TARGET=Build"

echo Select [D]ebug or [R]elease configuration.
choice /C DR /N
if errorlevel 2 (set "BUILD_CONFIGURATION=Release") else set "BUILD_CONFIGURATION=Debug"

echo Running %BUILD_TARGET% with %BUILD_CONFIGURATION% using:
echo %MSBUILD_PATH%
if defined EAW_LAUNCHER_VERSION (
    echo Applying launcher version %EAW_LAUNCHER_VERSION%.
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%ROOT%tools\Set-LauncherVersion.ps1" -Version "%EAW_LAUNCHER_VERSION%"
    if errorlevel 1 goto fail
)
echo Restoring NuGet packages.
"%MSBUILD_PATH%" "%HOST_PROJECT%" /t:Restore /p:Configuration=%BUILD_CONFIGURATION% /p:Platform="%BUILD_PLATFORM%" /m:1
if errorlevel 1 goto fail
"%MSBUILD_PATH%" "%METADATA_PROJECT%" /t:Restore /p:Configuration=%BUILD_CONFIGURATION% /p:Platform="%BUILD_PLATFORM%" /m:1
if errorlevel 1 goto fail
echo Building the launcher host and metadata tool (production projects only).
"%MSBUILD_PATH%" "%HOST_PROJECT%" /t:%BUILD_TARGET% /p:Configuration=%BUILD_CONFIGURATION% /p:Platform="%BUILD_PLATFORM%" /m:1
if errorlevel 1 goto fail
"%MSBUILD_PATH%" "%METADATA_PROJECT%" /t:%BUILD_TARGET% /p:Configuration=%BUILD_CONFIGURATION% /p:Platform="%BUILD_PLATFORM%" /m:1
if errorlevel 1 goto fail

if /I not "%BUILD_CONFIGURATION%"=="Release" (
    echo Debug build complete. Release feed generation requires Release configuration.
    goto success
)

if not exist "%METADATA_CREATOR%" (
    echo MetadataCreator.exe not found at:
    echo %METADATA_CREATOR%
    goto fail
)

if not exist "%RELEASE_OUTPUT%" (
    echo Release output not found at:
    echo %RELEASE_OUTPUT%
    goto fail
)

if not exist "%APP_UPDATER_OUTPUT%\EMPIRE AT WAR Launcher Updater.exe" goto missingReleaseFile
if not exist "%LAUNCHER_OUTPUT%\FocLauncher.dll" goto missingReleaseFile
if not exist "%THEMING_OUTPUT%\FocLauncher.Theming.dll" goto missingReleaseFile
if not exist "%THREADING_OUTPUT%\FocLauncher.Threading.dll" goto missingReleaseFile

if exist "%RELEASE_SOURCE%" rmdir /s /q "%RELEASE_SOURCE%"
mkdir "%RELEASE_SOURCE_BUILD%"
xcopy "%RELEASE_OUTPUT%\*" "%RELEASE_SOURCE_BUILD%\" /e /i /y >nul
copy /y "%APP_UPDATER_OUTPUT%\EMPIRE AT WAR Launcher Updater.exe" "%RELEASE_SOURCE_BUILD%\" >nul
copy /y "%LAUNCHER_OUTPUT%\FocLauncher.dll" "%RELEASE_SOURCE_BUILD%\" >nul
copy /y "%THEMING_OUTPUT%\FocLauncher.Theming.dll" "%RELEASE_SOURCE_BUILD%\" >nul
copy /y "%THREADING_OUTPUT%\FocLauncher.Threading.dll" "%RELEASE_SOURCE_BUILD%\" >nul

echo Select release channel: [1] Stable, [2] Beta, [3] Test.
choice /C 123 /N
if errorlevel 3 (set "APPLICATION_TYPE=Test") else if errorlevel 2 (set "APPLICATION_TYPE=Beta") else set "APPLICATION_TYPE=Stable"

echo Select metadata integration: [0] replace, [1] add/replace channel product.
choice /C 01 /N
if errorlevel 2 (set "INTEGRATION_MODE=1") else set "INTEGRATION_MODE=0"

if not exist "%RELEASE_FEED%" mkdir "%RELEASE_FEED%"

"%METADATA_CREATOR%" -o "%RELEASE_FEED%" -b Release -s "%RELEASE_SOURCE%" -f "%UPDATE_METADATA_URL%" -r "%UPDATE_FILE_ROOT%" -t "%APPLICATION_TYPE%" -m %INTEGRATION_MODE% -l "%RELEASE_FEED%"
if errorlevel 1 goto fail

echo Release feed generated in:
echo %RELEASE_FEED%
echo Review generated files, then commit and push the release feed from the repository root.
goto success

:success
popd
exit /b 0

:fail
popd
echo Release operation failed.
exit /b 1

:missingReleaseFile
popd
echo Release staging input is missing. Rebuild the production projects first.
exit /b 1
