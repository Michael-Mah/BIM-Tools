using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using BIM.Tools.Health;

internal static class AuditLayoutTests
{
    [STAThread]private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string name=new AssemblyName(e.Name).Name+".dll";string file=Path.Combine(args[0],name);if(!File.Exists(file))file=Path.Combine(args[1],name);return File.Exists(file)?Assembly.LoadFrom(file):null;};
        try{return Run(args[2]);}catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    [MethodImpl(MethodImplOptions.NoInlining)]private static int Run(string output)
    {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Directory.CreateDirectory(output);
        using(FederationAuditForm form=new FederationAuditForm(null,new AuditStore(Path.Combine(output,"unused"))))
        {
            form.Opacity=0;form.ShowInTaskbar=false;form.Show();Application.DoEvents();
            foreach(Size size in new[]{new Size(1100,760),new Size(850,580),new Size(760,540)})
            {
                form.ClientSize=size;for(int i=0;i<3;i++){form.PerformLayout();Application.DoEvents();}
                Console.WriteLine("VIEW "+size+" DPI="+form.DeviceDpi+" font="+form.Font.Height);
                foreach(FlowLayoutPanel panel in All(form).OfType<FlowLayoutPanel>())
                {
                    Console.WriteLine("FLOW "+panel.Bounds+" Client="+panel.ClientSize+" pref="+panel.PreferredSize);
                    foreach(Control control in panel.Controls)Console.WriteLine(control.Text+" "+control.Bounds+" pref="+control.PreferredSize+" margin="+control.Margin+" clip="+(control.Bottom>panel.ClientSize.Height));
                    if(panel.Controls.Cast<Control>().Any(c=>c.Bottom>panel.ClientSize.Height))throw new Exception("Clipped action control at "+size);
                }
                using(Bitmap image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(output,"layout-"+size.Width+".png"));}
            }
            form.Close();
        }
        foreach(float points in new[]{9F,11.25F,13.5F,18F})
        using(FederationAuditForm form=new FederationAuditForm(null,new AuditStore(Path.Combine(output,"unused"))))
        {
            form.Font=new Font("Segoe UI",points);form.Opacity=0;form.ShowInTaskbar=false;form.Show();
            form.ClientSize=new Size(1100,800);
            for(int i=0;i<5;i++){form.PerformLayout();Application.DoEvents();}
            Button mapping=All(form).OfType<Button>().Single(b=>b.Text=="Review mapping…");
            if(mapping.Height< TextRenderer.MeasureText(mapping.Text,mapping.Font).Height+mapping.Padding.Vertical+4)throw new Exception("Mapping caption clipped at font "+points);
            foreach(FlowLayoutPanel panel in All(form).OfType<FlowLayoutPanel>())
                if(panel.Controls.Cast<Control>().Any(c=>c.Bottom>panel.ClientSize.Height))throw new Exception("Toolbar clipped at font "+points);
            using(Bitmap bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(output,"font-"+points+".png"));}
            Console.WriteLine("PASS font scaling "+points+"pt; mapping height="+mapping.Height);
            form.Close();
        }
        AuditStore store=new AuditStore(Path.Combine(output,"registered"));
        AuditBaseline first=new AuditBaseline{Name="Arbitrary registered view A",Snapshot=new AuditSnapshot{DocumentPath="C:/Views/A.nwf"}},second=new AuditBaseline{Name="Another registered view B",Snapshot=new AuditSnapshot{DocumentPath="C:/Views/B.nwf"}};
        first.Snapshot.Files.Add(new AuditFile{Name="STRUCTURE.rvt"});second.Snapshot.Files.Add(new AuditFile{Name="SERVICES.rvt"});
        store.SetAutomatic(first,true);store.Save(second);
        using(AuditWatchedViewsForm manager=new AuditWatchedViewsForm(store))
        {
            manager.Opacity=0;manager.Show();Application.DoEvents();
            foreach(Size size in new[]{new Size(800,450),new Size(640,360)})
            {
                manager.ClientSize=size;manager.PerformLayout();Application.DoEvents();
                DataGridView grid=All(manager).OfType<DataGridView>().Single();
                if(grid.Rows.Count!=2||grid.Height<100)throw new Exception("Registered view table is not usable");
                foreach(Button button in All(manager).OfType<Button>())
                {Point p=manager.PointToClient(button.PointToScreen(Point.Empty));if(p.X<0||p.Y<0||p.X+button.Width>size.Width||p.Y+button.Height>size.Height)throw new Exception("Watched view action is outside viewport");}
                using(Bitmap image=new Bitmap(manager.Width,manager.Height)){manager.DrawToBitmap(image,new Rectangle(Point.Empty,manager.Size));image.Save(Path.Combine(output,"watched-views-"+size.Width+".png"));}
            }
            DataGridView table=All(manager).OfType<DataGridView>().Single();
            foreach(DataGridViewRow row in table.Rows)row.Cells[0].Value=((AuditBaseline)row.Tag).Id==second.Id;
            manager.ApplyChanges();
            if(store.EnabledFor(first.Snapshot.DocumentPath)!=null||store.EnabledFor(second.Snapshot.DocumentPath).Id!=second.Id||store.Load(new System.Collections.Generic.List<string>()).Any(b=>b.Snapshot.Files.Count!=1))throw new Exception("Watched view checkbox changes were not saved correctly");
            Console.WriteLine("PASS Watched views UI toggles arbitrary views independently and retains their model lists");
            manager.Close();
        }
        return 0;
    }
    private static System.Collections.Generic.IEnumerable<Control> All(Control root){foreach(Control child in root.Controls){yield return child;foreach(Control nested in All(child))yield return nested;}}
}
