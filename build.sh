#!/bin/bash
echo "Building WorkbenchConnect mod..."
cd Source/WorkbenchConnect
dotnet build --configuration Release
if [ $? -eq 0 ]; then
    echo "Build successful! DLL created in Assemblies folder."
else
    echo "Build failed!"
    exit 1
fi