# Revit Sleeve Tools 2020 – Original Logic + Professional WPF UI

- **Placement logic** = exact original from your working native DLLs (unchanged)
- **UI** = professional WPF dialog (clearance + linked models)

## Build

1. Open `RevitSleeveTools_2020_WPF.sln` in Visual Studio
2. If Revit is not in `C:\Program Files\Autodesk\Revit 2020`, edit `RevitPath` in both `.csproj` files
3. Build (Release | Any CPU is fine; PlatformTarget is x64)
4. Copy DLLs + `SleeveTools.addin` to:
   `%APPDATA%\Autodesk\Revit\Addins\2020\`

## Use

1. Load families `WallOpening_Round` and `WallOpening_Rectangular`
2. Open a view showing MEP + walls
3. **BIM TOOLS → Sleeve Tools → Create Sleeves**
4. Choose clearance → Create Sleeves
