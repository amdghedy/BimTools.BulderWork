using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitSleeveAddin
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                var win = new UI.CreateSleevesWindow();
                var helper = new WindowInteropHelper(win);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

                win.ShowDialog();
                if (!win.Confirmed)
                    return Result.Cancelled;

                double clearanceFt = win.ClearanceMm / 304.8;
                SleeveService.Execute(doc, uidoc, clearanceFt, win.ProcessLinkedModels,
                    win.ProcessWalls, win.ProcessFloors);
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
