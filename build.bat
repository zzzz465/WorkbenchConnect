@echo off
setlocal
echo Building WorkbenchConnect mod...
if "%CONFIGURATION%"=="" set CONFIGURATION=Release
dotnet build Source\WorkbenchConnect\WorkbenchConnect.csproj --configuration %CONFIGURATION%
if %ERRORLEVEL% EQU 0 (
    echo Build successful! DLL created in 1.7\Assemblies folder.
) else (
    echo Build failed!
    pause
    exit /b 1
)
pause
