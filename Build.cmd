@echo off

rem ---------------------------------------------------------------------------

rem  Build.cmd — dual output: live console + .\build.log (ANSI colors in log).

rem  Log is deleted at the start of each run; header/footer include timestamps.

rem ---------------------------------------------------------------------------

if /I not "%~1"=="__BUILD_LOGGED__" (

  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0developer_tools\BuildLog.ps1" -BatchPath "%~f0" %*

  exit /b %ERRORLEVEL%

)

shift



setlocal enabledelayedexpansion

cd /d "%~dp0"



if not defined NUGET_PACKAGES set "NUGET_PACKAGES=%USERPROFILE%\.nuget\packages"

set "PROJECT_FILE=src\SnapVox\SnapVox.csproj"

set "PUBLISH_AOT_ARGS=-p:PublishAot=true -p:TreatWarningsAsErrors=true"

set "DOTNET_LOG_ARGS=-consoleLoggerParameters:Summary;NoItemAndPropertyList"



echo ###########################################################

echo PURGING PREVIOUS BUILD ARTIFACTS...

echo ###########################################################

if exist ".\compiled" rd /s /q ".\compiled"

mkdir ".\compiled"

call :CLEAN_ALL



echo.

echo ###########################################################

echo BUILDING BRANCH 1: Native

echo ###########################################################

call :BUILD_STANDALONE "Branch1" "SnapVox" "USE_TESSERACT=false"

if errorlevel 1 exit /b 1



echo.

echo ###########################################################

echo BUILDING BRANCH 2: Tesseract (Standard Deployment)

echo ###########################################################

call :BUILD_STANDALONE "Branch2" "SnapVox_tesseract" "USE_TESSERACT=true"

if errorlevel 1 exit /b 1



call :VALIDATE_COMPILED_OUTPUT

if errorlevel 1 exit /b 1



echo.

echo ###########################################################

echo SUCCESS: Build completed successfully.

echo.

echo Branch 1 (Native): .\compiled\SnapVox.exe

echo Branch 2 (Tesseract):   .\compiled\SnapVox_tesseract.exe

echo Log file:               .\build.log  (first line: OK / WARN / FAIL)

echo ###########################################################

exit /b 0



:BUILD_STANDALONE

set "BRANCH_NAME=%~1"

set "OUTPUT_NAME=%~2"

set "EXTRA_ARGS=%~3"

set "STAGING_DIR=.\obj\StandaloneTemp\%BRANCH_NAME%_staging"

set "FINAL_DIR=.\obj\StandaloneTemp\%BRANCH_NAME%_final"



echo [%BRANCH_NAME%] 1. Purging old temp folders...

if exist "%STAGING_DIR%" rd /s /q "%STAGING_DIR%"

if exist "%FINAL_DIR%" rd /s /q "%FINAL_DIR%"

if exist "src\SnapVox\payload.zip" del /f /q "src\SnapVox\payload.zip"



echo [%BRANCH_NAME%] 2. Publishing raw payload to staging...

dotnet publish "%PROJECT_FILE%" -c Release -r win-x64 %PUBLISH_AOT_ARGS% -p:%EXTRA_ARGS% -o "%STAGING_DIR%" %DOTNET_LOG_ARGS%

if errorlevel 1 exit /b 1



echo [%BRANCH_NAME%] 3. Zipping payload...

powershell -NoProfile -Command "Compress-Archive -Path '%STAGING_DIR%\*' -DestinationPath 'src\SnapVox\payload.zip' -Force"



echo [%BRANCH_NAME%] 4. Publishing standalone installer...

dotnet publish "%PROJECT_FILE%" -c Release -r win-x64 %PUBLISH_AOT_ARGS% -p:%EXTRA_ARGS% -o "%FINAL_DIR%" %DOTNET_LOG_ARGS%

if errorlevel 1 exit /b 1



echo [%BRANCH_NAME%] 5. Moving final EXE to compiled folder...

if not exist "%FINAL_DIR%\SnapVox.exe" (

  echo ERROR: Expected standalone EXE was not produced in %FINAL_DIR%

  exit /b 1

)

move /y "%FINAL_DIR%\SnapVox.exe" ".\compiled\%OUTPUT_NAME%.exe"

if errorlevel 1 exit /b 1

call :PURGE_COMPILED_EXTRAS



echo [%BRANCH_NAME%] 6. Cleaning up temporary artifacts...

if exist "%STAGING_DIR%" rd /s /q "%STAGING_DIR%"

if exist "%FINAL_DIR%" rd /s /q "%FINAL_DIR%"

if exist "src\SnapVox\payload.zip" del /f /q "src\SnapVox\payload.zip"



exit /b 0



:PURGE_COMPILED_EXTRAS

for %%F in (".\compiled\*") do (

  if /I not "%%~xF"==".exe" (

    echo ERROR: Removing disallowed artifact from compiled: %%~fF

    rd /s /q "%%~fF" 2>nul

    del /f /q "%%~fF" 2>nul

    exit /b 1

  )

)

exit /b 0



:VALIDATE_COMPILED_OUTPUT

set "FILE_COUNT=0"

set "INVALID=0"

if not exist ".\compiled\SnapVox.exe" set "INVALID=1"

if not exist ".\compiled\SnapVox_tesseract.exe" set "INVALID=1"

for %%F in (".\compiled\*") do (

  set /a FILE_COUNT+=1

  if /I not "%%~xF"==".exe" set "INVALID=1"

)

if not "!FILE_COUNT!"=="2" set "INVALID=1"

if "!INVALID!"=="1" (

  echo ERROR: .\compiled must contain exactly these two installer EXE files and nothing else:

  echo   SnapVox.exe

  echo   SnapVox_tesseract.exe

  dir /b ".\compiled" 2>nul

  exit /b 1

)

echo Verified .\compiled contains exactly 2 standalone installer EXE files with no DLLs or subfolders.

exit /b 0



:DETECT_NATIVE_AOT

where link.exe >nul 2>&1

if not errorlevel 1 goto DETECT_NATIVE_AOT_OK

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"

if exist "%VSWHERE%" (

  for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2^>nul`) do (

    if exist "%%I\Common7\Tools\VsDevCmd.bat" (

      call "%%I\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul

      where link.exe >nul 2>&1

      if not errorlevel 1 goto DETECT_NATIVE_AOT_OK

    )

  )

)

echo ERROR: Native AOT platform linker (link.exe) not found.

exit /b 1



:DETECT_NATIVE_AOT_OK

echo Native AOT toolchain detected.

exit /b 0



:TERMINATE_PROCESSES

taskkill /F /IM SnapVox.exe /T 2>nul

taskkill /F /IM SnapVox_Cleanup.exe /T 2>nul

dotnet build-server shutdown 2>nul

exit /b 0



:SLEEP_SEC

set /a "_sleep_ping=%~1+1"

ping 127.0.0.1 -n !_sleep_ping! >nul 2>&1

exit /b 0



:CLEAN_ALL

for /d /r . %%d in (bin obj) do @if exist "%%d" rd /s /q "%%d" 2>nul

exit /b 0



:UNLOCK_MAIN_EXE

exit /b 0


