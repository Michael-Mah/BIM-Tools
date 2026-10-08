using System;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using BIM.Tools.Services;

namespace BIM.Tools.UI
{
    internal static class SectionBoxBuilderWindow
    {
        private static SectionBoxBuilderForm _current;

        public static void Open(
            Document document,
            IWin32Window owner,
            EventHandler sectionBoxCreated,
            bool toggleMinimizeWhenOpen)
        {
            if (document == null || document.IsClear)
            {
                MessageBox.Show(
                    owner,
                    "Open a federated Navisworks document first.",
                    "Section Box Builder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string key = SectionBoxSettingsStore.GetModelKey(document);
            if (_current != null && !_current.IsDisposed)
            {
                if (string.Equals(
                    _current.ModelKey,
                    key,
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (sectionBoxCreated != null)
                    {
                        _current.SectionBoxCreated -= sectionBoxCreated;
                        _current.SectionBoxCreated += sectionBoxCreated;
                    }
                    if (toggleMinimizeWhenOpen &&
                        _current.WindowState != FormWindowState.Minimized)
                    {
                        _current.WindowState = FormWindowState.Minimized;
                        return;
                    }
                    if (_current.WindowState == FormWindowState.Minimized)
                    {
                        _current.WindowState = FormWindowState.Normal;
                    }
                    _current.Show();
                    _current.BringToFront();
                    _current.Activate();
                    return;
                }
                _current.Close();
            }

            SectionBoxBuilderForm builder = new SectionBoxBuilderForm(document);
            _current = builder;
            if (sectionBoxCreated != null)
            {
                builder.SectionBoxCreated += sectionBoxCreated;
            }
            builder.FormClosed += delegate
            {
                if (ReferenceEquals(_current, builder))
                {
                    _current = null;
                }
                builder.Dispose();
            };

            // Keep the Builder unowned and modeless. An owned tool window can
            // follow its owner into a disabled/minimized state; an independent
            // modeless window lets the user keep navigating and selecting in
            // Navisworks while the Builder is open or minimized.
            builder.Show();
            builder.Activate();
        }
    }
}
