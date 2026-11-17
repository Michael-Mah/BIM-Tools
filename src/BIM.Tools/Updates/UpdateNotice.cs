using System;
using System.Drawing;
using System.Windows.Forms;
using BIM.Tools.UI;

namespace BIM.Tools.Updates
{
    internal sealed class UpdateNotice : Form
    {
        internal UpdateNotice(string installed,string available,Action download)
        {
            Text="BIM Tools · Update available";Font=new Font("Segoe UI",10F);AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            ClientSize=new Size(500,230);MinimumSize=new Size(420,230);StartPosition=FormStartPosition.CenterScreen;ShowInTaskbar=false;BackColor=BimTheme.Paper;MaximizeBox=false;MinimizeBox=false;
            TableLayoutPanel layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=4};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label{Text="A new version of BIM Tools is available",AutoSize=true,ForeColor=BimTheme.Brand,Font=new Font("Segoe UI",12F,FontStyle.Bold)},0,0);
            layout.Controls.Add(new Label{Text="Installed: "+installed+"    New version: "+available,AutoSize=true,Margin=new Padding(3,12,3,12)},0,1);
            layout.Controls.Add(new Label{Text="Download the update now? Save your work and close Navisworks before installation can finish.",Dock=DockStyle.Fill},0,2);
            FlowLayoutPanel actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=true};
            Button later=new Button{Text="Later",AutoSize=true,Padding=new Padding(10,5,10,5)},install=new Button{Text="Download and install",AutoSize=true,Padding=new Padding(10,5,10,5),BackColor=BimTheme.Brand,ForeColor=Color.White,FlatStyle=FlatStyle.Flat};
            later.Click+=delegate{Close();};install.Click+=delegate{Close();download();};actions.Controls.Add(later);actions.Controls.Add(install);layout.Controls.Add(actions,0,3);Controls.Add(layout);CancelButton=later;
            Shown+=delegate{BimWindowLayout.FitToWorkingArea(this);};
        }
    }
}
