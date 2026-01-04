# Installer build (Inno Setup)

This repository includes an Inno Setup script to build a classic Windows installer (`setup.exe`) for the WPF app.

## Requirements
- Inno Setup 6 (locally) OR GitHub Actions workflow (CI)
- .NET SDK 8.x

## Build locally
1. Publish the app (framework-dependent, win-x64):
   - `dotnet publish .\PhotoSorterApp\PhotoSorterApp.csproj -c Release -r win-x64 --self-contained false -o .\artifacts\publish`
2. Compile installer:
   - Open `installer/PhotoSorter.iss` in Inno Setup and click **Compile**
   - Or via CLI:
     - `ISCC.exe installer\PhotoSorter.iss /DAppVersion=1.0.2 /DPublishDir="%CD%\artifacts\publish" /O"%CD%\artifacts\installer"`

## Runtime requirement
Installer checks for **.NET 8 Desktop Runtime (x64)** and opens the official download page if it is missing.

## CI / GitHub Actions
Workflow: `.github/workflows/build-installer.yml`

Triggers:
- manual (`workflow_dispatch`)
- on tag push like `v1.0.3`

Artifacts:
- `setup.exe` uploaded as workflow artifact and attached to the GitHub Release for tag builds.
