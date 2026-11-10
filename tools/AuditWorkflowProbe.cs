using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using BIM.Tools.Health;
using BIM.Tools.Models;
using NavisApplication=Autodesk.Navisworks.Api.Application;

[Plugin("BIM.AuditWorkflowProbe","BIMT")]
public sealed class AuditWorkflowProbe : AddInPlugin
{
    private readonly StringBuilder log=new StringBuilder();
    private void Check(bool condition,string message){if(!condition)throw new Exception(message);log.AppendLine("PASS "+message);}
    private AuditSnapshot Read(Document document){AuditSnapshot snapshot=new AuditSnapshot();foreach(SharingProgress progress in new AuditDiscovery().Read(document,snapshot)){}return snapshot;}
    public override int Execute(params string[] args)
    {
        string folder=args[0],phase=args[1];Directory.CreateDirectory(folder);
        try
        {
            Document document=NavisApplication.ActiveDocument;AuditStore store=new AuditStore(Path.Combine(folder,"baselines"));
            log.AppendLine("API="+typeof(Document).Assembly.GetName().Version+" Phase="+phase);
            if(phase=="interaction")
                CheckModelessSave(document,store,folder);
            else if(phase=="initial")
            {
                document.AppendFile(args[2]);document.SaveFile(Path.Combine(folder,"live.nwf"));
                Model[] models=document.Models.ToArray();int selected=document.CurrentSelection.SelectedItems.Count;
                Stopwatch watch=Stopwatch.StartNew();AuditSnapshot snapshot=Read(document);log.AppendLine("DiscoveryMs="+watch.ElapsedMilliseconds+" Models="+models.Length+" Files="+snapshot.Files.Count);
                Check(snapshot.Files.Count==2&&snapshot.Files.All(f=>f.Assembly&&f.Readable),"Actual source filenames discovered from two IFC models");
                Check(snapshot.Files.All(f=>f.Name.EndsWith(".ifc",StringComparison.OrdinalIgnoreCase)),"Geometry hierarchy is not misclassified as source files");
                Check(document.Models.SequenceEqual(models)&&document.CurrentSelection.SelectedItems.Count==selected,"File-list read leaves models and user selection unchanged");
                AuditBaseline baseline=new AuditBaseline{Name="Synthetic complete model list",Snapshot=snapshot};store.Save(baseline);
                File.WriteAllText(Path.Combine(folder,"baseline-id.txt"),baseline.Id);
                Check(store.Load(new List<string>()).Single().Snapshot.Files.Count==2,"Actual host model list saved independently of the NWF");
                Preview(document,store,baseline,snapshot,folder);
                AuditStore compareStore=new AuditStore(Path.Combine(folder,"compare-baselines"));compareStore.Save(baseline);
                AuditBaseline other=baseline.Copy();other.Id=Guid.NewGuid().ToString("D");other.Name="Second federation";other.Snapshot.DocumentPath=Path.Combine(folder,"second.nwf");other.Snapshot.Files.RemoveAt(1);compareStore.Save(other);
                using(AuditCompareListsForm compare=new AuditCompareListsForm(compareStore,document.FileName))
                {
                    compare.Opacity=0;compare.ShowInTaskbar=false;compare.Show();System.Windows.Forms.Application.DoEvents();
                    foreach(Size size in new[]{new Size(1060,720),new Size(760,540)})
                    {
                        compare.ClientSize=size;for(int i=0;i<4;i++){compare.PerformLayout();System.Windows.Forms.Application.DoEvents();}
                        DataGridView grid=Descendants(compare).OfType<DataGridView>().Single();
                        Check(grid.Rows.Count==2&&grid.Height>=80,"Compare Lists renders both recorded inventories at "+size.Width);
                        using(Bitmap bitmap=new Bitmap(compare.Width,compare.Height)){compare.DrawToBitmap(bitmap,new Rectangle(Point.Empty,compare.Size));bitmap.Save(Path.Combine(folder,"compare-"+size.Width+".png"));}
                    }
                    compare.Close();
                }
                document.SaveFile(Path.Combine(folder,"packed.nwd"));
            }
            else
            {
                AuditBaseline baseline=store.Load(new List<string>()).Single();
                if(phase=="rename"||phase=="moved")document.AppendFile(args[2]);
                AuditSnapshot snapshot=Read(document);List<AuditRow> rows=AuditComparison.Compare(baseline.Snapshot,snapshot);
                foreach(AuditFile file in snapshot.Files)log.AppendLine(file.Name+" | "+file.Source+" | parent="+file.ParentId+" limited="+file.LimitedChildren);
                if(phase=="reopen")Check(rows.Count==2&&rows.All(r=>r.Status==AuditStatus.Loaded),"Saved NWF reopens with both expected source models matched");
                if(phase=="missing")
                {
                    Check(rows.Count(r=>r.Status==AuditStatus.Missing)==1&&rows.Count(r=>r.Status==AuditStatus.Loaded)==1,"Actual dropped model appears as a missing expected file");
                    Check(store.Load(new List<string>()).Single().Snapshot.Files.Count==2,"Missing-file check does not overwrite the complete list");
                    document.SaveFile(Path.Combine(folder,"first-user-chosen-view.nwf"));
                    AuditBaseline automatic=baseline.Copy();automatic.Id=Guid.NewGuid().ToString("D");automatic.Snapshot.DocumentPath=AuditNames.Clean(document.FileName);automatic.AutoCheck=true;store.Save(automatic);
                    AuditWatcher watcher=new AuditWatcher(store);watcher.OnLoaded();
                    try
                    {
                        using(FederationAuditForm form=new FederationAuditForm(document,store))
                        {
                            typeof(FederationAuditWindow).GetField("_form",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,form);
                            form.Opacity=0;form.ShowInTaskbar=false;form.Show();System.Windows.Forms.Application.DoEvents();
                            PumpUntil(delegate{return watcher.CompletedChecks>=1;});
                            Check(watcher.NotificationCount==1,"Opt-in automatic check reports the missing model after loading settles");
                            AuditWatcher.Queue();PumpUntil(delegate{return watcher.CompletedChecks>=2;});
                            Check(watcher.NotificationCount==1,"Unchanged problems do not repeatedly notify");
                            string firstPath=document.FileName,secondPath=Path.Combine(folder,"second-user-chosen-view.nwf");
                            AuditBaseline second=new AuditBaseline{Name="Another user-chosen view",Snapshot=snapshot.Copy()};second.Snapshot.DocumentPath=secondPath;store.SetAutomatic(second,true);
                            document.SaveFile(secondPath);PumpUntil(delegate{return watcher.CompletedChecks>=3;});
                            AuditBaseline displayed=(AuditBaseline)typeof(FederationAuditForm).GetField("_baseline",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
                            AuditSnapshot displayedSnapshot=(AuditSnapshot)typeof(FederationAuditForm).GetField("_snapshot",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
                            Check(displayed.Id==second.Id&&AuditNames.SameDocument(displayedSnapshot.DocumentPath,secondPath),"Open audit window switches to the new view and receives its own automatic results");
                            Check(watcher.NotificationCount==1,"Complete second view does not inherit the first view's missing model warning");
                            document.SaveFile(Path.Combine(folder,"unregistered-view.nwf"));PauseForWatch();
                            Check(watcher.CompletedChecks==3,"Unregistered view is not checked against another view's complete list");
                            document.SaveFile(firstPath);PumpUntil(delegate{return watcher.CompletedChecks>=4;});
                            displayed=(AuditBaseline)typeof(FederationAuditForm).GetField("_baseline",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
                            Check(displayed.Id==automatic.Id&&watcher.NotificationCount==2,"Returning to an enabled view restores its own expected models and missing warning");
                            store.SetAutomatic(automatic,false);AuditWatcher.Queue();PauseForWatch();
                            Check(watcher.CompletedChecks==4&&store.EnabledFor(secondPath).Id==second.Id,"Disabling one watched view stops its checks while the other remains enabled");
                            Check(store.Load(new List<string>()).Single(b=>b.Id==automatic.Id).Snapshot.Files.Count==2,"View switching and opt-out never overwrite saved model expectations");
                            form.Close();typeof(FederationAuditWindow).GetField("_form",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,null);
                        }
                    }
                    finally{watcher.OnUnloading();}
                }
                if(phase=="rename")Check(rows.Any(r=>r.Status==AuditStatus.Review&&r.Current==null&&r.Candidates.Count>0),"Actual renamed source is suggested for confirmation");
                if(phase=="moved")Check(rows.Count(r=>r.Status==AuditStatus.SourceChanged)==1,"Actual same-name source at another path is marked changed");
                if(phase=="packed")
                {
                    Check(snapshot.Files.Count>=2&&snapshot.Files.Any(f=>f.Name.Contains("GEN-STR"))&&snapshot.Files.Any(f=>f.Name.Contains("GEN-MEP")),"Packed NWD exposes original source model identities");
                }
            }
            File.WriteAllText(Path.Combine(folder,phase+"-report.txt"),log.ToString());return 0;
        }
        catch(Exception error){log.AppendLine(error.ToString());File.WriteAllText(Path.Combine(folder,phase+"-report.txt"),log.ToString());return 1;}
    }
    private void CheckModelessSave(Document document, AuditStore store, string folder)
    {
        int expectedFiles = Read(document).Files.Count;
        Check(expectedFiles > 0, "The native test document exposes model files");
        using (Form peer = new Form { Opacity = 0, ShowInTaskbar = false })
        using (FederationAuditForm form = new FederationAuditForm(document, store) { Opacity = 0, ShowInTaskbar = false })
        {
            peer.Show();
            IntPtr[] hostWindows = EnabledThreadWindows();
            bool shown = false;
            form.Shown += delegate
            {
                Check(FindButton(form, "CANCEL CHECK") != null, "Opening audit schedules the initial read and returns");
                CheckModeless(form, hostWindows, "initial check");
                shown = true;
            };
            form.Show();
            PumpUntil(() => shown && FindButton(form, "CHECK NOW") != null);
            FindButton(form, "CHECK NOW").PerformClick();
            CheckModeless(form, hostWindows, "manual check");
            Check(FindButton(form, "CANCEL CHECK") != null, "Manual check returns before discovery completes");
            FindButton(form, "CANCEL CHECK").PerformClick();
            Check(!AuditWatcher.Suspended && FindButton(form, "CHECK NOW") != null, "Cancel releases the scan and restores the check action");

            OpenSaveReview(form);
            CheckModeless(form, hostWindows, "save review");
            foreach (float points in new[] { 9F, 13.5F, 18F })
            using (Font font = new Font("Segoe UI", points))
            {
                Font original = form.Font;
                form.Font = font;
                form.ClientSize = new Size(760, 540);
                form.PerformLayout();
                System.Windows.Forms.Application.DoEvents();
                foreach (string label in new[] { "Cancel", "Confirm save" })
                {
                    Button button = FindButton(form, label);
                    Point origin = form.PointToClient(button.PointToScreen(Point.Empty));
                    Check(form.ClientRectangle.Contains(new Rectangle(origin, button.Size)) &&
                        button.Height >= TextRenderer.MeasureText(button.Text, button.Font).Height,
                        label + " is fully visible at 760x540 and " + points + " pt");
                }
                form.Font = original;
            }
            document.CurrentSelection.CopyFrom(new[] { document.Models.First().RootItem });
            Check(document.CurrentSelection.SelectedItems.Count == 1, "Navisworks selection can change during the save review");
            FindButton(form, "Cancel").PerformClick();
            Check(store.Load(new List<string>()).Count == 0, "Cancel leaves the store untouched");

            OpenSaveReview(form);
            FindButton(form, "Confirm save").PerformClick();
            AuditBaseline saved = store.Load(new List<string>()).Single();
            Check(saved.Snapshot.Files.Count == expectedFiles && saved.Revision == 1, "Confirm writes exactly one complete list");
            CheckModeless(form, hostWindows, "save completion");

            OpenSaveReview(form);
            Descendants(form).OfType<CheckBox>().Single(control => control.Visible && control.Text.StartsWith("Replace")).Checked = true;
            FindButton(form, "Confirm save").PerformClick();
            AuditBaseline replaced = store.Load(new List<string>()).Single();
            Check(replaced.Id == saved.Id && replaced.Revision == 2, "Replacement preserves the list identity and increments its revision");

            OpenSaveReview(form);
            document.SaveFile(Path.Combine(folder, "switched.nwf"));
            Check(FindButton(form, "Confirm save") == null &&
                store.Load(new List<string>()).Single().SavedUtc == replaced.SavedUtc,
                "Changing the document invalidates the review without writing a list");
            FindButton(form, "CHECK NOW").PerformClick();
            form.Close();
            Check(!AuditWatcher.Suspended, "Closing during a check releases the watcher");
        }
    }
    private void CheckModeless(Form form, IntPtr[] hostWindows, string stage)
    {
        Check(!form.Modal && form.Enabled &&
            hostWindows.All(window => !IsWindow(window) || IsWindowEnabled(window)) &&
            !System.Windows.Forms.Application.OpenForms.Cast<Form>().Any(open => open.Modal),
            "Navisworks UI thread windows stay enabled during " + stage);
    }
    private static Button FindButton(Form form, string text)
    {
        return Descendants(form).OfType<Button>().SingleOrDefault(button => button.Visible && button.Text == text);
    }
    private static void OpenSaveReview(Form form)
    {
        FindButton(form, "Save complete model list").PerformClick();
        PumpUntil(() => FindButton(form, "Confirm save") != null);
    }
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumWindowCallback callback, IntPtr data);
    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    private static IntPtr[] EnabledThreadWindows()
    {
        List<IntPtr> windows = new List<IntPtr>();
        EnumThreadWindows(GetCurrentThreadId(), (window, data) =>
        {
            if (IsWindowEnabled(window)) windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }
    private static void PumpUntil(Func<bool> complete)
    {Stopwatch watch=Stopwatch.StartNew();while(!complete()&&watch.ElapsedMilliseconds<20000){System.Windows.Forms.Application.DoEvents();Thread.Sleep(20);}if(!complete())throw new Exception("Automatic check did not complete in time.");}
    private static void PauseForWatch()
    {Stopwatch watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<5200){System.Windows.Forms.Application.DoEvents();Thread.Sleep(20);}}
    private void Preview(Document doc,AuditStore store,AuditBaseline baseline,AuditSnapshot snapshot,string folder)
    {
        using(FederationAuditForm form=new FederationAuditForm(doc,store))
        {
            form.Opacity=0;form.ShowInTaskbar=false;form.Show();System.Windows.Forms.Application.DoEvents();
            AuditBaseline comparison=baseline.Copy();comparison.Snapshot.Files[0].Source="C:/Previous-source/"+comparison.Snapshot.Files[0].Name;
            typeof(FederationAuditForm).GetField("_baseline",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,comparison);
            typeof(FederationAuditForm).GetField("_snapshot",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,snapshot);
            typeof(FederationAuditForm).GetMethod("Recompare",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,null);
            foreach(Size size in new[]{new Size(1100,760),new Size(850,580),new Size(760,540)})
            {
                form.ClientSize=size;form.CreateControl();form.PerformLayout();
                System.Windows.Forms.Application.DoEvents();
                DataGridView grid=Descendants(form).OfType<DataGridView>().Single();
                Check(grid.Visible&&grid.Width>=250&&grid.Height>=100,"Native UI table remains usable at "+size.Width+"x"+size.Height);
                TextBox details=(TextBox)typeof(FederationAuditForm).GetField("_details",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
                Point detailOrigin=form.PointToClient(details.PointToScreen(Point.Empty));
                Check(detailOrigin.X+details.Width<=form.ClientSize.Width&&detailOrigin.X>=0,"Details pane stays inside the viewport at "+size.Width);
                Check(Descendants(form).OfType<FlowLayoutPanel>().All(p=>p.Controls.Cast<Control>().All(c=>c.Bottom<=p.ClientSize.Height)),"Wrapped action rows do not clip controls at "+size.Width);
                Check(!Descendants(form).Any(c=>c.Text.Contains("Illustrative")||c.Text.Contains("Model Check")),"Production UI has no draft labels at "+size.Width);
                using(Bitmap bitmap=new Bitmap(form.Width,form.Height))
                {
                    form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));int blue=0;
                    for(int y=0;y<bitmap.Height;y+=16)for(int x=0;x<bitmap.Width;x+=16)if(bitmap.GetPixel(x,y).ToArgb()==BIM.Tools.UI.BimTheme.Brand.ToArgb())blue++;
                    Check(blue>10,"Native UI header and child controls actually paint at "+size.Width);
                    bitmap.Save(Path.Combine(folder,"ui-"+size.Width+".png"));
                }
            }
            form.Close();
        }
    }
    private static IEnumerable<Control> Descendants(Control root){foreach(Control child in root.Controls){yield return child;foreach(Control nested in Descendants(child))yield return nested;}}
}
