@echo off
setlocal

set PROJECT_DIR=%~dp0
set PUBLISH_DIR=%PROJECT_DIR%bin\publish

echo [1/3] Cleaning previous publish output...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

echo [2/3] Building solution...
dotnet build "%PROJECT_DIR%Macronic.csproj" -c Release -r win-x64
if errorlevel 1 (
    echo BUILD FAILED.
    pause
    exit /b 1
)

echo [3/3] Publishing single-file exe...
dotnet publish "%PROJECT_DIR%Macronic.csproj" -c Release -r win-x64 -o "%PUBLISH_DIR%" ^
  --no-build ^
  /p:PublishSingleFile=true ^
  /p:SelfContained=false ^
  /p:PublishReadyToRun=false
if errorlevel 1 (
    echo PUBLISH FAILED.
    pause
    exit /b 1
)

echo.
echo Done. Output:
echo   %PUBLISH_DIR%\Macronic.exe
echo.
pause
endlocal
