using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BIM.Tools.Services;

namespace BIM.Tools.UI
{
    internal sealed class SavedBoxPickerForm : Form
    {
        private readonly ListView _views;
        private readonly Button _use;
        internal SavedBoxViewpoint Selected { get; private set; }
        internal SavedBoxPickerForm(IList<SavedBoxViewpoint> views)
        {
            SuspendLayout(); Text = "Use Existing Box · Saved Viewpoints";
            Font = new Font("Segoe UI", 9F); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96,96);
            ClientSize = new Size(650,410); MinimumSize = new Size(600,350); StartPosition = FormStartPosition.CenterParent;
            BackColor = BimTheme.Paper; ShowInTaskbar = false; MinimizeBox = false;
            Controls.Add(new Label { Text = "Choose a Saved Viewpoint containing a Section Box.", Location = new Point(18,16), Size = new Size(612,24), ForeColor = BimTheme.Brand });
            TextBox search = new TextBox { Location = new Point(18,47), Size = new Size(612,26), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _views = new ListView { Location = new Point(18,85), Size = new Size(612,266), View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            _views.Columns.Add("Saved Viewpoint / folder", 470); _views.Columns.Add("Section Box", 115);
            _views.Resize += delegate { int statusWidth = (int)(130 * _views.DeviceDpi / 96F); _views.Columns[1].Width = statusWidth; _views.Columns[0].Width = System.Math.Max(160, _views.ClientSize.Width - statusWidth - 8); };
            _use = new Button { Text = "USE SELECTED BOX", Location = new Point(342,367), Size = new Size(170,28), Enabled = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            Button cancel = new Button { Text = "CANCEL", DialogResult = DialogResult.Cancel, Location = new Point(524,367), Size = new Size(106,28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            System.Action render = delegate
            {
                _views.BeginUpdate(); _views.Items.Clear();
                foreach (SavedBoxViewpoint view in views.Where(v => v.Path.IndexOf(search.Text, System.StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(v => v.Path))
                {
                    ListViewItem row = new ListViewItem(view.Path) { Tag = view, ForeColor = view.HasBox ? BimTheme.Graphite : BimTheme.Muted };
                    row.SubItems.Add(view.HasBox ? "Available" : "No saved box"); _views.Items.Add(row);
                }
                _views.EndUpdate(); _use.Enabled = false;
            };
            search.TextChanged += delegate { render(); };
            _views.SelectedIndexChanged += delegate { Selected = _views.SelectedItems.Count == 1 ? (SavedBoxViewpoint)_views.SelectedItems[0].Tag : null; _use.Enabled = Selected != null && Selected.HasBox; };
            _use.Click += delegate { if (_use.Enabled) { DialogResult = DialogResult.OK; Close(); } };
            _views.DoubleClick += delegate { if (_use.Enabled) _use.PerformClick(); };
            Controls.AddRange(new Control[] { search, _views, _use, cancel }); AcceptButton = _use; CancelButton = cancel;
            render(); ResumeLayout(true); Shown += delegate { BimWindowLayout.FitToWorkingArea(this); };
        }
    }
}
