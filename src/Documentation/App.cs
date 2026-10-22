using System;
using System.Linq;
using Autodesk.Revit.UI;

namespace RevitSleeveDocumentation
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "BIM TOOLS";
            const string panelName = "Sleeve Tools";

            try { application.CreateRibbonTab(tabName); } catch { }

            // Reuse panel if Creation add-in already created it
            RibbonPanel panel = application.GetRibbonPanels(tabName)
                .FirstOrDefault(p => p.Name == panelName);
            if (panel == null)
                panel = application.CreateRibbonPanel(tabName, panelName);

            string path = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var btn = new PushButtonData(
                "cmdDocSleeves",
                "Document\nSleeves",
                path,
                "RevitSleeveDocumentation.Command");
            btn.ToolTip = "Generate Sleeve Documentation";
            panel.AddItem(btn);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }
}
