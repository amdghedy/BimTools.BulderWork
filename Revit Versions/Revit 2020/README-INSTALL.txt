Revit Sleeve Tools - Revit 2020
==================================
Files:
- RevitSleeveAddin.dll
- RevitSleeveDocumentation.dll
- RevitSleeveTools-Create.addin (template; installer writes the valid local assembly path)
- RevitSleeveTools-Documentation.addin (template; installer writes the valid local assembly path)
- Install-RevitSleeveTools.ps1

Installation:
1. Close Revit.
2. Extract this artifact to a local folder.
3. Open PowerShell in that folder and run:
   powershell.exe -ExecutionPolicy Bypass -File .\Install-RevitSleeveTools.ps1
4. Start Revit 2020. The installer creates both .addin manifests in:
   %APPDATA%\Autodesk\Revit\Addins\2020

The creation command requires the WallOpening_Round and WallOpening_Rectangular
families to be loaded in the project. Build artifacts are compiled for Revit 2020.
