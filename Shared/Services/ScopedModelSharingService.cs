using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using BIM.Tools.Models;
using COM = Autodesk.Navisworks.Api.Interop.ComApi;

namespace BIM.Tools.Services
{
    public sealed class SharingSource
    {
        public string Name { get; private set; }
        public ModelItem Item { get; private set; }
        public List<SharingElement> Elements { get; private set; }
        public string ScopeJson { get; internal set; }
        public SharingSource(string name, ModelItem item) { Name = name; Item = item; Elements = new List<SharingElement>(); }
        public override string ToString() { return Name; }
    }

    public sealed class SharingSummary
    {
        public string FileName { get; internal set; }
        public long Objects { get; internal set; }
        public long Triangles { get; internal set; }
        public long Lines { get; internal set; }
        public long Points { get; internal set; }
        public long Bytes { get; internal set; }
        public string[] Sources { get; internal set; }
        public string PackageFileName { get; internal set; }
        public long PackageBytes { get; internal set; }
        public string[] AdditionalFiles { get; internal set; }
    }

    public sealed class ScopedModelSharingService
    {
        public IList<SharingSource> GetSources(Document document)
        {
            List<SharingSource> sources = new List<SharingSource>();
            foreach (SharingProgress step in new SharingModelDiscovery().Discover(document, sources)) { }
            return sources.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public SharingSummary Export(Document document, SectionBoxScope scope,
            IList<SharingSource> sources, string file, Func<bool> cancelled, Action<string> progress)
        { return ExportWithProgress(document,scope,sources,file,cancelled,progress == null ? (Action<SharingExportProgress>)null : step=>progress(step.Detail)); }

        public SharingSummary ExportWithProgress(Document document, SectionBoxScope scope,
            IList<SharingSource> sources, string file, Func<bool> cancelled, Action<SharingExportProgress> progress)
        {
            string extension=Path.GetExtension(file);
            if(extension.Equals(".nwc",StringComparison.OrdinalIgnoreCase) || extension.Equals(".nwd",StringComparison.OrdinalIgnoreCase)) return ExportNative(document,scope,sources,file,cancelled,progress);
            if (document == null || document.IsClear || scope == null) throw new InvalidOperationException("An open model and an enabled Section Box are required.");
            if (sources == null || sources.Count == 0) throw new InvalidOperationException("Select at least one source model.");
            string fullPath = Path.GetFullPath(file);
            bool obj=string.Equals(Path.GetExtension(fullPath),".obj",StringComparison.OrdinalIgnoreCase);
            string format=obj ? "OBJ" : "IFC";
            if (!obj && !string.Equals(Path.GetExtension(fullPath), ".ifc", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose an IFC or OBJ output file.");
            if (string.Equals(fullPath, document.FileName, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The output must be a separate file.");
            HashSet<ModelItem> documentRoots = new HashSet<ModelItem>(document.Models.RootItems);
            // ModelItem.Model is populated only on model-root nodes, not every geometry/discipline branch.
            if (sources.Any(s => s.Item == null || s.Item.IsDisposed || !s.Item.AncestorsAndSelf.Any(documentRoots.Contains))) throw new InvalidOperationException("The source list is stale. Refresh it after opening or appending models.");
            if (scope.Minimum.X >= scope.Maximum.X || scope.Minimum.Y >= scope.Maximum.Y || scope.Minimum.Z >= scope.Maximum.Z) throw new InvalidOperationException("The Section Box has no volume.");
            double metres = UnitConversion.ScaleFactor(document.Units, Units.Meters);
            string token=Guid.NewGuid().ToString("N");
            string temporary = Path.Combine(Path.GetDirectoryName(fullPath), ".bim-" + token + (obj ? ".obj" : ".ifc"));
            // An immutable companion lets the OBJ itself be committed atomically.
            // An existing OBJ still references its previous, untouched material file.
            string material=Path.Combine(Path.GetDirectoryName(fullPath),"bim_colors_"+token+".mtl"), temporaryMaterial=temporary+".mtl";
            bool committed=false;
            SharingSummary result = new SharingSummary { FileName = fullPath, Sources = sources.Select(s => s.Name).ToArray(), AdditionalFiles=obj ? new[]{material} : new string[0] };
            HashSet<ModelItem> visited = new HashSet<ModelItem>();
            List<Model> originalModels = document.Models.ToList();
            bool exact=sources.All(s=>s.ScopeJson==scope.RawJson && s.Elements.Count>0 && s.Elements.All(e=>e.Item!=null));
            long total=exact ? sources.Sum(s=>(long)s.Elements.Count) : 0, completed=0;
            Stopwatch update=Stopwatch.StartNew(); string activeSource="";
            Action pulse=delegate
            {
                if(update.ElapsedMilliseconds < 35) return;
                if(progress!=null) progress(new SharingExportProgress("Clipping geometry",activeSource + " · " + completed.ToString("N0") + " / " + (total>0?total.ToString("N0"):"?") + " items · " + result.Triangles.ToString("N0") + " triangles",completed,total));
                update.Restart();
            };
            if(progress!=null) progress(new SharingExportProgress("Prepare " + format,"Preparing selected geometry",0,total));
            try
            {
                using (IScopedGeometryWriter writer = obj ? (IScopedGeometryWriter)new ScopedObjWriter(temporary,temporaryMaterial,Path.GetFileName(material),metres) : new ScopedIfcWriter(temporary, scope.Center, metres))
                {
                    foreach (SharingSource source in sources)
                    {
                        activeSource=source.Name;
                        Stack<ModelItem> pending = new Stack<ModelItem>();
                        if(exact) foreach(SharingElement element in source.Elements) pending.Push(element.Item);
                        else pending.Push(source.Item);
                        int scanned = 0;
                        while (pending.Count != 0)
                        {
                            if ((scanned++ % 100) == 0)
                            {
                                pulse();
                                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                                if (document.IsClear || !document.Models.SequenceEqual(originalModels)) throw new InvalidOperationException("The source document changed during export. Refresh and try again.");
                            }
                            ModelItem item = pending.Pop();
                            if (!visited.Add(item) || (!exact && !scope.Intersects(item.BoundingBox()))) continue;
                            if(!exact) foreach (ModelItem child in item.Children) pending.Push(child);
                            if (!item.HasGeometry) continue;
                            string itemPath = string.Join("/", document.Models.CreateIndexPath(item).Select(i => i.ToString(CultureInfo.InvariantCulture)));
                            string name=string.IsNullOrWhiteSpace(item.DisplayName) ? item.AncestorsAndSelf.Select(a=>a.DisplayName).FirstOrDefault(n=>!string.IsNullOrWhiteSpace(n)) ?? "Unnamed geometry item" : item.DisplayName;
                            Autodesk.Navisworks.Api.Color color=item.Geometry.ActiveColor;
                            writer.BeginProduct(name,source.Name,itemPath,color);
                            PrimitiveSink sink = new PrimitiveSink(scope, writer, result) { Heartbeat=pulse, Cancelled=cancelled };
                            COM.InwOaPath3 path = (COM.InwOaPath3)ComApiBridge.ToInwOaPath(item);
                            foreach (COM.InwOaFragment3 fragment in path.Fragments())
                            {
                                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                                sink.SetMatrix((Array)((COM.InwLTransform3f3)fragment.GetLocalToWorldMatrix()).Matrix);
                                fragment.GenerateSimplePrimitives(COM.nwEVertexProperty.eNONE, sink);
                                if(cancelled!=null && cancelled()) throw new OperationCanceledException();
                            }
                            sink.Flush();
                            completed++; pulse();
                            if (sink.Meshes.Count == 0 && sink.Curves.Count == 0 && sink.Points.Count == 0) continue;
                            string identity = obj ? string.Empty : SourceIdentity(item);
                            // CAD readers can reuse InstanceGuid/Element ID across several geometry
                            // nodes or block instances. The document index path distinguishes them.
                            string key = source.Name + "/" + identity + "/path:" + itemPath;
                            writer.Product(key, name, source.Name, itemPath, identity,
                                sink.Meshes, sink.Curves, sink.Points, color);
                            result.Objects++;
                        }
                    }
                    if (result.Objects == 0) throw new InvalidOperationException("No geometry from the selected sources intersects this Section Box.");
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                    if(progress!=null) progress(new SharingExportProgress("Finalize " + format,"Finishing the coordination model",completed,total));
                    writer.Complete();
                }
                // Commit only a complete file. Preserve an existing output on cancellation or export failure.
                if(cancelled!=null && cancelled()) throw new OperationCanceledException();
                if(obj) File.Move(temporaryMaterial,material);
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
                else File.Move(temporary, fullPath);
                committed=true;
                result.Bytes = new FileInfo(fullPath).Length + result.AdditionalFiles.Sum(p=>new FileInfo(p).Length);
                if(progress!=null) progress(new SharingExportProgress(format + " complete",result.Objects.ToString("N0") + " objects exported",completed,total));
                return result;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); if(File.Exists(temporaryMaterial)) File.Delete(temporaryMaterial); if(!committed && File.Exists(material)) File.Delete(material); }
        }

        private SharingSummary ExportNative(Document document,SectionBoxScope scope,IList<SharingSource> sources,string file,Func<bool> cancelled,Action<SharingExportProgress> progress)
        {
            string fullPath=Path.GetFullPath(file);
            if(document!=null && string.Equals(fullPath,document.FileName,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The output must be a separate file.");
            string converter=Path.Combine(Path.GetDirectoryName(typeof(ScopedModelSharingService).Assembly.Location),EditionBrand.NativeConverterName);
            if(!File.Exists(converter)) throw new InvalidOperationException("Native conversion helper is missing. Reinstall BIM Tools.");
            string work=Path.Combine(Path.GetDirectoryName(fullPath),".bim-native-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string status=Path.Combine(work,"status.txt"),cancelFile=Path.Combine(work,"cancel.txt"),staged=Path.Combine(work,"native-output"+Path.GetExtension(fullPath));
            try
            {
                // IFC carries metre units explicitly, so native files cannot inherit
                // a recipient's default OBJ unit setting and silently shift scale.
                SharingSummary result=ExportWithProgress(document,scope,sources,Path.Combine(work,"clipped.ifc"),cancelled,progress);
                string api=Path.GetDirectoryName(typeof(Document).Assembly.Location);
                ProcessStartInfo start=new ProcessStartInfo(converter,Quote(api)+" "+Quote(result.FileName)+" "+Quote(staged)+" "+Quote(status)+" "+Quote(cancelFile))
                {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=work};
                using(Process process=Process.Start(start))
                {
                    while(!process.WaitForExit(75))
                    {
                        if(cancelled!=null && cancelled() && !File.Exists(cancelFile)) File.WriteAllText(cancelFile,"Cancel requested");
                        string detail="Starting native conversion";
                        try {if(File.Exists(status)) detail=File.ReadAllText(status);} catch(IOException) { }
                        if(progress!=null) progress(new SharingExportProgress("Convert to " + Path.GetExtension(fullPath).Substring(1).ToUpperInvariant(),detail,0,0));
                    }
                    if((cancelled!=null && cancelled()) || process.ExitCode==3) throw new OperationCanceledException();
                    if(process.ExitCode!=0 || !File.Exists(staged)) throw new IOException("Native conversion failed: " + (File.Exists(status) ? File.ReadAllText(status) : "no converter report"));
                }
                SharingNativeTiming.Record(Path.GetExtension(fullPath).Substring(1),status+".metrics");
                if(File.Exists(fullPath)) File.Replace(staged,fullPath,null); else File.Move(staged,fullPath);
                result.FileName=fullPath; result.AdditionalFiles=new string[0]; result.Bytes=new FileInfo(fullPath).Length;
                if(progress!=null) progress(new SharingExportProgress("Native model complete","Strictly clipped native model ready",result.Objects,result.Objects));
                return result;
            }
            finally
            {
                // Only delete files directly inside the fresh directory owned by this export.
                foreach(string owned in Directory.GetFiles(work,"*",SearchOption.TopDirectoryOnly)) File.Delete(owned);
                Directory.Delete(work,false);
            }
        }
        private static string Quote(string value) {return "\""+value+"\"";}

        private static string SourceIdentity(ModelItem item)
        {
            if (item.InstanceGuid != Guid.Empty) return "InstanceGuid:" + item.InstanceGuid.ToString("D");
            // Preserve a source element identifier where the reader exposes it.
            foreach (ModelItem ancestor in item.AncestorsAndSelf)
                foreach (PropertyCategory category in ancestor.PropertyCategories)
                    foreach (DataProperty property in category.Properties)
                        if (property.Name == "LcRevitPropertyElementId" ||
                            string.Equals(property.DisplayName, "Element ID", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(property.DisplayName, "Unique ID", StringComparison.OrdinalIgnoreCase))
                        {
                            string value = ReadSourceIdentifier(property.Value);
                            if (!string.IsNullOrWhiteSpace(value)) return property.DisplayName + ":" + value;
                        }
            return string.Empty;
        }

        internal static string ReadSourceIdentifier(VariantData value)
        {
            // ToDisplayString is a typed accessor, not a general conversion.
            // Revit Element IDs can be integers, while Unique IDs can be
            // identifier strings. Unknown metadata must not stop geometry export;
            // the document index path still supplies a distinct product identity.
            if(value == null || value.IsNone) return string.Empty;
            if(value.IsDisplayString) return value.ToDisplayString();
            if(value.IsIdentifierString) return value.ToIdentifierString();
            if(value.IsInt32) return value.ToInt32().ToString(CultureInfo.InvariantCulture);
            if(value.IsInt64) return value.ToInt64().ToString(CultureInfo.InvariantCulture);
            if(value.IsNat32) return value.ToNat32().ToString(CultureInfo.InvariantCulture);
            if(value.IsNat64) return value.ToNat64().ToString(CultureInfo.InvariantCulture);
            return string.Empty;
        }

        public void PackageForTransfer(SharingSummary summary)
        { PackageForTransfer(summary,null,null); }
        public void PackageForTransfer(SharingSummary summary,Func<bool> cancelled,Action<SharingExportProgress> progress)
        {
            string package = Path.ChangeExtension(summary.FileName,"zip");
            string temporary = package + ".bim-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream file = new FileStream(temporary,FileMode.CreateNew))
                using (ZipArchive zip = new ZipArchive(file,ZipArchiveMode.Create))
                {
                    // Include only our generated model and its explicit companions.
                    string[] outputs=new[]{summary.FileName}.Concat(summary.AdditionalFiles ?? new string[0]).ToArray();
                    long total=outputs.Sum(p=>new FileInfo(p).Length), copied=0;
                    foreach(string output in outputs)
                    {
                    ZipArchiveEntry entry = zip.CreateEntry(Path.GetFileName(output),CompressionLevel.Fastest);
                    using (Stream target = entry.Open())
                    using (FileStream source = File.OpenRead(output))
                    {
                        byte[] buffer=new byte[131072]; int read;
                        while((read=source.Read(buffer,0,buffer.Length))>0)
                        {
                            if(cancelled!=null && cancelled()) throw new OperationCanceledException();
                            target.Write(buffer,0,read); copied+=read;
                            if(progress!=null) progress(new SharingExportProgress("Create ZIP","Packaging clipped model · " + (copied/1048576.0).ToString("0.0") + " MB",copied,total));
                        }
                    }
                    }
                }
                if (File.Exists(package)) File.Replace(temporary,package,null); else File.Move(temporary,package);
                summary.PackageFileName = package; summary.PackageBytes = new FileInfo(package).Length;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        [System.Runtime.InteropServices.ComVisible(true)]
        public sealed class PrimitiveSink : COM.InwSimplePrimitivesCB
        {
            private readonly SectionBoxScope _scope;
            private readonly BoxGeometryClipper _clipper;
            private readonly IScopedGeometryWriter _writer;
            private readonly SharingSummary _summary;
            private readonly List<Point3D> _vertices = new List<Point3D>();
            private readonly double[] _matrix = new double[16];
            public readonly List<int> Meshes = new List<int>();
            public readonly List<int> Curves = new List<int>();
            public readonly List<int> Points = new List<int>();
            internal Action Heartbeat;
            internal Func<bool> Cancelled;
            private int _primitives;
            private bool Continue()
            { if(++_primitives%256==0 && Heartbeat!=null) Heartbeat(); return Cancelled==null || !Cancelled(); }
            internal PrimitiveSink(SectionBoxScope scope, IScopedGeometryWriter writer, SharingSummary summary)
            { _scope = scope; _clipper = new BoxGeometryClipper(scope); _writer = writer; _summary = summary; }
            public void SetMatrix(Array matrix)
            { for (int i = 0; i < 16; i++) _matrix[i] = Convert.ToDouble(matrix.GetValue(matrix.GetLowerBound(0) + i)); }
            public void Triangle(COM.InwSimpleVertex a, COM.InwSimpleVertex b, COM.InwSimpleVertex c)
            {
                if(!Continue()) return;
                IList<Point3D> polygon = _clipper.Triangle(World(a), World(b), World(c));
                for (int i = 1; i + 1 < polygon.Count; i++)
                {
                    Vector3D first = polygon[i].Subtract(polygon[0]), second = polygon[i+1].Subtract(polygon[0]);
                    // Drop zero-area triangles created at a box edge.
                    double x = first.Y * second.Z - first.Z * second.Y;
                    double y = first.Z * second.X - first.X * second.Z;
                    double z = first.X * second.Y - first.Y * second.X;
                    if (x*x + y*y + z*z <= 1e-20) continue;
                    _vertices.Add(polygon[0]); _vertices.Add(polygon[i]); _vertices.Add(polygon[i+1]);
                    _summary.Triangles++;
                    if (_vertices.Count >= 60000) Flush();
                }
            }
            public void Line(COM.InwSimpleVertex a, COM.InwSimpleVertex b)
            {
                if(!Continue()) return;
                Point3D first, last;
                if (_clipper.Line(World(a), World(b), out first, out last))
                { Curves.Add(_writer.Line(first, last)); _summary.Lines++; }
            }
            public void Point(COM.InwSimpleVertex a)
            { if(!Continue()) return; Point3D p = World(a); if (_scope.Contains(p)) { Points.Add(_writer.Point(p)); _summary.Points++; } }
            // Snapping markers are auxiliary editor data, not model geometry.
            public void SnapPoint(COM.InwSimpleVertex a) { }
            public void Flush()
            { if (_vertices.Count != 0) { Meshes.Add(_writer.Mesh(_vertices)); _vertices.Clear(); } }
            private Point3D World(COM.InwSimpleVertex vertex)
            {
                Array coords = (Array)vertex.coord; int lower = coords.GetLowerBound(0);
                double x = Convert.ToDouble(coords.GetValue(lower)), y = Convert.ToDouble(coords.GetValue(lower+1)), z = Convert.ToDouble(coords.GetValue(lower+2));
                return new Point3D(x*_matrix[0]+y*_matrix[4]+z*_matrix[8]+_matrix[12],
                    x*_matrix[1]+y*_matrix[5]+z*_matrix[9]+_matrix[13],
                    x*_matrix[2]+y*_matrix[6]+z*_matrix[10]+_matrix[14]);
            }
        }
    }
}
