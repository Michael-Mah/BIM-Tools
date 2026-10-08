using System;
using Autodesk.Navisworks.Api;

namespace BIM.Tools.Services
{
    internal sealed class SectionBoxWorkspaceService
    {
        public const string NativeSourceName = "Current Navisworks Section Box";
        public static readonly string SavedSourceName = EditionBrand.CompanyName + " Saved Section Box";
        public static readonly string SavedViewpointName = EditionBrand.ViewpointPrefix + " SECTION BOX";

        public bool HasSavedSectionBox(Document document)
        {
            return FindSavedSectionBox(document) != null;
        }

        public void SaveCurrentSectionBox(Document document)
        {
            if (document == null || document.IsClear)
            {
                throw new InvalidOperationException("Open a federated document first.");
            }

            SavedViewpoint existing = FindSavedSectionBox(document);
            if (existing != null)
            {
                document.SavedViewpoints.Remove(existing);
            }

            SavedViewpoint saved = new SavedViewpoint(
                document.CurrentViewpoint.CreateCopy());
            saved.DisplayName = SavedViewpointName;
            document.SavedViewpoints.AddCopy(saved);
        }

        public bool TryActivateSavedSectionBox(
            Document document,
            out string reason)
        {
            reason = string.Empty;
            if (document == null || document.IsClear)
            {
                reason = "Open a federated document first.";
                return false;
            }

            SavedViewpoint saved = FindSavedSectionBox(document);
            if (saved == null)
            {
                reason = SavedViewpointName +
                         " has not been created for this model. Open Section Box Builder to configure it.";
                return false;
            }

            document.CurrentViewpoint.CopyFrom(saved.Viewpoint);
            document.SavedViewpoints.CurrentSavedViewpoint = saved;

            Models.SectionBoxScope scope;
            if (!Models.SectionBoxScope.TryRead(
                document.ActiveView,
                out scope,
                out reason))
            {
                reason = SavedViewpointName +
                         " was restored, but it does not contain an active Section Box: " +
                         reason;
                return false;
            }
            return true;
        }

        private static SavedViewpoint FindSavedSectionBox(Document document)
        {
            if (document == null || document.IsClear)
            {
                return null;
            }
            return FindSavedSectionBox(document.SavedViewpoints.Value);
        }

        private static SavedViewpoint FindSavedSectionBox(
            SavedItemCollection items)
        {
            foreach (SavedItem item in items)
            {
                SavedViewpoint viewpoint = item as SavedViewpoint;
                if (viewpoint != null && string.Equals(
                    viewpoint.DisplayName,
                    SavedViewpointName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return viewpoint;
                }

                GroupItem group = item as GroupItem;
                if (group != null)
                {
                    SavedViewpoint nested = FindSavedSectionBox(group.Children);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }
            return null;
        }
    }
}
