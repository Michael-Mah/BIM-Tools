using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using BIM.Tools.Models;
using BIM.Tools.UI;
using Color = System.Drawing.Color;
using NavisApplication = Autodesk.Navisworks.Api.Application;

namespace BIM.Tools.Health
{
    internal sealed class FederationAuditForm : Form
    {
        internal Document Document { get; private set; }
        private readonly AuditStore _store;
        private readonly ComboBox _baselines;
        private readonly CheckBox _auto, _problems;
        private readonly TextBox _search, _details;
        private readonly DataGridView _grid;
        private readonly ListBox _candidates;
        private readonly Label _summary, _status, _coverage, _baselineInfo, _viewInfo;
        private readonly Button _map, _export, _assemblies, _children, _check, _save;
        private readonly TableLayoutPanel _layout;
        private readonly Timer _scanTimer = new Timer { Interval = 15 };
        private IEnumerator<SharingProgress> _scan;
        private AuditSnapshot _scanSnapshot;
        private System.Action _afterScan;
        private Control _saveReview;
        private AuditBaseline _baseline;
        private AuditSnapshot _snapshot;
        private string _contextKey;
        private List<AuditRow> _rows = new List<AuditRow>();
        private string _loadWarning = "";
        private bool _ignore, _childView, _stale, _associationConfirmed, _layoutBusy, _reflowQueued;
        internal FederationAuditForm(Document document) : this(document, new AuditStore()) { }
        internal FederationAuditForm(Document document, AuditStore store)
        {
            Document = document; _store = store; _contextKey=AuditNames.DocumentKey(document==null?"":document.FileName);
            Text = "BIM Tools · Federation Audit"; Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96,96);
            ClientSize = new Size(1100,760); MinimumSize = new Size(760,540); StartPosition = FormStartPosition.CenterScreen;
            BackColor = BimTheme.Paper; ForeColor = BimTheme.Graphite;
            TableLayoutPanel layout = _layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach (float height in new[] { 72F,132F,30F }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));
            Panel header = new Panel { Size=new Size(1100,88), Dock = DockStyle.Fill, BackColor = BimTheme.Brand };
            using (Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("BIM.FederationAuditIcon"))
                if (stream != null) using (Image source = Image.FromStream(stream)) header.Controls.Add(new PictureBox { Location = new Point(18,16), Size = new Size(40,40), Image = new Bitmap(source), SizeMode = PictureBoxSizeMode.Zoom });
            header.Controls.Add(new Label { Location=new Point(72,10), AutoSize=true, ForeColor=Color.White, Font=new Font("Segoe UI",12F), Text="FEDERATION AUDIT" });
            _viewInfo=new Label { Location=new Point(72,46), Size=new Size(990,28), Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right, ForeColor=Color.White, Font=new Font("Segoe UI",8.5F), Text=ViewCaption(), AutoEllipsis=true }; header.Controls.Add(_viewInfo);
            layout.Controls.Add(header,0,0);

            TableLayoutPanel settings = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16,10,16,0), ColumnCount = 1, RowCount = 2 };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute,76)); settings.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            actions.Controls.Add(new Label { Text = "Complete list", AutoSize=true, Padding=new Padding(0,7,4,0) });
            _baselines = new ComboBox { Width = 245, DropDownStyle = ComboBoxStyle.DropDownList }; actions.Controls.Add(_baselines);
            _save = Action("Save complete model list",205,SaveComplete);
            actions.Controls.Add(_save);
            actions.Controls.Add(Action("Watched views…",150,ManageViews));
            actions.Controls.Add(Action("Compare Lists…",145,delegate{using(AuditCompareListsForm dialog=new AuditCompareListsForm(_store,Document.FileName))dialog.ShowDialog(this);}));
            _check = Action("CHECK NOW",120,CheckNow,true);
            _check.BackColor = BimTheme.Brand; _check.ForeColor = Color.White; actions.Controls.Add(_check);
            _export = Action("Export CSV",100,Export); actions.Controls.Add(_export); settings.Controls.Add(actions,0,0);
            TableLayoutPanel metadata = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount=1 };
            metadata.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            metadata.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); metadata.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,TextRenderer.MeasureText("Check on open / refresh",Font).Width+34));
            _baselineInfo = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = BimTheme.Muted };
            _auto = new CheckBox { Text = "Check on open / refresh", AutoSize=true, Anchor=AnchorStyles.Top|AnchorStyles.Right };
            metadata.Controls.Add(_baselineInfo,0,0); metadata.Controls.Add(_auto,1,0); settings.Controls.Add(metadata,0,1); layout.Controls.Add(settings,0,1);
            _summary = new Label { Dock = DockStyle.Fill, Padding = new Padding(16,5,16,0), ForeColor = BimTheme.Brand }; layout.Controls.Add(_summary,0,2);

            TableLayoutPanel workspace = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16,0,16,0), ColumnCount = 2, RowCount=1 };
            workspace.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,68)); workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,32));
            TableLayoutPanel files = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0,0,12,0) };
            files.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            files.RowStyles.Add(new RowStyle(SizeType.Absolute,78)); files.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            FlowLayoutPanel filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            _assemblies = Action("Assemblies",105,delegate { _childView=false; Render(); }); _children = Action("Contained models",140,delegate { _childView=true; Render(); });
            _problems = new CheckBox { Text = "Problems only", AutoSize=true, Padding=new Padding(0,5,0,0), Checked = true };
            _search = new TextBox { Width = 150 }; filters.Controls.AddRange(new Control[] { _assemblies,_children,_problems,_search }); files.Controls.Add(filters,0,0);
            _grid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Color.White, RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None };
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText="Model file", AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill, FillWeight=45 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText="Parent assembly", AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill, FillWeight=32 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText="State", Width=TextRenderer.MeasureText("Changed source",Font).Width+22 });
            _grid.RowTemplate.Height=Math.Max(26,Font.Height+8);
            _grid.ColumnHeadersHeight=Math.Max(30,Font.Height+12);
            foreach(DataGridViewColumn column in _grid.Columns) column.SortMode=DataGridViewColumnSortMode.NotSortable;
            files.Controls.Add(_grid,0,1); workspace.Controls.Add(files,0,0);
            TableLayoutPanel detail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute,30)); detail.RowStyles.Add(new RowStyle(SizeType.Percent,60)); detail.RowStyles.Add(new RowStyle(SizeType.Percent,40)); detail.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
            detail.Controls.Add(new Label { Text="MODEL DETAILS", Dock=DockStyle.Fill, ForeColor=BimTheme.Brand },0,0);
            _details = new TextBox { Dock=DockStyle.Fill, Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical, BackColor=Color.White, BorderStyle=BorderStyle.FixedSingle };
            _candidates = new ListBox { Dock=DockStyle.Fill, HorizontalScrollbar=true, IntegralHeight=false };
            _map=Action("Review mapping…",230,ReviewMapping); _map.MinimumSize=Size.Empty; _map.AutoSize=false; _map.Dock=DockStyle.Fill;
            detail.Controls.Add(_details,0,1); detail.Controls.Add(_candidates,0,2); detail.Controls.Add(_map,0,3); workspace.Controls.Add(detail,1,0); layout.Controls.Add(workspace,0,3);
            _coverage = new Label { Dock=DockStyle.Fill, Padding=new Padding(16,8,16,0), ForeColor=BimTheme.Warning, AutoEllipsis=true }; layout.Controls.Add(_coverage,0,4);
            _status = new Label { Dock=DockStyle.Fill, Padding=new Padding(16,5,16,0), BackColor=BimTheme.Secondary, AutoEllipsis=true }; layout.Controls.Add(_status,0,5); Controls.Add(layout);
            LayoutEventHandler reflow=delegate
            {
                if(_layoutBusy)return; _layoutBusy=true;
                try
                {
                    int actionHeight=FlowHeight(actions)+actions.Margin.Vertical;settings.RowStyles[0].Height=actionHeight;
                    metadata.ColumnStyles[1].Width=_auto.GetPreferredSize(Size.Empty).Width+16;
                    _grid.ColumnHeadersHeight=Font.Height+12;
                    _grid.RowTemplate.Height=Font.Height+8;
                    int metadataHeight = Math.Max(_auto.GetPreferredSize(Size.Empty).Height + _auto.Margin.Vertical, Font.Height);
                    layout.RowStyles[1].Height = actionHeight + metadataHeight + metadata.Margin.Vertical
                        + settings.Padding.Vertical + settings.Margin.Vertical + 4;
                    files.RowStyles[0].Height=FlowHeight(filters)+filters.Margin.Vertical;
                    detail.RowStyles[0].Height=Font.Height+6;
                    detail.RowStyles[3].Height=Math.Max(_map.GetPreferredSize(Size.Empty).Height+_map.Margin.Vertical,Font.Height+28);
                    layout.RowStyles[2].Height = Font.Height + _summary.Padding.Vertical + _summary.Margin.Vertical;
                    layout.RowStyles[4].Height = Font.Height + _coverage.Padding.Vertical + _coverage.Margin.Vertical;
                    layout.RowStyles[5].Height = Font.Height + _status.Padding.Vertical + _status.Margin.Vertical;
                }
                finally{_layoutBusy=false;}
            };
            layout.Layout+=reflow;actions.Layout+=reflow;filters.Layout+=reflow;
            EventHandler queueReflow=delegate
            {
                if(!IsHandleCreated||_reflowQueued)return;_reflowQueued=true;
                BeginInvoke((System.Action)delegate
                {
                    try
                    {
                        if(IsDisposed)return;
                        for(int pass=0;pass<3;pass++){reflow(this,null);layout.PerformLayout();actions.PerformLayout();filters.PerformLayout();}
                    }
                    finally{_reflowQueued=false;}
                });
            };
            ClientSizeChanged+=queueReflow;actions.SizeChanged+=queueReflow;filters.SizeChanged+=queueReflow;
            _baselines.SelectedIndexChanged += delegate { if(!_ignore) SelectBaseline(); };
            _auto.CheckedChanged += delegate { if(!_ignore) Safe(ChangeAuto); };
            _problems.CheckedChanged += delegate { Render(); }; _search.TextChanged += delegate { Render(); };
            _grid.SelectionChanged += delegate { ShowDetails(); }; _candidates.SelectedIndexChanged += delegate { UpdateMappingButton(); };
            _scanTimer.Tick += AdvanceRead;
            if (Document != null)
            {
                Document.Models.CollectionChanged += Changed;
                Document.FileNameChanged += Changed;
                NavisApplication.ActiveDocumentChanged += ActiveDocumentChanged;
            }
            Shown += delegate
            {
                BimWindowLayout.FitToWorkingArea(this);
                if (Document != null && _scan == null && _saveReview == null) Safe(CheckNow);
            };
        }
        private Button Action(string text,int width,System.Action action,bool allowWhileChecking=false)
        { Button button=new Button { Text=text, AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, MinimumSize=new Size(width,Math.Max(32,Font.Height+14)), Padding=new Padding(8,2,8,2), FlatStyle=FlatStyle.Flat, BackColor=BimTheme.Secondary }; button.Click+=delegate { if(_scan==null||allowWhileChecking)Safe(action); }; return button; }
        private static int FlowHeight(FlowLayoutPanel panel)
        {
            int width=Math.Max(1,panel.ClientSize.Width-panel.Padding.Horizontal),x=0,row=0,total=0;
            foreach(Control control in panel.Controls)
            {
                Size natural=control.AutoSize?control.GetPreferredSize(Size.Empty):control.Size;
                int w=Math.Max(control.MinimumSize.Width,natural.Width)+control.Margin.Horizontal;
                int h=Math.Max(control.MinimumSize.Height,natural.Height)+control.Margin.Vertical;
                if(x>0&&x+w>width){total+=row;x=0;row=0;}
                x+=w;row=Math.Max(row,h);
            }
            int measured=panel.Controls.Cast<Control>().Select(c=>c.Bottom+c.Margin.Bottom).DefaultIfEmpty(0).Max()+4;
            return Math.Max(total+row+panel.Padding.Vertical+4,measured);
        }
        private void Safe(System.Action action)
        {
            try { EnsureDocument(); action(); }
            catch(Exception error) { EndSaveReview(); _status.Text=error.Message; }
        }
        private void EnsureDocument()
        { if(Document==null || Document.IsDisposed || !ReferenceEquals(Document,NavisApplication.ActiveDocument)) throw new InvalidOperationException("Open Federation Audit again for the active document."); }
        private string ViewCaption()
        { return Document==null?"Check loaded models against your saved complete list":"Current model: "+AuditNames.ModelName(Document.FileName,Document.Title); }
        private void Changed(object sender,EventArgs args)
        {
            InvalidateResults("The model list changed. Check again after loading finishes.");
            string key=AuditNames.DocumentKey(Document.FileName);
            if(!AuditNames.Same(key,_contextKey)){_contextKey=key;_snapshot=null;_rows.Clear();Reload(null);}
            _viewInfo.Text=ViewCaption();
        }
        private void ActiveDocumentChanged(object sender, EventArgs args)
        {
            InvalidateResults("The active document changed. Check again for the active model.");
        }
        private void InvalidateResults(string message)
        {
            StopRead();
            EndSaveReview();
            _stale = true;
            _status.Text = message;
            _summary.Text = "Previous results need to be checked again.";
            _export.Enabled = false;
            _map.Enabled = false;
        }
        internal void SynchronizeContext()
        { if(_scan==null&&_saveReview==null&&Document!=null&&!Document.IsDisposed){if(!AuditNames.Same(AuditNames.DocumentKey(Document.FileName),_contextKey))Changed(null,EventArgs.Empty);else Reload(null);} }
        private void ReadCurrent(System.Action afterScan)
        {
            _scanSnapshot = new AuditSnapshot();
            _scan = new AuditDiscovery().Read(Document, _scanSnapshot).GetEnumerator();
            _afterScan = afterScan;
            AuditWatcher.Suspended = true;
            _check.Text = "CANCEL CHECK";
            _save.Enabled = _auto.Enabled = _baselines.Enabled = false;
            _status.Text = "Reading the current model list…";
            _scanTimer.Start();
        }
        private void AdvanceRead(object sender, EventArgs args)
        {
            _scanTimer.Stop();
            IEnumerator<SharingProgress> scan = _scan;
            if (scan == null) return;
            try
            {
                EnsureDocument();
                // Yield to Navisworks between batches. Its model API stays on this UI thread.
                Stopwatch budget = Stopwatch.StartNew();
                do
                {
                    bool hasMore = scan.MoveNext();
                    if (!ReferenceEquals(scan, _scan)) return;
                    if (!hasMore)
                    {
                        AuditSnapshot snapshot = _scanSnapshot;
                        System.Action completed = _afterScan;
                        StopRead();
                        _snapshot = snapshot;
                        _contextKey = AuditNames.DocumentKey(snapshot.DocumentPath);
                        _viewInfo.Text = ViewCaption();
                        _stale = false;
                        Recompare();
                        completed();
                        return;
                    }
                } while (budget.ElapsedMilliseconds < 20);
                _status.Text = scan.Current.Stage + " · " + scan.Current.Detail;
                _scanTimer.Start();
            }
            catch (Exception error)
            {
                StopRead();
                EndSaveReview();
                _status.Text = error.Message;
            }
        }
        private void StopRead()
        {
            if (_scan == null) return;
            _scanTimer.Stop();
            IEnumerator<SharingProgress> scan = _scan;
            _scan = null;
            _scanSnapshot = null;
            _afterScan = null;
            try { scan.Dispose(); }
            finally
            {
                AuditWatcher.Suspended = false;
                _check.Text = "CHECK NOW";
                _save.Enabled = _baselines.Enabled = true;
                _auto.Enabled = _baseline != null;
            }
        }
        private void Reload(string id)
        {
            _ignore=true; List<string> warnings=new List<string>(); List<AuditBaseline> baselines=_store.ForDocument(Document.FileName,warnings);
            _baselines.Items.Clear();
            foreach(AuditBaseline baseline in baselines){baseline.DisplayModelName=AuditNames.ModelName(Document.FileName,Document.Title);_baselines.Items.Add(baseline);}
            _baselines.DropDownWidth=Math.Min(Screen.FromControl(this).WorkingArea.Width-60,Math.Max(_baselines.Width,_baselines.Items.Cast<AuditBaseline>().Select(b=>TextRenderer.MeasureText(b.ToString(),_baselines.Font).Width+36).DefaultIfEmpty(0).Max()));
            int selected=-1;
            for(int i=0;i<_baselines.Items.Count;i++) { AuditBaseline item=(AuditBaseline)_baselines.Items[i]; if((id!=null&&item.Id==id)||(id==null&&selected<0&&AuditNames.SameDocument(item.Snapshot.DocumentPath,Document.FileName))) selected=i; }
            _baselines.SelectedIndex=selected; _ignore=false; SelectBaseline();
            _loadWarning = warnings.Count == 0 ? "" : " " + warnings.Count + " unreadable saved lists retained.";
            if (warnings.Count > 0) _status.Text = _loadWarning.TrimStart();
        }
        private void SelectBaseline()
        {
            _baseline=_baselines.SelectedItem as AuditBaseline; _associationConfirmed=_baseline==null||AuditNames.SameDocument(_baseline.Snapshot.DocumentPath,Document.FileName); _ignore=true;
            if(!_associationConfirmed)_stale=true;
            _auto.Enabled=_baseline!=null; _auto.Checked=_baseline!=null&&_baseline.AutoCheck; _ignore=false;
            _baselineInfo.Text=_baseline==null ? "No complete list selected. Confirm that all models are loaded, then save the list."
                : "Complete list for: "+AuditNames.ModelName(_baseline.Snapshot.DocumentPath,_baseline.Name)+" · "+SavedTime(_baseline.SavedUtc);
            Recompare();
        }
        internal void CheckNow()
        {
            if (_scan != null)
            {
                StopRead();
                _status.Text = "Check cancelled. The saved complete list was retained.";
                return;
            }
            SynchronizeContext();
            if(_baseline!=null&&!AuditNames.SameDocument(_baseline.Snapshot.DocumentPath,Document.FileName))
                throw new InvalidOperationException("The complete list does not match the open model.\r\n\r\nSaved for: "+AuditNames.ModelName(_baseline.Snapshot.DocumentPath,_baseline.Name)+"\r\nOpen model: "+AuditNames.ModelName(Document.FileName,Document.Title)+"\r\n\r\nReopen Federation Audit to load the matching complete list.");
            ReadCurrent(delegate
            {
                _status.Text = "Checked " + DateTime.Now.ToString("HH:mm:ss") + " · " + _snapshot.Files.Count
                    + " file entries. The saved complete list was not changed." + _loadWarning;
            });
        }
        private void Recompare()
        {
            _rows=_snapshot==null?new List<AuditRow>():_baseline==null?_snapshot.Files.Select(f=>new AuditRow { Current=f, Parent=AuditNames.ParentName(_snapshot,f), Status=f.Readable?AuditStatus.Loaded:AuditStatus.Unverifiable, Detail="Loaded file. Save a complete list after verifying all expected models are present." }).ToList():AuditComparison.Compare(_baseline.Snapshot,_snapshot);
            Render();
        }
        private static string SavedTime(string text)
        { DateTime value;return DateTime.TryParse(text,null,System.Globalization.DateTimeStyles.RoundtripKind,out value)?"Saved "+value.ToLocalTime().ToString("dd MMM yyyy HH:mm"):"Saved time unavailable"; }
        internal void Receive(AuditBaseline baseline,AuditSnapshot snapshot)
        {
            if(_scan!=null||_saveReview!=null||Document==null||Document.IsDisposed||!AuditNames.SameDocument(snapshot.DocumentPath,Document.FileName))return;
            if(_baseline==null||!AuditNames.SameDocument(_baseline.Snapshot.DocumentPath,snapshot.DocumentPath))Reload(baseline.Id);
            if(_baseline==null||_baseline.Id!=baseline.Id)return;
            _baseline=baseline;_snapshot=snapshot;_contextKey=AuditNames.DocumentKey(snapshot.DocumentPath);_viewInfo.Text=ViewCaption();_stale=false;Recompare();_status.Text="Automatic file-list check completed at "+DateTime.Now.ToString("HH:mm:ss")+".";
        }
        private void Render()
        {
            if(_grid==null)return;
            _assemblies.BackColor=_childView?BimTheme.Secondary:Color.FromArgb(232,241,252); _children.BackColor=_childView?Color.FromArgb(232,241,252):BimTheme.Secondary;
            IEnumerable<AuditRow> level=_rows.Where(r=>r.File.Assembly!=_childView); string query=_search.Text.Trim();
            _grid.Columns[1].HeaderText=_childView?"Parent assembly":"Current source";
            _grid.Rows.Clear();
            foreach(AuditRow row in level.Where(r=>(_baseline==null||!_problems.Checked||r.Status!=AuditStatus.Loaded)&&(query.Length==0||(r.File.Name+" "+r.Parent).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)).OrderBy(r=>r.Status))
            {
                int index=_grid.Rows.Add(row.File.Name,_childView?row.Parent:row.Current==null?"—":SourceLocation(row.Current),row.Label); _grid.Rows[index].Tag=row;
                _grid.Rows[index].DefaultCellStyle.ForeColor=row.Status==AuditStatus.Missing?BimTheme.Error:row.Status==AuditStatus.Loaded?BimTheme.Graphite:row.Status==AuditStatus.New?BimTheme.Info:BimTheme.Warning;
                _grid.Rows[index].Cells[0].ToolTipText=row.Expected==null?row.Current.Source:row.Expected.Source;
            }
            List<AuditRow> group=level.ToList();
            _summary.Text=_baseline==null ? (_snapshot==null?"Read the current model list to begin.":"No complete list saved · "+group.Count+" file entries loaded.")
                : string.Format("{0} expected · {1} loaded · {2} missing · {3} need review · {4} new",group.Count(r=>r.Expected!=null),group.Count(r=>r.Expected!=null&&r.Current!=null),group.Count(r=>r.Status==AuditStatus.Missing),group.Count(r=>r.Status==AuditStatus.Review||r.Status==AuditStatus.SourceChanged||r.Status==AuditStatus.Unverifiable),group.Count(r=>r.Status==AuditStatus.New));
            int limited=_snapshot==null?0:_snapshot.Files.Count(f=>f.LimitedChildren);
            int unknown=_snapshot==null?0:_snapshot.Files.Count(f=>!f.Readable);
            _coverage.Text=_snapshot==null?"Read the current view's model list to check source-file coverage.":limited+unknown>0 ? limited+" containers do not expose embedded file lists; "+unknown+" unreadable file identities. Internal completeness is not confirmed."
                : _baseline!=null&&_baseline.Snapshot.Files.Any(f=>f.LimitedChildren)?"The saved list has limited embedded-file coverage. Only recorded source files can be checked.":"All exposed source-file entries were read.";
            _coverage.ForeColor=limited+unknown>0||(_baseline!=null&&_baseline.Snapshot.Files.Any(f=>f.LimitedChildren))?BimTheme.Warning:BimTheme.Muted;
            _export.Enabled=_baseline!=null&&_snapshot!=null&&!_stale;
            if(_stale)_summary.Text="The model list changed. Previous results need to be checked again.";
            ShowDetails();
        }
        private AuditRow Selected() { return _grid.CurrentRow==null?null:_grid.CurrentRow.Tag as AuditRow; }
        private void ShowDetails()
        {
            if(_details==null)return; _candidates.Items.Clear(); AuditRow row=Selected();
            _details.Text=row==null?"Select a file to inspect its saved and loaded sources.":row.File.Name+"\r\n"+row.Parent+"\r\n\r\n"+row.Label+"\r\n"+row.Detail+"\r\n\r\nSAVED SOURCE\r\n"+(row.Expected==null?"Not recorded":SourceLocation(row.Expected))+"\r\n\r\nLOADED SOURCE\r\n"+(row.Current==null?"No confirmed match":SourceLocation(row.Current));
            if(row!=null&&_baseline!=null&&row.Status!=AuditStatus.Loaded)
            {
                if(row.Current!=null) _candidates.Items.Add(new AuditCandidate { File=row.Current, Score=1 });
                foreach(AuditCandidate candidate in row.Candidates) _candidates.Items.Add(candidate);
                if(row.Current!=null&&_candidates.Items.Count>0)_candidates.SelectedIndex=0;
            }
            UpdateMappingButton();
        }
        private static string SourceLocation(AuditFile file) { return string.IsNullOrEmpty(file.Source)?"Source location unavailable":file.Source; }
        private void UpdateMappingButton() { _map.Enabled=!_stale&&_baseline!=null&&Selected()!=null&&Selected().Expected!=null&&_candidates.SelectedItem is AuditCandidate; }
        private void SaveComplete()
        {
            if (!AuditNames.Same(AuditNames.DocumentKey(Document.FileName), _contextKey))
                Changed(null, EventArgs.Empty);
            ReadCurrent(ReviewCompleteList);
        }
        private void ReviewCompleteList()
        {
            AuditSnapshot snapshot = _snapshot;
            if (snapshot.Files.Count == 0)
                throw new InvalidOperationException("No model files are loaded. The complete list was not changed.");

            AuditBaseline selected = _baseline;
            string name = (AuditNames.ModelName(Document.FileName, Document.Title) ?? "").Trim();
            TableLayoutPanel review = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(Font.Height),
                ColumnCount = 1, RowCount = 5
            };
            review.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            review.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            review.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int row = 2; row < 5; row++) review.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label title = new Label { Text = "Save complete model list", AutoSize = true, ForeColor = BimTheme.Brand };
            TextBox details = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, BackColor = Color.White
            };
            CheckBox replace = new CheckBox
            {
                Text = "Replace the selected list and keep its previous version",
                AutoSize = true, Enabled = selected != null
            };
            CheckBox watch = new CheckBox
            {
                Text = "Check this view automatically on open / refresh",
                AutoSize = true, Checked = _auto.Checked,
                Enabled = !string.IsNullOrEmpty(AuditNames.DocumentKey(snapshot.DocumentPath))
            };
            int assemblies = snapshot.Files.Count(file => file.Assembly);
            int limited = snapshot.Files.Count(file => file.LimitedChildren || !file.Readable);
            string message = "Model: " + name + "\r\n\r\n"
                + "Confirm that all expected models are loaded. Files missing before this list is saved cannot be detected from this list.\r\n\r\n"
                + "Save " + assemblies + " assemblies and " + (snapshot.Files.Count - assemblies) + " contained files as the complete list?";
            if (limited > 0)
                message += "\r\n\r\n" + limited + " entries have limited readable coverage. Only exposed files can be checked.";
            details.Text = message;
            replace.CheckedChanged += delegate
            {
                details.Text = replace.Checked ? message + "\r\n\r\n" + ReplacementSummary(selected, snapshot) : message;
            };
            Button cancel = Action("Cancel", 100, delegate
            {
                EndSaveReview();
                _status.Text = "Save cancelled. The complete list was not changed.";
            });
            Button save = Action("Confirm save", 120, delegate
            {
                if (_stale || !AuditNames.Same(AuditNames.DocumentKey(Document.FileName), AuditNames.DocumentKey(snapshot.DocumentPath)))
                    throw new InvalidOperationException("The model list changed. Review it again before saving.");
                if (replace.Checked && !_store.ForDocument(snapshot.DocumentPath, new List<string>())
                    .Any(list => list.Id == selected.Id && list.SavedUtc == selected.SavedUtc))
                    throw new InvalidOperationException("The selected complete list changed. Review it again before replacing it.");
                AuditBaseline next = new AuditBaseline
                {
                    Name = name, Snapshot = snapshot.Copy(),
                    Revision = replace.Checked ? selected.Revision + 1 : 1
                };
                if (replace.Checked) next.Id = selected.Id;
                _store.SetAutomatic(next, watch.Checked);
                EndSaveReview();
                Reload(next.Id);
                AuditWatcher.Queue();
                _status.Text = "Complete list saved independently of the Navisworks document.";
            });
            save.BackColor = BimTheme.Brand;
            save.ForeColor = Color.White;
            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false };
            buttons.Controls.AddRange(new Control[] { cancel, save });
            review.Controls.Add(title, 0, 0);
            review.Controls.Add(details, 0, 1);
            review.Controls.Add(replace, 0, 2);
            review.Controls.Add(watch, 0, 3);
            review.Controls.Add(buttons, 0, 4);
            _saveReview = review;
            Controls.Add(review);
            _layout.Hide();
            CancelButton = cancel;
            cancel.Focus();
        }
        private static string ReplacementSummary(AuditBaseline selected, AuditSnapshot snapshot)
        {
            List<AuditRow> changes = AuditComparison.Compare(selected.Snapshot, snapshot);
            return "This replaces expectations: " + changes.Count(row => row.Status == AuditStatus.Missing)
                + " missing, " + changes.Count(row => row.Status == AuditStatus.New)
                + " new, " + changes.Count(row => row.Status == AuditStatus.Review || row.Status == AuditStatus.SourceChanged)
                + " changed or unresolved. The previous version is retained.";
        }
        private void EndSaveReview()
        {
            if (_saveReview == null) return;
            CancelButton = null;
            _saveReview.Dispose();
            _saveReview = null;
            _layout.Show();
        }
        private void ReviewMapping()
        {
            AuditRow row=Selected(); AuditCandidate candidate=_candidates.SelectedItem as AuditCandidate;
            if(row==null||row.Expected==null||candidate==null||_stale)return;
            if(MessageBox.Show(this,"Update the saved reference?\r\n\r\n"+row.Expected.Name+"\r\n"+SourceLocation(row.Expected)+"\r\n\r\n→ "+candidate.File.Name+"\r\n"+SourceLocation(candidate.File)+"\r\n\r\nThis changes the saved complete list only. It does not relink ACC files or append models.","Confirm model mapping",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            AuditBaseline updated=AuditComparison.Map(_baseline,_snapshot,row.Expected.Id,candidate.File.Id); _store.Save(updated); Reload(updated.Id); AuditWatcher.Queue(); _status.Text="Saved reference updated. Other expected files were retained.";
        }
        private void ChangeAuto()
        {
            if(_baseline==null)return;
            try { string id=_baseline.Id;bool enabled=_auto.Checked;_store.SetAutomatic(_baseline,enabled);Reload(id);AuditWatcher.Queue();_status.Text=enabled?"Automatic checks enabled for this view.":"Automatic checks disabled for this view."; }
            catch { _ignore=true; _auto.Checked=_baseline.AutoCheck; _ignore=false; throw; }
        }
        private void ManageViews()
        {
            using(AuditWatchedViewsForm dialog=new AuditWatchedViewsForm(_store))
                if(dialog.ShowDialog(this)==DialogResult.OK){string id=_baseline==null?null:_baseline.Id;Reload(id);AuditWatcher.Queue();_status.Text="Watched views updated. Complete model lists were retained.";}
        }
        private void Export()
        {
            if(_baseline==null||_snapshot==null||_stale)throw new InvalidOperationException("Run a current check before exporting a report.");
            using(SaveFileDialog dialog=new SaveFileDialog { Filter="CSV report (*.csv)|*.csv", FileName="Federation-Audit-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".csv" })
                if(dialog.ShowDialog(this)==DialogResult.OK){AuditComparison.Export(dialog.FileName,_baseline,_snapshot,_rows);_status.Text="Federation Audit report exported.";}
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                StopRead();
                _scanTimer.Dispose();
                if (Document != null)
                {
                    NavisApplication.ActiveDocumentChanged -= ActiveDocumentChanged;
                    if (!Document.IsDisposed)
                    {
                        Document.Models.CollectionChanged -= Changed;
                        Document.FileNameChanged -= Changed;
                    }
                }
                foreach (PictureBox picture in _layout.Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<PictureBox>()))
                    if (picture.Image != null) picture.Image.Dispose();
            }
            base.Dispose(disposing);
        }
    }
    internal static class FederationAuditWindow
    {
        private static FederationAuditForm _form;
        internal static void Receive(AuditBaseline baseline,AuditSnapshot snapshot)
        { if(_form!=null&&!_form.IsDisposed)_form.Receive(baseline,snapshot); }
        internal static void Open(Document document)
        {
            if(document==null||document.IsDisposed)throw new InvalidOperationException("Open a Navisworks document first.");
            if(_form!=null&&!_form.IsDisposed)
            { if(ReferenceEquals(document,_form.Document)){_form.SynchronizeContext();_form.WindowState=FormWindowState.Normal;_form.Show();_form.Activate();return;} _form.Close(); if(!_form.IsDisposed)return; }
            FederationAuditForm form=new FederationAuditForm(document);_form=form;
            form.FormClosed+=delegate {if(ReferenceEquals(_form,form))_form=null;};form.Show();form.Activate();
        }
    }
}
