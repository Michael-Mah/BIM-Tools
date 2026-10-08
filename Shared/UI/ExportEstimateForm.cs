using System.Drawing;
using System.Windows.Forms;
using BIM.Tools.Services;

namespace BIM.Tools.UI
{
    public sealed class ExportEstimateForm : Form
    {
        private readonly Label _estimate;
        public ExportEstimateForm(int items,string format)
        {
            SuspendLayout(); Text=format + " Export · Estimation time";
            Font=new Font("Segoe UI",9F); AutoScaleMode=AutoScaleMode.Dpi; AutoScaleDimensions=new SizeF(96,96);
            ClientSize=new Size(430,178); FormBorderStyle=FormBorderStyle.FixedDialog; MinimizeBox=false; MaximizeBox=false; ShowInTaskbar=false;
            BackColor=BimTheme.Paper; StartPosition=FormStartPosition.Manual;
            _estimate=new Label {Location=new Point(18,16),Size=new Size(394,98),Text=items.ToString("N0") + " geometry items\r\nMeasuring a small sample to estimate " + format + " export time…\r\nThe estimate will update as export progresses.",ForeColor=BimTheme.Graphite};
            Button close=new Button {Text="CLOSE",Location=new Point(306,132),Size=new Size(106,28)}; close.Click+=delegate { Hide(); };
            Controls.AddRange(new Control[] {_estimate,close}); ResumeLayout(true);
        }
        public void UpdateEstimate(string time,string detail)
        { _estimate.Text=time + "\r\n" + detail + "\r\nApproximate; geometry complexity and disk speed affect duration."; }
    }
}
