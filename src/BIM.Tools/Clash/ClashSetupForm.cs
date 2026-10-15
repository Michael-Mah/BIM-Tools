using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using BIM.Tools.UI;
using Color = System.Drawing.Color;
using NavisApplication = Autodesk.Navisworks.Api.Application;

namespace BIM.Tools.Clash
{
    internal sealed class ClashSetupForm : Form
    {
        private sealed class ModelRow
        {
            internal ClashModelIdentity Identity;
            internal LoadedClashModel Loaded;
            internal bool A, B;
            internal string Status;
        }
        internal Document Document { get; private set; }
        private readonly ClashProfileStore _store;
        private readonly ComboBox _profiles, _kind, _scope;
        private readonly TextBox _name, _search;
        private readonly NumericUpDown _tolerance;
        private readonly DataGridView _grid;
        private readonly Label _counts, _status, _scopeInfo;
        private readonly FlowLayoutPanel _actions;
        private readonly Button _resolve;
        private List<LoadedClashModel> _loaded = new List<LoadedClashModel>();
        private List<ModelRow> _rows = new List<ModelRow>();
        private ClashProfile _draft = new ClashProfile();
        private bool _ignore, _dirty, _busy;

        internal ClashSetupForm(Document document) : this(document, new ClashProfileStore()) { }
        internal ClashSetupForm(Document document, ClashProfileStore store)
        {
            Document = document; _store = store; _ignore = true;
            Text = "BIM Tools · Clash Setup"; Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(1050, 715); MinimumSize = new Size(770, 540);
            StartPosition = FormStartPosition.CenterScreen; BackColor = BimTheme.Paper;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 115));
            Label header = new Label { Dock = DockStyle.Fill, BackColor = BimTheme.Brand, ForeColor = Color.White,
                Padding = new Padding(20, 12, 12, 8), Font = new Font("Segoe UI", 13F),
                Text = "CLASH SETUP\r\nSelect loaded models · save the intended selections · restore after updates" };
            layout.Controls.Add(header, 0, 0);

            TableLayoutPanel settings = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 8, 18, 2), ColumnCount = 1, RowCount = 4 };
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FlowLayoutPanel saved = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            saved.Controls.Add(Caption("Saved setup", 82));
            _profiles = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
            saved.Controls.Add(_profiles); saved.Controls.Add(Button("New", 70, NewProfile));
            ContextMenuStrip moreMenu = new ContextMenuStrip();
            moreMenu.Items.Add("Import setup XML…", null, delegate { Safe(Import); });
            moreMenu.Items.Add("Export setup XML…", null, delegate { Safe(Export); });
            moreMenu.Items.Add("Use existing native test…", null, delegate { Safe(ImportNative); });
            Button more = new Button { Text = "More ▾", Width = 85, Height = 30 };
            more.Click += delegate { moreMenu.Show(more, new Point(0, more.Height)); }; saved.Controls.Add(more);
            settings.Controls.Add(saved, 0, 0);
            FlowLayoutPanel nameRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            nameRow.Controls.Add(Caption("Setup name", 82)); _name = new TextBox { Width = 300 };
            nameRow.Controls.Add(_name); _kind = new ComboBox { Width = 155, DropDownStyle = ComboBoxStyle.DropDownList };
            _kind.Items.AddRange(new object[] { ClashKind.Hard, ClashKind.HardConservative, ClashKind.Clearance, ClashKind.Duplicate });
            nameRow.Controls.Add(_kind); nameRow.Controls.Add(Caption("Tolerance", 66));
            _tolerance = new NumericUpDown { Width = 90, Minimum = 0, Maximum = 1000000, DecimalPlaces = 2, Value = 1 };
            nameRow.Controls.Add(_tolerance); nameRow.Controls.Add(Caption("mm", 25)); settings.Controls.Add(nameRow, 0, 1);
            FlowLayoutPanel scopeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            scopeRow.Controls.Add(Caption("Range", 82)); _scope = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
            _scope.Items.AddRange(new object[] { "Whole selected models (default)", "Saved Section Box only" });
            scopeRow.Controls.Add(_scope); scopeRow.Controls.Add(Button("Capture current box", 155, CaptureBox));
            _scopeInfo = new Label { Dock = DockStyle.Fill, ForeColor = BimTheme.Muted, AutoEllipsis = true };
            settings.Controls.Add(scopeRow, 0, 2); settings.Controls.Add(_scopeInfo, 0, 3);
            layout.Controls.Add(settings, 0, 1);

            TableLayoutPanel models = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 0, 18, 0), ColumnCount = 1, RowCount = 3 };
            models.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); models.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); models.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            tools.Controls.Add(Button("Refresh Models", 125, RefreshModels));
            _resolve = Button("Resolve missing / renamed", 190, Resolve); tools.Controls.Add(_resolve);
            tools.Controls.Add(Button("Clear selections", 125, Clear));
            tools.Controls.Add(Caption("Search", 48)); _search = new TextBox { Width = 180 }; tools.Controls.Add(_search); models.Controls.Add(tools, 0, 0);
            _grid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, MultiSelect = false };
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "A", HeaderText = "A", Width = 38, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "B", HeaderText = "B", Width = 38, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "File", HeaderText = "Model file", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 55, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Container", HeaderText = "Container / source", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 28, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Model status", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 17, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.Columns[0].ToolTipText = _grid.Columns[1].ToolTipText = "Click the header to select / clear the visible models on this side.";
            models.Controls.Add(_grid, 0, 1); _counts = new Label { Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 0), ForeColor = BimTheme.Muted };
            models.Controls.Add(_counts, 0, 2); layout.Controls.Add(models, 0, 2);

            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 4, 18, 8), ColumnCount = 1, RowCount = 2 };
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            Button saveButton = Button("SAVE SETUP", 160, Save); saveButton.BackColor = BimTheme.Brand; saveButton.ForeColor = Color.White;
            _actions.Controls.Add(saveButton); _actions.Controls.Add(Button("RESTORE CLASH GROUP", 210, Restore));
            _actions.Controls.Add(Button("RUN CLASH", 160, Run)); _actions.Controls.Add(Button("Show selections in model", 195, Highlight));
            footer.Controls.Add(_actions, 0, 0); _status = new Label { Dock = DockStyle.Fill, ForeColor = BimTheme.Brand, AutoEllipsis = true };
            footer.Controls.Add(_status, 0, 1); layout.Controls.Add(footer, 0, 3); Controls.Add(layout);

            _profiles.SelectedIndexChanged += delegate { if (!_ignore && _profiles.SelectedItem is ClashProfile) LoadSelected(); };
            _name.TextChanged += Changed; _kind.SelectedIndexChanged += Changed; _tolerance.ValueChanged += Changed;
            _scope.SelectedIndexChanged += delegate { if (!_ignore) { _dirty = true; ScopeDescription(); } };
            _search.TextChanged += delegate { RenderModels(); };
            _grid.CurrentCellDirtyStateChanged += delegate { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e) {
                if (_ignore || e.RowIndex < 0 || e.ColumnIndex > 1) return;
                DataGridViewRow view = _grid.Rows[e.RowIndex]; ModelRow row = (ModelRow)view.Tag;
                if (e.ColumnIndex == 0) row.A = Convert.ToBoolean(view.Cells[0].Value); else row.B = Convert.ToBoolean(view.Cells[1].Value);
                _dirty = true; Counts(); };
            _grid.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.ColumnIndex <= 1) ToggleVisible(e.ColumnIndex == 0); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (_busy || !DiscardChanges()) e.Cancel = true; };
            _draft.ToleranceMillimetres = 1; SetFields(); ReloadProfiles(); _ignore = false;
            Shown += delegate
            {
                BimWindowLayout.FitToWorkingArea(this);
                Safe(delegate
                {
                    if (ReadModels()) SelectRecentSetupForDocument();
                });
            };
        }
        private static Label Caption(string text, int width)
        { return new Label { Text = text, Width = width, Height = 26, TextAlign = ContentAlignment.MiddleLeft }; }
        private Button Button(string text, int width, System.Action action)
        {
            Button button = new Button { Text = text, Width = width, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = BimTheme.Secondary };
            button.Click += delegate { Safe(action); }; return button;
        }
        private void Changed(object sender, EventArgs e) { if (!_ignore) _dirty = true; }
        private void Safe(System.Action action)
        {
            if (_busy) return;
            try { EnsureDocument(); action(); }
            catch (OperationCanceledException error) { _status.Text = error.Message; }
            catch (Exception error) { _status.Text = error.Message; BimDialog.ShowError(this, "Clash Setup", error.Message, error.ToString()); }
        }
        private void EnsureDocument()
        {
            if (Document.IsDisposed || Document.IsClear || !ReferenceEquals(NavisApplication.ActiveDocument, Document))
                throw new InvalidOperationException("Open Clash Setup again for the active Navisworks document.");
        }
        private bool DiscardChanges()
        {
            if (!_dirty) return true;
            DialogResult answer = MessageBox.Show(this, "Save changes to this clash setup?", "BIM Tools", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes) { try { Save(); } catch (Exception error) { _status.Text = error.Message; return false; } }
            return true;
        }
        private void SetFields()
        {
            bool before = _ignore; _ignore = true;
            _name.Text = _draft.Name; _kind.SelectedItem = _draft.Kind; _tolerance.Value = (decimal)_draft.ToleranceMillimetres;
            _scope.SelectedIndex = _draft.UseSectionBox ? 1 : 0; ScopeDescription(); _ignore = before;
        }
        private ClashProfile CaptureDraft()
        {
            _grid.EndEdit(); ClashProfile next = _draft.Copy(); next.Name = _name.Text.Trim(); next.Kind = (ClashKind)_kind.SelectedItem;
            next.ProjectPath = Document.FileName; next.ToleranceMillimetres = (double)_tolerance.Value; next.UseSectionBox = _scope.SelectedIndex == 1;
            next.SelectionA = _rows.Where(r => r.A).Select(r => r.Identity.Copy()).ToList(); next.SelectionB = _rows.Where(r => r.B).Select(r => r.Identity.Copy()).ToList();
            return next;
        }
        private void ReloadProfiles()
        {
            bool before = _ignore; _ignore = true; List<string> warnings = new List<string>();
            List<ClashProfile> profiles = _store.LoadAll(warnings);
            _profiles.Items.Clear();
            foreach (ClashProfile profile in profiles
                .OrderByDescending(p => ClashModelMatcher.Same(p.ProjectPath, Document.FileName))
                .ThenByDescending(p => p.UpdatedUtc ?? "", StringComparer.Ordinal)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)) _profiles.Items.Add(profile);
            for (int i = 0; i < _profiles.Items.Count; i++) if (((ClashProfile)_profiles.Items[i]).Id == _draft.Id) { _profiles.SelectedIndex = i; break; }
            _ignore = before; if (warnings.Count > 0) _status.Text = "Some saved setups could not be read; their files were retained. " + string.Join("; ", warnings);
        }
        private void SelectRecentSetupForDocument()
        {
            if (_profiles.SelectedIndex >= 0 || string.IsNullOrWhiteSpace(Document.FileName)) return;
            for (int i = 0; i < _profiles.Items.Count; i++)
            {
                ClashProfile profile = _profiles.Items[i] as ClashProfile;
                if (profile != null && ClashModelMatcher.Same(profile.ProjectPath, Document.FileName))
                {
                    // Selecting the last saved setup makes the normal recovery path
                    // a single explicit click on RESTORE CLASH GROUP.
                    _profiles.SelectedIndex = i;
                    return;
                }
            }
        }
        private void LoadSelected()
        {
            ClashProfile selected = (ClashProfile)_profiles.SelectedItem;
            if (!DiscardChanges()) { ReloadProfiles(); return; }
            _draft = selected.Copy(); _dirty = false; SetFields(); BuildRows();
            _status.Text = "Loaded saved setup. Check model status, then Restore Selections. No clash has been run.";
        }
        private void NewProfile()
        {
            if (!DiscardChanges()) return;
            _draft = new ClashProfile { Name = "Clash setup " + DateTime.Now.ToString("MMdd-HHmm"), ToleranceMillimetres = 1 };
            _dirty = false; SetFields(); ReloadProfiles(); BuildRows(); _status.Text = "Choose models for side A and side B, then Save Setup.";
        }
        private bool ReadModels()
        {
            List<LoadedClashModel> next = new List<LoadedClashModel>();
            if (!SharingProgressForm.Run(this, "Read loaded models · file names only", new ClashModelDiscovery().Discover(Document, next)))
            { _status.Text = "Model refresh cancelled. Existing setup and choices are retained."; return false; }
            _loaded = next; BuildRows(); _status.Text = "Read " + _loaded.Count + " loaded model files. No clash detection has started."; return true;
        }
        private void RefreshModels() { _draft = CaptureDraft(); ReadModels(); }
        private void BuildRows()
        {
            List<ClashMatch> matches = ClashModelMatcher.Resolve(_draft, _loaded.Select(m => m.Identity).ToList());
            _rows = new List<ModelRow>();
            foreach (LoadedClashModel model in _loaded)
            {
                ClashMatch match = matches.FirstOrDefault(m => m.Resolved && m.Current.Id == model.Identity.Id);
                ClashModelIdentity identity = model.Identity.Copy();
                if (match != null) identity.Id = match.Saved.Id;
                _rows.Add(new ModelRow { Identity = identity, Loaded = model,
                    A = match != null && _draft.SelectionA.Any(m => m.Id == match.Saved.Id),
                    B = match != null && _draft.SelectionB.Any(m => m.Id == match.Saved.Id), Status = "Loaded" });
            }
            foreach (ClashMatch match in matches.Where(m => !m.Resolved))
                _rows.Add(new ModelRow { Identity = match.Saved.Copy(),
                    A = _draft.SelectionA.Any(m => m.Id == match.Saved.Id), B = _draft.SelectionB.Any(m => m.Id == match.Saved.Id),
                    Status = match.Candidates.Any(c => c.Score > 0.5) ? "Review replacement" : "Missing" });
            RenderModels();
        }
        private void RenderModels()
        {
            bool before = _ignore; _ignore = true; _grid.Rows.Clear(); string query = _search.Text.Trim();
            foreach (ModelRow row in _rows.Where(r => query.Length == 0 || (r.Identity.FileName + " " + r.Identity.Container).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                int index = _grid.Rows.Add(row.A, row.B, row.Identity.FileName, row.Identity.Container, row.Status);
                DataGridViewRow view = _grid.Rows[index]; view.Tag = row; view.Cells[2].ToolTipText = row.Identity.SourcePath;
                if (row.Loaded == null) view.DefaultCellStyle.ForeColor = BimTheme.Warning;
            }
            _ignore = before; Counts();
        }
        private void Counts()
        {
            int unresolved = _rows.Count(r => r.Loaded == null && (r.A || r.B));
            _counts.Text = string.Format("A: {0} models · B: {1} models · {2} missing / need review · {3} files shown. Click A/B headers to select visible files.", _rows.Count(r => r.A), _rows.Count(r => r.B), unresolved, _grid.Rows.Count);
            _resolve.Enabled = unresolved > 0;
        }
        private void ToggleVisible(bool sideA)
        {
            List<ModelRow> visible = _grid.Rows.Cast<DataGridViewRow>().Select(r => (ModelRow)r.Tag).ToList();
            bool value = !visible.All(r => sideA ? r.A : r.B);
            foreach (ModelRow row in visible) { if (sideA) row.A = value; else row.B = value; }
            _dirty = true; RenderModels();
        }
        private void Clear() { foreach (ModelRow row in _rows) { row.A = false; row.B = false; } _dirty = true; RenderModels(); }
        private void ScopeDescription()
        {
            _scopeInfo.Text = _scope.SelectedIndex == 0 ? "No Section Box needed. Reading files / Save Setup / Restore Selections do not detect clashes. Only Run Clash starts detection."
                : _draft.Box == null ? "Capture a box before saving."
                : "Saved box · results filtered by clash centre; whole elements stay.";
        }
        private void CaptureBox()
        {
            string json; _draft.Box = ClashNativeService.CaptureBox(Document, out json); _draft.SectionBoxJson = json;
            _scope.SelectedIndex = 1; _dirty = true; ScopeDescription(); _status.Text = "Section Box captured. No clash detection has started.";
        }
        private void Save()
        {
            _draft = CaptureDraft(); _store.Save(_draft); _dirty = false; ReloadProfiles();
            _status.Text = "Saved independently of the Navisworks file. Missing models remain remembered. Native test was not run.";
        }
        private bool ResolveDraft()
        {
            List<ClashModelIdentity> loaded = _loaded.Select(m => m.Identity).ToList();
            if (ClashModelMatcher.Resolve(_draft, loaded).All(m => m.Resolved)) return true;
            using (ClashResolveForm picker = new ClashResolveForm(_draft, loaded))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return false;
                _draft = picker.Result; _dirty = true; BuildRows(); return true;
            }
        }
        private void Resolve() { _draft = CaptureDraft(); if (ResolveDraft()) _status.Text = "Replacements confirmed in the draft. Save Setup or Restore Selections to store them."; }
        private bool RestoreCore()
        {
            _draft = CaptureDraft(); if (!ReadModels()) return false;
            if (!ResolveDraft()) { _status.Text = "Restore cancelled. Saved selections and the native test are retained."; return false; }
            PreparedClash prepared = new PreparedClash(); ClashNativeService service = new ClashNativeService();
            if (!SharingProgressForm.Run(this, "Prepare remembered model selections", service.Prepare(Document, _draft, _loaded, prepared)))
            { _status.Text = "Selection preparation cancelled. The native test was not changed."; return false; }
            _store.Save(_draft); service.Restore(Document, _draft, prepared); _store.Save(_draft);
            _dirty = false; ReloadProfiles(); _status.Text = "Restored " + _draft.NativeTestName + ". Existing result groups / statuses are retained. No clash run started.";
            _prepared = prepared; return true;
        }
        private PreparedClash _prepared;
        private void Restore() { RestoreCore(); }
        private void Run()
        {
            if (!RestoreCore()) return;
            _busy = true; Enabled = false; _status.Text = "Navisworks is running " + _draft.NativeTestName + "… See the native progress dialog."; Refresh();
            try
            {
                ClashRunSummary summary = new ClashNativeService().Run(Document, _draft, _prepared);
                _status.Text = summary.TestName + " · " + summary.Results.ToString("N0") + " results"
                    + (_draft.UseSectionBox ? " inside the saved box; " + summary.OutsideBoxRemoved.ToString("N0") + " outside-centre results excluded." : ".");
            }
            finally { Enabled = true; _busy = false; }
        }
        private void Highlight()
        {
            ModelItemCollection selected = new ModelItemCollection();
            foreach (ModelRow row in _rows.Where(r => r.Loaded != null && (r.A || r.B)))
                if (!row.Loaded.Root.IsDisposed) selected.Add(row.Loaded.Root);
            Document.CurrentSelection.CopyFrom(selected); _status.Text = "Highlighted the currently available selected models. No clash run started.";
        }
        private void Import()
        {
            if (!DiscardChanges()) return;
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "BIM Tools clash setup (*.xml)|*.xml", Title = "Import clash setup" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _draft = _store.Import(dialog.FileName); _dirty = true; SetFields(); BuildRows();
                _status.Text = "Imported as a new setup. Check model mappings, then Save Setup.";
            }
        }
        private void Export()
        {
            ClashProfile profile = CaptureDraft();
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "BIM Tools clash setup (*.xml)|*.xml", FileName = "clash-setup.xml", Title = "Export remembered model selections" })
                if (dialog.ShowDialog(this) == DialogResult.OK) { _store.Export(dialog.FileName, profile); _status.Text = "Setup exported. It contains selection identities, not model geometry or clash results."; }
        }
        private void ImportNative()
        {
            if (!DiscardChanges() || !ReadModels()) return;
            List<ClashTest> tests = ClashNativeService.AllTests(ClashNativeService.GetTests(Document)).ToList();
            if (tests.Count == 0) throw new InvalidOperationException("There are no native clash tests in this document.");
            using (Form picker = new Form { Text = "Use an existing Clash Test", Font = Font, ClientSize = new Size(650, 340), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false })
            {
                ListBox list = new ListBox { Dock = DockStyle.Fill, DisplayMember = "DisplayName", DataSource = tests };
                Label explanation = new Label { Dock = DockStyle.Top, Height = 62, Padding = new Padding(12),
                    Text = "This creates a remembered MODEL-level setup for the chosen test. Element subsets become whole models. Native rules/results stay with the original test; an explicit Restore updates its selections." };
                Button ok = new Button { Dock = DockStyle.Bottom, Height = 38, Text = "Use selected test", DialogResult = DialogResult.OK };
                picker.Controls.Add(list); picker.Controls.Add(explanation); picker.Controls.Add(ok); picker.AcceptButton = ok;
                picker.Shown += delegate { BimWindowLayout.FitToWorkingArea(picker); };
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                ClashTest test = list.SelectedItem as ClashTest; if (test == null) return;
                ClashKind kind; if (!Enum.TryParse(test.TestType.ToString(), out kind) || test.SimulationType != SimulationType.None)
                    throw new InvalidOperationException("Custom/time-linked tests are not supported by this model-level setup.");
                _draft = new ClashProfile { Name = test.DisplayName, NativeTestId = test.Guid.ToString("D"), NativeTestName = test.DisplayName,
                    Kind = kind, ToleranceMillimetres = test.Tolerance * UnitConversion.ScaleFactor(Document.Units, Units.Millimeters) };
                _draft.SelectionA = ModelsForSelection(test.SelectionA.Selection.GetSelectedItems(Document));
                _draft.SelectionB = ModelsForSelection(test.SelectionB.Selection.GetSelectedItems(Document));
                _dirty = true; SetFields(); BuildRows(); _status.Text = "Existing test selections read. Save the setup to remember them independently.";
            }
        }
        private List<ClashModelIdentity> ModelsForSelection(ModelItemCollection selected)
        {
            HashSet<ModelItem> items = new HashSet<ModelItem>(selected); List<ClashModelIdentity> result = new List<ClashModelIdentity>();
            foreach (LoadedClashModel model in _loaded)
                if (model.Root.AncestorsAndSelf.Any(items.Contains) || selected.Any(item => item.AncestorsAndSelf.Any(a => a.Equals(model.Root)))) result.Add(model.Identity.Copy());
            return result;
        }
    }
}
