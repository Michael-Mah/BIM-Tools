using BIM.Tools.Models;
using BIM.Tools.Services;
using BIM.Tools.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;

namespace BIM.Tools.WorkflowProbe
{
    [Plugin("BIM.SharingWorkflowProbe", "BIMT")]
    public sealed class SharingWorkflowProbePlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            string folder = parameters[0]; Directory.CreateDirectory(folder); StringBuilder log = new StringBuilder();
            try
            {
                Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                log.AppendLine("Product=" + typeof(ScopedModelSharingForm).Assembly.FullName);
                if (typeof(ScopedModelSharingForm).Assembly.GetName().Version != new Version("1.4.1.0")) throw new Exception("Wrong product assembly loaded.");
                bool oldAccessorFailed=false;
                using(VariantData numericId=VariantData.FromInt32(17449713))
                {
                    try { numericId.ToDisplayString(); } catch(NotSupportedException) { oldAccessorFailed=true; }
                    CheckIdentifier(numericId,"17449713");
                }
                if(!oldAccessorFailed) throw new Exception("Numeric Element ID regression did not reproduce the old accessor failure.");
#if !NAVISWORKS2024 && !NAVISWORKS2025
                using(VariantData value=VariantData.FromInt64(4294967296L)) CheckIdentifier(value,"4294967296");
                using(VariantData value=VariantData.FromNat32(uint.MaxValue)) CheckIdentifier(value,"4294967295");
                using(VariantData value=VariantData.FromNat64(ulong.MaxValue)) CheckIdentifier(value,"18446744073709551615");
#endif
                using(VariantData value=VariantData.FromDisplayString("element-123")) CheckIdentifier(value,"element-123");
                using(VariantData value=VariantData.FromIdentifierString("unique-456")) CheckIdentifier(value,"unique-456");
                using(VariantData value=VariantData.FromNone()) CheckIdentifier(value,string.Empty);
                using(VariantData value=VariantData.FromPoint3D(1,2,3)) CheckIdentifier(value,string.Empty);
                CheckIdentifier(null,string.Empty);
                log.AppendLine("Numeric Element ID old accessor failure reproduced; typed signed/unsigned IDs, display/identifier strings and optional metadata fallback=Passed");
                string realName = SharingModelDiscovery.DisplayFileName("Example-Services.rvt - 3D View", "0.svf?acmsession=private", "3D%20View.svf?acmsession=private", 1);
                if (realName != "Example-Services.rvt") throw new Exception("ACC model name failed: " + realName);
                if (SharingModelDiscovery.DisplayFileName(null,"0.svf?acmsession=private",null,2).Contains("private")) throw new Exception("Session URI leaked.");
                if (SharingModelDiscovery.DisplayFileName(null,@"C:\models\test model.nwc",null,3) != "test model.nwc") throw new Exception("Local basename failed.");
                log.AppendLine("ACC filenames and technical-URI suppression=Passed");
                ModelItem chosen = doc.Models.RootItemDescendantsAndSelf.First(i => i.HasGeometry && !i.BoundingBox().IsEmpty);
                Point3D center = chosen.BoundingBox().Center; Vector3D half = new Vector3D(2500,2500,2500);
                Viewpoint view = doc.CurrentViewpoint.CreateCopy();
                ClipPlaneCompatibility.ConfigureOrientedBox(view, new BoundingBox3D(center.Subtract(half),center.Add(half)), Rotation3D.CreateFromEulerAngles(.12,.08,.25));
                doc.CurrentViewpoint.CopyFrom(view);
                SectionBoxScope scope; string reason; if (!SectionBoxScope.TryRead(doc.ActiveView,out scope,out reason)) throw new Exception(reason);
                List<SharingSource> all = new List<SharingSource>(), inside = new List<SharingSource>(); SharingModelDiscovery discovery = new SharingModelDiscovery();
                SharingContentCache cache=null;
                Stopwatch watch = Stopwatch.StartNew(); int steps = 0;
                foreach (SharingProgress step in discovery.DetectCached(doc,scope,null,inside,c=>cache=c)) steps++;
                log.AppendLine("Sources=" + all.Count + ";InsideModels=" + inside.Count + ";Elements=" + inside.Sum(s=>s.Elements.Count) + ";Steps=" + steps + ";Seconds=" + watch.Elapsed.TotalSeconds.ToString("0.00"));
                if (inside.Count == 0 || inside.Any(s=>s.Elements.Count == 0)) throw new Exception("Scope auto discovery empty.");
                watch.Restart();
                SharingSummary summary = new ScopedModelSharingService().ExportWithProgress(doc,scope,inside,Path.Combine(folder,"auto-scope.ifc"),()=>false,step=>System.Windows.Forms.Application.DoEvents());
                log.AppendLine("Cached IFC seconds=" + watch.Elapsed.TotalSeconds.ToString("0.00") + ";Bytes=" + summary.Bytes);
                if (summary.Objects != inside.Sum(s=>s.Elements.Count)) throw new Exception("Preview/export mismatch: " + summary.Objects + " vs " + inside.Sum(s=>s.Elements.Count));
                File.WriteAllText(Path.Combine(folder,"auto-scope.json"),scope.RawJson);
                log.AppendLine("Exact automatic preview/export geometry count=Passed;Objects=" + summary.Objects);
                ScopedModelSharingService service=new ScopedModelSharingService();
                watch.Restart(); SharingSummary obj=service.ExportWithProgress(doc,scope,inside,Path.Combine(folder,"auto-scope.obj"),()=>false,step=>System.Windows.Forms.Application.DoEvents());
                log.AppendLine("Cached OBJ seconds="+watch.Elapsed.TotalSeconds.ToString("0.00")+";Bytes="+obj.Bytes+";Objects="+obj.Objects+";Triangles="+obj.Triangles);
                List<double> ifcTimes=new List<double>(),objTimes=new List<double>();
                for(int trial=0;trial<3;trial++) foreach(string ext in (trial%2==0 ? new[]{"obj","ifc"} : new[]{"ifc","obj"}))
                {
                    watch.Restart(); service.ExportWithProgress(doc,scope,inside,Path.Combine(folder,"timing."+ext),()=>false,null);
                    if(ext=="ifc") ifcTimes.Add(watch.Elapsed.TotalSeconds); else objTimes.Add(watch.Elapsed.TotalSeconds);
                }
                log.AppendLine("Alternating timing trials IFC="+string.Join(",",ifcTimes.Select(t=>t.ToString("0.00")))+";OBJ="+string.Join(",",objTimes.Select(t=>t.ToString("0.00"))));
                if(obj.Objects!=summary.Objects || obj.Triangles!=summary.Triangles || obj.Lines!=summary.Lines || obj.Points!=summary.Points) throw new Exception("OBJ/IFC geometry differs.");
                service.PackageForTransfer(obj); service.PackageForTransfer(summary);
                log.AppendLine("IFC ZIP bytes="+summary.PackageBytes+";OBJ ZIP bytes="+obj.PackageBytes);
                string cancelledObj=Path.Combine(folder,"preserve-existing.obj"); File.WriteAllText(cancelledObj,"previous complete output");
                bool stopped=false;
                try { service.ExportWithProgress(doc,scope,inside,cancelledObj,()=>true,null); } catch(OperationCanceledException) { stopped=true; }
                if(!stopped || File.ReadAllText(cancelledObj)!="previous complete output") throw new Exception("OBJ cancellation replaced previous output.");
                log.AppendLine("OBJ cancellation preserves old output=Passed");
                foreach(string extension in new[]{"nwc","nwd"})
                {
                    watch.Restart(); SharingSummary native=service.ExportWithProgress(doc,scope,inside,Path.Combine(folder,"auto-scope-native."+extension),()=>false,step=>System.Windows.Forms.Application.DoEvents());
                    log.AppendLine("Cached "+extension.ToUpperInvariant()+" seconds="+watch.Elapsed.TotalSeconds.ToString("0.00")+";Bytes="+native.Bytes+";Objects="+native.Objects);
                }
                foreach (double[] change in new[] { new[]{3000.0,0.0,.25},new[]{1800.0,0.0,.25},new[]{1800.0,700.0,.25},new[]{2200.0,0.0,.45},new[]{2500.0,0.0,.25} })
                {
                    Vector3D size=new Vector3D(change[0],change[0],change[0]); Point3D at=center.Add(new Vector3D(change[1],0,0));
                    Viewpoint changed=doc.CurrentViewpoint.CreateCopy();
                    ClipPlaneCompatibility.ConfigureOrientedBox(changed, new BoundingBox3D(at.Subtract(size),at.Add(size)),Rotation3D.CreateFromEulerAngles(.12,.08,change[2]));
                    doc.CurrentViewpoint.CopyFrom(changed);
                    SectionBoxScope nextScope; if(!SectionBoxScope.TryRead(doc.ActiveView,out nextScope,out reason)) throw new Exception(reason);
                    List<SharingSource> warm=new List<SharingSource>(), cold=new List<SharingSource>(); SharingContentCache next=null;
                    foreach(SharingProgress step in discovery.DetectCached(doc,nextScope,cache,warm,c=>next=c)) { }
                    foreach(SharingProgress step in discovery.DetectCached(doc,nextScope,null,cold,c=>{})) { }
                    if(!new HashSet<ModelItem>(warm.SelectMany(s=>s.Elements).Select(e=>e.Item)).SetEquals(cold.SelectMany(s=>s.Elements).Select(e=>e.Item))) throw new Exception("Incremental refresh differs from full scan.");
                    log.AppendLine("Incremental/full equivalence half="+change[0]+" shift="+change[1]+" angle="+change[2]+";Items="+warm.Sum(s=>s.Elements.Count)+";Reused="+next.Reused+";NewBounds="+next.BoundsRead+";Passed");
                    cache=next;
                }
                doc.CurrentViewpoint.CopyFrom(view);
                if (inside.SelectMany(s=>s.Elements).Any(e=>string.IsNullOrWhiteSpace(e.Name))) throw new Exception("An element preview name is blank.");
                if (parameters.Length > 1 && parameters[1] == "preview")
                {
                    using (SharingElementsForm elements = new SharingElementsForm(inside[0])) Save(elements,Path.Combine(folder,"elements-inside-box.png"));
                    log.AppendLine("Element-name fallback and final preview=Passed");
                    File.WriteAllText(Path.Combine(folder,"workflow-report.txt"),log.ToString()); return 0;
                }
                SavedViewpoint saved = new SavedViewpoint(doc.CurrentViewpoint.CreateCopy()) { DisplayName = "Coordination sample box" };
                FolderItem group = new FolderItem { DisplayName = "Custom saved views" }; group.Children.Add(saved); doc.SavedViewpoints.AddCopy(group);
                Viewpoint noBox = doc.CurrentViewpoint.CreateCopy(); ClipPlaneCompatibility.SetEnabled(noBox, false);
                doc.SavedViewpoints.AddCopy(new SavedViewpoint(noBox) { DisplayName = "View without box" });
                List<SavedBoxViewpoint> savedViews = new List<SavedBoxViewpoint>(); foreach (SharingProgress step in new SavedBoxViewpoints().Read(doc,savedViews)) { }
                SavedBoxViewpoint entry = savedViews.Single(v=>v.Path == "Custom saved views / Coordination sample box");
                if (!entry.HasBox || savedViews.Single(v=>v.Path == "View without box").HasBox) throw new Exception("Saved box validation failed.");
                doc.CurrentViewpoint.CopyFrom(noBox); SavedBoxViewpoints.Activate(doc,entry);
                SectionBoxScope restored; if (!SectionBoxScope.TryRead(doc.ActiveView,out restored,out reason) || restored.RawJson != scope.RawJson) throw new Exception("Saved box rotation/range changed.");
                using (SavedBoxPickerForm picker = new SavedBoxPickerForm(savedViews)) Save(picker,Path.Combine(folder,"saved-box-picker.png"));
                log.AppendLine("Nested arbitrary Saved Viewpoint + rotation + no-box rejection=Passed");
                string secondFile = Path.Combine(folder,"second-source.nwd");
                File.Copy(doc.FileName,secondFile,true); doc.AppendFile(secondFile); doc.CurrentViewpoint.CopyFrom(view);
                all.Clear(); inside.Clear();
                foreach (SharingProgress step in discovery.Discover(doc,all)) { }
                foreach (SharingProgress step in discovery.Scan(doc,scope,all,inside)) { }
                if (inside.Count != 2) throw new Exception("Two-model fixture discovery failed.");
                using (ScopedModelSharingForm form = new ScopedModelSharingForm(doc))
                {
                    form.ShowInTaskbar = false; form.Show(); System.Windows.Forms.Application.DoEvents();
                    HashSet<SharingSource> checkedModels = (HashSet<SharingSource>)Field(form,"_checked");
                    List<SharingSource> detected = (List<SharingSource>)Field(form,"_sources");
                    if(detected.Count!=0) throw new Exception("Opening the form started detection.");
                    Call(form,"ReadScope");
                    if(detected.Count!=0) throw new Exception("Confirming the box started detection.");
                    Call(form,"DetectContent",false);
                    if(((Label[])Field(form,"_formatTimes")).Any(label=>!label.Text.StartsWith("Est. ") || label.Text.Contains("unavailable"))) throw new Exception("Four format estimates were not calculated after Detect.");
                    log.AppendLine("Four format estimates after explicit Detect=Passed");
                    if (checkedModels.Count != detected.Count || detected.Count != inside.Count) throw new Exception("Auto check failed.");
                    ((ListView)Field(form,"_models")).Items[0].Checked = false;
                    if (checkedModels.Count != detected.Count - 1) throw new Exception("Uncheck failed.");
                    SharingSummary excluded = new ScopedModelSharingService().Export(doc,scope,checkedModels.ToList(),Path.Combine(folder,"uncheck-exclusion.ifc"),()=>false,null);
                    if (excluded.Objects != checkedModels.Sum(s=>s.Elements.Count) || excluded.Sources.Length != 1) throw new Exception("Unchecked model was not excluded.");
                    log.AppendLine("Two models in box; unchecked model excluded from IFC=Passed;RetainedObjects=" + excluded.Objects);
                    watch.Restart(); Call(form,"DetectContent",true);
                    if(checkedModels.Count!=detected.Count-1) throw new Exception("Refresh lost a manual exclusion.");
                    SharingContentCache reused=(SharingContentCache)Field(form,"_cache");
                    if(reused.BoundsRead!=0 || reused.GeometryChecked!=0 || reused.Reused==0) throw new Exception("Unchanged refresh re-read geometry.");
                    log.AppendLine("Unchanged refresh seconds="+watch.Elapsed.TotalSeconds.ToString("0.00")+";BoundsRead="+reused.BoundsRead+";GeometryChecked="+reused.GeometryChecked+";Reused="+reused.Reused);
                    Call(form,"DetectContent",false); if(checkedModels.Count!=detected.Count) throw new Exception("Full Detect did not check all models.");
                    Save(form,Path.Combine(folder,"automatic-model-sharing.png"));
                    using (SharingElementsForm elements = new SharingElementsForm(detected[0])) Save(elements,Path.Combine(folder,"elements-inside-box.png"));
                    int exportTicks=0; bool sawPopup=false;
                    using(Timer responsiveness=new Timer {Interval=10})
                    {
                        responsiveness.Tick+=delegate {exportTicks++; foreach(Form popup in System.Windows.Forms.Application.OpenForms) if(popup.Text.Contains("Export · Estimation time")) {sawPopup=true; if(exportTicks==1) Save(popup,Path.Combine(folder,"export-estimate.png"));} };
                        responsiveness.Start(); Call(form,"ExportToFile",Path.Combine(folder,"ui-export.obj")); responsiveness.Stop();
                    }
                    if(!File.Exists(Path.Combine(folder,"ui-export.zip")) || !sawPopup || exportTicks<2 || ((ProgressBar)Field(form,"_progress")).Value!=100) throw new Exception("Export ETA/progress workflow failed: " + ((Label)Field(form,"_status")).Text);
                    Save(form,Path.Combine(folder,"export-progress-completed.png"));
                    log.AppendLine("Actual UI export + estimated-time popup + footer progress=Passed;HeartbeatTicks="+exportTicks);
                    if(typeof(ScopedModelSharingForm).GetMethod("AddSelectedModels",BindingFlags.Instance|BindingFlags.NonPublic)!=null) throw new Exception("Removed manual-add workflow remains.");
                    form.Close();
                }
                log.AppendLine("Explicit Detect / manual uncheck / incremental Refresh preserves exclusions / manual Add removed=Passed");
                int ticks = 0;
                using (Timer heartbeat = new Timer { Interval = 10 })
                {
                    heartbeat.Tick += delegate { ticks++; }; heartbeat.Start();
                    if (!SharingProgressForm.Run(null,"UI responsiveness stress",Stress(102940))) throw new Exception("Progress did not complete.");
                    heartbeat.Stop();
                }
                if (ticks < 2) throw new Exception("Message loop did not yield.");
                log.AppendLine("102940 bounded-work iterations; UI heartbeat ticks=" + ticks + ";Passed");
                int cancelledSteps = 0;
                using (Timer cancel = new Timer { Interval = 80 })
                {
                    cancel.Tick += delegate { foreach (Form progress in System.Windows.Forms.Application.OpenForms) if (progress.Text == "Cancel verification") { Save(progress,Path.Combine(folder,"operation-progress.png")); progress.Close(); cancel.Stop(); break; } };
                    cancel.Start(); bool completed = SharingProgressForm.Run(null,"Cancel verification",Cancelable(()=>cancelledSteps++));
                    if (completed || cancelledSteps == 0) throw new Exception("Cancel did not stop iterator.");
                }
                log.AppendLine("Progress cancellation=Passed;Steps=" + cancelledSteps);
                File.WriteAllText(Path.Combine(folder,"workflow-report.txt"),log.ToString()); return 0;
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(folder,"workflow-report.txt"),log + "\r\n" + error); return -1; }
        }
        private static void CheckIdentifier(VariantData value,string expected)
        {
            string actual=ScopedModelSharingService.ReadSourceIdentifier(value);
            if(actual!=expected) throw new Exception("Source identifier conversion failed: " + actual + " vs " + expected);
        }
        private static IEnumerable<SharingProgress> Stress(int count)
        { for (int i=0;i<count;i++) { if (i % 128 == 0) System.Threading.Thread.SpinWait(80000); yield return new SharingProgress("Selected model matching","Item " + i,i+1,count); } }
        private static IEnumerable<SharingProgress> Cancelable(Action step)
        { for(int i=0;i<1000000;i++) { step(); System.Threading.Thread.SpinWait(80000); yield return new SharingProgress("Cancellation test","Item " + i,i,1000000); } }
        private static object Field(object target,string name) { return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target); }
        private static void Call(object target,string name,params object[] args) { target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args); }
        private static void Save(Control control,string path)
        {
            Form form = control as Form;
            if (form != null && !form.Visible) { form.ShowInTaskbar = false; form.Show(); form.Location = new Point(-20000,-20000); System.Windows.Forms.Application.DoEvents(); }
            control.CreateControl(); control.PerformLayout(); using(Bitmap bitmap = new Bitmap(control.Width,control.Height)) { control.DrawToBitmap(bitmap,new Rectangle(Point.Empty,control.Size)); bitmap.Save(path); }
        }
    }
}
