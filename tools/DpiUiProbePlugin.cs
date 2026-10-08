using BIM.Tools.Models;
using BIM.Tools.Services;
using BIM.Tools.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;

namespace BIM.Tools.DpiUiProbe
{
    [Plugin("BIM.DpiUiProbe", "BIMT", DisplayName = "BIM DPI UI Probe")]
    public sealed class DpiUiProbePlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            string outputFolder = parameters != null && parameters.Length > 0
                ? parameters[0]
                : Path.Combine(Path.GetTempPath(), "bim-dpi-ui-probe");
            Directory.CreateDirectory(outputFolder);

            StringBuilder report = new StringBuilder();
            report.AppendLine("RelativeLayoutScaleSimulation=True;ActualDeviceDpiRecordedSeparately");

            try
            {
                foreach (float scale in new[] { 1F, 1.25F, 1.5F, 2F })
                {
                string suffix = ((int)(scale * 100)).ToString();
                Document document = Autodesk.Navisworks.Api.Application.ActiveDocument;
                using (ScopedModelSharingForm sharing = new ScopedModelSharingForm(document))
                {
                    sharing.Scale(new SizeF(scale, scale)); sharing.ShowInTaskbar = false; sharing.Show();
                    sharing.Location = new Point(-20000,-20000);
                    System.Windows.Forms.Application.DoEvents(); sharing.PerformLayout();
                    SaveControl(sharing, Path.Combine(outputFolder, "model-sharing-" + suffix + ".png"));
                    Audit(sharing, "ModelSharing" + suffix, report);
                    report.AppendLine("ModelSharing" + suffix + "=" + sharing.Width + "x" + sharing.Height + ";DeviceDpi=" + sharing.DeviceDpi);
                    foreach (Size size in new[] { new Size(1366,728), new Size(1920,1040), new Size(3840,2320) })
                    {
                        Rectangle area = new Rectangle(Point.Empty,size); BimWindowLayout.FitToWorkingArea(sharing, area);
                        if (!area.Contains(sharing.Bounds)) report.AppendLine("OVERFLOW SharingWindowOutsideWorkingArea");
                    }
                    sharing.Close();
                }
                List<SavedBoxViewpoint> sampleViews = new List<SavedBoxViewpoint>
                {
                    new SavedBoxViewpoint { Path = "Coordination / A long saved viewpoint containing a section box", HasBox = true },
                    new SavedBoxViewpoint { Path = "Coordination / View without a saved section box", HasBox = false }
                };
                using(ExportEstimateForm estimate=new ExportEstimateForm(323489,"OBJ"))
                { estimate.Scale(new SizeF(scale,scale)); estimate.Show(); estimate.Location=new Point(-20000,-20000); estimate.UpdateEstimate("Estimation time: about 12–30 min","Exporting representative geometry sample"); System.Windows.Forms.Application.DoEvents(); SaveControl(estimate,Path.Combine(outputFolder,"export-estimate-"+suffix+".png")); Audit(estimate,"ExportEstimate"+suffix,report); estimate.Close(); }
                using (SavedBoxPickerForm picker = new SavedBoxPickerForm(sampleViews))
                { picker.Scale(new SizeF(scale,scale)); picker.Show(); picker.Location = new Point(-20000,-20000); System.Windows.Forms.Application.DoEvents(); SaveControl(picker,Path.Combine(outputFolder,"saved-box-picker-"+suffix+".png")); Audit(picker,"SavedBoxPicker"+suffix,report); picker.Close(); }
                SharingSource sampleModel = new SharingSource("Example-Services.rvt",null);
                sampleModel.Elements.Add(new SharingElement("Bend Flanged DI-PN16 [5720504]","Level 1 / Pipe Fittings / Bend Flanged DI-PN16"));
                using (SharingElementsForm elements = new SharingElementsForm(sampleModel))
                { elements.Scale(new SizeF(scale,scale)); elements.Show(); elements.Location = new Point(-20000,-20000); System.Windows.Forms.Application.DoEvents(); SaveControl(elements,Path.Combine(outputFolder,"box-elements-"+suffix+".png")); Audit(elements,"BoxElements"+suffix,report); elements.Close(); }
                using (Timer snapshot = new Timer { Interval = 100 })
                {
                    string title = "Progress layout " + suffix;
                    snapshot.Tick += delegate
                    {
                        foreach (Form progress in System.Windows.Forms.Application.OpenForms)
                            if (progress.Text == title)
                            { snapshot.Stop(); progress.Scale(new SizeF(scale,scale)); BimWindowLayout.FitToWorkingArea(progress); progress.PerformLayout(); SaveControl(progress,Path.Combine(outputFolder,"progress-"+suffix+".png")); Audit(progress,"Progress"+suffix,report); progress.Close(); break; }
                    };
                    snapshot.Start(); SharingProgressForm.Run(null,title,ProgressLayoutWork());
                }
                using (SectionBoxBuilderForm form = new SectionBoxBuilderForm(document))
                {
                    form.Scale(new SizeF(scale, scale));
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-20000, -20000);
                    form.Show();
                    // OnShown intentionally keeps user windows on-screen; move
                    // the probe off-screen only after that production hook runs.
                    form.Location = new Point(-20000, -20000);
                    System.Windows.Forms.Application.DoEvents();
                    form.PerformLayout();
                    SaveControl(form, Path.Combine(outputFolder, "section-builder-" + suffix + ".png"));
                    Audit(form, "SectionBuilder" + suffix, report);
                    report.AppendLine("SectionBuilder" + suffix + "=" + form.Width + "x" + form.Height + ";DeviceDpi=" + form.DeviceDpi);
                    foreach (Size size in new[] { new Size(1366, 728), new Size(1920, 1040), new Size(3840, 2320) })
                    {
                        Rectangle area = new Rectangle(Point.Empty, size);
                        form.Size = new Size((int)(736 * scale), (int)(759 * scale));
                        BimWindowLayout.FitToWorkingArea(form, area);
                        bool fits = area.Contains(form.Bounds);
                        report.AppendLine("WorkingArea" + suffix + "_" + size.Width + "x" + size.Height + "=" + fits);
                        if (!fits) { report.AppendLine("OVERFLOW WindowOutsideWorkingArea"); }
                    }
                    form.Close();
                }
                }
                using (Timer timer = new Timer())
                {
                    timer.Interval = 100;
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        foreach (Form dialog in System.Windows.Forms.Application.OpenForms)
                        {
                            if (dialog.Text != "BIM Tools") { continue; }
                            SaveControl(dialog, Path.Combine(outputFolder, "error-dialog-actual-dpi.png"));
                            Audit(dialog, "ErrorDialog", report);
                            report.AppendLine("ErrorDialogDeviceDpi=" + dialog.DeviceDpi);
                            dialog.Close();
                            break;
                        }
                    };
                    timer.Start();
                    BimDialog.ShowError(null, "Layout verification", "A sample technical error for layout verification.",
                        "Project: disposable compatibility model\r\nStep: layout verification\r\nException: sample details only");
                }

                File.WriteAllText(
                    Path.Combine(outputFolder, "dpi-layout-report.txt"),
                    report.ToString(),
                    Encoding.UTF8);
                return report.ToString().Contains("OVERFLOW") ? -1 : 0;
            }
            catch (Exception exception)
            {
                report.AppendLine(exception.ToString());
                File.WriteAllText(
                    Path.Combine(outputFolder, "dpi-layout-report.txt"),
                    report.ToString(),
                    Encoding.UTF8);
                return -1;
            }
        }

        private static void ClickPrivateButton(object owner, string fieldName)
        {
            FieldInfo field = owner.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Button button = field == null ? null : field.GetValue(owner) as Button;
            if (button != null)
            {
                button.PerformClick();
            }
        }
        private static IEnumerable<SharingProgress> ProgressLayoutWork()
        {
            for(int i=0;i<1000000;i++)
            { System.Threading.Thread.SpinWait(80000); yield return new SharingProgress("2 / 3 · Check geometry inside box","Example-Services.rvt\r\n12,000 tree items checked · 1,433 geometry items found",i,1000000); }
        }

        private static void SetPrivateControlHeight(
            object owner,
            string fieldName,
            int height)
        {
            FieldInfo field = owner.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Control control = field == null ? null : field.GetValue(owner) as Control;
            if (control != null)
            {
                control.Height = height;
            }
        }

        private static void SaveControl(Control control, string path)
        {
            using (Bitmap bitmap = new Bitmap(control.Width, control.Height))
            {
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(path);
            }
        }

        private static void Audit(Control parent, string path, StringBuilder report)
        {
            foreach (Control child in parent.Controls)
            {
                string childPath = path + "/" + child.GetType().Name;
                if (child.Visible &&
                    !(parent is Form) &&
                    !IsScrollable(parent))
                {
                    Rectangle bounds = child.Bounds;
                    if (bounds.Left < -1 || bounds.Top < -1 ||
                        bounds.Right > parent.ClientSize.Width + 1 ||
                        bounds.Bottom > parent.ClientSize.Height + 1)
                    {
                        report.AppendLine(string.Format(
                            "OVERFLOW {0} child={1} parent={2}x{3}",
                            childPath,
                            bounds,
                            parent.ClientSize.Width,
                            parent.ClientSize.Height));
                    }
                }
                Audit(child, childPath, report);
            }
        }

        private static bool IsScrollable(Control control)
        {
            ScrollableControl scrollable = control as ScrollableControl;
            return scrollable != null && scrollable.AutoScroll;
        }
    }
}
