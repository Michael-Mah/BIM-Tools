using System.Drawing;
using System.Windows.Forms;

namespace BIM.Tools.UI
{
    public static class BimDialog
    {
        public static void ShowError(
            IWin32Window owner,
            string title,
            string message,
            string details)
        {
            using (Form form = new Form())
            {
                form.SuspendLayout();
                form.Text = EditionBrand.ProductName;
                form.Font = new Font("Segoe UI", 9F);
                form.AutoScaleMode = AutoScaleMode.Dpi;
                form.AutoScaleDimensions = new SizeF(96F, 96F);
                form.BackColor = BimTheme.Paper;
                form.ForeColor = BimTheme.Graphite;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = false;
                form.StartPosition = owner == null
                    ? FormStartPosition.CenterScreen
                    : FormStartPosition.CenterParent;
                form.ClientSize = new Size(520, 330);

                Panel header = new Panel();
                header.Dock = DockStyle.Top;
                header.Height = 46;
                header.BackColor = BimTheme.Secondary;
                header.Padding = new Padding(14, 0, 14, 0);

                Label headerText = new Label();
                headerText.Dock = DockStyle.Fill;
                headerText.TextAlign = ContentAlignment.MiddleLeft;
                headerText.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                headerText.Text = title;
                header.Controls.Add(headerText);

                Label summary = new Label();
                summary.Dock = DockStyle.Top;
                summary.Height = 62;
                summary.Padding = new Padding(18, 14, 18, 8);
                summary.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
                summary.ForeColor = BimTheme.Error;
                summary.Text = message;

                TextBox detailsBox = new TextBox();
                detailsBox.Dock = DockStyle.Fill;
                detailsBox.Multiline = true;
                detailsBox.ReadOnly = true;
                detailsBox.ScrollBars = ScrollBars.Vertical;
                detailsBox.Font = new Font("Consolas", 8.5F);
                detailsBox.BackColor = BimTheme.Secondary;
                detailsBox.BorderStyle = BorderStyle.FixedSingle;
                detailsBox.Text = details;

                Panel body = new Panel();
                body.Dock = DockStyle.Fill;
                body.Padding = new Padding(18, 0, 18, 14);
                body.Controls.Add(detailsBox);

                Panel footer = new Panel();
                footer.Dock = DockStyle.Bottom;
                // Establish the intended width before anchoring the close button.
                // A Panel otherwise starts at 200px and stores a negative right gap.
                footer.Size = new Size(form.ClientSize.Width, 72);
                footer.BackColor = BimTheme.Paper;

                Label support = new Label();
                support.Text =
                    "If the issue persists, contact Smart Build Technology IT Support at " +
                    "IT@smartbuildtech.com.sg and include the project name, steps performed, " +
                    "and an error screenshot.";
                support.Font = new Font("Segoe UI", 7.5F);
                support.ForeColor = BimTheme.Muted;
                support.Location = new Point(18, 8);
                support.Size = new Size(380, 52);
                support.AutoEllipsis = true;
                footer.Controls.Add(support);

                Button close = new Button();
                close.Text = "Close";
                close.DialogResult = DialogResult.OK;
                close.FlatStyle = FlatStyle.Flat;
                close.FlatAppearance.BorderSize = 0;
                close.BackColor = BimTheme.Brand;
                close.ForeColor = Color.White;
                close.Size = new Size(86, 32);
                close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                close.Location = new Point(416, 20);
                footer.Controls.Add(close);

                form.Controls.Add(body);
                form.Controls.Add(summary);
                form.Controls.Add(footer);
                form.Controls.Add(header);
                form.AcceptButton = close;
                form.CancelButton = close;
                form.ResumeLayout(true);

                if (owner == null)
                {
                    form.ShowDialog();
                }
                else
                {
                    form.ShowDialog(owner);
                }
            }
        }
    }
}
