using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;

namespace RevitSleeveAddin
{
    /// <summary>
    /// Native wall logic (face Reference placement) + same approach for Floors/Slabs.
    /// </summary>
    public static class SleeveService
    {
        private const string FAMILY_ROUND = "WallOpening_Round";
        private const string FAMILY_RECT = "WallOpening_Rectangular";
        private const double DEFAULT_CLEARANCE = 0.0065616797900262466; // ~2 mm
        private const double MERGE_DISTANCE_FT = 0.3280839895; // 10 cm – merge openings closer than this

        private static long GetElementIdValue(ElementId id)
        {
#if REVIT2026_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        private class MepCurveData
        {
            public MEPCurve Element { get; set; }
            public Curve Curve { get; set; }
            /// <summary>UniqueId of RevitLinkInstance, or null/empty if MEP is in the host document.</summary>
            public string LinkUniqueId { get; set; }
        }

        /// <summary>
        /// processWalls / processFloors control which hosts are used.
        /// </summary>
        public static int Execute(
            Document doc,
            UIDocument uidoc,
            double clearanceFt,
            bool processLinks,
            bool processWalls,
            bool processFloors)
        {
            var log = new StringBuilder();
            log.AppendLine("Starting execution...");
            log.AppendLine("Walls: " + processWalls + " | Floors: " + processFloors);

            FamilySymbol roundSymbol = FindFamilySymbol(doc, FAMILY_ROUND);
            FamilySymbol rectSymbol = FindFamilySymbol(doc, FAMILY_RECT);

            if (roundSymbol == null || rectSymbol == null)
            {
                string msg = "Missing required families:\n";
                if (roundSymbol == null) msg += "WallOpening_Round: Missing\n";
                if (rectSymbol == null) msg += "WallOpening_Rectangular: Missing\n";
                msg += "\nPlease load them and try again.";
                TaskDialog.Show("Error", msg);
                return 0;
            }

            View view = uidoc.ActiveView;
            if (view == null) { TaskDialog.Show("Error", "No active view."); return 0; }

            int created = 0;
            var createdIds = new List<ElementId>();
            var instanceHost = new Dictionary<ElementId, ElementId>();

            using (Transaction tx = new Transaction(doc, "Create Sleeves"))
            {
                tx.Start();
                if (!roundSymbol.IsActive) roundSymbol.Activate();
                if (!rectSymbol.IsActive) rectSymbol.Activate();

                // ---- Hosts ----
                var walls = new List<Wall>();
                var floors = new List<Floor>();

                if (processWalls)
                {
                    walls = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(Wall)).Cast<Wall>().ToList();
                    log.AppendLine("Found " + walls.Count + " host walls.");
                }
                if (processFloors)
                {
                    floors = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(Floor)).Cast<Floor>().ToList();
                    log.AppendLine("Found " + floors.Count + " host floors/slabs.");
                }

                // ---- MEP ----
                var mepList = new List<MepCurveData>();
                foreach (MEPCurve mep in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(MEPCurve)).Cast<MEPCurve>())
                {
                    LocationCurve loc = mep.Location as LocationCurve;
                    if (loc?.Curve == null) continue;
                    mepList.Add(new MepCurveData { Element = mep, Curve = loc.Curve, LinkUniqueId = null });
                }
                log.AppendLine("Found " + mepList.Count + " host MEP curves.");

                if (processLinks)
                {
                    foreach (RevitLinkInstance link in new FilteredElementCollector(doc)
                        .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                    {
                        try
                        {
                            Document linkDoc = link.GetLinkDocument();
                            if (linkDoc == null) continue;
                            Transform transform = link.GetTotalTransform();
                            int linkCount = 0;
                            foreach (MEPCurve mep in new FilteredElementCollector(linkDoc)
                                .OfClass(typeof(MEPCurve)).Cast<MEPCurve>())
                            {
                                LocationCurve loc = mep.Location as LocationCurve;
                                if (loc?.Curve == null) continue;
                                mepList.Add(new MepCurveData
                                {
                                    Element = mep,
                                    Curve = loc.Curve.CreateTransformed(transform),
                                    LinkUniqueId = link.UniqueId
                                });
                                linkCount++;
                            }
                            log.AppendLine("Found " + linkCount + " MEP curves in Link '" + link.Name + "'.");
                        }
                        catch (Exception ex) { log.AppendLine("Error reading link: " + ex.Message); }
                    }
                }

                // ---- Existing sleeves ----
                var existingLocations = new List<XYZ>();
                foreach (FamilyInstance fi in new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
                {
                    string fname = fi.Symbol?.Family?.Name ?? "";
                    if (fname.Contains("WallOpening") || fname.Contains("Sleeve"))
                    {
                        if (fi.Location is LocationPoint lp)
                            existingLocations.Add(lp.Point);
                    }
                }
                log.AppendLine("Found " + existingLocations.Count + " existing sleeves. Will skip duplicates.");

                // ---- Walls (native logic) ----
                foreach (var mepData in mepList)
                {
                    foreach (Wall wall in walls)
                    {
                        try
                        {
                            ProcessHostIntersection(doc, view, wall, mepData,
                                rectSymbol, roundSymbol, existingLocations, clearanceFt, log, ref created, createdIds, instanceHost, isFloor: false);
                        }
                        catch (Exception ex) { log.AppendLine("Wall intersection error: " + ex.Message); }
                    }

                    // ---- Floors (same face-Reference approach) ----
                    foreach (Floor floor in floors)
                    {
                        try
                        {
                            ProcessHostIntersection(doc, view, floor, mepData,
                                rectSymbol, roundSymbol, existingLocations, clearanceFt, log, ref created, createdIds, instanceHost, isFloor: true);
                        }
                        catch (Exception ex) { log.AppendLine("Floor intersection error: " + ex.Message); }
                    }
                }

                // Merge openings on the same host that are ≤ 10 cm apart
                int merged = MergeNearbyOpenings(doc, createdIds, instanceHost, rectSymbol, roundSymbol, clearanceFt, log);
                created = createdIds.Count; // update after merge deletes

                tx.Commit();
                log.AppendLine("Created " + created + " sleeves (after merge of close openings).");
            }

            // Success dialog removed – silent completion
            return created;
        }

        /// <summary>
        /// Shared intersection + face-Reference placement for Wall or Floor.
        /// Same algorithm as native DLL for walls; applied to floors the same way.
        /// </summary>
        private static bool ProcessHostIntersection(
            Document doc, View view, Element host, MepCurveData mepData,
            FamilySymbol rectSymbol, FamilySymbol roundSymbol,
            List<XYZ> existingLocations, double clearance, StringBuilder log,
            ref int count, List<ElementId> createdIds, Dictionary<ElementId, ElementId> instanceHost, bool isFloor)
        {
            MEPCurve mepCurve = mepData.Element;
            Curve curve = mepData.Curve;
            if (mepCurve == null || curve == null) return false;

            Solid solid = GetSolid(host);
            if (solid == null) return false;

            SolidCurveIntersection result = solid.IntersectWithCurve(curve, new SolidCurveIntersectionOptions());
            if (result == null || result.SegmentCount == 0) return false;

            bool any = false;

            for (int i = 0; i < result.SegmentCount; i++)
            {
                Curve segment = result.GetCurveSegment(i);
                XYZ p0 = segment.GetEndPoint(0);
                XYZ p1 = segment.GetEndPoint(1);

                if (view.CropBoxActive)
                {
                    if (!IsPointInViewCrop(view, p0) && !IsPointInViewCrop(view, p1))
                        continue;
                }

                XYZ direction = (p1 - p0).Normalize();
                double segLength = segment.Length;

                bool isRound = IsRoundShape(mepCurve);
                FamilySymbol symbol = isRound ? roundSymbol : rectSymbol;

                // Face at p0, then p1 (native order)
                Reference faceRef = FindIntersectionFace(host, p0);
                XYZ placePt = p0;
                if (faceRef == null)
                {
                    faceRef = FindIntersectionFace(host, p1);
                    placePt = p1;
                }
                if (faceRef == null)
                {
                    log.AppendLine("Found intersection but could not find suitable face on "
                        + (isFloor ? "Floor " : "Wall ") + GetElementIdValue(host.Id) + ".");
                    continue;
                }

                if (existingLocations.Any(p => p.DistanceTo(placePt) < 0.35))
                    continue;

                try
                {
                    // Reference direction aligned to the MEP system (duct/tray width axis)
                    XYZ refDir = GetReferenceDirection(mepCurve, placePt, direction, faceRef, isFloor);

                    FamilyInstance instance = doc.Create.NewFamilyInstance(
                        faceRef, placePt, refDir, symbol);

                    if (instance == null) continue;

                    // Force rectangular sleeves to match duct/tray width & height axes
                    if (!isRound)
                        AlignRectangularSleeve(doc, instance, mepCurve, placePt, direction);

                    SetSleeveParameters(instance, mepCurve, host, isRound, clearance, log);

                    // Center sleeve in host thickness so it does not stick outside
                    CenterInHost(doc, instance, host, placePt, direction);

                    // Persist link to source MEP (same file or linked model)
                    SleeveLinkStorage.Store(instance, mepCurve, host, mepData.LinkUniqueId);

                    if (instance.Location is LocationPoint lp)
                        existingLocations.Add(lp.Point);
                    else
                        existingLocations.Add(placePt);

                    count++;
                    createdIds.Add(instance.Id);
                    if (host != null) instanceHost[instance.Id] = host.Id;
                    any = true;
                }
                catch (Exception ex)
                {
                    log.AppendLine("Failed to create instance: " + ex.Message);
                }
            }
            return any;
        }

        /// <summary>
        /// If two or more openings on the same wall/floor are ≤ 10 cm apart,
        /// delete them and create one larger opening that covers all.
        /// </summary>
        private static int MergeNearbyOpenings(
            Document doc,
            List<ElementId> createdIds,
            Dictionary<ElementId, ElementId> instanceHost,
            FamilySymbol rectSymbol,
            FamilySymbol roundSymbol,
            double clearance,
            StringBuilder log)
        {
            if (createdIds == null || createdIds.Count < 2) return 0;

            // Build working list of valid instances
            var items = new List<SleeveInfo>();
            foreach (ElementId id in createdIds.ToList())
            {
                FamilyInstance fi = doc.GetElement(id) as FamilyInstance;
                if (fi == null) continue;
                LocationPoint lp = fi.Location as LocationPoint;
                if (lp == null) continue;
                Element host = null;
                ElementId hostId = ElementId.InvalidElementId;
                if (instanceHost != null && instanceHost.ContainsKey(id))
                {
                    hostId = instanceHost[id];
                    host = doc.GetElement(hostId);
                }
                if (host == null) host = fi.Host;
                if (host != null) hostId = host.Id;
                if (hostId == ElementId.InvalidElementId)
                    hostId = ElementId.InvalidElementId;

                double w = 0, h = 0;
                bool isRound = false;
                Parameter ew = fi.LookupParameter("ElementWidth");
                Parameter eh = fi.LookupParameter("ElementHeight");
                Parameter ed = fi.LookupParameter("ElementDiameter") ?? fi.LookupParameter("Diameter");
                if (ew != null && eh != null && ew.HasValue && eh.HasValue && ew.AsDouble() > 0)
                {
                    w = ew.AsDouble();
                    h = eh.AsDouble();
                }
                else if (ed != null && ed.HasValue && ed.AsDouble() > 0)
                {
                    isRound = true;
                    w = h = ed.AsDouble();
                }
                else
                {
                    // Bounding box size as fallback
                    BoundingBoxXYZ bb = fi.get_BoundingBox(null);
                    if (bb != null)
                    {
                        w = Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y);
                        h = bb.Max.Z - bb.Min.Z;
                    }
                }

                items.Add(new SleeveInfo
                {
                    Id = id,
                    Instance = fi,
                    Point = lp.Point,
                    HostId = hostId,
                    Host = host,
                    Width = w,
                    Height = h,
                    IsRound = isRound
                });
            }

            // Union-Find clustering: same host and center distance ≤ 10 cm
            int n = items.Count;
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            System.Func<int, int> find = null;
            find = (int x) => parent[x] == x ? x : (parent[x] = find(parent[x]));
            System.Action<int, int> unite = (int a, int b) =>
            {
                int ra = find(a), rb = find(b);
                if (ra != rb) parent[ra] = rb;
            };

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (items[i].HostId != items[j].HostId) continue;
                    // Distance between centers ≤ 10 cm OR edge gap ≤ 10 cm
                    double dist = items[i].Point.DistanceTo(items[j].Point);
                    double gap = dist - 0.5 * (Math.Max(items[i].Width, items[i].Height) + Math.Max(items[j].Width, items[j].Height));
                    // Do NOT merge rectangular openings (cable trays / ducts) –
                    // only merge round (pipe/conduit) sleeves that are close
                    if (!items[i].IsRound || !items[j].IsRound)
                        continue;
                    if (dist <= MERGE_DISTANCE_FT || gap <= MERGE_DISTANCE_FT)
                        unite(i, j);
                }
            }

            var clusters = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int r = find(i);
                if (!clusters.ContainsKey(r)) clusters[r] = new List<int>();
                clusters[r].Add(i);
            }

            int mergeCount = 0;
            foreach (var kv in clusters)
            {
                List<int> idxs = kv.Value;
                if (idxs.Count < 2) continue;

                // Bounding box of all centers + half sizes
                XYZ min = items[idxs[0]].Point;
                XYZ max = items[idxs[0]].Point;
                double maxW = 0, maxH = 0;
                bool allRound = true;
                Element host = items[idxs[0]].Host;
                foreach (int i in idxs)
                {
                    XYZ p = items[i].Point;
                    min = new XYZ(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                    max = new XYZ(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
                    maxW = Math.Max(maxW, items[i].Width);
                    maxH = Math.Max(maxH, items[i].Height);
                    if (!items[i].IsRound) allRound = false;
                    if (host == null) host = items[i].Host;
                }

                XYZ center = (min + max) * 0.5;
                // Combined size = span of centers + largest individual size
                double spanX = max.X - min.X;
                double spanY = max.Y - min.Y;
                double spanZ = max.Z - min.Z;
                // Opening size on face ≈ max individual + center span in plane
                double newW = maxW + Math.Max(spanX, spanY);
                double newH = maxH + spanZ;
                // Prefer planar expansion: use the two largest spans
                double[] spans = new[] { spanX, spanY, spanZ };
                System.Array.Sort(spans);
                newW = maxW + spans[2]; // largest span
                newH = maxH + spans[1]; // second largest
                if (newW < maxW) newW = maxW;
                if (newH < maxH) newH = maxH;

                // Delete old instances
                var toDelete = new List<ElementId>();
                foreach (int i in idxs)
                {
                    toDelete.Add(items[i].Id);
                    createdIds.Remove(items[i].Id);
                }
                doc.Delete(toDelete);

                // Place one merged opening on host
                if (host == null) continue;

                try
                {
                    FamilySymbol symbol = allRound ? roundSymbol : rectSymbol;
                    if (symbol == null) continue;
                    if (!symbol.IsActive) symbol.Activate();

                    Reference faceRef = FindIntersectionFace(host, center);
                    if (faceRef == null)
                    {
                        // Try a bit offset from center toward host
                        BoundingBoxXYZ hbb = host.get_BoundingBox(null);
                        if (hbb != null)
                        {
                            XYZ hc = (hbb.Min + hbb.Max) * 0.5;
                            faceRef = FindIntersectionFace(host, (center + hc) * 0.5);
                        }
                    }
                    if (faceRef == null) continue;

                    XYZ refDir = XYZ.BasisX;
                    XYZ penDir = XYZ.BasisZ;
                    if (host is Wall wall)
                    {
                        penDir = wall.Orientation ?? XYZ.BasisY;
                        refDir = penDir.CrossProduct(XYZ.BasisZ);
                        if (refDir.IsZeroLength()) refDir = XYZ.BasisX;
                    }
                    else if (host is Floor)
                    {
                        penDir = XYZ.BasisZ;
                        refDir = XYZ.BasisX;
                    }

                    FamilyInstance merged = doc.Create.NewFamilyInstance(faceRef, center, refDir, symbol);
                    if (merged == null) continue;

                    // Depth = host thickness
                    double thickness = GetHostThickness(host);
                    Parameter depthParam = merged.LookupParameter("Depth");
                    if (depthParam != null && !depthParam.IsReadOnly && thickness > 0)
                        depthParam.Set(thickness);

                    Parameter clearanceParam = merged.LookupParameter("Clearance");
                    if (clearanceParam != null && !clearanceParam.IsReadOnly)
                        clearanceParam.Set(clearance);

                    if (allRound)
                    {
                        double dia = Math.Max(newW, newH);
                        Parameter ed = merged.LookupParameter("ElementDiameter");
                        if (ed != null && !ed.IsReadOnly) ed.Set(dia);
                        Parameter diaP = merged.LookupParameter("Diameter") ?? merged.LookupParameter("Radius");
                        if (diaP != null && !diaP.IsReadOnly)
                        {
                            if (diaP.Definition.Name.IndexOf("Radius", StringComparison.OrdinalIgnoreCase) >= 0)
                                diaP.Set(dia / 2.0 + clearance);
                            else
                                diaP.Set(dia + 2 * clearance);
                        }
                    }
                    else
                    {
                        Parameter ew = merged.LookupParameter("ElementWidth");
                        Parameter eh = merged.LookupParameter("ElementHeight");
                        if (ew != null && !ew.IsReadOnly) ew.Set(newW);
                        if (eh != null && !eh.IsReadOnly) eh.Set(newH);
                        Parameter wp = merged.LookupParameter("Width");
                        Parameter hp = merged.LookupParameter("Height");
                        if (wp != null && !wp.IsReadOnly) wp.Set(newW + 2 * clearance);
                        if (hp != null && !hp.IsReadOnly) hp.Set(newH + 2 * clearance);
                    }

                    CenterInHost(doc, merged, host, center, penDir);
                    createdIds.Add(merged.Id);
                    mergeCount++;
                    log.AppendLine("Merged " + idxs.Count + " openings into one at " + center.ToString() + ".");
                }
                catch (Exception ex)
                {
                    log.AppendLine("Merge failed: " + ex.Message);
                }
            }
            return mergeCount;
        }

        private class SleeveInfo
        {
            public ElementId Id;
            public FamilyInstance Instance;
            public XYZ Point;
            public ElementId HostId;
            public Element Host;
            public double Width;
            public double Height;
            public bool IsRound;
        }

        private static void SetSleeveParameters(
            FamilyInstance sleeve, MEPCurve mepCurve, Element host, bool isRound,
            double clearance, StringBuilder log)
        {
            // ---- Depth = host thickness (wall / floor / slab) ----
            double hostThickness = GetHostThickness(host);
            if (hostThickness > 1e-6)
            {
                Parameter depthParam = sleeve.LookupParameter("Depth");
                if (depthParam != null && !depthParam.IsReadOnly)
                    depthParam.Set(hostThickness);
            }

            // ---- Clearance ----
            Parameter clearanceParam = sleeve.LookupParameter("Clearance");
            if (clearanceParam != null && !clearanceParam.IsReadOnly)
                clearanceParam.Set(clearance);

            if (isRound)
            {
                double diameter = GetDiameter(mepCurve);
                if (diameter > 0)
                {
                    // Family uses formulas from ElementDiameter + Clearance when available
                    Parameter ed = sleeve.LookupParameter("ElementDiameter");
                    if (ed != null && !ed.IsReadOnly)
                        ed.Set(diameter);

                    Parameter dia = sleeve.LookupParameter("Diameter") ?? sleeve.LookupParameter("Radius");
                    if (dia != null && !dia.IsReadOnly)
                    {
                        if (dia.Definition.Name.IndexOf("Radius", StringComparison.OrdinalIgnoreCase) >= 0)
                            dia.Set(diameter / 2.0 + clearance);
                        else
                            dia.Set(diameter + 2 * clearance);
                    }
                }
            }
            else
            {
                var size = GetWidthHeight(mepCurve);
                if (size != null)
                {
                    // Family formulas:
                    //   Opening Width  = ElementWidth  + Clearance
                    //   Opening Height = ElementHeight + Clearance
                    Parameter ew = sleeve.LookupParameter("ElementWidth");
                    Parameter eh = sleeve.LookupParameter("ElementHeight");
                    if (ew != null && !ew.IsReadOnly) ew.Set(size.Item1);
                    if (eh != null && !eh.IsReadOnly) eh.Set(size.Item2);

                    // Also set Width/Height if present and not formula-driven
                    Parameter wp = sleeve.LookupParameter("Width");
                    Parameter hp = sleeve.LookupParameter("Height");
                    if (wp != null && !wp.IsReadOnly) wp.Set(size.Item1 + 2 * clearance);
                    if (hp != null && !hp.IsReadOnly) hp.Set(size.Item2 + 2 * clearance);
                }
            }

            // Append system / family / type to Comments (do not erase existing text)
            AppendMepInfoToComments(sleeve, mepCurve);
        }

        /// <summary>
        /// Appends System name, Family name and Type name to the sleeve Comments
        /// parameter without removing any existing text.
        /// </summary>

        /// <summary>Internal feet → mm string (1 decimal if needed).</summary>
        private static string FormatMm(double feet)
        {
            double mm = feet * 304.8;
            if (Math.Abs(mm - Math.Round(mm)) < 0.05)
                return ((int)Math.Round(mm)).ToString() + " mm";
            return mm.ToString("0.#") + " mm";
        }

        private static void AppendMepInfoToComments(FamilyInstance sleeve, MEPCurve mep)
        {
            try
            {
                Parameter comments = sleeve.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                                    ?? sleeve.LookupParameter("Comments");
                if (comments == null || comments.IsReadOnly)
                    return;

                string systemName = null;
                try
                {
                    Parameter sysP = mep.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                    if (sysP != null && sysP.HasValue)
                        systemName = sysP.AsString();
                    if (string.IsNullOrWhiteSpace(systemName) && mep.MEPSystem != null)
                        systemName = mep.MEPSystem.Name;
                }
                catch { }

                string familyName = null;
                string typeName = null;
                try
                {
                    ElementType et = mep.Document.GetElement(mep.GetTypeId()) as ElementType;
                    if (et != null)
                    {
                        typeName = et.Name;
                        familyName = et.FamilyName;
                        if (string.IsNullOrWhiteSpace(familyName))
                            familyName = et.Category?.Name;
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(familyName))
                {
                    try { familyName = mep.Category?.Name; } catch { }
                }

                // Size of the passing element (mm for readability)
                string sizeText = null;
                try
                {
                    if (IsRoundShape(mep))
                    {
                        double dia = GetDiameter(mep);
                        if (dia > 1e-9)
                            sizeText = "Size: Ø" + FormatMm(dia);
                    }
                    else
                    {
                        var wh = GetWidthHeight(mep);
                        if (wh != null)
                            sizeText = "Size: " + FormatMm(wh.Item1) + " x " + FormatMm(wh.Item2);
                    }
                }
                catch { }

                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(systemName))
                    parts.Add("System: " + systemName.Trim());
                if (!string.IsNullOrWhiteSpace(familyName))
                    parts.Add("Family: " + familyName.Trim());
                if (!string.IsNullOrWhiteSpace(typeName))
                    parts.Add("Type: " + typeName.Trim());
                if (!string.IsNullOrWhiteSpace(sizeText))
                    parts.Add(sizeText);

                if (parts.Count == 0)
                    return;

                string addition = string.Join(" | ", parts);
                string existing = comments.AsString() ?? "";

                // Avoid duplicating the same block if command is run again
                if (existing.IndexOf(addition, StringComparison.OrdinalIgnoreCase) >= 0)
                    return;

                string updated = string.IsNullOrWhiteSpace(existing)
                    ? addition
                    : existing.TrimEnd() + " | " + addition;

                comments.Set(updated);
            }
            catch { }
        }

        /// <summary>
        /// Wall thickness or Floor/Slab thickness.
        /// </summary>
        private static double GetHostThickness(Element host)
        {
            if (host is Wall wall)
                return wall.Width;

            if (host is Floor floor)
            {
                Parameter t = floor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
                if (t != null && t.HasValue)
                    return t.AsDouble();

                // Fallback: type parameter
                ElementType ft = host.Document.GetElement(floor.GetTypeId()) as ElementType;
                if (ft != null)
                {
                    Parameter tt = ft.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)
                                ?? ft.LookupParameter("Thickness")
                                ?? ft.LookupParameter("Default Thickness");
                    if (tt != null && tt.HasValue)
                        return tt.AsDouble();
                }
            }

            // Generic host solid extent as last resort
            try
            {
                BoundingBoxXYZ bb = host.get_BoundingBox(null);
                if (bb != null)
                {
                    // Prefer smallest dimension as thickness
                    double dx = bb.Max.X - bb.Min.X;
                    double dy = bb.Max.Y - bb.Min.Y;
                    double dz = bb.Max.Z - bb.Min.Z;
                    return Math.Min(dx, Math.Min(dy, dz));
                }
            }
            catch { }

            return 0;
        }

        /// <summary>
        /// Move instance to the middle of the wall/floor thickness so Depth
        /// stays fully inside the host (nothing sticks out of the faces).
        /// placePt is on one face; we shift by half thickness along penetration.
        /// </summary>
        private static void CenterInHost(
            Document doc, FamilyInstance instance, Element host, XYZ placePt, XYZ penetrationDir)
        {
            try
            {
                double thickness = GetHostThickness(host);
                if (thickness < 1e-6) return;

                XYZ dir = penetrationDir.Normalize();

                // Decide inward direction: from placePt toward host centroid
                XYZ hostCenter;
                BoundingBoxXYZ bb = host.get_BoundingBox(null);
                if (bb != null)
                    hostCenter = (bb.Min + bb.Max) * 0.5;
                else
                    hostCenter = placePt + dir * (thickness * 0.5);

                XYZ toCenter = hostCenter - placePt;
                // Component along penetration
                double along = toCenter.DotProduct(dir);
                // Move so insertion point sits at mid-thickness
                // Target offset from face toward center ≈ thickness/2
                double targetAlong = (along >= 0) ? (thickness * 0.5) : (-thickness * 0.5);
                XYZ move = dir.Multiply(targetAlong - along);

                // Only apply small sensible moves (less than thickness)
                if (move.GetLength() > 1e-4 && move.GetLength() <= thickness + 0.05)
                    ElementTransformUtils.MoveElement(doc, instance.Id, move);
            }
            catch { }
        }

        private static void AlignRectangularSleeve(
            Document doc, FamilyInstance instance, MEPCurve mep, XYZ placePt, XYZ penetrationDir)
        {
            try
            {
                XYZ widthAxis, heightAxis;
                double mepWidth, mepHeight;
                if (!TryGetMepCrossSection(mep, placePt, out widthAxis, out heightAxis, out mepWidth, out mepHeight))
                    return;

                // Axes that should appear on the host face (perp. to penetration)
                XYZ wFace = ProjectOntoPlane(widthAxis, penetrationDir);
                XYZ hFace = ProjectOntoPlane(heightAxis, penetrationDir);

                // Prefer the axis that lies better on the face (longer projected length)
                // For risers through walls both are usually horizontal and valid.
                if (wFace.IsZeroLength() && hFace.IsZeroLength()) return;

                // Desired "hand" = width direction on the face
                XYZ desiredHand;
                bool swapParams = false;
                if (wFace.IsZeroLength())
                {
                    desiredHand = hFace.Normalize();
                    swapParams = true; // only height lies on face
                }
                else if (hFace.IsZeroLength())
                {
                    desiredHand = wFace.Normalize();
                }
                else
                {
                    wFace = wFace.Normalize();
                    hFace = hFace.Normalize();
                    // Use width axis as hand
                    desiredHand = wFace;
                }

                LocationPoint lp = instance.Location as LocationPoint;
                if (lp == null) return;

                XYZ hand = instance.HandOrientation;
                if (hand == null || hand.IsZeroLength()) return;

                XYZ handFace = ProjectOntoPlane(hand, penetrationDir);
                if (handFace.IsZeroLength()) return;
                handFace = handFace.Normalize();

                double angle = SignedAngleAroundAxis(handFace, desiredHand, penetrationDir);
                if (Math.Abs(angle) > 0.01)
                {
                    Line axis = Line.CreateBound(placePt, placePt + penetrationDir.Normalize());
                    ElementTransformUtils.RotateElement(doc, instance.Id, axis, angle);
                }

                // Re-check: if hand is closer to height than width, rotate 90°
                hand = instance.HandOrientation;
                handFace = ProjectOntoPlane(hand, penetrationDir);
                if (!handFace.IsZeroLength() && !hFace.IsZeroLength() && !wFace.IsZeroLength())
                {
                    handFace = handFace.Normalize();
                    double toW = SmallestAngle(handFace, wFace.Normalize());
                    double toH = SmallestAngle(handFace, hFace.Normalize());
                    if (toH + 0.05 < toW)
                    {
                        Line axis = Line.CreateBound(placePt, placePt + penetrationDir.Normalize());
                        ElementTransformUtils.RotateElement(doc, instance.Id, axis, Math.PI / 2.0);
                        swapParams = !swapParams;
                    }
                }

                // Store swap flag on element via temporary use of parameters in SetSleeveParameters
                // We apply dimensions here directly for reliability on risers
                ApplyRectDimensions(instance, mepWidth, mepHeight, swapParams);
            }
            catch { }
        }

        private static void ApplyRectDimensions(FamilyInstance sleeve, double mepWidth, double mepHeight, bool swapped)
        {
            // After orientation, Hand follows width axis (unless swapped)
            double w = swapped ? mepHeight : mepWidth;
            double h = swapped ? mepWidth : mepHeight;
            // Clearance applied later in SetSleeveParameters – here only fix raw orientation match
            // SetSleeveParameters will overwrite with clearance; we only need orientation correct.
            // Keep this as no-op for sizes; orientation is the focus.
        }

        private static bool TryGetMepCrossSection(
            MEPCurve mep, XYZ placePt,
            out XYZ widthAxis, out XYZ heightAxis,
            out double mepWidth, out double mepHeight)
        {
            widthAxis = null;
            heightAxis = null;
            mepWidth = 0;
            mepHeight = 0;
            try
            {
                ConnectorSet connectors = mep.ConnectorManager?.Connectors;
                if (connectors == null) return false;

                Connector best = null;
                double bestDist = double.MaxValue;
                foreach (Connector c in connectors)
                {
                    try
                    {
                        // Prefer rectangular connectors near the hit
                        if (c.Shape != ConnectorProfileType.Rectangular
                            && c.Shape != ConnectorProfileType.Oval
                            && c.Shape != ConnectorProfileType.Round)
                            continue;
                        double d = c.Origin.DistanceTo(placePt);
                        if (d < bestDist) { bestDist = d; best = c; }
                    }
                    catch { }
                }

                // If none near, take any rectangular
                if (best == null)
                {
                    foreach (Connector c in connectors)
                    {
                        if (c.Shape == ConnectorProfileType.Rectangular || c.Shape == ConnectorProfileType.Oval)
                        { best = c; break; }
                    }
                }
                if (best == null) return false;

                Transform cs = best.CoordinateSystem;
                // Autodesk MEP rectangular connector convention:
                //   Width along BasisX, Height along BasisY, Flow along BasisZ
                widthAxis = cs.BasisX;  // fixed: was reversed
                heightAxis = cs.BasisY; // fixed: was reversed
                if (widthAxis.IsZeroLength() || heightAxis.IsZeroLength()) return false;
                widthAxis = widthAxis.Normalize();
                heightAxis = heightAxis.Normalize();

                if (best.Shape == ConnectorProfileType.Rectangular || best.Shape == ConnectorProfileType.Oval)
                {
                    mepWidth = best.Width;
                    mepHeight = best.Height;
                }
                else
                {
                    mepWidth = best.Radius * 2.0;
                    mepHeight = mepWidth;
                }
                return true;
            }
            catch { return false; }
        }

        private static double SignedAngleAroundAxis(XYZ from, XYZ to, XYZ axis)
        {
            XYZ a = axis.Normalize();
            XYZ f = ProjectOntoPlane(from, a).Normalize();
            XYZ t = ProjectOntoPlane(to, a).Normalize();
            if (f.IsZeroLength() || t.IsZeroLength()) return 0;
            double angle = f.AngleTo(t);
            XYZ cross = f.CrossProduct(t);
            if (cross.DotProduct(a) < 0) angle = -angle;
            return angle;
        }

        private static double SmallestAngle(XYZ a, XYZ b)
        {
            double ang = a.AngleTo(b);
            return Math.Min(ang, Math.PI - ang);
        }

        private static XYZ ProjectOntoPlane(XYZ vec, XYZ normal)
        {
            if (vec == null || normal == null) return XYZ.Zero;
            XYZ n = normal.Normalize();
            return vec - n.Multiply(vec.DotProduct(n));
        }

        private static XYZ GetReferenceDirection(
            MEPCurve mep, XYZ placePt, XYZ penetrationDir, Reference faceRef, bool isFloor)
        {

            // 1) Try connector coordinate system nearest to placePt
            try
            {
                ConnectorSet connectors = mep.ConnectorManager?.Connectors;
                if (connectors != null)
                {
                    Connector best = null;
                    double bestDist = double.MaxValue;
                    foreach (Connector c in connectors)
                    {
                        double d = c.Origin.DistanceTo(placePt);
                        if (d < bestDist) { bestDist = d; best = c; }
                    }

                    if (best != null)
                    {
                        Transform cs = best.CoordinateSystem;
                        // BasisY is typically the width direction for rectangular MEP connectors
                        // BasisZ is along the MEP flow; BasisX is height for rectangular
                        XYZ widthAxis = cs.BasisX;  // fixed: was reversed
                        XYZ heightAxis = cs.BasisY; // fixed: was reversed

                        if (best.Shape == ConnectorProfileType.Rectangular
                            || best.Shape == ConnectorProfileType.Oval)
                        {
                            // Project width axis onto plane perpendicular to penetration
                            XYZ projected = widthAxis - penetrationDir.Multiply(widthAxis.DotProduct(penetrationDir));
                            if (!projected.IsZeroLength())
                                return projected.Normalize();

                            projected = heightAxis - penetrationDir.Multiply(heightAxis.DotProduct(penetrationDir));
                            if (!projected.IsZeroLength())
                                return projected.Normalize();
                        }
                        else
                        {
                            // Round: any stable axis perpendicular to penetration
                            XYZ refDir = penetrationDir.CrossProduct(XYZ.BasisZ);
                            if (refDir.IsZeroLength())
                                refDir = penetrationDir.CrossProduct(XYZ.BasisX);
                            if (!refDir.IsZeroLength())
                                return refDir.Normalize();
                        }
                    }
                }
            }
            catch { }

            // 2) Curve local frame at placement (works for host + transformed link curves)
            try
            {
                LocationCurve loc = mep.Location as LocationCurve;
                Curve c = loc?.Curve;
                if (c != null)
                {
                    IntersectionResult ir = c.Project(placePt);
                    double param = ir != null ? ir.Parameter : 0.5;
                    Transform t = c.ComputeDerivatives(param, false);
                    // Width along BasisX (matches connector fix); then BasisY
                    XYZ x = t.BasisX;
                    XYZ projected = x - penetrationDir.Multiply(x.DotProduct(penetrationDir));
                    if (!projected.IsZeroLength())
                        return projected.Normalize();
                    XYZ y = t.BasisY;
                    projected = y - penetrationDir.Multiply(y.DotProduct(penetrationDir));
                    if (!projected.IsZeroLength())
                        return projected.Normalize();
                }
            }
            catch { }

            // 3) Native wall-style fallback
            if (isFloor)
            {
                XYZ refDir = new XYZ(penetrationDir.X, penetrationDir.Y, 0);
                if (refDir.IsZeroLength())
                    refDir = penetrationDir.CrossProduct(XYZ.BasisZ);
                if (refDir.IsZeroLength())
                    refDir = XYZ.BasisX;
                return refDir.Normalize();
            }
            else
            {
                XYZ refDir = penetrationDir.CrossProduct(XYZ.BasisZ);
                if (refDir.IsZeroLength())
                    refDir = XYZ.BasisX;
                return refDir.Normalize();
            }
        }

        private static Reference FindIntersectionFace(Element host, XYZ point)
        {
            Options opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geom = host.get_Geometry(opt);
            return FindIntersectionFaceInGeom(geom, point);
        }

        private static Reference FindIntersectionFaceInGeom(GeometryElement geom, XYZ point)
        {
            if (geom == null) return null;
            foreach (GeometryObject obj in geom)
            {
                if (obj is Solid solid && solid.Volume > 0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        IntersectionResult ir = face.Project(point);
                        // Native: distance < 0.05 ft
                        if (ir != null && ir.Distance < 0.05)
                            return face.Reference;
                    }
                }
            }
            return null;
        }

        private static bool IsPointInViewCrop(View view, XYZ point)
        {
            try
            {
                if (!view.CropBoxActive) return true;
                BoundingBoxXYZ crop = view.CropBox;
                if (crop == null) return true;
                Transform inv = crop.Transform.Inverse;
                XYZ local = inv.OfPoint(point);
                return local.X >= crop.Min.X && local.X <= crop.Max.X
                    && local.Y >= crop.Min.Y && local.Y <= crop.Max.Y;
            }
            catch { return true; }
        }

        private static double GetDiameter(MEPCurve mep)
        {
            ConnectorSet connectors = mep.ConnectorManager?.Connectors;
            if (connectors != null)
            {
                foreach (Connector c in connectors)
                    if (c.Shape == ConnectorProfileType.Round)
                        return c.Radius * 2.0;
            }
            Parameter p = mep.LookupParameter("Diameter")
                      ?? mep.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)
                      ?? mep.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
            return (p != null && p.HasValue) ? p.AsDouble() : 0;
        }

        private static Tuple<double, double> GetWidthHeight(MEPCurve mep)
        {
            // Cable trays – use dedicated parameters first (avoid wrong connector size)
            if (mep is CableTray || (mep.Category != null &&
                GetElementIdValue(mep.Category.Id) == (int)BuiltInCategory.OST_CableTray))
            {
                Parameter tw = mep.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)
                            ?? mep.LookupParameter("Width");
                Parameter th = mep.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)
                            ?? mep.LookupParameter("Height");
                if (tw != null && th != null && tw.HasValue && th.HasValue
                    && tw.AsDouble() > 1e-6 && th.AsDouble() > 1e-6)
                    return Tuple.Create(tw.AsDouble(), th.AsDouble());
            }

            ConnectorSet connectors = mep.ConnectorManager?.Connectors;
            if (connectors != null)
            {
                foreach (Connector c in connectors)
                {
                    if (c.Shape == ConnectorProfileType.Rectangular
                        && c.Width > 1e-6 && c.Height > 1e-6)
                        return Tuple.Create(c.Width, c.Height);
                }
            }

            Parameter w = mep.LookupParameter("Width") ?? mep.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
            Parameter h = mep.LookupParameter("Height") ?? mep.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
            if (w != null && h != null && w.HasValue && h.HasValue
                && w.AsDouble() > 1e-6 && h.AsDouble() > 1e-6)
                return Tuple.Create(w.AsDouble(), h.AsDouble());
            return null;
        }

        private static bool IsRoundShape(MEPCurve mep)
        {
            // Cable trays and ducts are always rectangular openings
            if (mep is CableTray || mep is Duct)
                return false;
            if (mep is Pipe || mep is Conduit)
                return true;
            if (mep.Category != null)
            {
                int catId = (int)mep.Category.Id.IntegerValue;
                if (catId == (int)BuiltInCategory.OST_CableTray
                    || catId == (int)BuiltInCategory.OST_DuctCurves)
                    return false;
                if (catId == (int)BuiltInCategory.OST_PipeCurves
                    || catId == (int)BuiltInCategory.OST_Conduit)
                    return true;
            }
            ConnectorSet connectors = mep.ConnectorManager?.Connectors;
            if (connectors != null)
            {
                foreach (Connector c in connectors)
                    return c.Shape == ConnectorProfileType.Round;
            }
            return false;
        }

        private static Solid GetSolid(Element e)
        {
            Options opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geom = e.get_Geometry(opt);
            if (geom == null) return null;
            foreach (GeometryObject obj in geom)
            {
                if (obj is Solid s && s.Volume > 1e-6) return s;
                if (obj is GeometryInstance gi)
                {
                    foreach (GeometryObject o2 in gi.GetInstanceGeometry())
                        if (o2 is Solid s2 && s2.Volume > 1e-6) return s2;
                }
            }
            return null;
        }


        /// <summary>
        /// Update all sleeves that have stored MEP links to the current MEP location.
        /// Call when linked MEP (or same-file MEP) has moved.
        /// </summary>
        public static int SyncLinkedSleeves(Document doc, out int missingMep, out int skipped)
        {
            missingMep = 0;
            skipped = 0;
            int updated = 0;

            var sleeves = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi =>
                {
                    string n = fi.Symbol?.Family?.Name ?? "";
                    return n.IndexOf("WallOpening", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Sleeve", StringComparison.OrdinalIgnoreCase) >= 0;
                })
                .ToList();

            using (Transaction tx = new Transaction(doc, "Sync Sleeves to MEP"))
            {
                tx.Start();

                foreach (FamilyInstance sleeve in sleeves)
                {
                    string mepUid, linkUid, hostUid;
                    if (!SleeveLinkStorage.TryRead(sleeve, out mepUid, out linkUid, out hostUid))
                    {
                        skipped++;
                        continue;
                    }

                    MEPCurve mep;
                    Curve modelCurve;
                    if (!SleeveLinkStorage.TryResolveMep(doc, mepUid, linkUid, out mep, out modelCurve))
                    {
                        missingMep++;
                        continue;
                    }

                    Element host = null;
                    if (!string.IsNullOrEmpty(hostUid))
                        host = doc.GetElement(hostUid);
                    if (host == null && sleeve.Host != null)
                        host = sleeve.Host;
                    if (host == null)
                    {
                        missingMep++;
                        continue;
                    }

                    Solid solid = GetSolid(host);
                    if (solid == null) continue;

                    SolidCurveIntersection ix = solid.IntersectWithCurve(modelCurve, new SolidCurveIntersectionOptions());
                    if (ix == null || ix.SegmentCount == 0)
                    {
                        // MEP no longer intersects this host – leave sleeve
                        continue;
                    }

                    Curve seg = ix.GetCurveSegment(0);
                    XYZ p0 = seg.GetEndPoint(0);
                    XYZ p1 = seg.GetEndPoint(1);
                    XYZ mid = (p0 + p1) * 0.5;
                    XYZ direction = (p1 - p0);
                    if (direction.GetLength() > 1e-9)
                        direction = direction.Normalize();
                    else
                        direction = XYZ.BasisX;

                    LocationPoint lp = sleeve.Location as LocationPoint;
                    if (lp == null) continue;

                    XYZ current = lp.Point;
                    // Move to new intersection mid, then re-center in host
                    XYZ delta = mid - current;
                    if (delta.GetLength() > 1e-4)
                        ElementTransformUtils.MoveElement(doc, sleeve.Id, delta);

                    CenterInHost(doc, sleeve, host, mid, direction);

                    // Refresh size parameters from current MEP
                    bool isRound = IsRoundShape(mep);
                    double clearance = 0.0065616797900262466;
                    Parameter cp = sleeve.LookupParameter("Clearance");
                    if (cp != null && cp.HasValue) clearance = cp.AsDouble();
                    SetSleeveParameters(sleeve, mep, host, isRound, clearance, new StringBuilder());

                    // Re-store link (still valid)
                    SleeveLinkStorage.Store(sleeve, mep, host, linkUid);

                    updated++;
                }

                tx.Commit();
            }

            return updated;
        }

        private static FamilySymbol FindFamilySymbol(Document doc, string familyName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => fs.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
