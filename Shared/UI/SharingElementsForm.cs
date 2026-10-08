using System.Drawing;
using System.Windows.Forms;
using BIM.Tools.Services;

namespace BIM.Tools.UI
{
    internal sealed class SharingElementsForm : Form
    {
        internal SharingElementsForm(SharingSource source)
        {
            SuspendLayout(); Text = "Elements inside Section Box · " + source.Name;
            Font = new Font("Segoe UI", 9F); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96,96);
            ClientSize = new Size(850,490); MinimumSize = new Size(600,350); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            BackColor = BimTheme.Paper;
            Controls.Add(new Label { Text = source.Name + " · " + source.Elements.Count.ToString("N0") + " geometry items intersect the box", Location = new Point(18,16), Size = new Size(810,38), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right });
            // Virtual rows keep even a large model from constructing 100k controls.
            ListView list = new ListView { Location = new Point(18,61), Size = new Size(814,368), View = View.Details, VirtualMode = true, FullRowSelect = true, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            list.Columns.Add("Element", 300); list.Columns.Add("Model hierarchy", 490);
            list.Resize += delegate { list.Columns[0].Width = list.ClientSize.Width * 2 / 5; list.Columns[1].Width = System.Math.Max(160,list.ClientSize.Width - list.Columns[0].Width - 8); };
            list.RetrieveVirtualItem += delegate(object sender, RetrieveVirtualItemEventArgs args)
            { SharingElement element = source.Elements[args.ItemIndex]; args.Item = new ListViewItem(new[] { element.Name, element.Hierarchy }); };
            list.VirtualListSize = source.Elements.Count;
            Button close = new Button { Text = "CLOSE", DialogResult = DialogResult.OK, Location = new Point(722,447), Size = new Size(110,28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            Controls.AddRange(new Control[] { list, close }); CancelButton = close; ResumeLayout(true);
            Shown += delegate { BimWindowLayout.FitToWorkingArea(this); };
        }
    }
}
