using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BIM.Tools.UI;

namespace BIM.Tools.Clash
{
    internal sealed class ClashResolveForm : Form
    {
        private readonly List<ClashMatch> _pending;
        private readonly IList<ClashModelIdentity> _loaded;
        private readonly ListBox _saved, _candidates;
        private readonly Label _detail, _evidence, _summary;
        private readonly Button _confirm, _apply;
        internal ClashProfile Result { get; private set; }
        internal ClashResolveForm(ClashProfile profile, IList<ClashModelIdentity> loaded)
        {
            Result = profile.Copy(); _loaded = loaded;
            _pending = ClashModelMatcher.Resolve(Result, loaded).Where(m => !m.Resolved).ToList();
            Text = "BIM Tools · Resolve model changes"; Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(890, 510); MinimumSize = new Size(650, 400);
            StartPosition = FormStartPosition.CenterParent; BackColor = BimTheme.Paper;
            ShowInTaskbar = false; MinimizeBox = false;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 5 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            _summary = new Label { Dock = DockStyle.Fill, Text = "Saved models stay in the setup until you confirm a replacement or explicitly uncheck them. Name similarity is a suggestion, not proof.", ForeColor = BimTheme.Brand };
            layout.Controls.Add(_summary, 0, 0); layout.SetColumnSpan(_summary, 2);
            layout.Controls.Add(new Label { Text = "SAVED MODEL", Dock = DockStyle.Fill }, 0, 1);
            layout.Controls.Add(new Label { Text = "LOADED MODELS · best name matches first", Dock = DockStyle.Fill }, 1, 1);
            _saved = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
            _candidates = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
            layout.Controls.Add(_saved, 0, 2); layout.Controls.Add(_candidates, 1, 2);
            _detail = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
            _evidence = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
            layout.Controls.Add(_detail, 0, 3); layout.Controls.Add(_evidence, 1, 3);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            Button cancel = new Button { Text = "Cancel", Width = 90, Height = 34, DialogResult = DialogResult.Cancel };
            _apply = new Button { Text = "Apply confirmed changes", Width = 180, Height = 34, Enabled = false };
            _confirm = new Button { Text = "Confirm replacement →", Width = 180, Height = 34, Enabled = false };
            actions.Controls.AddRange(new Control[] { cancel, _apply, _confirm }); layout.Controls.Add(actions, 0, 4); layout.SetColumnSpan(actions, 2);
            Controls.Add(layout); CancelButton = cancel;
            foreach (ClashMatch match in _pending) _saved.Items.Add(match.Saved);
            _saved.SelectedIndexChanged += delegate { ShowCandidates(); };
            _candidates.SelectedIndexChanged += delegate { ShowEvidence(); };
            _confirm.Click += delegate { Confirm(); };
            _apply.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Shown += delegate { BimWindowLayout.FitToWorkingArea(this); if (_saved.Items.Count > 0) _saved.SelectedIndex = 0; };
        }
        private void ShowCandidates()
        {
            _candidates.Items.Clear(); int index = _saved.SelectedIndex;
            if (index < 0) return;
            ClashMatch match = _pending[index];
            _detail.Text = match.Saved.FileName + "\r\n" + match.Saved.Container + "\r\n" + match.Reason;
            HashSet<string> assigned = new HashSet<string>(ClashModelMatcher.Resolve(Result, _loaded)
                .Where(m => m.Resolved && m.Saved.Id != match.Saved.Id).Select(m => m.Current.Id), StringComparer.OrdinalIgnoreCase);
            foreach (ClashCandidate candidate in match.Candidates.Where(c => !assigned.Contains(c.Model.Id))) _candidates.Items.Add(candidate);
            // No replacement is preselected, even for a very strong suggestion.
            _evidence.Text = _candidates.Items.Count == 0
                ? "All available loaded models are already mapped to other saved entries. Load the intended model or remove an unwanted saved selection."
                : "Select a loaded file to compare it with the saved model.";
            _confirm.Enabled = false;
        }
        private void ShowEvidence()
        {
            ClashCandidate candidate = _candidates.SelectedItem as ClashCandidate;
            _confirm.Enabled = candidate != null;
            _evidence.Text = candidate == null ? "" : candidate.Model.FileName + "\r\n" + candidate.Model.Container + "\r\n" + candidate.Model.SourcePath + "\r\n" + candidate.Evidence;
        }
        private void Confirm()
        {
            if (_saved.SelectedIndex < 0 || _candidates.SelectedItem == null) return;
            ClashMatch match = _pending[_saved.SelectedIndex]; ClashCandidate candidate = (ClashCandidate)_candidates.SelectedItem;
            // Confirm is explicit, but changes remain a draft until Apply.
            ClashModelMatcher.Confirm(Result, match.Saved.Id, candidate.Model);
            _pending.RemoveAt(_saved.SelectedIndex); _saved.Items.RemoveAt(_saved.SelectedIndex);
            List<ClashMatch> unresolved = ClashModelMatcher.Resolve(Result, _loaded).Where(m => !m.Resolved).ToList();
            _apply.Enabled = unresolved.Count == 0;
            _summary.Text = _pending.Count + " saved model(s) still need a decision. "
                + (_apply.Enabled ? "Apply will update the draft; Save/Restore stores it." : "Unresolved files block restoring the native test.");
            _confirm.Enabled = false; _candidates.Items.Clear(); _detail.Text = ""; _evidence.Text = "";
            if (_saved.Items.Count > 0) _saved.SelectedIndex = 0;
            else if (!_apply.Enabled) _summary.Text += " A replacement is still ambiguous. Cancel and load the intended model, or remove an unwanted saved entry from the setup.";
        }
    }
}
