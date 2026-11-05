using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BIM.Tools.UI;

namespace BIM.Tools.Health
{
    internal sealed class AuditWatchedViewsForm : Form
    {
        private readonly AuditStore _store;
        private readonly DataGridView _grid;
        internal AuditWatchedViewsForm(AuditStore store)
        {
            _store=store;Text="Federation Audit · Watched views";Font=new Font("Segoe UI",9F);
            ClientSize=new Size(800,450);MinimumSize=new Size(640,360);StartPosition=FormStartPosition.CenterParent;
            AutoScaleMode=AutoScaleMode.Dpi;BackColor=BimTheme.Paper;ShowInTaskbar=false;MinimizeBox=false;
            TableLayoutPanel layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(16)};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,70));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
            List<string> warnings=new List<string>();List<AuditBaseline> views=store.WatchedViews(warnings);
            layout.Controls.Add(new Label{Dock=DockStyle.Fill,Text="Save a complete model list for each view you want to register. Enable the views to check when opened or refreshed in Navisworks.\r\n"+(warnings.Count>0?"Some saved lists could not be read. Open the complete-list selector for details.":views.Count==0?"No views registered yet.":"Each view uses its own complete list. Checks do not replace saved expectations."),ForeColor=BimTheme.Muted},0,0);
            _grid=new DataGridView{Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,BackgroundColor=Color.White,BorderStyle=BorderStyle.FixedSingle};
            _grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="enabled",HeaderText="Check",FillWeight=14,MinimumWidth=60});
            _grid.Columns.Add(new DataGridViewTextBoxColumn{Name="name",HeaderText="View / active complete list",ReadOnly=true,FillWeight=65});
            _grid.Columns.Add(new DataGridViewTextBoxColumn{Name="files",HeaderText="Expected files",ReadOnly=true,FillWeight=21});
            foreach(AuditBaseline baseline in views)
            {
                int index=_grid.Rows.Add(baseline.AutoCheck,baseline.ToString(),baseline.Snapshot.Files.Count);
                DataGridViewRow row=_grid.Rows[index];row.Tag=baseline;
                row.Cells[1].ToolTipText=baseline.Snapshot.DocumentPath;
                if(string.IsNullOrEmpty(AuditNames.DocumentKey(baseline.Snapshot.DocumentPath)))
                {row.Cells[0].ReadOnly=true;row.Cells[0].ToolTipText="Save this Navisworks document and register its complete list before enabling checks.";}
            }
            layout.Controls.Add(_grid,0,1);
            FlowLayoutPanel buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,8,0,0)};
            Button cancel=new Button{Text="Cancel",Size=new Size(100,30),DialogResult=DialogResult.Cancel};
            Button apply=new Button{Text="Save changes",Size=new Size(140,30),BackColor=BimTheme.Brand,ForeColor=Color.White,FlatStyle=FlatStyle.Flat};
            apply.Click+=delegate {try{ApplyChanges();DialogResult=DialogResult.OK;Close();}catch(Exception error){BimDialog.ShowError(this,"Watched views",error.Message,error.ToString());}};
            buttons.Controls.Add(cancel);buttons.Controls.Add(apply);layout.Controls.Add(buttons,0,2);Controls.Add(layout);AcceptButton=apply;CancelButton=cancel;
            foreach(Button button in new[]{cancel,apply}){button.AutoSize=true;button.MinimumSize=new Size(100,Font.Height+16);}
            Layout+=delegate
            {
                Label explanation=layout.GetControlFromPosition(0,0) as Label;
                if(explanation!=null)layout.RowStyles[0].Height=Math.Max(70,TextRenderer.MeasureText(explanation.Text,Font,new Size(Math.Max(100,layout.ClientSize.Width-40),int.MaxValue),TextFormatFlags.WordBreak).Height+16);
                layout.RowStyles[2].Height=Math.Max(48,Font.Height+40);
            };
            Shown+=delegate{BimWindowLayout.FitToWorkingArea(this);};
        }
        internal void ApplyChanges()
        {
            _grid.EndEdit();
            foreach(DataGridViewRow row in _grid.Rows)
            {
                AuditBaseline baseline=(AuditBaseline)row.Tag;bool enabled=Convert.ToBoolean(row.Cells[0].Value);
                if(enabled!=baseline.AutoCheck)_store.SetAutomatic(baseline,enabled);
            }
        }
    }
}
