using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using BIM.Tools.Models;

namespace BIM.Tools.Services
{
    internal sealed class CachedSharingNode
    {
        internal BoundingBox3D Bounds;
        internal ModelItem[] Children;
        internal bool HasGeometry;
        internal bool Hit;
        internal SharingElement Element;
        internal CachedSharingNode Copy() { return (CachedSharingNode)MemberwiseClone(); }
    }
    public sealed class SharingContentCache
    {
        internal readonly Dictionary<ModelItem,CachedSharingNode> Nodes = new Dictionary<ModelItem,CachedSharingNode>();
        internal readonly List<SharingSource> AllSources = new List<SharingSource>();
        internal readonly List<Model> Models = new List<Model>();
        internal SectionBoxScope Scope;
        public long BoundsRead { get; internal set; }
        public long GeometryChecked { get; internal set; }
        public long Reused { get; internal set; }
        internal bool Matches(Document doc) { return !doc.IsDisposed && !doc.IsClear && doc.Models.SequenceEqual(Models); }
        internal SharingContentCache Fork()
        {
            SharingContentCache copy = new SharingContentCache { Scope = Scope };
            foreach (KeyValuePair<ModelItem,CachedSharingNode> entry in Nodes) copy.Nodes.Add(entry.Key,entry.Value.Copy());
            foreach (SharingSource source in AllSources) copy.AllSources.Add(new SharingSource(source.Name,source.Item));
            copy.Models.AddRange(Models); return copy;
        }
    }
}
