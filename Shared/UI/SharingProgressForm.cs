using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using BIM.Tools.Models;

namespace BIM.Tools.UI
{
    // Navisworks model APIs must stay on the owning UI thread. A timer advances
    // bounded units of work between message-loop turns; no worker touches the API.
    internal sealed class SharingProgressForm : Form
    {
        private readonly Label _stage, _detail, _count;
        private readonly ProgressBar _bar;
        private readonly Timer _timer;
        private readonly IEnumerator<SharingProgress> _work;
        private Exception _error;
        internal int MessageLoopTurns { get; private set; }
        internal string LastStage { get { return _stage.Text; } }

        private SharingProgressForm(string title, IEnumerable<SharingProgress> work)
        {
            SuspendLayout();
            Text = title; Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(520, 204); BackColor = BimTheme.Paper;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            _stage = new Label { Location = new Point(20,18), Size = new Size(480,27), ForeColor = BimTheme.Brand, Text = "Preparing…", Font = new Font("Segoe UI",11F) };
            _detail = new Label { Location = new Point(20,52), Size = new Size(480,44), Text = "Opening progress window…" };
            _bar = new ProgressBar { Location = new Point(20,104), Size = new Size(480,20), Style = ProgressBarStyle.Marquee };
            _count = new Label { Location = new Point(20,140), Size = new Size(330,44) };
            Button cancel = new Button { Text = "CANCEL", Location = new Point(396,145), Size = new Size(104,30), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { _stage, _detail, _bar, _count, cancel }); CancelButton = cancel;
            _work = work.GetEnumerator(); _timer = new Timer { Interval = 15 };
            _timer.Tick += Advance;
            Shown += delegate { BimWindowLayout.FitToWorkingArea(this); _timer.Start(); };
            ResumeLayout(true);
        }
        private void Advance(object sender, EventArgs args)
        {
            _timer.Stop(); MessageLoopTurns++;
            try
            {
                Stopwatch budget = Stopwatch.StartNew();
                SharingProgress latest = null;
                do
                {
                    if (!_work.MoveNext()) { if (latest != null) Display(latest); DialogResult = DialogResult.OK; Close(); return; }
                    latest = _work.Current;
                } while (budget.ElapsedMilliseconds < 35);
                if (latest != null) Display(latest);
                _timer.Start();
            }
            catch (Exception error) { _error = error; DialogResult = DialogResult.Abort; Close(); }
        }
        private void Display(SharingProgress step)
        {
            _stage.Text = step.Stage; _detail.Text = step.Detail;
            ProgressBarStyle style = step.Total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
            if (_bar.Style != style) _bar.Style = style;
            if (step.Total > 0) _bar.Value = (int)Math.Min(100L, (long)step.Completed * 100 / step.Total);
            _count.Text = step.Total > 0 ? string.Format("{0:N0} / {1:N0} · {2}%", step.Completed, step.Total, _bar.Value) : string.Format("{0:N0} processed", step.Completed);
        }
        internal static bool Run(IWin32Window owner, string title, IEnumerable<SharingProgress> work)
        {
            using (SharingProgressForm form = new SharingProgressForm(title, work))
            {
                bool completed = form.ShowDialog(owner) == DialogResult.OK;
                if (form._error != null) throw form._error;
                return completed;
            }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { _timer.Dispose(); _work.Dispose(); } base.Dispose(disposing); }
    }
}
