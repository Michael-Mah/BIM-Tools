using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BIM.Tools.UI;

namespace BIM.Tools.Health
{
    internal sealed class AuditCompareListsForm : Form
    {
        private readonly ComboBox _a,_b,_filter;
        private readonly Label _info,_summary;
        private readonly TextBox _search,_details;
        private readonly CheckBox _differences;
        private readonly DataGridView _grid;
        private readonly Button _export;
        private AuditBaseline _comparedA,_comparedB;
        private List<AuditListRow> _rows=new List<AuditListRow>();
        internal AuditCompareListsForm(AuditStore store,string currentPath)
        {
            Text="Federation Audit · Compare Lists";Font=new Font("Segoe UI",9F);AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            ClientSize=new Size(1060,720);MinimumSize=new Size(760,540);StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;BackColor=BimTheme.Paper;
            TableLayoutPanel root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=6};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,70));root.RowStyles.Add(new RowStyle(SizeType.Percent,30));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label heading=new Label{Text="COMPARE SAVED MODEL LISTS",AutoSize=true,ForeColor=BimTheme.Brand,Font=new Font("Segoe UI",12F),Margin=new Padding(0,0,0,12)};root.Controls.Add(heading,0,0);
            TableLayoutPanel selectors=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,RowCount=3};selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,48));selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            _a=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};_b=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
            foreach(ComboBox combo in new[]{_a,_b}){combo.FormattingEnabled=true;combo.Format+=delegate(object sender,ListControlConvertEventArgs e){AuditBaseline list=e.ListItem as AuditBaseline;if(list!=null)e.Value=list+" · "+list.Snapshot.CapturedUtc;};}
            selectors.Controls.Add(new Label{Text="List A",AutoSize=true},0,0);selectors.Controls.Add(_a,1,0);selectors.Controls.Add(new Label{Text="List B",AutoSize=true},0,1);selectors.Controls.Add(_b,1,1);
            _info=new Label{AutoSize=true,Dock=DockStyle.Fill,ForeColor=BimTheme.Muted,Margin=new Padding(0,6,0,8)};selectors.Controls.Add(_info,0,2);selectors.SetColumnSpan(_info,2);root.Controls.Add(selectors,0,1);
            FlowLayoutPanel filters=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true};
            _filter=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=135};_filter.Items.AddRange(new object[]{"All entries","Both","Only A","Only B","Unverified"});_filter.SelectedIndex=0;
            _differences=new CheckBox{Text="Differences only",AutoSize=true};_search=new TextBox{Width=220};
            filters.Controls.Add(_filter);filters.Controls.Add(_differences);filters.Controls.Add(new Label{Text="Search",AutoSize=true,Padding=new Padding(0,4,0,0)});filters.Controls.Add(_search);root.Controls.Add(filters,0,2);
            _grid=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White};
            foreach(string name in new[]{"Presence","State","File A","Parent A","File B","Parent B"})_grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=name,SortMode=DataGridViewColumnSortMode.NotSortable,MinimumWidth=65});
            root.Controls.Add(_grid,0,3);
            _details=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.White};root.Controls.Add(_details,0,4);
            FlowLayoutPanel footer=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true};_summary=new Label{AutoSize=true,ForeColor=BimTheme.Brand,Padding=new Padding(0,8,12,0)};
            _export=new Button{Text="Export all to CSV",AutoSize=true,MinimumSize=new Size(145,Font.Height+18)};_export.Click+=delegate{Export();};Button close=new Button{Text="Close",AutoSize=true,MinimumSize=new Size(90,Font.Height+18),DialogResult=DialogResult.Cancel};
            footer.Controls.Add(_summary);footer.Controls.Add(_export);footer.Controls.Add(close);root.Controls.Add(footer,0,5);Controls.Add(root);CancelButton=close;
            List<string> warnings=new List<string>();List<AuditBaseline> lists=store.Load(warnings);
            foreach(AuditBaseline list in lists){_a.Items.Add(list);_b.Items.Add(list);}
            _a.SelectedIndexChanged+=delegate{CompareSelected();};_b.SelectedIndexChanged+=delegate{CompareSelected();};_filter.SelectedIndexChanged+=delegate{Render();};_search.TextChanged+=delegate{Render();};_differences.CheckedChanged+=delegate{Render();};_grid.SelectionChanged+=delegate{ShowDetails();};
            int preferred=lists.FindIndex(b=>AuditNames.SameDocument(b.Snapshot.DocumentPath,currentPath));if(lists.Count>0)_a.SelectedIndex=preferred>=0?preferred:0;if(lists.Count>1)_b.SelectedIndex=_a.SelectedIndex==0?1:0;
            if(lists.Count<2)_info.Text="Save complete lists for two models, or two versions of one model, to compare them.";
            if(warnings.Count>0)_info.Text+="\r\n"+warnings.Count+" saved lists could not be read and were retained.";
            Shown+=delegate{BimWindowLayout.FitToWorkingArea(this);};
            Layout+=delegate{_grid.ColumnHeadersHeight=Font.Height+14;_grid.RowTemplate.Height=Font.Height+8;};
            CompareSelected();
        }
        internal void CompareSelected()
        {
            _comparedA=_a.SelectedItem as AuditBaseline;_comparedB=_b.SelectedItem as AuditBaseline;_rows.Clear();_export.Enabled=false;
            if(_comparedA==null||_comparedB==null){Render();return;}
            if(_comparedA.Id==_comparedB.Id){_info.Text="Choose two different saved lists. Different revisions of the same model can be compared after saving them as separate lists.";Render();return;}
            try
            {
                _rows=AuditListComparison.Compare(_comparedA,_comparedB);_export.Enabled=true;
                _info.Text="A captured: "+_comparedA.Snapshot.CapturedUtc+"\r\nB captured: "+_comparedB.Snapshot.CapturedUtc+"\r\nSaved file lists only. Possible matches require review; neither list is changed.";
            }
            catch(Exception error){_info.Text="Comparison could not finish: "+error.Message;}
            Render();
        }
        private void Render()
        {
            _grid.Rows.Clear();string query=_search.Text.Trim(),presence=_filter.SelectedIndex>0?_filter.SelectedItem.ToString():null;
            foreach(AuditListRow row in _rows.Where(r=>(presence==null||r.Presence==presence)&&(!_differences.Checked||r.Evidence.Status!=AuditStatus.Loaded)))
            {
                AuditRow e=row.Evidence;string nameA=e.Expected==null?"":e.Expected.Name,nameB=e.Current==null?"":e.Current.Name;
                if(query.Length>0&&(nameA+" "+nameB+" "+e.Parent).IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)continue;
                int index=_grid.Rows.Add(row.Presence,row.State,nameA,e.Expected==null?"":AuditNames.ParentName(_comparedA.Snapshot,e.Expected),nameB,e.Current==null?"":AuditNames.ParentName(_comparedB.Snapshot,e.Current));_grid.Rows[index].Tag=row;
            }
            _summary.Text=_rows.Count==0?"No comparison results":AuditListComparison.Summary(_rows);ShowDetails();
        }
        private void ShowDetails(){AuditListRow row=_grid.CurrentRow==null?null:_grid.CurrentRow.Tag as AuditListRow;_details.Text=row==null?"Select an entry to inspect sources and possible matches.":row.Details(_comparedA,_comparedB);}
        private void Export()
        {
            if(!_export.Enabled)return;
            using(SaveFileDialog dialog=new SaveFileDialog{Filter="CSV comparison (*.csv)|*.csv",FileName="Federation-Compare-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".csv"})
            if(dialog.ShowDialog(this)==DialogResult.OK)try{AuditListComparison.Export(dialog.FileName,_comparedA,_comparedB,_rows);}catch(Exception error){BimDialog.ShowError(this,"Export comparison",error.Message,error.ToString());}
        }
    }
}
