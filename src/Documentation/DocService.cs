using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace RevitSleeveDocumentation
{
    /// <summary>
    /// Original documentation service (reconstructed from IL).
    /// Creates section views, dimensions and room annotations for sleeves.
    /// </summary>
    public static class DocService
    {
        private static long GetElementIdValue(ElementId id)
        {
#if REVIT2026_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        private static readonly string[] TargetFamilies =
        {
            "WallOpening_Round",
            "WallOpening_Rectangular"
        };

        public static bool IsSleeve(FamilyInstance fi)
        {
            if (fi?.Symbol?.Family == null) return false;
            string name = fi.Symbol.Family.Name;
            return TargetFamilies.Any(f => name.Equals(f, StringComparison.OrdinalIgnoreCase))
                   || name.IndexOf("Sleeve", StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("WallOpening", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void ProcessSleeves(Document doc, UIDocument uidoc, List<FamilyInstance> sleeves)
        {
            using (Transaction tx = new Transaction(doc, "Generate Sleeve Documentation"))
            {
                tx.Start();

                int index = 1;
                foreach (var sleeve in sleeves)
                {
                    try
                    {
                        string roomName = GetRoomNameAtSleeve(doc, sleeve);
                        CreateProfessionalSection(doc, sleeve, index, roomName);
                        index++;
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("Error", $"Failed on sleeve {sleeve.Id}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Success", $"Processed {sleeves.Count} sleeves.");
        }

        private static void CreateProfessionalSection(Document doc, FamilyInstance sleeve, int index, string roomName)
        {
            LocationPoint loc = sleeve.Location as LocationPoint;
            if (loc == null) return;

            XYZ origin = loc.Point;
            // Simple vertical section looking at the sleeve
            // (original IL creates a section with specific view family type)

            ViewFamilyType sectionType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.Section);

            if (sectionType == null) return;

            // Create a bounding box centered on the sleeve
            double halfSize = 2.0; // feet
            XYZ min = new XYZ(origin.X - halfSize, origin.Y - 0.5, origin.Z - halfSize);
            XYZ max = new XYZ(origin.X + halfSize, origin.Y + 0.5, origin.Z + halfSize);
            BoundingBoxXYZ bbox = new BoundingBoxXYZ
            {
                Min = min,
                Max = max,
                Transform = Transform.Identity
            };

            // Orient the section
            // For simplicity we use a standard section; original code had more precise orientation logic
            ViewSection section = ViewSection.CreateSection(doc, sectionType.Id, bbox);
            if (section == null) return;

            string name = string.Format("Sleeve_{0}_{1}_{2}", index, roomName ?? "Unknown", GetElementIdValue(sleeve.Id));
            // Make name unique
            int suffix = 1;
            string finalName = name;
            while (new FilteredElementCollector(doc).OfClass(typeof(View)).Any(v => v.Name == finalName))
            {
                finalName = name + "_" + suffix++;
            }
            section.Name = finalName;

            // Hide non-relevant categories if possible
            // (original had SetCategoryHidden logic)

            // Annotate
            AnnotateVertical(doc, section, sleeve);
        }

        private static void AnnotateVertical(Document doc, ViewSection section, FamilyInstance sleeve)
        {
            // Original creates IndependentTag and NewDimension
            // Simplified reconstruction – full original used FamilyInstanceReferenceType and ReferenceArray

            try
            {
                // Tag the sleeve
                IndependentTag tag = IndependentTag.Create(
                    doc,
                    section.Id,
                    new Reference(sleeve),
                    false,
                    TagMode.TM_ADDBY_CATEGORY,
                    TagOrientation.Horizontal,
                    (sleeve.Location as LocationPoint)?.Point ?? XYZ.Zero);
            }
            catch { /* non-critical */ }

            // Dimension (requires a suitable DimensionType named "ECG_Dimn-Linr" in the original)
            // Left as optional enhancement
        }

        public static string GetRoomNameAtSleeve(Document doc, FamilyInstance sleeve)
        {
            LocationPoint lp = sleeve.Location as LocationPoint;
            if (lp == null) return "Unknown";

            Room room = doc.GetRoomAtPoint(lp.Point);
            if (room != null) return room.Name ?? "Unknown";

            // Try a small offset
            room = doc.GetRoomAtPoint(lp.Point + new XYZ(0.1, 0.1, 0));
            return room?.Name ?? "Unknown";
        }
    }
}
