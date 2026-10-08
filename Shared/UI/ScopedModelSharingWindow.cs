using System;
using Autodesk.Navisworks.Api;
using BIM.Tools.Services;
using System.Windows.Forms;

namespace BIM.Tools.UI
{
    internal static class ScopedModelSharingWindow
    {
        private static ScopedModelSharingForm _current;
        internal static void Open(Document document)
        {
            if (document == null || document.IsClear) throw new InvalidOperationException("Open a Navisworks model first.");
            string key = SectionBoxSettingsStore.GetModelKey(document);
            if (_current != null && !_current.IsDisposed)
            {
                if (_current.ModelKey == key)
                {
                    if (_current.WindowState != FormWindowState.Minimized) { _current.WindowState = FormWindowState.Minimized; return; }
                    _current.WindowState = FormWindowState.Normal; _current.Show(); _current.Activate(); return;
                }
                _current.Close();
            }
            ScopedModelSharingForm form = new ScopedModelSharingForm(document); _current = form;
            form.FormClosed += delegate { if (ReferenceEquals(_current, form)) _current = null; form.Dispose(); };
            form.Show(); form.Activate();
        }
    }
}
