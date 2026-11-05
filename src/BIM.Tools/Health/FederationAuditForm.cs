using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
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
        private readonly Button _map, _export, _assemblies, _children;
        private AuditBaseline _baseline;
        private AuditSnapshot _snapshot;
        private string _contextKey;
        private List<AuditRow> _rows = new List<AuditRow>();
        private bool _ignore, _busy, _childView, _stale, _associationConfirmed, _layoutBusy, _reflowQueued;
        internal FederationAuditForm(Document document) : this(document, new AuditStore()) { }
        internal FederationAuditForm(Document document, AuditStore store)
        {
            Document = document; _store = store; _contextKey=AuditNames.DocumentKey(document==null?"":document.FileName);
            Text = "BIM Tools · Federation Audit"; Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96,96);
            ClientSize = new Size(1100,760); MinimumSize = new Size(760,540); StartPosition = FormStartPosition.CenterScreen;
            BackColor = BimTheme.Paper; ForeColor = BimTheme.Graphite;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
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
            actions.Controls.Add(Action("Save complete model list",205,SaveComplete));
            actions.Controls.Add(Action("Watched views…",150,ManageViews));
            actions.Controls.Add(Action("Compare Lists…",145,delegate{using(AuditCompareListsForm dialog=new AuditCompareListsForm(_store,Document.FileName))dialog.ShowDialog(this);}));
            Button check = Action("CHECK NOW",120,CheckNow); check.BackColor = BimTheme.Brand; check.ForeColor = Color.White; actions.Controls.Add(check);
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
                    layout.RowStyles[1].Height=actionHeight+Math.Max(46,Font.Height+22);
                    files.RowStyles[0].Height=FlowHeight(filters)+filters.Margin.Vertical;
                    detail.RowStyles[0].Height=Font.Height+6;
                    detail.RowStyles[3].Height=Math.Max(_map.GetPreferredSize(Size.Empty).Height+_map.Margin.Vertical,Font.Height+28);
                    layout.RowStyles[2].Height=Font.Height+6;
                    layout.RowStyles[4].Height=Font.Height+10;
                    layout.RowStyles[5].Height=Font.Height+8;
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
            if(Document!=null){Document.Models.CollectionChanged += Changed; Document.FileNameChanged += Changed;}
            FormClosing += delegate(object sender,FormClosingEventArgs e) { if(_busy)e.Cancel=true; };
            FormClosed += delegate { if(Document!=null&&!Document.IsDisposed){Document.Models.CollectionChanged-=Changed; Document.FileNameChanged-=Changed;} };
            Shown += delegate { BimWindowLayout.FitToWorkingArea(this); if(Document!=null)Safe(delegate { Reload(null); CheckNow(); }); };
        }
        private Button Action(string text,int width,System.Action action)
        { Button button=new Button { Text=text, AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, MinimumSize=new Size(width,Math.Max(32,Font.Height+14)), Padding=new Padding(8,2,8,2), FlatStyle=FlatStyle.Flat, BackColor=BimTheme.Secondary }; button.Click+=delegate { Safe(action); }; return button; }
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
            if(_busy)return;
            try { EnsureDocument(); action(); }
            catch(Exception error) { _status.Text=error.Message; BimDialog.ShowError(this,"Federation Audit",error.Message,error.ToString()); }
        }
        private void EnsureDocument()
        { if(Document==null || Document.IsDisposed || !ReferenceEquals(Document,NavisApplication.ActiveDocument)) throw new InvalidOperationException("Open Federation Audit again for the active document."); }
        private string ViewCaption()
        { return Document==null?"Check loaded models against your saved complete list":"Current model: "+AuditNames.ModelName(Document.FileName,Document.Title); }
        private void Changed(object sender,EventArgs args)
        {
            string key=AuditNames.DocumentKey(Document.FileName);
            if(!_busy&&!AuditNames.Same(key,_contextKey)){_contextKey=key;_snapshot=null;_rows.Clear();Reload(null);}
            _viewInfo.Text=ViewCaption();_stale=true; _status.Text="The model list changed. Check again after loading finishes."; _summary.Text="Previous results need to be checked again."; _export.Enabled=false; _map.Enabled=false;
        }
        internal void SynchronizeContext()
        { if(Document!=null&&!Document.IsDisposed){if(!AuditNames.Same(AuditNames.DocumentKey(Document.FileName),_contextKey))Changed(null,EventArgs.Empty);else Reload(null);} }
        private bool ReadCurrent()
        {
            AuditSnapshot snapshot=new AuditSnapshot(); _busy=true; AuditWatcher.Suspended=true;
            try { if(!SharingProgressForm.Run(this,"Federation Audit · Read model list",new AuditDiscovery().Read(Document,snapshot))) { _status.Text="Check cancelled. The saved complete list was retained."; return false; } }
            finally { _busy=false; AuditWatcher.Suspended=false; }
            bool switched=!AuditNames.Same(_contextKey,AuditNames.DocumentKey(snapshot.DocumentPath));
            _snapshot=snapshot; _contextKey=AuditNames.DocumentKey(snapshot.DocumentPath);_viewInfo.Text=ViewCaption();if(switched)Reload(null);_stale=false; return true;
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
            if(warnings.Count>0) MessageBox.Show(this,"Some saved complete lists could not be read. Their files were retained.\r\n"+string.Join("\r\n",warnings),"Federation Audit",MessageBoxButtons.OK,MessageBoxIcon.Warning);
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
            SynchronizeContext();
            if(_baseline!=null&&!AuditNames.SameDocument(_baseline.Snapshot.DocumentPath,Document.FileName))
                throw new InvalidOperationException("The complete list does not match the open model.\r\n\r\nSaved for: "+AuditNames.ModelName(_baseline.Snapshot.DocumentPath,_baseline.Name)+"\r\nOpen model: "+AuditNames.ModelName(Document.FileName,Document.Title)+"\r\n\r\nReopen Federation Audit to load the matching complete list.");
            if(!ReadCurrent())return; Recompare();
            _status.Text="Checked "+DateTime.Now.ToString("HH:mm:ss")+" · "+_snapshot.Files.Count+" file entries. The saved complete list was not changed.";
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
            if(_busy||Document==null||Document.IsDisposed||!AuditNames.SameDocument(snapshot.DocumentPath,Document.FileName))return;
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
            if(!ReadCurrent())return;
            Recompare();
            if(_snapshot.Files.Count==0)throw new InvalidOperationException("No model files are loaded. The complete list was not changed.");
            using(Form dialog=new Form { Text="Save complete model list", Font=Font, ClientSize=new Size(520,285), StartPosition=FormStartPosition.CenterParent, ShowInTaskbar=false, FormBorderStyle=FormBorderStyle.FixedDialog, MaximizeBox=false, MinimizeBox=false, AutoScaleMode=AutoScaleMode.Dpi })
            {
                Label explanation=new Label { Location=new Point(18,15), Size=new Size(480,52), Text="Confirm that all expected models are loaded. Files missing before this list is saved cannot be detected from this list." };
                TextBox name=new TextBox { Location=new Point(18,75), Width=480, MaxLength=200, ReadOnly=true, Text=AuditNames.ModelName(Document.FileName,Document.Title) };
                CheckBox replace=new CheckBox { Location=new Point(18,112), Size=new Size(480,30), Text="Replace the selected list and keep its previous version", Enabled=_baseline!=null };
                CheckBox watch=new CheckBox { Location=new Point(18,148), Size=new Size(480,30), Text="Check this view automatically on open / refresh", Checked=_auto.Checked };
                Button save=new Button { Location=new Point(290,230), Size=new Size(110,32), Text="Continue", DialogResult=DialogResult.OK };
                Button cancel=new Button { Location=new Point(408,230), Size=new Size(90,32), Text="Cancel", DialogResult=DialogResult.Cancel };
                dialog.Controls.AddRange(new Control[]{explanation,name,replace,watch,save,cancel}); dialog.AcceptButton=save; dialog.CancelButton=cancel;
                // Content sizes follow the host font; Navisworks can supply a larger UI font than Windows.
                dialog.AutoSize=true;dialog.AutoSizeMode=AutoSizeMode.GrowAndShrink;
                TableLayoutPanel fields=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(18),Width=520};
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
                explanation.AutoSize=true;explanation.MaximumSize=new Size(480,0);explanation.Margin=new Padding(0,0,0,12);
                name.Dock=DockStyle.Top;name.Margin=new Padding(0,0,0,10);
                replace.AutoSize=true;replace.MaximumSize=new Size(480,0);watch.AutoSize=true;watch.MaximumSize=new Size(480,0);
                FlowLayoutPanel buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Margin=new Padding(0,16,0,0)};
                foreach(Button button in new[]{cancel,save}){button.AutoSize=true;button.MinimumSize=new Size(100,Font.Height+18);buttons.Controls.Add(button);}
                fields.Controls.Add(explanation);fields.Controls.Add(name);fields.Controls.Add(replace);fields.Controls.Add(watch);fields.Controls.Add(buttons);
                dialog.Controls.Clear();dialog.Controls.Add(fields);
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                AuditBaseline next=new AuditBaseline { Name=name.Text.Trim(), Snapshot=_snapshot.Copy(), AutoCheck=watch.Checked, Revision=replace.Checked?_baseline.Revision+1:1 };
                if(string.IsNullOrWhiteSpace(next.Name))throw new InvalidOperationException("Enter a name for the complete model list.");
                if(replace.Checked)next.Id=_baseline.Id;
                List<AuditRow> changes=replace.Checked?AuditComparison.Compare(_baseline.Snapshot,_snapshot):new List<AuditRow>();
                string message="Save "+_snapshot.Files.Count(f=>f.Assembly)+" assemblies and "+_snapshot.Files.Count(f=>!f.Assembly)+" contained files as the complete list?";
                if(replace.Checked)message+="\r\nThis replaces expectations: "+changes.Count(r=>r.Status==AuditStatus.Missing)+" missing, "+changes.Count(r=>r.Status==AuditStatus.New)+" new, "+changes.Count(r=>r.Status==AuditStatus.Review||r.Status==AuditStatus.SourceChanged)+" changed or unresolved. The previous version is retained.";
                int limited=_snapshot.Files.Count(f=>f.LimitedChildren||!f.Readable); if(limited>0)message+="\r\n"+limited+" entries have limited readable coverage. Only exposed files can be checked.";
                if(MessageBox.Show(this,message,"Confirm complete model list",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                _store.SetAutomatic(next,next.AutoCheck); Reload(next.Id); AuditWatcher.Queue(); _status.Text="Complete list saved independently of the Navisworks document.";
            }
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
        { if(disposing) { foreach(PictureBox picture in Controls.OfType<TableLayoutPanel>().SelectMany(p=>p.Controls.OfType<Panel>()).SelectMany(p=>p.Controls.OfType<PictureBox>())) if(picture.Image!=null)picture.Image.Dispose(); } base.Dispose(disposing); }
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
            form.FormClosed+=delegate {if(ReferenceEquals(_form,form))_form=null;form.Dispose();};form.Show();form.Activate();
        }
    }
}
