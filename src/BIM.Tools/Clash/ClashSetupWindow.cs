using System;
using Autodesk.Navisworks.Api;
using System.Windows.Forms;

namespace BIM.Tools.Clash
{
    internal static class ClashSetupWindow
    {
        private static ClashSetupForm _current;
        internal static void Open(Document document)
        {
            if (document == null || document.IsClear) throw new InvalidOperationException("Open a Navisworks model first.");
            ClashNativeService.GetTests(document);
            if (_current != null && !_current.IsDisposed)
            {
                if (ReferenceEquals(_current.Document, document))
                {
                    _current.WindowState = FormWindowState.Normal; _current.Show(); _current.Activate(); return;
                }
                _current.Close(); if (!_current.IsDisposed) return;
            }
            ClashSetupForm form = new ClashSetupForm(document); _current = form;
            form.FormClosed += delegate { if (ReferenceEquals(_current, form)) _current = null; form.Dispose(); };
            form.Show(); form.Activate();
        }
    }
}
