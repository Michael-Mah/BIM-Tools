using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using BIM.Tools.Models;
using BIM.Tools.UI;
using NavisApplication = Autodesk.Navisworks.Api.Application;

namespace BIM.Tools.Health
{
#if !AUDIT_PROBE
    [Plugin("BIM.FederationAudit.Watcher","BIMT",DisplayName="Federation Audit monitoring")]
#endif
    public sealed class AuditWatcher : EventWatcherPlugin
    {
        private static AuditWatcher _instance;
        internal static bool Suspended;
        private Document _document;
        private Timer _timer;
        private DateTime _due;
        private bool _pending, _advancing;
        private int _generation, _scanGeneration;
        private IEnumerator<SharingProgress> _work;
        private AuditBaseline _baseline;
        private AuditSnapshot _snapshot;
        private string _lastAlert;
        private Form _notice;
        private readonly AuditStore _store;
        internal int CompletedChecks { get; private set; }
#if AUDIT_PROBE
        internal int NotificationCount;
#endif
        public AuditWatcher() : this(new AuditStore()) { }
        internal AuditWatcher(AuditStore store) { _store=store; }
        public override void OnLoaded()
        {
            _instance=this; NavisApplication.ActiveDocumentChanged+=ActiveChanged;
            _timer=new Timer { Interval=250 }; _timer.Tick+=Tick; _timer.Start(); Hook();
        }
        public override void OnUnloading()
        {
            NavisApplication.ActiveDocumentChanged-=ActiveChanged; Unhook();
            if(_timer!=null){_timer.Stop();_timer.Dispose();} Cancel(); if(_notice!=null)_notice.Close();
            if(ReferenceEquals(_instance,this))_instance=null;
        }
        internal static void Queue() { if(_instance!=null)_instance.Changed(null,EventArgs.Empty); }
        private void ActiveChanged(object sender,EventArgs args) { Hook(); }
        private void Unhook()
        { if(_document!=null&&!_document.IsDisposed){_document.Models.CollectionChanged-=Changed;_document.FileNameChanged-=Changed;} _document=null; }
        private void Hook()
        { Cancel(); Unhook(); _lastAlert=null; _document=NavisApplication.ActiveDocument; if(_document!=null&&!_document.IsDisposed){_document.Models.CollectionChanged+=Changed;_document.FileNameChanged+=Changed;Changed(null,EventArgs.Empty);} }
        private void Changed(object sender,EventArgs args) { _generation++; _pending=true; _due=DateTime.UtcNow.AddSeconds(4); }
        private void Cancel() { if(_work!=null){_work.Dispose();_work=null;} }
        private void Tick(object sender,EventArgs args)
        {
            if(_advancing||Suspended||_document==null||_document.IsDisposed)return;
            _advancing=true;
            try
            {
                if(_work!=null&&_generation!=_scanGeneration)Cancel();
                if(_work==null)
                {
                    if(!_pending||DateTime.UtcNow<_due)return; _pending=false;
                    string documentPath=AuditNames.Clean(_document.FileName); if(string.IsNullOrEmpty(documentPath))return;
                    List<string> warnings=new List<string>();
                    _baseline=_store.Load(warnings).FirstOrDefault(b=>b.AutoCheck&&AuditNames.Same(b.Snapshot.DocumentPath,documentPath));
                    if(_baseline==null)return;
                    _snapshot=new AuditSnapshot(); _scanGeneration=_generation; _work=new AuditDiscovery().Read(_document,_snapshot).GetEnumerator();
                }
                Stopwatch budget=Stopwatch.StartNew();
                while(budget.ElapsedMilliseconds<35)
                {
                    if(_work.MoveNext())continue;
                    Cancel(); if(_scanGeneration!=_generation)return;
                    List<AuditRow> rows=AuditComparison.Compare(_baseline.Snapshot,_snapshot);
                    CompletedChecks++;
                    FederationAuditWindow.Receive(_baseline,_snapshot);
                    List<AuditRow> issues=rows.Where(r=>r.Status!=AuditStatus.Loaded).ToList();
                    int limited=_snapshot.Files.Count(f=>f.LimitedChildren);
                    if(issues.Count==0&&limited==0){_lastAlert=null;return;}
                    string fingerprint=_baseline.Id+"|"+string.Join("|",issues.Select(r=>r.File.Name+":"+r.Parent+":"+r.Status).OrderBy(s=>s))+"|"+limited;
                    if(fingerprint!=_lastAlert){_lastAlert=fingerprint;Notify("Model list needs attention",issues.Count(r=>r.Status==AuditStatus.Missing)+" missing file entries · "+issues.Count(r=>r.Status!=AuditStatus.Missing)+" other changes\r\n"+limited+" containers with limited child-file coverage.");}
                    return;
                }
            }
            catch(Exception error)
            {
                Cancel(); string fingerprint="error:"+error.Message;
                if(_lastAlert!=fingerprint){_lastAlert=fingerprint;Notify("Automatic check could not finish","Open Federation Audit to check the loaded model list again.");}
            }
            finally {_advancing=false;}
        }
        private void Notify(string title,string text)
        {
#if AUDIT_PROBE
            NotificationCount++; return;
#else
            if(_notice!=null&&!_notice.IsDisposed)_notice.Close();
            Form notice=new Form { Text="Federation Audit", Font=new Font("Segoe UI",9F), ClientSize=new Size(440,155), FormBorderStyle=FormBorderStyle.FixedToolWindow, ShowInTaskbar=false, StartPosition=FormStartPosition.Manual, AutoScaleMode=AutoScaleMode.Dpi };
            notice.Controls.Add(new Label { Text=title, ForeColor=BimTheme.Brand, Font=new Font("Segoe UI",11F,FontStyle.Bold), Location=new Point(16,12), Size=new Size(405,28) });
            notice.Controls.Add(new Label { Text=text, Location=new Point(16,46), Size=new Size(405,55) });
            Button open=new Button { Text="Open Federation Audit",Location=new Point(225,111),Size=new Size(195,30) };
            open.Click+=delegate {notice.Close();FederationAuditWindow.Open(NavisApplication.ActiveDocument);};notice.Controls.Add(open);
            Timer lifetime=new Timer {Interval=30000}; lifetime.Tick+=delegate {notice.Close();};
            notice.FormClosed+=delegate {lifetime.Dispose();notice.Dispose();};
            notice.Shown+=delegate {Rectangle screen=Screen.FromControl(notice).WorkingArea;notice.Location=new Point(screen.Right-notice.Width-16,screen.Bottom-notice.Height-16);BimWindowLayout.FitToWorkingArea(notice);lifetime.Start();};
            _notice=notice;notice.Show();
#endif
        }
    }
}
