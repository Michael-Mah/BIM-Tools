using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
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
            if(phase=="initial")
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
