using System;
using System.Linq;
using Autodesk.Revit.UI;

namespace RevitSleeveAddin
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "BIM TOOLS";
            const string panelName = "Sleeve Tools";

            try { application.CreateRibbonTab(tabName); } catch { }

            RibbonPanel panel = application.GetRibbonPanels(tabName)
                .FirstOrDefault(p => p.Name == panelName);
            if (panel == null)
                panel = application.CreateRibbonPanel(tabName, panelName);

            string path = System.Reflection.Assembly.GetExecutingAssembly().Location;

            var createBtn = new PushButtonData(
                "cmdCreateSleeves",
                "Create\nSleeves",
                path,
                "RevitSleeveAddin.Command");
            createBtn.ToolTip = "Create sleeves for MEP elements passing through walls and floors.";
            createBtn.LongDescription =
                "Each sleeve is linked to its source pipe/duct/tray so it can be updated when MEP moves.";

            var syncBtn = new PushButtonData(
                "cmdSyncSleeves",
                "Sync\nSleeves",
                path,
                "RevitSleeveAddin.SyncSleevesCommand");
            syncBtn.ToolTip = "Move sleeves to match current MEP positions (linked or same file).";
            syncBtn.LongDescription =
                "After pipes/ducts/trays move in the MEP model, open the architecture/structure file and run Sync Sleeves.\n" +
                "Sleeves that were created by this tool will move to the new intersection points.";

            panel.AddItem(createBtn);
            panel.AddItem(syncBtn);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }
}
