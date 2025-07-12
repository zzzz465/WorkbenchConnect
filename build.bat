@echo off
echo Building WorkbenchConnect mod...
cd Source\WorkbenchConnect
dotnet build --configuration Release
if %ERRORLEVEL% EQU 0 (
    echo Build successful! DLL created in Assemblies folder.
) else (
    echo Build failed!
    pause
    exit /b 1
)
pause