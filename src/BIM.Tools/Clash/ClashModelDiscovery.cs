using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using BIM.Tools.Models;
using BIM.Tools.Services;

namespace BIM.Tools.Clash
{
    internal sealed class LoadedClashModel
    {
        internal ClashModelIdentity Identity;
        internal ModelItem Root;
    }
    internal sealed class ClashModelDiscovery
    {
        internal IEnumerable<SharingProgress> Discover(Document document, IList<LoadedClashModel> result)
        {
            List<SharingSource> sources = new List<SharingSource>();
            foreach (SharingProgress step in new SharingModelDiscovery().Discover(document, sources))
                yield return new SharingProgress("Read loaded model files", step.Detail, step.Completed, step.Total);
            Model[] models = document.Models.ToArray();
            int index = 0;
            foreach (SharingSource source in sources)
            {
                Model model = models.FirstOrDefault(m => source.Item.AncestorsAndSelf.Any(a => a.Equals(m.RootItem)));
                string container = string.Join(" / ", source.Item.Ancestors.Reverse().Select(a => a.DisplayName)
                    .Where(n => !string.IsNullOrWhiteSpace(n) && n.IndexOf('?') < 0 && n.IndexOf("urn:", StringComparison.OrdinalIgnoreCase) < 0));
                if (string.IsNullOrWhiteSpace(container)) container = "Top-level file";
                result.Add(new LoadedClashModel { Root = source.Item, Identity = new ClashModelIdentity {
                    FileName = source.Name, Container = container,
                    SourcePath = CleanSource(model == null ? null : (model.SourceFileName ?? model.FileName)),
                    ModelGuid = model == null ? null : model.Guid.ToString("D"), RootGuid = source.Item.InstanceGuid.ToString("D") } });
                yield return new SharingProgress("Read loaded model files", source.Name, ++index, sources.Count);
            }
        }
        private static string CleanSource(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            // Session tokens and transient ACC query strings are not durable identities.
            int end = text.IndexOfAny(new[] { '?', '#' });
            return Uri.UnescapeDataString(end < 0 ? text : text.Substring(0, end));
        }
    }
}
