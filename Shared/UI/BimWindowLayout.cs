using System;
using System.Drawing;
using System.Windows.Forms;

namespace BIM.Tools.UI
{
    internal static class BimWindowLayout
    {
        public static void FitToWorkingArea(Form form)
        {
            FitToWorkingArea(form, Screen.FromControl(form).WorkingArea);
        }

        // The overload allows layout probes to check small displays without
        // changing the user's display resolution or Windows scaling settings.
        internal static void FitToWorkingArea(Form form, Rectangle workingArea)
        {
            int margin = Math.Max(8, (int)Math.Round(8 * form.DeviceDpi / 96F));
            Size available = new Size(
                Math.Max(1, workingArea.Width - margin * 2),
                Math.Max(1, workingArea.Height - margin * 2));
            form.MinimumSize = new Size(
                Math.Min(form.MinimumSize.Width, available.Width),
                Math.Min(form.MinimumSize.Height, available.Height));
            form.Size = new Size(
                Math.Min(form.Width, available.Width),
                Math.Min(form.Height, available.Height));
            form.Location = new Point(
                Math.Max(workingArea.Left + margin, Math.Min(form.Left, workingArea.Right - margin - form.Width)),
                Math.Max(workingArea.Top + margin, Math.Min(form.Top, workingArea.Bottom - margin - form.Height)));
        }
    }
}
