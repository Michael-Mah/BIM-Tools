using BIM.Tools.Models;
using BIM.Tools.Services;
using BIM.Tools.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Plugins;
using COM = Autodesk.Navisworks.Api.Interop.ComApi;

namespace BIM.Tools.SharingProbe
{
    [Plugin("BIM.ScopedSharingProbe", "BIMT")]
    public sealed class ScopedSharingProbePlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            Directory.CreateDirectory(parameters[0]);
            File.WriteAllText(Path.Combine(parameters[0], "entry.txt"), "Entered sharing probe\r\n");
            try { return ExecuteCore(parameters); }
            catch (Exception ex) { File.AppendAllText(Path.Combine(parameters[0], "entry.txt"), ex.ToString()); return -1; }
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private int ExecuteCore(string[] parameters)
        {
            string folder = parameters[0]; Directory.CreateDirectory(folder);
            StringBuilder log = new StringBuilder();
            Document document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            try
            {
                File.AppendAllText(Path.Combine(folder, "entry.txt"), "Choosing geometry\r\n");
                // Use a representative real geometry node with nonzero extent on all three axes.
                ModelItem chosen = document.Models.RootItemDescendantsAndSelf.First(i =>
                    i.HasGeometry && i.BoundingBox().Size.X > 10 && i.BoundingBox().Size.Y > 10 && i.BoundingBox().Size.Z > 10);
                BoundingBox3D sourceBounds = chosen.BoundingBox();
                Point3D center = sourceBounds.Center;
                Vector3D half = new Vector3D(sourceBounds.Size.X * 0.25, sourceBounds.Size.Y * 0.6, sourceBounds.Size.Z * 0.6);
                ClipPlaneSet planes = new ClipPlaneSet();
                planes.SetOrientedBox(new BoundingBox3D(center.Subtract(half), center.Add(half)), Rotation3D.CreateFromEulerAngles(0.12, 0.08, 0.25));
                planes.Enabled = true; planes.Mode = ClipPlaneSetMode.Box;
                Viewpoint view = document.CurrentViewpoint.CreateCopy(); view.ClipPlanes = planes;
                document.CurrentViewpoint.CopyFrom(view);
                SectionBoxScope scope; string reason;
                if (!SectionBoxScope.TryRead(document.ActiveView, out scope, out reason)) throw new Exception(reason);
                double sourceMetres = UnitConversion.ScaleFactor(document.Units, Units.Meters);
                Rotation3D exactRotation = new Rotation3D(); BoundingBox3D exactBox = new BoundingBox3D();
                planes.GetOrientedBox(exactBox, exactRotation);
                File.WriteAllText(Path.Combine(folder,"source-quaternion.txt"),string.Join(",", new[] { exactRotation.A, exactRotation.B, exactRotation.C, exactRotation.D }.Select(v => v.ToString("G17", System.Globalization.CultureInfo.InvariantCulture))));
                File.AppendAllText(Path.Combine(folder, "entry.txt"), "Box ready\r\n");
                string beforeFile = document.FileName;
                string beforeClipping = document.ActiveView.GetClippingPlanes();
                bool beforeModified = document.IsModified;
                string output = Path.Combine(folder, "strict-partial.ifc");
                SharingSummary summary = new ScopedModelSharingService().Export(document, scope,
                    new List<SharingSource> { new SharingSource("ALLOWED-SOURCE", chosen) }, output, () => false, message => File.AppendAllText(Path.Combine(folder,"entry.txt"),message+"\r\n"));
                bool sourcePreserved = beforeFile == document.FileName && beforeClipping == document.ActiveView.GetClippingPlanes() && beforeModified == document.IsModified;
                log.AppendLine("SourceStatePreserved=" + sourcePreserved);
                log.AppendLine("SourceBounds=" + sourceBounds);
                log.AppendLine("SourceUnits=" + document.Units);
                log.AppendLine("OutputObjects=" + summary.Objects + ";Triangles=" + summary.Triangles + ";Lines=" + summary.Lines + ";Points=" + summary.Points + ";Bytes=" + summary.Bytes);
                // A second, broader scope exercises model-root traversal and multiple IFC products.
                ClipPlaneSet broadPlanes = new ClipPlaneSet();
                broadPlanes.SetOrientedBox(new BoundingBox3D(center.Subtract(new Vector3D(5000,5000,5000)), center.Add(new Vector3D(5000,5000,5000))), Rotation3D.CreateFromEulerAngles(0,0,0.25));
                broadPlanes.Enabled = true; broadPlanes.Mode = ClipPlaneSetMode.Box;
                Viewpoint broadView = document.CurrentViewpoint.CreateCopy(); broadView.ClipPlanes = broadPlanes; document.CurrentViewpoint.CopyFrom(broadView);
                SectionBoxScope broadScope;
                if (!SectionBoxScope.TryRead(document.ActiveView, out broadScope, out reason)) throw new Exception(reason);
                SharingSummary broadSummary = new ScopedModelSharingService().Export(document, broadScope,
                    new ScopedModelSharingService().GetSources(document).Take(1).ToList(), Path.Combine(folder,"model-root-partial.ifc"), () => false, null);
                log.AppendLine("RootTraversalObjects=" + broadSummary.Objects + ";Triangles=" + broadSummary.Triangles);
                new ScopedModelSharingService().PackageForTransfer(broadSummary);
                log.AppendLine("TransferPackageBytes=" + broadSummary.PackageBytes);
                File.WriteAllText(Path.Combine(folder,"root-scope.json"), broadScope.RawJson);
                broadPlanes.GetOrientedBox(exactBox, exactRotation);
                File.WriteAllText(Path.Combine(folder,"root-quaternion.txt"),string.Join(",", new[] { exactRotation.A, exactRotation.B, exactRotation.C, exactRotation.D }.Select(v => v.ToString("G17", System.Globalization.CultureInfo.InvariantCulture))));
                document.CurrentViewpoint.CopyFrom(view);
                File.WriteAllText(Path.Combine(folder, "source-scope.json"), scope.RawJson);
                File.WriteAllText(Path.Combine(folder, "stage.txt"), log.ToString());

                // Cancel with an existing sentinel: neither replace it nor leave temporary geometry.
                string sentinel = Path.Combine(folder, "cancelled.ifc"); File.WriteAllText(sentinel, "EXISTING-OUTPUT");
                bool cancelled = false;
                try { new ScopedModelSharingService().Export(document, scope, new List<SharingSource> { new SharingSource("ALLOWED", chosen) }, sentinel, () => true, null); }
                catch (OperationCanceledException) { cancelled = true; }
                bool cancellationPassed = cancelled && File.ReadAllText(sentinel) == "EXISTING-OUTPUT" && Directory.GetFiles(folder, ".sbt-*.ifc").Length == 0;
                log.AppendLine("CancellationPreservedExistingFile=" + cancellationPassed);

                log.AppendLine("ExportPassed=" + (sourcePreserved && cancellationPassed && summary.Triangles > 0));
                return sourcePreserved && cancellationPassed && summary.Triangles > 0 ? 0 : -1;
            }
            catch (Exception exception) { log.AppendLine(exception.ToString()); return -1; }
            finally { File.WriteAllText(Path.Combine(folder, "sharing-report.txt"), log.ToString()); }
        }
    }

    [Plugin("BIM.ScopedSharingVerifyProbe", "BIMT")]
    public sealed class ScopedSharingVerifyProbePlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            string folder = parameters[0];
            StringBuilder log = new StringBuilder();
            Document document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            try
            {
                string scopeFile = parameters.Length > 1 ? parameters[1] : "source-scope.json";
                if(Path.GetExtension(document.FileName).Equals(".obj",StringComparison.OrdinalIgnoreCase))
                {
                    foreach(Model model in document.Models) document.Models.SetModelUnitsAndTransform(model,Units.Meters,model.Transform,false);
                    log.AppendLine("OBJ import units explicitly set to metres=True");
                }
                document.ActiveView.SetClippingPlanes(File.ReadAllText(Path.Combine(folder,scopeFile)));
                SectionBoxScope scope; string reason;
                if (!SectionBoxScope.TryRead(document.ActiveView, out scope, out reason)) throw new Exception(reason);
                document.Models.ResetAllHidden();
                Viewpoint plain = document.CurrentViewpoint.CreateCopy();
                ClipPlaneSet off = plain.ClipPlanes; off.Enabled = false; plain.ClipPlanes = off;
                document.CurrentViewpoint.CopyFrom(plain);
                double outputMetres = UnitConversion.ScaleFactor(document.Units, Units.Meters);
                // This real-model fixture's source geometry uses millimetres; output is in metres.
                GeometryInspector inspector = new GeometryInspector(scope, outputMetres / 0.001, 1.0);
                inspector.Scan(document);
                log.AppendLine("ReopenedWithoutClipping=True");
                log.AppendLine("OutputUnits=" + document.Units);
                log.AppendLine("VerifiedVertices=" + inspector.Count + ";OutsideVertices=" + inspector.Outside + ";BoundaryVertices=" + inspector.Boundary);
                log.AppendLine("FirstOutside=" + inspector.FirstOutside);
                bool passed = inspector.Count > 0 && inspector.Outside == 0 && inspector.Boundary > 0;
                log.AppendLine("Passed=" + passed);
                return passed ? 0 : -1;
            }
            catch (Exception exception) { log.AppendLine(exception.ToString()); return -1; }
            finally { File.WriteAllText(Path.Combine(folder, parameters.Length > 1 ? "root-reopen-report.txt" : "reopen-report.txt"), log.ToString()); }
        }
    }

    public sealed class GeometryInspector : COM.InwSimplePrimitivesCB
    {
        private readonly SectionBoxScope _scope;
        private readonly double _scale, _tolerance;
        private readonly double[] _matrix = new double[16];
        public long Count, Outside, Boundary;
        public string FirstOutside;
        public GeometryInspector(SectionBoxScope scope, double scale, double tolerance) { _scope = scope; _scale = scale; _tolerance = tolerance; }
        public void Scan(Document document)
        {
            foreach (ModelItem item in document.Models.RootItemDescendantsAndSelf)
            {
                if (!item.HasGeometry) continue;
                COM.InwOaPath3 path = (COM.InwOaPath3)ComApiBridge.ToInwOaPath(item);
                foreach (COM.InwOaFragment3 fragment in path.Fragments())
                {
                    Array values = (Array)((COM.InwLTransform3f3)fragment.GetLocalToWorldMatrix()).Matrix;
                    for (int i = 0; i < 16; i++) _matrix[i] = Convert.ToDouble(values.GetValue(values.GetLowerBound(0)+i));
                    fragment.GenerateSimplePrimitives(COM.nwEVertexProperty.eNONE, this);
                }
            }
        }
        public void Triangle(COM.InwSimpleVertex a, COM.InwSimpleVertex b, COM.InwSimpleVertex c) { Check(a); Check(b); Check(c); }
        public void Line(COM.InwSimpleVertex a, COM.InwSimpleVertex b) { Check(a); Check(b); }
        public void Point(COM.InwSimpleVertex a) { Check(a); }
        public void SnapPoint(COM.InwSimpleVertex a) { }
        private void Check(COM.InwSimpleVertex vertex)
        {
            Array v = (Array)vertex.coord; int l = v.GetLowerBound(0);
            double x = Convert.ToDouble(v.GetValue(l)), y = Convert.ToDouble(v.GetValue(l+1)), z = Convert.ToDouble(v.GetValue(l+2));
            Point3D world = new Point3D((x*_matrix[0]+y*_matrix[4]+z*_matrix[8]+_matrix[12])*_scale,
                (x*_matrix[1]+y*_matrix[5]+z*_matrix[9]+_matrix[13])*_scale,
                (x*_matrix[2]+y*_matrix[6]+z*_matrix[10]+_matrix[14])*_scale);
            Point3D p = _scope.ToLocal(world);
            Count++;
            if (p.X < _scope.Minimum.X-_tolerance || p.X > _scope.Maximum.X+_tolerance || p.Y < _scope.Minimum.Y-_tolerance || p.Y > _scope.Maximum.Y+_tolerance || p.Z < _scope.Minimum.Z-_tolerance || p.Z > _scope.Maximum.Z+_tolerance)
            { Outside++; if (FirstOutside == null) FirstOutside = p.ToString(); }
            if (Math.Abs(p.X-_scope.Minimum.X)<_tolerance || Math.Abs(p.X-_scope.Maximum.X)<_tolerance || Math.Abs(p.Y-_scope.Minimum.Y)<_tolerance || Math.Abs(p.Y-_scope.Maximum.Y)<_tolerance || Math.Abs(p.Z-_scope.Minimum.Z)<_tolerance || Math.Abs(p.Z-_scope.Maximum.Z)<_tolerance) Boundary++;
        }
    }
}
