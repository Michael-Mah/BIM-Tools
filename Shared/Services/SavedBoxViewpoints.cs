using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using BIM.Tools.Models;

namespace BIM.Tools.Services
{
    internal sealed class SavedBoxViewpoint
    {
        public SavedViewpoint Viewpoint;
        public string Path;
        public bool HasBox;
    }
    internal sealed class SavedBoxViewpoints
    {
        internal IEnumerable<SharingProgress> Read(Document document, IList<SavedBoxViewpoint> result)
        {
            Stack<KeyValuePair<SavedItem,string>> pending = new Stack<KeyValuePair<SavedItem,string>>();
            foreach (SavedItem item in document.SavedViewpoints.Value) pending.Push(new KeyValuePair<SavedItem,string>(item, string.Empty));
            int read = 0;
            while (pending.Count > 0)
            {
                KeyValuePair<SavedItem,string> entry = pending.Pop(); SavedItem item = entry.Key;
                string path = entry.Value + item.DisplayName;
                SavedViewpoint view = item as SavedViewpoint;
                if (view != null) result.Add(new SavedBoxViewpoint { Viewpoint = view, Path = path, HasBox = HasSavedBox(view) });
                GroupItem group = item as GroupItem;
                if (group != null) foreach (SavedItem child in group.Children) pending.Push(new KeyValuePair<SavedItem,string>(child, path + " / "));
                yield return new SharingProgress("Read Saved Viewpoints", path, ++read, 0);
            }
        }
        internal static bool HasSavedBox(SavedViewpoint saved)
        {
            try
            {
                ClipPlaneSet clipping = saved.Viewpoint.ClipPlanes;
                if (clipping == null || clipping.Mode != ClipPlaneSetMode.Box) return false;
                BoundingBox3D bounds = new BoundingBox3D(); Rotation3D rotation = new Rotation3D();
                clipping.GetOrientedBox(bounds, rotation);
                return !bounds.IsEmpty && bounds.Size.X > 0 && bounds.Size.Y > 0 && bounds.Size.Z > 0;
            }
            catch { return false; }
        }
        internal static void Activate(Document document, SavedBoxViewpoint saved)
        {
            if (!HasSavedBox(saved.Viewpoint)) throw new InvalidOperationException("This Saved Viewpoint has no valid Section Box.");
            // Copy only the camera and clipping snapshot. Do not apply saved hide /
            // appearance overrides, which could touch hundreds of thousands of items.
            Viewpoint view = saved.Viewpoint.Viewpoint.CreateCopy();
            ClipPlaneSet planes = view.ClipPlanes; planes.Enabled = true; view.ClipPlanes = planes;
            document.CurrentViewpoint.CopyFrom(view);
        }
    }
}
