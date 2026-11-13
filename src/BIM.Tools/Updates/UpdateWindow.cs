using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using BIM.Tools.UI;

namespace BIM.Tools.Updates
{
    [Plugin("BIM.Tools.Updates", "BIMT", DisplayName = "BIM Tools update checks")]
    public sealed class UpdateWatcher : EventWatcherPlugin
    {
        private Timer _startup;
        public override void OnLoaded()
        {
            _startup = new Timer { Interval = 12000 };
            _startup.Tick += delegate
            {
                _startup.Stop();
                UpdateWindow.StartAutomatic();
            };
            _startup.Start();
        }
        public override void OnUnloading()
        {
            if (_startup != null) _startup.Dispose();
            UpdateWindow.Close();
        }
    }

    internal static class UpdateWindow
    {
        private static UpdateForm _form;
        private static UpdateForm Get()
        {
            if (_form == null || _form.IsDisposed) _form = new UpdateForm();
            return _form;
        }
        internal static void Open()
        {
            UpdateForm form = Get();
            form.Show();
            form.WindowState = FormWindowState.Normal;
            form.Activate();
        }
        internal static void StartAutomatic() { Get().Check(true); }
        internal static void Close() { if (_form != null) _form.Dispose(); _form = null; }
    }

    internal sealed class UpdateForm : Form
    {
        private UpdateSettings _settings;
        private readonly Label _status;
        private readonly TextBox _token;
        private readonly CheckBox _automatic;
        private readonly Button _check, _install;
        private AvailableUpdate _update;
        private bool _working;

        internal UpdateForm()
        {
            Text = "BIM Tools · Updates";
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(580, 380);
            MinimumSize = new Size(480, 350);
            StartPosition = FormStartPosition.CenterScreen;
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 6
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int row = 2; row < 6; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label
            {
                Text = "Installed " + UpdateClient.CurrentVersion.ToString(3) + " · Navisworks " + UpdateClient.HostYear,
                AutoSize = true, ForeColor = BimTheme.Brand
            }, 0, 0);
            _status = new Label { Dock = DockStyle.Fill, Text = "Ready to check GitHub releases.", Padding = new Padding(0, 12, 0, 8) };
            layout.Controls.Add(_status, 0, 1);
            layout.Controls.Add(new Label
            {
                Text = "Private repository access token (Contents: read). Saved encrypted for this Windows user.",
                AutoSize = true, MaximumSize = new Size(500, 0)
            }, 0, 2);
            _token = new TextBox { Dock = DockStyle.Top, UseSystemPasswordChar = true };
            layout.Controls.Add(_token, 0, 3);
            _automatic = new CheckBox { Text = "Check automatically when Navisworks starts", AutoSize = true };
            layout.Controls.Add(_automatic, 0, 4);
            FlowLayoutPanel actions = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false };
            _check = new Button { Text = "Save / Check", AutoSize = true };
            _install = new Button { Text = "Download / Install", AutoSize = true, Enabled = false };
            Button later = new Button { Text = "Later", AutoSize = true };
            _check.Click += delegate { Check(false); };
            _install.Click += Download;
            later.Click += delegate { Hide(); };
            foreach (Button button in new[] { _check, _install, later })
            {
                button.Padding = new Padding(4);
                actions.Controls.Add(button);
            }
            layout.Controls.Add(actions, 0, 5);
            Controls.Add(layout);
            try { _settings = UpdateSettings.Load(); }
            catch { _settings = new UpdateSettings(); _status.Text = "Update settings could not be read. Enter your access token again."; }
            _token.Text = _settings.Token;
            _automatic.Checked = _settings.Automatic;
            // Establish the UI dispatcher before any network work starts.
            IntPtr handle = Handle;
            Shown += delegate { BimWindowLayout.FitToWorkingArea(this); };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; Hide(); }
            };
        }

        internal async void Check(bool automatic)
        {
            if (_working || (automatic && !_settings.Automatic)) return;
            if (automatic && !_settings.Configured)
            {
                _status.Text = "Enable update checks on this computer by entering a GitHub token with Contents read access to "
                    + UpdateClient.Repository + ", then click Save / Check.";
                Show();
                return;
            }
            Busy(true);
            _update = null;
            try
            {
                if (!automatic)
                {
                    _settings.Token = _token.Text.Trim();
                    _settings.Automatic = _automatic.Checked;
                    _settings.Save();
                }
                string token = _settings.Token;
                _status.Text = "Checking GitHub releases…";
                AvailableUpdate update = await Task.Run(() => UpdateClient.Check(token));
                if (IsDisposed) return;
                _update = update;
                _status.Text = update == null ? "You have the latest published version."
                    : "Version " + update.Manifest.version + " is available. Download and install it?\r\n\r\n"
                        + "After downloading, save your work and close Navisworks. The installer will wait until Navisworks is closed.";
                if (update != null && automatic) Show();
            }
            catch (Exception error)
            {
                if (!IsDisposed) _status.Text = error.Message;
            }
            finally { if (!IsDisposed) Busy(false); }
        }

        private async void Download(object sender, EventArgs args)
        {
            if (_update == null || _working) return;
            Busy(true);
            try
            {
                AvailableUpdate update = _update;
                string token = _settings.Token;
                _status.Text = "Downloading " + update.Manifest.version + " and verifying its checksum…";
                string installer = await Task.Run(() => UpdateClient.Download(update, token));
                if (IsDisposed) return;
                Process.Start(new ProcessStartInfo(installer, "/wait") { UseShellExecute = true });
                _status.Text = "Installer opened. Save your work and close all Navisworks windows to complete the update.";
            }
            catch (Exception error) { if (!IsDisposed) _status.Text = error.Message; }
            finally { if (!IsDisposed) Busy(false); }
        }

        private void Busy(bool value)
        {
            _working = value;
            _check.Enabled = _token.Enabled = _automatic.Enabled = !value;
            _install.Enabled = !value && _update != null;
        }
    }
}
