using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using BIM.Tools.Models;

namespace BIM.Tools.Health
{
    internal sealed class AuditDiscovery
    {
        private sealed class Pending { internal ModelItem Item; internal AuditFile File; internal int Depth; }
        internal static string Source(ModelItem item)
        {
            DataProperty property = item.PropertyCategories.FindPropertyByName(PropertyCategoryNames.Item, DataPropertyNames.ItemSourceFileName);
            if (property == null) return "";
            VariantData value = property.Value;
            return AuditNames.Clean(value.IsDisplayString ? value.ToDisplayString() : value.IsIdentifierString ? value.ToIdentifierString() : "");
        }
        internal IEnumerable<SharingProgress> Read(Document document, AuditSnapshot snapshot)
        {
            if (document == null || document.IsDisposed) throw new InvalidOperationException("Open a Navisworks document first.");
            Model[] original = document.Models.ToArray(); string documentName = document.FileName;
            snapshot.DocumentPath = AuditNames.Clean(documentName); snapshot.Files.Clear();
            int completed = 0, inspected = 0;
            foreach (Model model in original)
            {
                EnsureUnchanged(document, original, documentName);
                string source = Source(model.RootItem);
                if (string.IsNullOrEmpty(source)) source = AuditNames.Clean(model.SourceFileName);
                // A durable, readable loaded filename adds relocation evidence; SVF caches do not.
                string loadedFrom = AuditNames.Clean(model.FileName);
                if (AuditNames.FileName(loadedFrom) != null) source = loadedFrom;
                string name = AuditNames.FileName(model.RootItem.DisplayName, source, model.SourceFileName, model.FileName);
                AuditFile root = new AuditFile { Name = name ?? "Unidentified loaded file", Source = source, NativeId = model.Guid.ToString("D"), Readable = name != null };
                snapshot.Files.Add(root);
                Stack<Pending> pending = new Stack<Pending>(); pending.Push(new Pending { Item = model.RootItem, File = root });
                while (pending.Count > 0)
                {
                    Pending current = pending.Pop(); int childFiles = 0;
                    List<Pending> children = new List<Pending>();
                    // Inspect immediate file containers only; never traverse a geometry tree.
                    foreach (ModelItem child in current.Item.Children)
                    {
                        string childName = AuditNames.FileName(child.DisplayName);
                        if (childName != null)
                        {
                            string childSource = Source(child);
                            AuditFile entry = new AuditFile { ParentId = current.File.Id, Name = childName, Source = childSource, NativeId = child.InstanceGuid.ToString("D") };
                            children.Add(new Pending { Item = child, File = entry, Depth = current.Depth + 1 }); childFiles++;
                        }
                        if (++inspected % 32 == 0)
                        {
                            EnsureUnchanged(document, original, documentName);
                            yield return new SharingProgress("Read model files", current.File.Name + " · " + snapshot.Files.Count + " file entries", completed, original.Length);
                        }
                    }
                    bool container = current.File.Name.EndsWith(".nwd", StringComparison.OrdinalIgnoreCase) || current.File.Name.EndsWith(".nwf", StringComparison.OrdinalIgnoreCase);
                    current.File.LimitedChildren = container && childFiles == 0;
                    foreach (Pending child in children)
                    {
                        if (child.Depth > 32 || snapshot.Files.Count >= 50000) throw new InvalidOperationException("This federation exceeds the supported file hierarchy size. The previous complete list was retained.");
                        snapshot.Files.Add(child.File);
                    }
                    // Reverse pushes preserve file order. All parent nodes are already recorded.
                    for (int i = children.Count - 1; i >= 0; i--)
                    {
                        Pending child = children[i];
                        if (child.File.Name.EndsWith(".nwd", StringComparison.OrdinalIgnoreCase) || child.File.Name.EndsWith(".nwf", StringComparison.OrdinalIgnoreCase)) pending.Push(child);
                    }
                    yield return new SharingProgress("Read model files", current.File.Name + " · " + snapshot.Files.Count + " file entries", completed, original.Length);
                }
                yield return new SharingProgress("Read model files", root.Name, ++completed, original.Length);
            }
            EnsureUnchanged(document, original, documentName);
            snapshot.CapturedUtc = DateTime.UtcNow.ToString("o");
            yield return new SharingProgress("Model list ready", snapshot.Files.Count + " file entries read", original.Length, Math.Max(1, original.Length));
        }
        private static void EnsureUnchanged(Document doc, Model[] original, string name)
        {
            if (doc.IsDisposed || !AuditNames.Same(doc.FileName, name) || !doc.Models.SequenceEqual(original)) throw new InvalidOperationException("The model list changed during the check. Check again after loading finishes.");
        }
    }
}
