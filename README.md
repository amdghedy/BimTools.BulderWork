# Revit Sleeve Tools

Automated sleeve and wall/floor opening creation for Revit **2020–2027**, with a WPF interface and a separate sleeve documentation command.

## Features

- Detects intersections between MEP curves and host walls or floors in the active view.
- Supports MEP elements from linked Revit models.
- Creates round and rectangular openings using the configured families.
- Configurable clearance (default: 2 mm).
- Skips nearby existing openings and merges closely spaced round sleeves on the same host.
- Includes a separate command for processing sleeve documentation.
- CI builds each supported Revit version independently and publishes a version-specific install package.

## Compatibility

| Revit version | Target framework |
|---|---|
| 2020–2024 | .NET Framework 4.8 (`net48`) |
| 2025–2026 | .NET 8 for Windows |
| 2027 | .NET 10 for Windows |

Each artifact is compiled against that Revit version's API. **Do not mix DLLs between Revit versions.**

## Download compiled DLLs and add-in installer

1. Open the repository's [Actions builds](https://github.com/amdghedy/BimTools.BulderWork/actions/workflows/build-revit-matrix.yml).
2. Select the latest successful workflow run.
3. Under **Artifacts**, download `RevitSleeveTools-Revit-2020`, `RevitSleeveTools-Revit-2021`, …, or the package matching your installed Revit version.
4. Extract the artifact to a local folder.
5. Close Revit and run the included `Install-RevitSleeveTools.ps1` script:
   ```powershell
   powershell.exe -ExecutionPolicy Bypass -File .\Install-RevitSleeveTools.ps1
   ```
6. Reopen the matching Revit version. The installer copies both DLLs and creates two `.addin` manifests under:
   `%APPDATA%\Autodesk\Revit\Addins\<version>\`

The package includes:
- `RevitSleeveAddin.dll` — Create Sleeves command and WPF interface.
- `RevitSleeveDocumentation.dll` — sleeve documentation command.
- `Install-RevitSleeveTools.ps1` — copies the matching DLLs and writes valid manifests with the current user's absolute assembly paths.
- `README-INSTALL.txt` — version-specific installation notes.

## Using Create Sleeves

1. Load the `WallOpening_Round` and `WallOpening_Rectangular` families into the project.
2. Open a view containing the relevant MEP elements and host walls/floors.
3. Run **BIM TOOLS → Sleeve Tools → Create Sleeves**.
4. Choose the clearance and whether to process linked models, walls, and floors.
5. Review the resulting openings and coordinate them with the project team.

## Build from source

Open `RevitSleeveTools_WPF.sln` in Visual Studio, or build a specific version from a Developer PowerShell prompt:

```powershell
dotnet restore .\RevitSleeveTools_WPF.sln -p:RevitVersion=2026
dotnet build .\RevitSleeveTools_WPF.sln --configuration Release --no-restore -p:RevitVersion=2026
```

Replace `2026` with one of `2020` through `2027`. The GitHub Actions matrix builds all eight versions and stores each successful package as a separate artifact.

## Important notes

- Close Revit before installing or replacing DLLs.
- Use the artifact for the exact Revit version you run.
- Test on a copy of the model first and verify family parameters, placement, and resulting openings before production use.
- The CI build verifies compilation; it does not replace in-Revit functional testing.
