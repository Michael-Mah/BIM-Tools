using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using BIM.Tools.Updates;
internal static class UpdateNoticeTests
{
    [STAThread]private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string name=new AssemblyName(e.Name).Name+".dll";foreach(string folder in new[]{args[0],args[1]}){string path=Path.Combine(folder,name);if(File.Exists(path))return Assembly.LoadFrom(path);}return null;};
        try{return Run(args[2]);}catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    [MethodImpl(MethodImplOptions.NoInlining)]private static int Run(string output)
    {
        Directory.CreateDirectory(output);Application.EnableVisualStyles();int downloads=0;
        using(UpdateNotice notice=new UpdateNotice("1.4.3","1.4.4",delegate{downloads++;}))
        {
            notice.Opacity=0;notice.Show();Application.DoEvents();
            if(notice.Modal)throw new Exception("Update notice blocks the host");
            var buttons=All(notice).OfType<Button>().ToArray();
            foreach(Button button in buttons){Point pos=notice.PointToClient(button.PointToScreen(Point.Empty));if(!notice.ClientRectangle.Contains(new Rectangle(pos,button.Size)))throw new Exception("Clipped update notice action");}
            using(Bitmap image=new Bitmap(notice.Width,notice.Height)){notice.DrawToBitmap(image,new Rectangle(Point.Empty,notice.Size));image.Save(Path.Combine(output,"update-notice.png"));}
            buttons.Single(b=>b.Text=="Later").PerformClick();if(downloads!=0)throw new Exception("Later downloaded an update");
        }
        using(UpdateNotice notice=new UpdateNotice("1.4.3","1.4.4",delegate{downloads++;}))
        {notice.Opacity=0;notice.Show();Application.DoEvents();All(notice).OfType<Button>().Single(b=>b.Text=="Download and install").PerformClick();if(downloads!=1)throw new Exception("Download action did not execute exactly once");}
        using(UpdateNotice notice=new UpdateNotice("1.4.3","1.4.4",delegate{downloads++;}))
        {notice.Opacity=0;notice.Show();Application.DoEvents();notice.Close();if(downloads!=1)throw new Exception("Closing triggered an unwanted download");}
        Console.WriteLine("PASS: nonmodal notice, visible actions, Later, Download and close behavior");return 0;
    }
    private static System.Collections.Generic.IEnumerable<Control> All(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(Control nested in All(child))yield return nested;}}
}
