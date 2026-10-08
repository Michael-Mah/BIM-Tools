using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using BIM.Tools.Models;
using BIM.Tools.Services;
using DrawingColor = System.Drawing.Color;

namespace BIM.Tools.UI
{
    public sealed class SectionBoxBuilderForm : Form
    {
        private readonly Document _document;
        private readonly DatumSectionBoxService _service;
        private readonly SectionBoxWorkspaceService _workspaceService;
        private readonly SectionBoxSettingsStore _settingsStore;
        private readonly SectionBoxBuilderSettings _settings;
        private readonly ComboBox _levelSource;
        private readonly ComboBox _gridSource;
        private readonly ComboBox _bottomLevel;
        private readonly ComboBox _topLevel;
        private readonly ComboBox[] _gridBoundaries;
        private readonly Button _readLevels;
        private readonly Button _readGrids;
        private readonly Button _createButton;
        private readonly Label _levelStatus;
        private readonly Label _gridStatus;
        private readonly Label _status;
        private readonly ProgressBar _progress;
        private readonly Dictionary<ComboBox, List<string>> _searchItems;
        private IList<LevelDatum> _loadedLevels;
        private IList<GridLineDatum> _loadedGridLines;
        private IList<DatumSource> _sources;
        private bool _filtering;
        private bool _restoring;
        private bool _createdSuccessfully;

        public SectionBoxBuilderForm(Document document)
        {
            SuspendLayout();
            _document = document;
            _service = new DatumSectionBoxService();
            _workspaceService = new SectionBoxWorkspaceService();
            _settingsStore = new SectionBoxSettingsStore();
            _settings = _settingsStore.Load(document);
            _searchItems = new Dictionary<ComboBox, List<string>>();
            _loadedLevels = new List<LevelDatum>();
            _loadedGridLines = new List<GridLineDatum>();
            _sources = new List<DatumSource>();
            _gridBoundaries = new ComboBox[4];

            Text = EditionBrand.ProductName + " · Section Box Builder";
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            BackColor = BimTheme.Paper;
            ForeColor = BimTheme.Graphite;
            ClientSize = new Size(720, 720);
            MinimumSize = new Size(680, 660);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MinimizeBox = true;
            MaximizeBox = false;

            Panel header = CreateHeader();
            Controls.Add(header);

            Panel body = new Panel();
            body.Location = new Point(0, 72);
            body.Size = new Size(ClientSize.Width, ClientSize.Height - 72);
            body.Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                          AnchorStyles.Left | AnchorStyles.Right;
            body.AutoScroll = true;
            body.Padding = new Padding(22, 18, 22, 18);
            Controls.Add(body);
            header.BringToFront();

            Panel sourceCard = CreateCard(new Point(22, 18), new Size(658, 150));
            body.Controls.Add(sourceCard);
            AddSectionTitle(sourceCard, "1  DATA SOURCES", "Choose separate model sources for Level and Grid data.");

            _levelSource = CreateSourceCombo(new Point(142, 55), new Size(374, 26));
            _levelSource.TextUpdate += SourceTextUpdated;
            _levelSource.SelectedIndexChanged += delegate
            {
                if (!_filtering && !_restoring)
                {
                    ClearLevels("Level source changed · click Read Levels");
                }
            };
            sourceCard.Controls.Add(_levelSource);
            sourceCard.Controls.Add(CreateFieldLabel("Level source", new Point(18, 59)));

            _readLevels = CreateActionButton("READ LEVELS", new Point(526, 54), new Size(112, 28), false);
            _readLevels.Click += ReadLevels;
            sourceCard.Controls.Add(_readLevels);

            _gridSource = CreateSourceCombo(new Point(142, 91), new Size(374, 26));
            _gridSource.TextUpdate += SourceTextUpdated;
            _gridSource.SelectedIndexChanged += delegate
            {
                if (!_filtering && !_restoring)
                {
                    ClearGrids("Grid source changed · click Read Grids");
                }
            };
            sourceCard.Controls.Add(_gridSource);
            sourceCard.Controls.Add(CreateFieldLabel("Grid source", new Point(18, 95)));

            _readGrids = CreateActionButton("READ GRIDS", new Point(526, 90), new Size(112, 28), false);
            _readGrids.Click += ReadGrids;
            sourceCard.Controls.Add(_readGrids);

            Label sourceHint = CreateLabel(
                "Architecture models are listed first. Reading starts only after your confirmation.",
                8F,
                BimTheme.Muted);
            sourceHint.Location = new Point(18, 124);
            sourceHint.Size = new Size(610, 20);
            sourceCard.Controls.Add(sourceHint);

            Panel levelCard = CreateCard(new Point(22, 180), new Size(658, 132));
            body.Controls.Add(levelCard);
            AddSectionTitle(levelCard, "2  VERTICAL RANGE", "Type 16.40 to find EL+16.40, or enter a manual elevation in metres.");

            _bottomLevel = CreateSearchCombo(new Point(96, 65), new Size(224, 26));
            levelCard.Controls.Add(_bottomLevel);
            levelCard.Controls.Add(CreateFieldLabel("Bottom", new Point(18, 69)));

            _topLevel = CreateSearchCombo(new Point(408, 65), new Size(224, 26));
            levelCard.Controls.Add(_topLevel);
            levelCard.Controls.Add(CreateFieldLabel("Top", new Point(350, 69)));

            _levelStatus = CreateLabel("No Level data loaded", 8F, BimTheme.Warning);
            _levelStatus.Location = new Point(18, 101);
            _levelStatus.Size = new Size(614, 20);
            levelCard.Controls.Add(_levelStatus);

            Panel gridCard = CreateCard(new Point(22, 324), new Size(658, 176));
            body.Controls.Add(gridCard);
            AddSectionTitle(gridCard, "3  PLAN BOUNDARIES", "Choose four lines forming two opposite, perpendicular Grid pairs.");

            for (int index = 0; index < 4; index++)
            {
                int column = index % 2;
                int row = index / 2;
                int x = column == 0 ? 96 : 408;
                int y = 64 + row * 38;
                _gridBoundaries[index] = CreateSearchCombo(new Point(x, y), new Size(224, 26));
                gridCard.Controls.Add(_gridBoundaries[index]);
                gridCard.Controls.Add(CreateFieldLabel(
                    "Grid " + (index + 1),
                    new Point(column == 0 ? 18 : 350, y + 4)));
            }

            _gridStatus = CreateLabel("No Grid data loaded", 8F, BimTheme.Warning);
            _gridStatus.Location = new Point(18, 145);
            _gridStatus.Size = new Size(614, 20);
            gridCard.Controls.Add(_gridStatus);

            Panel actionCard = CreateCard(new Point(22, 512), new Size(658, 92));
            body.Controls.Add(actionCard);
            _createButton = CreateActionButton(
                "CREATE NATIVE SECTION BOX",
                new Point(18, 14),
                new Size(620, 36),
                true);
            _createButton.Enabled = false;
            _createButton.Click += CreateSectionBox;
            actionCard.Controls.Add(_createButton);

            _progress = new ProgressBar();
            _progress.Location = new Point(18, 57);
            _progress.Size = new Size(620, 7);
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 22;
            _progress.Visible = false;
            actionCard.Controls.Add(_progress);

            _status = CreateLabel("Select sources, confirm reading, then choose Level and Grid boundaries.", 8F, BimTheme.Muted);
            _status.Location = new Point(18, 67);
            _status.Size = new Size(620, 19);
            actionCard.Controls.Add(_status);

            AcceptButton = _createButton;
            FormClosing += BuilderFormClosing;
            LoadSources();
            ResumeLayout(true);
        }

        public event EventHandler SectionBoxCreated;

        internal string ModelKey
        {
            get { return _settings.ModelKey; }
        }

        private Panel CreateHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 72;
            header.BackColor = BimTheme.Brand;
            header.Padding = new Padding(22, 12, 22, 10);

            Label mark = CreateLabel(EditionBrand.StorageFolder, 10F, DrawingColor.White);
            mark.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            mark.BackColor = DrawingColor.FromArgb(34, 92, 71);
            mark.TextAlign = ContentAlignment.MiddleCenter;
            mark.Location = new Point(22, 17);
            mark.Size = new Size(42, 38);

            Label title = CreateLabel("Section Box Builder", 15F, DrawingColor.White);
            title.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            title.Location = new Point(78, 12);
            title.Size = new Size(400, 29);

            Label subtitle = CreateLabel("Level controls height · Grid lines control four sides", 8.5F, DrawingColor.FromArgb(211, 230, 222));
            subtitle.Location = new Point(80, 41);
            subtitle.Size = new Size(430, 20);

            header.Controls.Add(subtitle);
            header.Controls.Add(title);
            header.Controls.Add(mark);
            return header;
        }

        private void LoadSources()
        {
            try
            {
                IList<DatumSource> sources = _service.GetSources(_document);
                _sources = sources;
                _levelSource.BeginUpdate();
                _gridSource.BeginUpdate();
                foreach (DatumSource source in sources)
                {
                    _levelSource.Items.Add(source);
                    _gridSource.Items.Add(source);
                }
                _levelSource.EndUpdate();
                _gridSource.EndUpdate();

                DatumSource recommended = _service.GetRecommendedSource(sources);
                DatumSource savedLevelSource = FindSource(_settings.LevelSource);
                DatumSource savedGridSource = FindSource(_settings.GridSource);
                _restoring = true;
                if (savedLevelSource != null)
                {
                    _levelSource.SelectedItem = savedLevelSource;
                }
                else if (recommended != null)
                {
                    _levelSource.SelectedItem = recommended;
                }
                if (savedGridSource != null)
                {
                    _gridSource.SelectedItem = savedGridSource;
                }
                else if (recommended != null)
                {
                    _gridSource.SelectedItem = recommended;
                }
                _restoring = false;

                if (savedLevelSource != null)
                {
                    LoadLevelData(
                        savedLevelSource,
                        _settings.BottomLevel,
                        _settings.TopLevel);
                }
                if (savedGridSource != null)
                {
                    LoadGridData(savedGridSource, _settings.GridBoundaries);
                }

                if (savedLevelSource != null || savedGridSource != null)
                {
                    _status.Text = "Restored the last selections saved for this model.";
                    _status.ForeColor = BimTheme.Success;
                }
                else if (recommended != null)
                {
                    _status.Text = "Recommended architecture source selected: " + recommended.Name;
                    _status.ForeColor = BimTheme.Info;
                }
                _readLevels.Enabled = sources.Count > 0;
                _readGrids.Enabled = sources.Count > 0;

                if (sources.Count == 0)
                {
                    _status.Text = "No Navisworks Grid Systems were found in this document.";
                    _status.ForeColor = BimTheme.Error;
                }
            }
            catch (Exception exception)
            {
                BimDialog.ShowError(this, "Cannot read data sources", "Navisworks Grid Systems could not be listed.", exception.ToString());
            }
        }

        private void ReadLevels(object sender, EventArgs e)
        {
            DatumSource source = GetSelectedSource(_levelSource);
            if (source == null)
            {
                return;
            }
            DialogResult answer = MessageBox.Show(
                this,
                "Read Level data from this model?\r\n\r\n" +
                source.Name + "\r\n" +
                source.LevelCount + " Level records available.\r\n\r\n" +
                "This only reads data and does not modify the document.",
                "Confirm Level source",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (answer != DialogResult.OK)
            {
                return;
            }

            LoadLevelData(source, _bottomLevel.Text, _topLevel.Text);
        }

        private void ReadGrids(object sender, EventArgs e)
        {
            DatumSource source = GetSelectedSource(_gridSource);
            if (source == null)
            {
                return;
            }
            DialogResult answer = MessageBox.Show(
                this,
                "Read Grid line data from this model?\r\n\r\n" +
                source.Name + "\r\n" +
                source.GridLineCount + " Grid lines available.\r\n\r\n" +
                "This only reads data and does not modify the document.",
                "Confirm Grid source",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (answer != DialogResult.OK)
            {
                return;
            }

            string[] previous = _gridBoundaries
                .Select(delegate(ComboBox combo) { return combo.Text; })
                .ToArray();
            LoadGridData(source, previous);
        }

        private void CreateSectionBox(object sender, EventArgs e)
        {
            LevelDatum bottom = _service.ResolveLevel(_document, _loadedLevels, _bottomLevel.Text);
            LevelDatum top = _service.ResolveLevel(_document, _loadedLevels, _topLevel.Text);
            List<GridLineDatum> boundaries = new List<GridLineDatum>();
            foreach (ComboBox combo in _gridBoundaries)
            {
                GridLineDatum line = _service.ResolveGridLine(_loadedGridLines, combo.Text);
                if (line == null)
                {
                    MessageBox.Show(
                        this,
                        "Grid entry ‘" + combo.Text + "’ is missing or matches more than one line. " +
                        "Choose an exact item from its filtered list.",
                        "Grid selection required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    combo.Focus();
                    return;
                }
                boundaries.Add(line);
            }

            if (bottom == null || top == null)
            {
                MessageBox.Show(
                    this,
                    "Choose a loaded Level or type a numeric elevation in metres for both Bottom and Top.",
                    "Level selection required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true, "Preparing Section Box...");
            bool created = false;
            try
            {
                double viewCorrectionDegrees = _service.CreateSectionBox(
                    _document,
                    GetSelectedSource(_gridSource),
                    bottom,
                    top,
                    boundaries,
                    delegate(string message)
                    {
                        _status.Text = message;
                        _status.ForeColor = BimTheme.Info;
                        System.Windows.Forms.Application.DoEvents();
                    });
                _status.Text = string.Format(
                    "Created · {0} to {1} · Grids {2}{3}",
                    bottom.Name,
                    top.Name,
                    string.Join(", ", boundaries.Select(delegate(GridLineDatum line) { return line.Name; }).ToArray()),
                    Math.Abs(viewCorrectionDegrees) < 0.05
                        ? string.Empty
                        : " · View aligned " +
                          viewCorrectionDegrees.ToString("+0.##;-0.##;0") + "°");
                _status.ForeColor = BimTheme.Success;
                CaptureSettings();
                _settings.SectionBoxSource = SectionBoxWorkspaceService.SavedSourceName;
                _workspaceService.SaveCurrentSectionBox(_document);
                _settingsStore.Save(_settings);
                EventHandler handler = SectionBoxCreated;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
                if (!_settings.AngleNoticeShown)
                {
                    _settings.AngleNoticeShown = true;
                    _settingsStore.Save(_settings);
                    MessageBox.Show(
                        this,
                        "The Section Box has been created and saved as " +
                        SectionBoxWorkspaceService.SavedViewpointName + ".\r\n\r\n" +
                        (Math.Abs(viewCorrectionDegrees) < 0.05
                            ? "The project Grid was already aligned."
                            : "Detected project rotation: " +
                              viewCorrectionDegrees.ToString("+0.##;-0.##;0") +
                              "°. The Section Box and current view were aligned to the Grid.") +
                        "\r\n\r\nThis angle notice is shown once for this model.",
                        "Section Box ready",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                _createdSuccessfully = true;
                created = true;
            }
            catch (Exception exception)
            {
                _status.Text = "Section Box was not created.";
                _status.ForeColor = BimTheme.Error;
                BimDialog.ShowError(this, "Section Box Builder", "The selected Level and Grid boundaries could not be applied.", exception.ToString());
            }
            finally
            {
                SetBusy(false, null);
            }
            if (created)
            {
                Close();
            }
        }

        private void LoadLevelData(
            DatumSource source,
            string bottomValue,
            string topValue)
        {
            _loadedLevels = _service.ReadLevels(_document, source);
            List<string> names = _loadedLevels
                .Select(delegate(LevelDatum level) { return level.Name; })
                .ToList();
            SetSearchItems(_bottomLevel, names);
            SetSearchItems(_topLevel, names);
            _bottomLevel.Text = bottomValue ?? string.Empty;
            _topLevel.Text = topValue ?? string.Empty;
            _levelStatus.Text = _loadedLevels.Count + " Levels loaded from " + source.Name;
            _levelStatus.ForeColor = BimTheme.Success;
            _status.Text = "Level data ready. You may type a name or elevation.";
            _status.ForeColor = BimTheme.Success;
            UpdateCreateState();
        }

        private void LoadGridData(DatumSource source, string[] previousValues)
        {
            _loadedGridLines = _service.ReadGridLines(source);
            List<string> names = _loadedGridLines
                .Select(delegate(GridLineDatum line) { return line.Name; })
                .ToList();
            for (int index = 0; index < _gridBoundaries.Length; index++)
            {
                ComboBox combo = _gridBoundaries[index];
                SetSearchItems(combo, names);
                combo.Text = previousValues != null && index < previousValues.Length
                    ? previousValues[index] ?? string.Empty
                    : string.Empty;
            }
            _gridStatus.Text = _loadedGridLines.Count + " Grid lines loaded from " + source.Name;
            _gridStatus.ForeColor = BimTheme.Success;
            _status.Text = "Grid data ready. Type part of a label, for example 15 to find S15.";
            _status.ForeColor = BimTheme.Success;
            UpdateCreateState();
        }

        private DatumSource FindSource(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }
            return _sources.FirstOrDefault(delegate(DatumSource source)
            {
                return string.Equals(
                    source.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase);
            });
        }

        private void CaptureSettings()
        {
            _settings.ModelKey = SectionBoxSettingsStore.GetModelKey(_document);
            DatumSource levelSource = GetSelectedSource(_levelSource);
            DatumSource gridSource = GetSelectedSource(_gridSource);
            _settings.LevelSource = levelSource == null
                ? _levelSource.Text.Trim()
                : levelSource.Name;
            _settings.GridSource = gridSource == null
                ? _gridSource.Text.Trim()
                : gridSource.Name;
            _settings.BottomLevel = _bottomLevel.Text.Trim();
            _settings.TopLevel = _topLevel.Text.Trim();
            for (int index = 0; index < _gridBoundaries.Length; index++)
            {
                _settings.GridBoundaries[index] = _gridBoundaries[index].Text.Trim();
            }
        }

        private void BuilderFormClosing(object sender, FormClosingEventArgs e)
        {
            CaptureSettings();
            if (!_createdSuccessfully)
            {
                SectionBoxBuilderSettings latest = _settingsStore.Load(_document);
                _settings.SectionBoxSource = latest.SectionBoxSource;
                _settings.AngleNoticeShown = latest.AngleNoticeShown;
            }
            _settingsStore.Save(_settings);
        }

        private void ClearLevels(string message)
        {
            _loadedLevels = new List<LevelDatum>();
            SetSearchItems(_bottomLevel, new List<string>());
            SetSearchItems(_topLevel, new List<string>());
            _levelStatus.Text = message;
            _levelStatus.ForeColor = BimTheme.Warning;
            UpdateCreateState();
        }

        private void ClearGrids(string message)
        {
            _loadedGridLines = new List<GridLineDatum>();
            foreach (ComboBox combo in _gridBoundaries)
            {
                SetSearchItems(combo, new List<string>());
            }
            _gridStatus.Text = message;
            _gridStatus.ForeColor = BimTheme.Warning;
            UpdateCreateState();
        }

        private void UpdateCreateState()
        {
            _createButton.Enabled = _loadedLevels.Count > 0 && _loadedGridLines.Count >= 4;
        }

        private void SetBusy(bool busy, string message)
        {
            _createButton.Enabled = !busy && _loadedLevels.Count > 0 && _loadedGridLines.Count >= 4;
            _readLevels.Enabled = !busy;
            _readGrids.Enabled = !busy;
            _progress.Visible = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (!string.IsNullOrEmpty(message))
            {
                _status.Text = message;
            }
            System.Windows.Forms.Application.DoEvents();
        }

        private ComboBox CreateSearchCombo(Point location, Size size)
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDown;
            combo.IntegralHeight = false;
            combo.MaxDropDownItems = 12;
            combo.Location = location;
            combo.Size = size;
            combo.TextUpdate += SearchTextUpdated;
            combo.TextChanged += delegate { UpdateCreateState(); };
            _searchItems.Add(combo, new List<string>());
            return combo;
        }

        private void SetSearchItems(ComboBox combo, IList<string> values)
        {
            List<string> master = values == null ? new List<string>() : values.ToList();
            _searchItems[combo] = master;
            _filtering = true;
            string previous = combo.Text;
            combo.BeginUpdate();
            combo.Items.Clear();
            combo.Items.AddRange(master.Cast<object>().ToArray());
            combo.EndUpdate();
            combo.Text = previous;
            _filtering = false;
        }

        private void SearchTextUpdated(object sender, EventArgs e)
        {
            if (_filtering)
            {
                return;
            }
            ComboBox combo = sender as ComboBox;
            List<string> master;
            if (combo == null || !_searchItems.TryGetValue(combo, out master))
            {
                return;
            }

            string query = combo.Text;
            List<string> filtered = master
                .Where(delegate(string item) { return DatumSectionBoxService.SearchMatches(item, query); })
                .Take(250)
                .ToList();
            _filtering = true;
            combo.BeginUpdate();
            combo.Items.Clear();
            combo.Items.AddRange(filtered.Cast<object>().ToArray());
            combo.EndUpdate();
            combo.Text = query;
            combo.SelectionStart = query.Length;
            combo.SelectionLength = 0;
            combo.DroppedDown = filtered.Count > 0;
            _filtering = false;
        }

        private void SourceTextUpdated(object sender, EventArgs e)
        {
            if (_filtering)
            {
                return;
            }
            ComboBox combo = sender as ComboBox;
            if (combo == null)
            {
                return;
            }

            string query = combo.Text;
            List<DatumSource> filtered = _sources
                .Where(delegate(DatumSource item)
                {
                    return DatumSectionBoxService.SearchMatches(item.Name, query);
                })
                .Take(250)
                .ToList();
            _filtering = true;
            combo.BeginUpdate();
            combo.Items.Clear();
            combo.Items.AddRange(filtered.Cast<object>().ToArray());
            combo.EndUpdate();
            combo.Text = query;
            combo.SelectionStart = query.Length;
            combo.SelectionLength = 0;
            combo.DroppedDown = filtered.Count > 0;
            _filtering = false;
        }

        private DatumSource GetSelectedSource(ComboBox combo)
        {
            DatumSource selected = combo.SelectedItem as DatumSource;
            if (selected != null)
            {
                return selected;
            }

            List<DatumSource> matches = _sources
                .Where(delegate(DatumSource source)
                {
                    return string.Equals(
                        source.Name,
                        combo.Text.Trim(),
                        StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            BimWindowLayout.FitToWorkingArea(this);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            BimWindowLayout.FitToWorkingArea(this);
        }

        private static ComboBox CreateSourceCombo(Point location, Size size)
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDown;
            combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            combo.AutoCompleteSource = AutoCompleteSource.ListItems;
            combo.IntegralHeight = false;
            combo.MaxDropDownItems = 16;
            combo.Location = location;
            combo.Size = size;
            return combo;
        }

        private static Panel CreateCard(Point location, Size size)
        {
            Panel card = new Panel();
            card.Location = location;
            card.Size = size;
            card.BackColor = BimTheme.Surface;
            card.BorderStyle = BorderStyle.FixedSingle;
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            return card;
        }

        private static void AddSectionTitle(Panel card, string title, string subtitle)
        {
            Label titleLabel = CreateLabel(title, 9F, BimTheme.Graphite);
            titleLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            titleLabel.Location = new Point(18, 12);
            titleLabel.Size = new Size(210, 20);
            Label subtitleLabel = CreateLabel(subtitle, 8F, BimTheme.Muted);
            subtitleLabel.Location = new Point(244, 13);
            subtitleLabel.Size = new Size(392, 20);
            card.Controls.Add(subtitleLabel);
            card.Controls.Add(titleLabel);
        }

        private static Label CreateFieldLabel(string text, Point location)
        {
            Label label = CreateLabel(text, 8.25F, BimTheme.Muted);
            label.Location = location;
            label.Size = new Size(72, 20);
            return label;
        }

        private static Label CreateLabel(string text, float size, DrawingColor color)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Segoe UI", size);
            label.ForeColor = color;
            label.AutoEllipsis = true;
            return label;
        }

        private static Button CreateActionButton(string text, Point location, Size size, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = location;
            button.Size = size;
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = BimTheme.StrongLine;
            button.BackColor = primary ? BimTheme.Brand : BimTheme.Surface;
            button.ForeColor = primary ? DrawingColor.White : BimTheme.Graphite;
            button.Font = new Font("Segoe UI", 8.25F, primary ? FontStyle.Bold : FontStyle.Regular);
            return button;
        }
    }
}
