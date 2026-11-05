using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using BIM.Tools.Models;
using BIM.Tools.Services;
using DrawingColor = System.Drawing.Color;

namespace BIM.Tools.UI
{
    public sealed class ScopedModelSharingForm : Form
    {
        private readonly Document _document;
        private readonly ScopedModelSharingService _service = new ScopedModelSharingService();
        private readonly List<SharingSource> _sources = new List<SharingSource>();
        private readonly HashSet<SharingSource> _checked = new HashSet<SharingSource>();
        private readonly ListView _models;
        private readonly TextBox _search;
        private readonly Label _scopeLabel, _selectionLabel, _status;
        private readonly Button _export, _cancel, _open;
        private readonly Button _detect, _refreshContent;
        private readonly RadioButton[] _formats=new RadioButton[4];
        private readonly Label[] _formatTimes=new Label[4];
        private double _ifcSecondsPerItem,_objSecondsPerItem,_ifcBytesPerItem;
        private string _estimateScopeJson;
        private readonly ProgressBar _progress;
        private readonly Label _estimation;
        private readonly Panel _footer;
        private readonly Panel _body;
        private SectionBoxScope _scope;
        private bool _refreshing, _busy, _cancelRequested;
        private string _output;
        private string _contentsScopeJson;
        private SharingContentCache _cache;
        private bool _hasDetected;
        private int _contentRevision;
        private readonly HashSet<string> _excluded=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private ExportEstimateForm _estimateWindow;
        private readonly Stopwatch _exportClock=new Stopwatch();
        private double _estimatedSeconds;
        private string _activeFormat="IFC";
        public string ModelKey { get; private set; }

        public ScopedModelSharingForm(Document document)
        {
            SuspendLayout();
            _document = document; ModelKey = SectionBoxSettingsStore.GetModelKey(document);
            Text = EditionBrand.ProductName + " · Scoped Model Sharing";
            Font = new Font("Segoe UI", 9F); AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            BackColor = BimTheme.Paper; ForeColor = BimTheme.Graphite;
            ClientSize = new Size(760, 720); MinimumSize = new Size(700, 600);
            StartPosition = FormStartPosition.CenterScreen; MaximizeBox = false;
            Panel header = new Panel { Location = Point.Empty, Size = new Size(760,72), BackColor = BimTheme.Brand, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            using (Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(EditionBrand.SharingIconResource))
            {
                if (stream != null)
                {
                    using (Image image = Image.FromStream(stream))
                    {
                        Bitmap icon = new Bitmap(image);
                        PictureBox mark = new PictureBox { Location = new Point(22,12), Size = new Size(36,36), Image = icon, SizeMode = PictureBoxSizeMode.Zoom };
                        header.Controls.Add(mark); Disposed += delegate { icon.Dispose(); };
                    }
                }
            }
            header.Controls.Add(Label("SCOPED MODEL SHARING", new Point(66,14), new Size(650,26), DrawingColor.White, 14F));
            header.Controls.Add(Label("Section Box → detected models and elements → strictly clipped coordination model", new Point(22,43), new Size(700,22), DrawingColor.White, 9F));
            foreach (Control label in header.Controls) if (label is System.Windows.Forms.Label) label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(header);
            _body = new Panel { Location = new Point(0,72), Size = new Size(760,568), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, AutoScroll = true, AutoScrollMinSize=new Size(0,758) };
            Controls.Add(_body);

            Panel scopeCard = Card(18, 156);
            scopeCard.Controls.Add(Label("1  SECTION BOX", new Point(16,12), new Size(440,22), BimTheme.Brand, 10F));
            _scopeLabel = Label("Read the enabled box in the active view.", new Point(16,40), new Size(670,40), BimTheme.Graphite, 9F);
            scopeCard.Controls.Add(_scopeLabel);
            Button read = Button("READ CURRENT BOX", new Point(16,80), new Size(176,26), false);
            read.Click += delegate { ReadScope(); }; scopeCard.Controls.Add(read);
            Button saved = Button("USE EXISTING BOX", new Point(204,80), new Size(176,26), false);
            saved.Click += delegate { PickSavedBox(); };
            scopeCard.Controls.Add(saved);
            _detect=Button("DETECT SECTION BOX CONTENT",new Point(16,116),new Size(264,28),true);
            _detect.Click+=delegate { DetectContent(false); }; scopeCard.Controls.Add(_detect);
            _refreshContent=Button("REFRESH CONTENT",new Point(292,116),new Size(184,28),false);
            _refreshContent.Click+=delegate { DetectContent(true); }; scopeCard.Controls.Add(_refreshContent);

            Panel modelsCard = Card(186, 304);
            modelsCard.Controls.Add(Label("2  CONTENT TO SHARE", new Point(16,12), new Size(440,22), BimTheme.Brand, 10F));
            modelsCard.Controls.Add(Label("Detect Content checks models automatically. Uncheck any model to exclude it.", new Point(16,38), new Size(670,22), BimTheme.Muted, 9F));
            _search = new TextBox { Location = new Point(16,66), Size = new Size(670,26) };
            _search.TextChanged += delegate { RenderSources(); }; modelsCard.Controls.Add(_search);
            _models = new ListView { Location = new Point(16,99), Size = new Size(670,130), CheckBoxes = true, View = System.Windows.Forms.View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
            _models.Columns.Add("Model file · double-click to view elements", 505);
            _models.Columns.Add("Geometry items", 130);
            _models.Resize += delegate
            {
                int countWidth = (int)(140 * _models.DeviceDpi / 96F);
                _models.Columns[1].Width = countWidth;
                _models.Columns[0].Width = Math.Max(160, _models.ClientSize.Width - countWidth - 8);
            };
            _models.DoubleClick += delegate { ShowElements(); };
            _models.ItemChecked += delegate(object sender, ItemCheckedEventArgs args)
            {
                if (_refreshing) return;
                SharingSource source = args.Item.Tag as SharingSource;
                if (source == null) return;
                if (args.Item.Checked) _checked.Add(source); else _checked.Remove(source);
                if(args.Item.Checked) _excluded.Remove(source.Name); else _excluded.Add(source.Name);
                UpdateSelection();
            };
            modelsCard.Controls.Add(_models);
            Button clear = Button("CLEAR CHECKS", new Point(16,239), new Size(148,28), false);
            clear.Click += delegate { RunOperation("Clear model checks", ClearSteps(), delegate { foreach(SharingSource source in _sources) _excluded.Add(source.Name); _checked.Clear(); RenderSources(); }); }; modelsCard.Controls.Add(clear);
            Button elements = Button("VIEW ELEMENTS", new Point(176,239), new Size(192,28), false);
            elements.Click += delegate { ShowElements(); }; modelsCard.Controls.Add(elements);
            _selectionLabel = Label("0 sources checked", new Point(16,275), new Size(670,22), BimTheme.Muted, 9F);
            modelsCard.Controls.Add(_selectionLabel);

            Panel outputCard = Card(502, 240);
            outputCard.Controls.Add(Label("3  EXPORT FOR COORDINATION", new Point(16,12), new Size(540,22), BimTheme.Brand, 10F));
            outputCard.Controls.Add(Label("All formats permanently cut geometry at the box boundary; cut ends are open.\r\nExtract the ZIP before import. OBJ needs its MTL beside it and units = metres.\r\nWorld coordinates and colours stay; original CAD properties/textures are omitted.", new Point(16,38), new Size(670,60), BimTheme.Muted, 9F));
            int hostYear=typeof(Document).Assembly.GetName().Version.Major+2003;
            string[] titles={"IFC4","OBJ","NWC","NWD"};
            string[] descriptions={"Source identifiers","Geometry + colours","Navisworks "+hostYear,"Navisworks "+hostYear};
            for(int i=0;i<4;i++)
            {
                _formats[i]=new RadioButton {Text=titles[i],Tag=new[]{"ifc","obj","nwc","nwd"}[i],Appearance=Appearance.Button,TextAlign=ContentAlignment.MiddleCenter,FlatStyle=FlatStyle.Flat,Location=new Point(16+i*170,104),Size=new Size(160,28),BackColor=BimTheme.Secondary};
                outputCard.Controls.Add(_formats[i]);
                _formatTimes[i]=Label("Est. time: after Detect\r\n"+descriptions[i],new Point(16+i*170,138),new Size(160,50),BimTheme.Muted,9F);
                outputCard.Controls.Add(_formatTimes[i]);
                _formats[i].CheckedChanged+=delegate(object sender,EventArgs e)
                { RadioButton option=(RadioButton)sender; option.BackColor=option.Checked ? BimTheme.Brand : BimTheme.Secondary; option.ForeColor=option.Checked ? DrawingColor.White : BimTheme.Graphite; if(option.Checked && _export!=null) _export.Text="EXPORT CLIPPED "+((string)option.Tag).ToUpperInvariant(); };
            }
            _formats[1].Checked=true;
            _export = Button("EXPORT CLIPPED OBJ", new Point(16,198), new Size(204,30), true);
            _export.Click += Export; outputCard.Controls.Add(_export);
            _cancel = Button("CANCEL", new Point(232,198), new Size(100,30), false);
            _cancel.Enabled = false; _cancel.Click += delegate { _cancelRequested = true; }; outputCard.Controls.Add(_cancel);
            _open = Button("SHOW OUTPUT", new Point(344,198), new Size(140,30), false);
            _open.Enabled = false;
            _open.Click += delegate { if (File.Exists(_output)) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + _output + "\"")); };
            outputCard.Controls.Add(_open);
            _footer=new Panel {Location=new Point(0,640),Size=new Size(760,80),Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom,BackColor=BimTheme.Paper};
            _progress=new ProgressBar {Location=new Point(22,25),Size=new Size(215,18)};
            _estimation=Label("Estimation time: —",new Point(249,12),new Size(175,56),BimTheme.Muted,9F);
            _status=Label("Confirm a Section Box, then click Detect Content.",new Point(436,10),new Size(302,60),BimTheme.Graphite,9F);
            _status.TextAlign=ContentAlignment.MiddleRight; _status.AutoEllipsis=true;
            _estimation.Click+=delegate { if(_estimateWindow!=null && !_estimateWindow.IsDisposed) { _estimateWindow.Show(this); _estimateWindow.Activate(); } };
            _footer.Controls.AddRange(new Control[] {_progress,_estimation,_status}); Controls.Add(_footer);
            _footer.Resize+=delegate { LayoutFooter(); };
            ResumeLayout(true);
            _document.Models.CollectionChanging+=InvalidateContent;
            _document.Models.ModelTransformChanging+=InvalidateContent;
            _document.FilesUpdating+=InvalidateContent;
            _document.UnitsChanged+=InvalidateContent;
            Disposed+=delegate
            {
                if(_estimateWindow!=null) _estimateWindow.Dispose();
                if(!_document.IsDisposed) { _document.Models.CollectionChanging-=InvalidateContent; _document.Models.ModelTransformChanging-=InvalidateContent; _document.FilesUpdating-=InvalidateContent; _document.UnitsChanged-=InvalidateContent; }
            };
            UpdateSelection();
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            { if (_busy) { _cancelRequested = true; args.Cancel = true; _status.Text = "Cancelling after the current geometry fragment…"; } };
        }

        protected override void OnShown(EventArgs args)
        { base.OnShown(args); BimWindowLayout.FitToWorkingArea(this); LayoutFooter(); }
        protected override void OnDpiChanged(DpiChangedEventArgs args) { base.OnDpiChanged(args); BimWindowLayout.FitToWorkingArea(this); }
        private Panel Card(int top, int height)
        { Panel card = new Panel { Location = new Point(22,top), Size = new Size(706,height), BackColor = BimTheme.Surface, BorderStyle = BorderStyle.FixedSingle }; _body.Controls.Add(card); return card; }
        private static Label Label(string text, Point at, Size size, DrawingColor color, float fontSize)
        { return new Label { Text = text, Location = at, Size = size, ForeColor = color, Font = new Font("Segoe UI", fontSize) }; }
        private static Button Button(string text, Point at, Size size, bool primary)
        { return new Button { Text = text, Location = at, Size = size, FlatStyle = FlatStyle.Flat, BackColor = primary ? BimTheme.Brand : BimTheme.Secondary, ForeColor = primary ? DrawingColor.White : BimTheme.Graphite, UseVisualStyleBackColor = false }; }
        private void ReadScope()
        {
            string reason;
            if (_document.IsDisposed || !SectionBoxScope.TryRead(_document.ActiveView, out _scope, out reason))
            { _scope = null; _scopeLabel.Text = "Enable a native Section Box, then click Read Current Box."; }
            else
            {
                double metres = UnitConversion.ScaleFactor(_document.Units, Units.Meters);
                Vector3D size = _scope.Maximum.Subtract(_scope.Minimum).Multiply(metres);
                _scopeLabel.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Strict clipping · {0:0.###} × {1:0.###} × {2:0.###} m\r\nCaptured from the active native Section Box (rotation preserved).", size.X, size.Y, size.Z);
            }
            UpdateSelection();
            if(_scope!=null) _status.Text="Section Box confirmed. Click Detect Content" + (_hasDetected ? " or Refresh Content." : ".");
        }
        private void DetectContent(bool refresh)
        {
            if (_busy) return;
            ReadScope();
            if (_scope == null) { _status.Text = "Confirm a Section Box before detecting its contents."; return; }
            RefreshSourcesCore(refresh);
        }
        private void RefreshSourcesCore(bool refresh)
        {
            List<SharingSource> inside = new List<SharingSource>(); SharingContentCache next=null;
            bool detected=false;
            SectionBoxScope scope = _scope;
            RunOperation(refresh ? "Refresh Section Box content" : "Detect Section Box content",new SharingModelDiscovery().DetectCached(_document,scope,refresh ? _cache : null,inside,c=>next=c), delegate
            {
                if(!refresh) _excluded.Clear();
                _sources.Clear(); _sources.AddRange(inside.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase));
                _checked.Clear(); foreach (SharingSource source in _sources) if(!_excluded.Contains(source.Name)) _checked.Add(source);
                _cache=next; _hasDetected=true;
                _contentsScopeJson = scope.RawJson; RenderSources();
                detected=true;
                _status.Text = (refresh ? "Content refreshed" : "Content detected") + " · " + _sources.Count + " models\r\n" + next.Reused.ToString("N0") + " cached results reused · " + next.GeometryChecked.ToString("N0") + " geometry checks";
            });
            if(detected) EstimateContentTimes();
        }
        private static IEnumerable<SharingProgress> ClearSteps()
        {
            yield return new SharingProgress("Clear checks", "Clearing the cached model selections; model geometry is unchanged.", 0, 1);
            yield return new SharingProgress("Clear checks", "Updating the model list.", 1, 1);
        }
        private void RunOperation(string title, IEnumerable<SharingProgress> steps, Action commit)
        {
            if (_busy) return;
            int revision=_contentRevision;
            _busy = true; _cancelRequested=false; UpdateSelection();
            foreach (Control card in _body.Controls) if (card is Panel) card.Enabled = false;
            try
            {
                if (SharingProgressForm.Run(this, title, steps) && revision==_contentRevision && !_cancelRequested) commit();
                else _status.Text = _scope != null && _contentsScopeJson != _scope.RawJson
                    ? "Scan cancelled. Previous checks were retained; scan this box before exporting."
                    : "Operation cancelled. Previous model checks were preserved.";
            }
            catch (Exception ex) { _status.Text = "Operation did not complete: " + ex.Message; }
            finally { _busy = false; foreach (Control card in _body.Controls) if (card is Panel) card.Enabled = true; UpdateSelection(); }
        }
        private void PickSavedBox()
        {
            List<SavedBoxViewpoint> views = new List<SavedBoxViewpoint>();
            bool loaded = false;
            RunOperation("Load Saved Viewpoints", new SavedBoxViewpoints().Read(_document, views), delegate { loaded = true; });
            if (!loaded) return;
            using (SavedBoxPickerForm picker = new SavedBoxPickerForm(views))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try { SavedBoxViewpoints.Activate(_document, picker.Selected); ReadScope(); }
                catch (Exception error) { _status.Text = error.Message; }
            }
        }
        private void ShowElements()
        {
            if (_models.SelectedItems.Count != 1) { _status.Text = "Select a model row to view its elements inside the box."; return; }
            using (SharingElementsForm form = new SharingElementsForm((SharingSource)_models.SelectedItems[0].Tag)) form.ShowDialog(this);
        }
        private void RenderSources()
        {
            _refreshing = true; _models.BeginUpdate(); _models.Items.Clear();
            foreach (SharingSource source in _sources.Where(s => s.Name.IndexOf(_search.Text, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                ListViewItem row = new ListViewItem(source.Name) { Tag = source, Checked = _checked.Contains(source) };
                row.SubItems.Add(source.Elements.Count.ToString("N0")); _models.Items.Add(row);
            }
            _models.EndUpdate(); _refreshing = false; UpdateSelection();
        }
        private void UpdateSelection()
        {
            _selectionLabel.Text = _checked.Count + " models checked · " + _checked.Sum(s => s.Elements.Count).ToString("N0") + " geometry items · " + _models.Items.Count + " shown";
            _export.Enabled = !_busy && _scope != null && _contentsScopeJson == _scope.RawJson && _checked.Count != 0;
            _detect.Enabled=!_busy && _scope!=null;
            _refreshContent.Enabled=!_busy && _scope!=null && _hasDetected;
            UpdateFormatTimes();
        }
        private void InvalidateContent(object sender,EventArgs args)
        {
            _contentRevision++;
            _cache=null; _contentsScopeJson=null;
            _estimateScopeJson=null;
            if(_busy) _cancelRequested=true;
            _status.Text="Models changed. Refresh Content before exporting."; UpdateSelection();
        }
        private void LayoutFooter()
        {
            int gap=(int)(12*DeviceDpi/96F), margin=(int)(22*DeviceDpi/96F), usable=_footer.ClientSize.Width-2*margin-2*gap;
            int bar=usable*32/100, time=usable*26/100;
            _progress.Left=margin; _progress.Width=bar;
            _estimation.Left=margin+bar+gap; _estimation.Width=time;
            _status.Left=_estimation.Right+gap; _status.Width=Math.Max(100,_footer.ClientSize.Width-margin-_status.Left);
        }
        private void Export(object sender, EventArgs args)
        {
            if (_busy) return;
            string ext=FormatExtension();
            using(SaveFileDialog dialog=new SaveFileDialog {Filter=ext.ToUpperInvariant()+" clipped coordination model (*."+ext+")|*."+ext,DefaultExt=ext,AddExtension=true,FileName="Shared_Model_"+DateTime.Now.ToString("yyyyMMdd")+"_"+ext.ToUpperInvariant()+"."+ext,OverwritePrompt=true})
            { if(dialog.ShowDialog(this)==DialogResult.OK) ExportToFile(dialog.FileName); }
        }
        private void ExportToFile(string fileName)
        {
            if(_busy) return;
            bool modelComplete=false;
            _activeFormat=Path.GetExtension(fileName).Substring(1).ToUpperInvariant();
            try
            {
                if (_document.IsDisposed || _document.IsClear || SectionBoxSettingsStore.GetModelKey(_document) != ModelKey) throw new InvalidOperationException("The open document changed. Reopen this tool for the current model.");
                SectionBoxScope current; string reason;
                if (!SectionBoxScope.TryRead(_document.ActiveView, out current, out reason)) throw new InvalidOperationException(reason);
                if (_scope == null || current.RawJson != _scope.RawJson) { ReadScope(); throw new InvalidOperationException("The Section Box changed. Click Refresh Content before exporting."); }
                if(_cache==null || !_cache.Matches(_document) || _contentsScopeJson!=_scope.RawJson) throw new InvalidOperationException("Detect or refresh this Section Box content before exporting.");
                    _busy = true; _cancelRequested = false; _export.Enabled = false; _cancel.Enabled = true; _open.Enabled = false;
                    foreach (Control control in _body.Controls) if (control is Panel) control.Enabled = false;
                    _cancel.Parent.Enabled = true; _export.Enabled = false; _open.Enabled=false;
                    _progress.Style=ProgressBarStyle.Continuous; _progress.Value=0;
                    List<SharingSource> selected=_checked.ToList();
                    int total=selected.Sum(s=>s.Elements.Count);
                    if(_estimateWindow!=null) _estimateWindow.Dispose();
                    _estimateWindow=new ExportEstimateForm(total,_activeFormat);
                    _estimateWindow.Location=PointToScreen(new Point(_estimation.Left,Math.Max(0,_footer.Top-_estimateWindow.Height-(int)(8*DeviceDpi/96F))));
                    _estimateWindow.Show(this); BimWindowLayout.FitToWorkingArea(_estimateWindow);
                    if(_estimateScopeJson==_contentsScopeJson)
                    {
                        _estimatedSeconds=EstimatedSeconds(Array.IndexOf(new[]{"IFC","OBJ","NWC","NWD"},_activeFormat),total);
                        _estimation.Text="Estimated time\r\n"+SharingExportEstimate.Range(_estimatedSeconds);
                        _estimateWindow.UpdateEstimate(_estimation.Text,total.ToString("N0")+" checked geometry items · measured after detection");
                    }
                    else
                    {
                    _estimation.Text="Estimation time: measuring sample…";
                    _status.Text="Estimate " + _activeFormat + " export\r\nMeasuring representative geometry"; System.Windows.Forms.Application.DoEvents();
                    bool native=_activeFormat=="NWC" || _activeFormat=="NWD";
                    string sampleFile=Path.Combine(Path.GetTempPath(),"bim-export-estimate-"+Guid.NewGuid().ToString("N")+(native ? ".ifc" : Path.GetExtension(fileName)));
                    SharingSummary sample=null;
                    try
                    {
                        IList<SharingSource> samples=SharingExportEstimate.Sample(selected,_scope.RawJson,64);
                        Stopwatch timing=Stopwatch.StartNew();
                        sample=_service.ExportWithProgress(_document,_scope,samples,sampleFile,()=>_cancelRequested,step=>
                        { _status.Text="Estimate " + _activeFormat + " export\r\n" + step.Detail; System.Windows.Forms.Application.DoEvents(); });
                        _estimatedSeconds=timing.Elapsed.TotalSeconds*total/Math.Max(1,sample.Objects);
                        SharingExportEstimate estimate=new SharingExportEstimate(_estimatedSeconds,true);
                        _estimation.Text=estimate.Description;
                        _estimateWindow.UpdateEstimate(estimate.Description,total.ToString("N0") + " items · measured " + sample.Objects + " representative items" + (native ? "\r\nNative conversion takes additional time." : ""));
                        if(native) _estimation.Text="Est. geometry time\r\n"+SharingExportEstimate.Range(_estimatedSeconds)+" + conversion";
                    }
                    finally { if(File.Exists(sampleFile)) File.Delete(sampleFile); if(sample!=null) foreach(string extra in sample.AdditionalFiles) if(File.Exists(extra)) File.Delete(extra); }
                    }
                    _exportClock.Restart();
                    SharingSummary result = _service.ExportWithProgress(_document, _scope, selected, fileName,
                        () => _cancelRequested, UpdateExportProgress);
                    modelComplete=true;
                    _output = result.FileName;
                    try { _service.PackageForTransfer(result,()=>_cancelRequested,UpdateExportProgress); }
                    catch(OperationCanceledException) { throw; }
                    catch (Exception ex) { _open.Enabled = true; _status.Text = _activeFormat + " exported, but ZIP packaging failed: " + ex.Message + "\r\n" + _output; return; }
                    _output = result.PackageFileName; _open.Enabled = true;
                    _progress.Value=100; _estimation.Text="Completed in " + Math.Ceiling(_exportClock.Elapsed.TotalSeconds) + " sec";
                    _status.Text = string.Format("Export complete · {0:N0} objects\r\n" + _activeFormat + " {1:0.##} MB · ZIP {2:0.##} MB", result.Objects, result.Bytes / 1048576.0, result.PackageBytes / 1048576.0);
                    _estimateWindow.UpdateEstimate(_estimation.Text,_activeFormat + " and ZIP ready. Use Show Output to locate the transfer file.");
            }
            catch (OperationCanceledException) { _status.Text = modelComplete ? "ZIP cancelled; the completed " + _activeFormat + " is available." : "Export cancelled. Previous output preserved."; _open.Enabled=modelComplete; _estimation.Text="Export cancelled"; }
            catch (Exception ex) { _status.Text = "Export did not complete: " + ex.Message; _estimation.Text="Export stopped"; }
            finally { _busy = false; _cancel.Enabled = false; foreach (Control control in _body.Controls) if (control is Panel) control.Enabled = true; if(_estimateWindow!=null && !_estimateWindow.IsDisposed) _estimateWindow.UpdateEstimate(_estimation.Text,_status.Text); UpdateSelection(); }
        }
        private string FormatExtension() {return (string)_formats.First(f=>f.Checked).Tag;}
        private void EstimateContentTimes()
        {
            if(_busy || _sources.Count==0) { _estimateScopeJson=null; UpdateFormatTimes(); return; }
            // An unchanged refresh already has representative measurements.
            if(_estimateScopeJson==_contentsScopeJson) { UpdateFormatTimes(); return; }
            _busy=true; _cancelRequested=false; _cancel.Enabled=true;
            foreach(Control card in _body.Controls) if(card is Panel) card.Enabled=false;
            _cancel.Parent.Enabled=true; foreach(RadioButton option in _formats) option.Enabled=false;
            _export.Enabled=false; _open.Enabled=false;
            _progress.Style=ProgressBarStyle.Marquee;
            string folder=Path.Combine(Path.GetTempPath(),"bim-time-estimates-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            try
            {
                foreach(Label label in _formatTimes) label.Text="Est. time: measuring…";
                IList<SharingSource> sample=SharingExportEstimate.Sample(_sources,_scope.RawJson,64);
                foreach(string extension in new[]{"ifc","obj"})
                {
                    Stopwatch clock=Stopwatch.StartNew();
                    SharingSummary result=_service.ExportWithProgress(_document,_scope,sample,Path.Combine(folder,"sample."+extension),()=>_cancelRequested,step=>
                    { _status.Text="Estimate " + extension.ToUpperInvariant()+"\r\n"+step.Detail; System.Windows.Forms.Application.DoEvents(); });
                    _service.PackageForTransfer(result,()=>_cancelRequested,step=>
                    { _status.Text="Estimate " + extension.ToUpperInvariant()+"\r\n"+step.Detail; System.Windows.Forms.Application.DoEvents(); });
                    double perItem=clock.Elapsed.TotalSeconds/Math.Max(1,result.Objects);
                    if(extension=="ifc") { _ifcSecondsPerItem=perItem; _ifcBytesPerItem=(double)result.Bytes/Math.Max(1,result.Objects); }
                    else _objSecondsPerItem=perItem;
                }
                foreach(string extension in new[]{"nwc","nwd"})
                {
                    double seconds;
                    if(!SharingNativeTiming.TryEstimate(extension,1,out seconds))
                    {
                        _estimation.Text="First-run calibration\r\n"+extension.ToUpperInvariant();
                        _service.ExportWithProgress(_document,_scope,sample,Path.Combine(folder,"native-sample."+extension),()=>_cancelRequested,step=>
                        { _status.Text="Estimate " + extension.ToUpperInvariant()+"\r\n"+step.Detail; System.Windows.Forms.Application.DoEvents(); });
                    }
                }
                if(_cancelRequested) throw new OperationCanceledException();
                _estimateScopeJson=_contentsScopeJson;
                _progress.Style=ProgressBarStyle.Continuous; _progress.Value=100;
                _status.Text="Time estimates ready\r\nChoose a format below";
                _estimation.Text="Estimated export times\r\nunder each format";
                _body.ScrollControlIntoView(_formats[0].Parent);
            }
            catch(OperationCanceledException) { _estimateScopeJson=null; _status.Text="Time estimation cancelled. Model detection is complete."; }
            catch(Exception error) { _estimateScopeJson=null; _status.Text="Time estimation unavailable: "+error.Message; }
            finally
            {
                foreach(string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder,false);
                _progress.Style=ProgressBarStyle.Continuous;
                _busy=false; _cancel.Enabled=false; foreach(Control card in _body.Controls) if(card is Panel) card.Enabled=true; foreach(RadioButton option in _formats) option.Enabled=true; _open.Enabled=File.Exists(_output); UpdateSelection();
            }
        }
        private void UpdateFormatTimes()
        {
            long count=_checked.Sum(s=>(long)s.Elements.Count);
            if(_formats[0]==null) return;
            string[] notes={"Source identifiers","No property table","Includes conversion","Includes conversion"};
            for(int i=0;i<4;i++)
            {
                string text=_hasDetected ? "Est. time unavailable" : "Est. time: after Detect";
                if(_estimateScopeJson!=null && _estimateScopeJson==_contentsScopeJson)
                {
                    double seconds=EstimatedSeconds(i,count);
                    text=count==0 ? "No models checked" : "Est. " + SharingExportEstimate.Range(seconds);
                }
                _formatTimes[i].Text=text+"\r\n"+notes[i];
            }
        }
        private double EstimatedSeconds(int format,long count)
        {
            double seconds=format==1 ? _objSecondsPerItem*count : _ifcSecondsPerItem*count;
            if(format>=2) {double extra; if(SharingNativeTiming.TryEstimate(format==2 ? "nwc" : "nwd",_ifcBytesPerItem*count,out extra)) seconds+=extra;}
            return seconds;
        }
        private void UpdateExportProgress(SharingExportProgress step)
        {
            bool zip=step.Stage=="Create ZIP";
            _progress.Style=step.Total>0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
            if(step.Total>0) _progress.Value=zip ? 95+(int)Math.Min(4,step.Completed*4/step.Total) : (int)Math.Min(95,step.Completed*95/step.Total);
            _status.Text=step.Stage + "\r\n" + step.Detail;
            if(!zip && step.Total>0 && step.Completed>0)
            {
                double measured=_exportClock.Elapsed.TotalSeconds*Math.Max(0,step.Total-step.Completed)/step.Completed;
                double remaining=_exportClock.Elapsed.TotalSeconds>=3 ? measured : Math.Max(0,_estimatedSeconds-_exportClock.Elapsed.TotalSeconds);
                _estimation.Text="Est. " + _activeFormat + " remaining\r\n"+SharingExportEstimate.Range(remaining);
            }
            if(zip) _estimation.Text=_activeFormat + " complete\r\nCreating ZIP…";
            if(step.Stage.StartsWith("Convert to ",StringComparison.Ordinal)) _estimation.Text="Native conversion\r\nElapsed " + SharingExportEstimate.Format(_exportClock.Elapsed.TotalSeconds);
            if(_estimateWindow!=null && !_estimateWindow.IsDisposed) _estimateWindow.UpdateEstimate(_estimation.Text,step.Stage);
            System.Windows.Forms.Application.DoEvents();
        }
    }
}
