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
        return 0;
    }
    private static System.Collections.Generic.IEnumerable<Control> All(Control root){foreach(Control child in root.Controls){yield return child;foreach(Control nested in All(child))yield return nested;}}
}
