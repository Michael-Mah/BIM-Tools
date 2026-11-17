using System;
using System.Drawing;
using System.Windows.Forms;
using BIM.Tools.UI;

namespace BIM.Tools.Updates
{
    internal static class AboutWindow
    {
        private static Form _form;

        internal static void Open()
        {
            if (_form == null || _form.IsDisposed) _form = Create();
            _form.Show();
            _form.WindowState = FormWindowState.Normal;
            _form.Activate();
        }

        internal static void Close()
        {
            if (_form != null) _form.Dispose();
            _form = null;
        }

        private static Form Create()
        {
            Form form = new Form
            {
                Text = "About BIM Tools",
                Font = new Font("Segoe UI", 9F),
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96, 96),
                ClientSize = new Size(560, 300),
                MinimumSize = new Size(500, 300),
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = BimTheme.Paper
            };
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(24),
                ColumnCount = 1, RowCount = 5
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label
            {
                Text = EditionBrand.ProductName, AutoSize = true,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = BimTheme.Brand
            }, 0, 0);
            layout.Controls.Add(new Label
            {
                Text = "Version " + UpdateClient.CurrentVersion.ToString(3)
                    + " · Navisworks Manage " + UpdateClient.HostYear,
                AutoSize = true, Margin = new Padding(3, 8, 3, 12)
            }, 0, 1);
            layout.Controls.Add(new Label
            {
                Text = "Federation Audit, Section Box, Model Sharing and Clash Setup.",
                Dock = DockStyle.Fill
            }, 0, 2);
            layout.Controls.Add(new Label
            {
                Text = "© Michael-Mah 2026", AutoSize = true,
                ForeColor = BimTheme.Muted, Margin = new Padding(3, 8, 3, 12)
            }, 0, 3);
            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false
            };
            Button check = new Button { Text = "Check for Updates", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            Button close = new Button { Text = "Close", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            check.Click += delegate { UpdateWindow.Open(true); };
            close.Click += delegate { form.Close(); };
            actions.Controls.Add(check);
            actions.Controls.Add(close);
            layout.Controls.Add(actions, 0, 4);
            form.Controls.Add(layout);
            form.CancelButton = close;
            form.Shown += delegate { BimWindowLayout.FitToWorkingArea(form); };
            return form;
        }
    }
}
