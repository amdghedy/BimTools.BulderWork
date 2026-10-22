using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitSleeveAddin
{
    /// <summary>
    /// Moves existing sleeves to match current positions of their linked MEP elements.
    /// Use after MEP moves in a linked model or in the same file.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SyncSleevesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                int updated = SleeveService.SyncLinkedSleeves(doc, out int missing, out int skipped);
                TaskDialog.Show("Sync Sleeves",
                    "Updated: " + updated + "\n" +
                    "MEP not found: " + missing + "\n" +
                    "Skipped (no link data): " + skipped);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", ex.Message);
                return Result.Failed;
            }
        }
    }
}
