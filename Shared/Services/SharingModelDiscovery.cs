using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using BIM.Tools.Models;
using COM = Autodesk.Navisworks.Api.Interop.ComApi;

namespace BIM.Tools.Services
{
    public sealed class SharingElement
    {
        public string Name { get; private set; }
        public string Hierarchy { get; private set; }
        public ModelItem Item { get; private set; }
        public SharingElement(string name, string hierarchy) { Name = name; Hierarchy = hierarchy; }
        public SharingElement(string name, string hierarchy, ModelItem item) : this(name,hierarchy) { Item = item; }
    }
    public sealed class SharingModelDiscovery
    {
        private static readonly Regex FileName = new Regex(@"[^/\\]+?\.(?:rvt|nwc|nwd|ifc|dwg|dgn|dxf|rfa|3ds|fbx|skp|obj|stp|step|igs|iges)(?=$|[\s?#])", RegexOptions.IgnoreCase);
        public static string DisplayFileName(string displayName, string sourceFile, string localFile, int ordinal)
        {
            foreach (string text in new[] { displayName, sourceFile, localFile })
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                string decoded = Uri.UnescapeDataString(text);
                Match name = FileName.Match(decoded);
                if (name.Success) return name.Value.Trim();
            }
            if (!string.IsNullOrWhiteSpace(displayName) && displayName.IndexOf('?') < 0 &&
                displayName.IndexOf(".svf", StringComparison.OrdinalIgnoreCase) < 0 &&
                displayName.IndexOf("urn:", StringComparison.OrdinalIgnoreCase) < 0) return displayName;
            return "Model " + ordinal + " (file name unavailable)";
        }
        public IEnumerable<SharingProgress> Discover(Document document, IList<SharingSource> result)
        {
            if (document == null || document.IsDisposed || document.IsClear) yield break;
            int ordinal = 0, total = document.Models.Count;
            foreach (Model model in document.Models)
            {
                ordinal++;
                string name = DisplayFileName(model.RootItem.DisplayName, model.SourceFileName, model.FileName, ordinal);
                yield return new SharingProgress("1 / 3 · Identify model files", name, ordinal - 1, total);
                // Only file-named federation containers are expanded. Never search
                // the BIM geometry tree to discover an ACC/SVF file name.
                Stack<ModelItem> pending = new Stack<ModelItem>(); pending.Push(model.RootItem);
                while (pending.Count > 0)
                {
                    ModelItem item = pending.Pop(); string file = DisplayFileName(item.DisplayName, null, null, ordinal);
                    bool federation = file.EndsWith(".nwd", StringComparison.OrdinalIgnoreCase);
                    List<ModelItem> nested = new List<ModelItem>();
                    if (federation)
                    {
                        int inspected = 0;
                        foreach (ModelItem child in item.Children)
                        {
                            if (FileName.IsMatch(child.DisplayName ?? string.Empty)) nested.Add(child);
                            if (++inspected % 32 == 0) yield return new SharingProgress("1 / 3 · Identify model files", name + " · inspecting file containers", ordinal - 1, total);
                        }
                    }
                    if (nested.Count > 0) foreach (ModelItem child in nested) pending.Push(child);
                    else result.Add(new SharingSource(item.Equals(model.RootItem) ? name : file, item));
                    yield return new SharingProgress("1 / 3 · Identify model files", name + " · " + result.Count + " model files found", ordinal, total);
                }
            }
        }
        public IEnumerable<SharingProgress> Scan(Document document, SectionBoxScope scope, IList<SharingSource> all, IList<SharingSource> inside)
        {
            int modelIndex = 0, inspected = 0;
            List<Model> original = document.Models.ToList();
            foreach (SharingSource source in all)
            {
                source.Elements.Clear();
                Stack<ModelItem> pending = new Stack<ModelItem>(); pending.Push(source.Item);
                while (pending.Count > 0)
                {
                    if (inspected % 128 == 0 && (document.IsDisposed || document.IsClear || !document.Models.SequenceEqual(original))) throw new InvalidOperationException("The open models changed. Confirm the Section Box again.");
                    ModelItem item = pending.Pop(); inspected++;
                    BoundingBox3D bounds = item.BoundingBox();
                    if (scope.Intersects(bounds))
                    {
                        int children = 0;
                        foreach (ModelItem child in item.Children)
                        {
                            pending.Push(child);
                            if (++children % 32 == 0) yield return new SharingProgress("2 / 3 · Check geometry inside box", source.Name + " · queueing " + children.ToString("N0") + " child items", modelIndex, all.Count);
                        }
                        if (item.HasGeometry)
                        {
                            bool hit = BoundsInside(scope, bounds);
                            if (!hit)
                            {
                                ScopeIntersectionSink sink = new ScopeIntersectionSink(scope);
                                COM.InwOaPath3 path = (COM.InwOaPath3)ComApiBridge.ToInwOaPath(item);
                                foreach (COM.InwOaFragment3 fragment in path.Fragments())
                                {
                                    sink.SetMatrix((Array)((COM.InwLTransform3f3)fragment.GetLocalToWorldMatrix()).Matrix);
                                    fragment.GenerateSimplePrimitives(COM.nwEVertexProperty.eNONE, sink);
                                    yield return new SharingProgress("2 / 3 · Check geometry inside box", source.Name + " · " + item.DisplayName + "\r\n" + inspected.ToString("N0") + " tree items checked", modelIndex, all.Count);
                                    if (sink.Hit) break;
                                }
                                hit = sink.Hit;
                            }
                            if (hit)
                            {
                                List<string> names = item.AncestorsAndSelf.Take(7).Select(a => a.DisplayName).Where(n => !string.IsNullOrWhiteSpace(n) && n.IndexOf('?') < 0 && n.IndexOf("urn:",StringComparison.OrdinalIgnoreCase) < 0).ToList();
                                string elementName = names.FirstOrDefault() ?? "Unnamed geometry item";
                                source.Elements.Add(new SharingElement(elementName,
                                    string.Join(" / ", names.Skip(1).Reverse())));
                            }
                        }
                    }
                    yield return new SharingProgress("2 / 3 · Check geometry inside box", source.Name + "\r\n" + inspected.ToString("N0") + " tree items checked · " + source.Elements.Count.ToString("N0") + " geometry items found", modelIndex, all.Count);
                }
                modelIndex++;
                if (source.Elements.Count > 0) inside.Add(source);
                yield return new SharingProgress("2 / 3 · Check geometry inside box", source.Name + " · " + source.Elements.Count.ToString("N0") + " geometry items found", modelIndex, all.Count);
            }
            if (document.IsDisposed || document.IsClear || !document.Models.SequenceEqual(original)) throw new InvalidOperationException("The open models changed. Confirm the Section Box again.");
            yield return new SharingProgress("3 / 3 · Prepare sharing contents", inside.Count + " models intersect the Section Box", all.Count, all.Count);
        }
        internal static bool BoundsInside(SectionBoxScope scope, BoundingBox3D box)
        {
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                if (!scope.Contains(new Point3D(x == 0 ? box.Min.X : box.Max.X, y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z))) return false;
            return true;
        }
        public IEnumerable<SharingProgress> DetectCached(Document document, SectionBoxScope scope,
            SharingContentCache previous, IList<SharingSource> inside, Action<SharingContentCache> complete)
        {
            bool refreshing = previous != null && previous.Matches(document);
            SharingContentCache cache = refreshing ? previous.Fork() : new SharingContentCache();
            if (!refreshing)
            {
                cache.Models.AddRange(document.Models);
                foreach (SharingProgress step in Discover(document,cache.AllSources)) yield return step;
            }
            int modelIndex = 0, inspected = 0;
            foreach (SharingSource source in cache.AllSources)
            {
                Stack<ModelItem> pending = new Stack<ModelItem>(); pending.Push(source.Item);
                while (pending.Count > 0)
                {
                    if (inspected % 128 == 0 && !cache.Matches(document)) throw new InvalidOperationException("Models changed during detection. Detect the content again.");
                    ModelItem item = pending.Pop(); inspected++;
                    CachedSharingNode node;
                    if (!cache.Nodes.TryGetValue(item,out node))
                    {
                        node = new CachedSharingNode { Bounds = item.BoundingBox(), HasGeometry = item.HasGeometry };
                        cache.Nodes.Add(item,node); cache.BoundsRead++;
                    }
                    if (scope.Intersects(node.Bounds))
                    {
                        if (node.Children == null)
                        {
                            List<ModelItem> children = new List<ModelItem>();
                            foreach (ModelItem child in item.Children)
                            {
                                children.Add(child);
                                if (children.Count % 32 == 0) yield return Step(source,modelIndex,cache.AllSources.Count,inspected,cache,"Reading child items");
                            }
                            node.Children = children.ToArray();
                        }
                        foreach (ModelItem child in node.Children) pending.Push(child);
                        if (node.HasGeometry)
                        {
                            // Reuse an exact result only if the old and new boxes
                            // cut this bounding box identically, including rotation.
                            bool reuse = refreshing && node.Element != null && SameCuts(cache.Scope,scope,node.Bounds);
                            bool hit;
                            if (reuse) { hit = node.Hit; cache.Reused++; }
                            else
                            {
                                cache.GeometryChecked++;
                                hit = BoundsInside(scope,node.Bounds);
                                if (!hit)
                                {
                                    ScopeIntersectionSink sink = new ScopeIntersectionSink(scope);
                                    var path = (COM.InwOaPath3)ComApiBridge.ToInwOaPath(item);
                                    foreach (COM.InwOaFragment3 fragment in path.Fragments())
                                    {
                                        sink.SetMatrix((Array)((COM.InwLTransform3f3)fragment.GetLocalToWorldMatrix()).Matrix);
                                        fragment.GenerateSimplePrimitives(COM.nwEVertexProperty.eNONE,sink);
                                        yield return Step(source,modelIndex,cache.AllSources.Count,inspected,cache,item.DisplayName);
                                        if (sink.Hit) break;
                                    }
                                    hit = sink.Hit;
                                }
                                node.Hit = hit;
                            }
                            if (node.Element == null)
                            {
                                List<string> names = item.AncestorsAndSelf.Take(7).Select(a=>a.DisplayName).Where(n=>!string.IsNullOrWhiteSpace(n) && n.IndexOf('?')<0 && n.IndexOf("urn:",StringComparison.OrdinalIgnoreCase)<0).ToList();
                                node.Element = new SharingElement(names.FirstOrDefault() ?? "Unnamed geometry item",string.Join(" / ",names.Skip(1).Reverse()),item);
                            }
                            if (hit) source.Elements.Add(node.Element);
                        }
                    }
                    yield return Step(source,modelIndex,cache.AllSources.Count,inspected,cache,refreshing ? "Reuse cached bounds; check changed region" : "Check geometry inside box");
                }
                source.ScopeJson = scope.RawJson;
                if (source.Elements.Count > 0) inside.Add(source);
                modelIndex++;
                yield return Step(source,modelIndex,cache.AllSources.Count,inspected,cache,"Model complete");
            }
            if (!cache.Matches(document)) throw new InvalidOperationException("Models changed during detection. Detect the content again.");
            cache.Scope = scope; complete(cache);
            yield return new SharingProgress("3 / 3 · Prepare sharing content",inside.Count + " models · " + cache.Reused.ToString("N0") + " cached geometry results reused",cache.AllSources.Count,cache.AllSources.Count);
        }
        private static SharingProgress Step(SharingSource source,int index,int total,int inspected,SharingContentCache cache,string detail)
        { return new SharingProgress("2 / 3 · Detect Section Box content",source.Name + " · " + detail + "\r\n" + inspected.ToString("N0") + " items · " + cache.Reused.ToString("N0") + " reused · " + cache.GeometryChecked.ToString("N0") + " geometry checks",index,total); }
        private static bool SameCuts(SectionBoxScope oldScope,SectionBoxScope next,BoundingBox3D bounds)
        {
            if (oldScope == null) return false;
            if (oldScope.RawJson == next.RawJson || (BoundsInside(oldScope,bounds) && BoundsInside(next,bounds))) return true;
            // For parallel boxes, an unchanged plane or a plane lying outside the
            // candidate's AABB imposes the same constraint on all its primitives.
            Point3D origin = new Point3D(0,0,0); Point3D a0 = oldScope.ToLocal(origin), b0 = next.ToLocal(origin);
            foreach (Point3D axis in new[] { new Point3D(1,0,0),new Point3D(0,1,0),new Point3D(0,0,1) })
            {
                Vector3D a = oldScope.ToLocal(axis).Subtract(a0), b = next.ToLocal(axis).Subtract(b0);
                if (Math.Abs(a.X-b.X)+Math.Abs(a.Y-b.Y)+Math.Abs(a.Z-b.Z)>1e-10) return false;
            }
            double[] lo = {double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity}, hi = {double.NegativeInfinity,double.NegativeInfinity,double.NegativeInfinity};
            for(int x=0;x<2;x++) for(int y=0;y<2;y++) for(int z=0;z<2;z++)
            {
                Point3D p = oldScope.ToLocal(new Point3D(x==0?bounds.Min.X:bounds.Max.X,y==0?bounds.Min.Y:bounds.Max.Y,z==0?bounds.Min.Z:bounds.Max.Z));
                double[] v = {p.X,p.Y,p.Z}; for(int i=0;i<3;i++) {lo[i]=Math.Min(lo[i],v[i]); hi[i]=Math.Max(hi[i],v[i]);}
            }
            Vector3D shift = b0.Subtract(a0);
            double[] oldLo = {oldScope.Minimum.X,oldScope.Minimum.Y,oldScope.Minimum.Z}, oldHi = {oldScope.Maximum.X,oldScope.Maximum.Y,oldScope.Maximum.Z};
            double[] newLo = {next.Minimum.X-shift.X,next.Minimum.Y-shift.Y,next.Minimum.Z-shift.Z}, newHi = {next.Maximum.X-shift.X,next.Maximum.Y-shift.Y,next.Maximum.Z-shift.Z};
            for(int i=0;i<3;i++)
                if (!(oldLo[i]==newLo[i] || (oldLo[i]<=lo[i] && newLo[i]<=lo[i])) || !(oldHi[i]==newHi[i] || (oldHi[i]>=hi[i] && newHi[i]>=hi[i]))) return false;
            return true;
        }
        public IEnumerable<SharingProgress> ResolveSelection(Document document, IList<SharingSource> sources, ISet<SharingSource> result)
        {
            Dictionary<ModelItem, SharingSource> cache = sources.ToDictionary(s => s.Item, s => s);
            Dictionary<ModelItem, List<SharingSource>> coverage = new Dictionary<ModelItem, List<SharingSource>>();
            foreach (SharingSource source in sources)
                foreach (ModelItem ancestor in source.Item.AncestorsAndSelf)
                {
                    List<SharingSource> children;
                    if (!coverage.TryGetValue(ancestor, out children)) coverage.Add(ancestor, children = new List<SharingSource>());
                    children.Add(source);
                }
            int count = 0, total = document.CurrentSelection.SelectedItems.Count;
            foreach (ModelItem selected in document.CurrentSelection.SelectedItems)
            {
                List<SharingSource> covered;
                if (coverage.TryGetValue(selected, out covered)) foreach (SharingSource source in covered) result.Add(source);
                else
                {
                    ModelItem cursor = selected; SharingSource owner = null; List<ModelItem> trail = new List<ModelItem>();
                    while (cursor != null && !cache.TryGetValue(cursor, out owner)) { trail.Add(cursor); cursor = cursor.Parent; }
                    foreach (ModelItem item in trail) cache[item] = owner;
                    if (owner != null) result.Add(owner);
                }
                count++;
                yield return new SharingProgress("Resolve selected models", count.ToString("N0") + " selected items checked · " + result.Count + " models matched", count, total);
            }
        }
        [System.Runtime.InteropServices.ComVisible(true)]
        [System.Runtime.InteropServices.ClassInterface(System.Runtime.InteropServices.ClassInterfaceType.None)]
        public sealed class ScopeIntersectionSink : COM.InwSimplePrimitivesCB
        {
            private readonly SectionBoxScope _scope; private readonly BoxGeometryClipper _clipper;
            private readonly double[] _matrix = new double[16];
            public bool Hit { get; private set; }
            public ScopeIntersectionSink(SectionBoxScope scope) { _scope = scope; _clipper = new BoxGeometryClipper(scope); }
            public void SetMatrix(Array matrix) { int start = matrix.GetLowerBound(0); for (int i = 0; i < 16; i++) _matrix[i] = Convert.ToDouble(matrix.GetValue(start + i)); }
            public void Triangle(COM.InwSimpleVertex a, COM.InwSimpleVertex b, COM.InwSimpleVertex c)
            {
                if (Hit) return;
                IList<Point3D> polygon = _clipper.Triangle(World(a), World(b), World(c));
                for (int i = 1; i + 1 < polygon.Count; i++)
                {
                    Vector3D u = polygon[i].Subtract(polygon[0]), v = polygon[i+1].Subtract(polygon[0]);
                    double x = u.Y*v.Z-u.Z*v.Y, y = u.Z*v.X-u.X*v.Z, z = u.X*v.Y-u.Y*v.X;
                    if (x*x+y*y+z*z > 1e-20) { Hit = true; return; }
                }
            }
            public void Line(COM.InwSimpleVertex a, COM.InwSimpleVertex b) { if (Hit) return; Point3D first, last; Hit = _clipper.Line(World(a), World(b), out first, out last); }
            public void Point(COM.InwSimpleVertex a) { if (!Hit) Hit = _scope.Contains(World(a)); }
            public void SnapPoint(COM.InwSimpleVertex a) { }
            private Point3D World(COM.InwSimpleVertex vertex)
            {
                Array c = (Array)vertex.coord; int start = c.GetLowerBound(0);
                double x = Convert.ToDouble(c.GetValue(start)), y = Convert.ToDouble(c.GetValue(start+1)), z = Convert.ToDouble(c.GetValue(start+2));
                return new Point3D(x*_matrix[0]+y*_matrix[4]+z*_matrix[8]+_matrix[12], x*_matrix[1]+y*_matrix[5]+z*_matrix[9]+_matrix[13], x*_matrix[2]+y*_matrix[6]+z*_matrix[10]+_matrix[14]);
            }
        }
    }
}
