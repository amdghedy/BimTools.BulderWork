# Revit Sleeve Tools installer
# Run this script on the target Windows account. Revit must be closed.
$ErrorActionPreference = 'Stop'
$version = '2021'
$target = Join-Path $env:APPDATA "Autodesk/Revit/Addins/$version"
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'RevitSleeveAddin.dll') $target -Force
Copy-Item (Join-Path $PSScriptRoot 'RevitSleeveDocumentation.dll') $target -Force
$creationAssembly = Join-Path $target 'RevitSleeveAddin.dll'
$docAssembly = Join-Path $target 'RevitSleeveDocumentation.dll'
$creation = @'
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Command">
    <Name>Revit Sleeve Tools - Create Sleeves</Name>
    <Assembly>ASSEMBLY_PATH</Assembly>
    <AddInId>D31A3D1A-4C26-4C95-9C5C-7E2D02C0A101</AddInId>
    <FullClassName>RevitSleeveAddin.Command</FullClassName>
    <VendorId>AMDG</VendorId>
    <VendorDescription>BIM Tools - Revit Sleeve Tools</VendorDescription>
  </AddIn>
</RevitAddIns>
'@
$documentation = @'
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Command">
    <Name>Revit Sleeve Tools - Documentation</Name>
    <Assembly>ASSEMBLY_PATH</Assembly>
    <AddInId>D31A3D1A-4C26-4C95-9C5C-7E2D02C0A102</AddInId>
    <FullClassName>RevitSleeveDocumentation.Command</FullClassName>
    <VendorId>AMDG</VendorId>
    <VendorDescription>BIM Tools - Revit Sleeve Tools</VendorDescription>
  </AddIn>
</RevitAddIns>
'@
$creation.Replace('ASSEMBLY_PATH', [System.Security.SecurityElement]::Escape($creationAssembly)) | Set-Content (Join-Path $target 'RevitSleeveTools-Create.addin') -Encoding UTF8
$documentation.Replace('ASSEMBLY_PATH', [System.Security.SecurityElement]::Escape($docAssembly)) | Set-Content (Join-Path $target 'RevitSleeveTools-Documentation.addin') -Encoding UTF8
Write-Host "Installed Revit Sleeve Tools for Revit $version to $target"
