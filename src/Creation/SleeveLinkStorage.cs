using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace RevitSleeveAddin
{
    /// <summary>
    /// Persists the relationship between a sleeve and its source MEP element
    /// (same document or linked model) so sleeves can be updated when MEP moves.
    /// </summary>
    public static class SleeveLinkStorage
    {
        // Fixed schema id – do not change after release
        private static readonly Guid SchemaGuid = new Guid("A7C3E91B-4D2F-4B8A-9E15-6F2D8C1A0B47");
        private const string SchemaName = "SleeveMepLink";
        private const string VendorId = "SleeveTools";

        private const string FieldMepUniqueId = "MepUniqueId";
        private const string FieldLinkUniqueId = "LinkUniqueId";
        private const string FieldHostUniqueId = "HostUniqueId";

        public static Schema GetOrCreateSchema()
        {
            Schema existing = Schema.Lookup(SchemaGuid);
            if (existing != null) return existing;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetVendorId(VendorId);
            builder.SetDocumentation("Links a sleeve opening to the MEP element that created it.");

            builder.AddSimpleField(FieldMepUniqueId, typeof(string))
                .SetDocumentation("UniqueId of the source MEP curve (pipe/duct/tray/conduit).");
            builder.AddSimpleField(FieldLinkUniqueId, typeof(string))
                .SetDocumentation("UniqueId of the RevitLinkInstance, or empty if MEP is in the same document.");
            builder.AddSimpleField(FieldHostUniqueId, typeof(string))
                .SetDocumentation("UniqueId of the host wall or floor.");

            return builder.Finish();
        }

        public static void Store(FamilyInstance sleeve, MEPCurve mep, Element host, string linkInstanceUniqueId)
        {
            if (sleeve == null || mep == null) return;
            try
            {
                Schema schema = GetOrCreateSchema();
                Entity entity = new Entity(schema);
                entity.Set(FieldMepUniqueId, mep.UniqueId ?? "");
                entity.Set(FieldLinkUniqueId, linkInstanceUniqueId ?? "");
                entity.Set(FieldHostUniqueId, host != null ? (host.UniqueId ?? "") : "");
                sleeve.SetEntity(entity);
            }
            catch { }
        }

        public static bool TryRead(FamilyInstance sleeve, out string mepUniqueId, out string linkUniqueId, out string hostUniqueId)
        {
            mepUniqueId = null;
            linkUniqueId = null;
            hostUniqueId = null;
            try
            {
                Schema schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return false;
                Entity entity = sleeve.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return false;
                mepUniqueId = entity.Get<string>(FieldMepUniqueId);
                linkUniqueId = entity.Get<string>(FieldLinkUniqueId);
                hostUniqueId = entity.Get<string>(FieldHostUniqueId);
                return !string.IsNullOrEmpty(mepUniqueId);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Resolve the current MEP element and its model-space curve from stored ids.
        /// </summary>
        public static bool TryResolveMep(
            Document hostDoc,
            string mepUniqueId,
            string linkUniqueId,
            out MEPCurve mep,
            out Curve modelCurve)
        {
            mep = null;
            modelCurve = null;
            if (hostDoc == null || string.IsNullOrEmpty(mepUniqueId)) return false;

            try
            {
                if (string.IsNullOrEmpty(linkUniqueId))
                {
                    Element e = hostDoc.GetElement(mepUniqueId);
                    mep = e as MEPCurve;
                    if (mep == null) return false;
                    LocationCurve loc = mep.Location as LocationCurve;
                    if (loc?.Curve == null) return false;
                    modelCurve = loc.Curve;
                    return true;
                }

                // Linked model
                Element linkEl = hostDoc.GetElement(linkUniqueId);
                RevitLinkInstance link = linkEl as RevitLinkInstance;
                if (link == null)
                {
                    // Fallback: search all links by UniqueId
                    foreach (RevitLinkInstance li in new FilteredElementCollector(hostDoc)
                        .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                    {
                        if (li.UniqueId == linkUniqueId) { link = li; break; }
                    }
                }
                if (link == null) return false;

                Document linkDoc = link.GetLinkDocument();
                if (linkDoc == null) return false;

                Element e2 = linkDoc.GetElement(mepUniqueId);
                mep = e2 as MEPCurve;
                if (mep == null) return false;
                LocationCurve loc2 = mep.Location as LocationCurve;
                if (loc2?.Curve == null) return false;

                Transform t = link.GetTotalTransform();
                modelCurve = loc2.Curve.CreateTransformed(t);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
