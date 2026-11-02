using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using NavisApplication=Autodesk.Navisworks.Api.Application;

[Plugin("BIM.AuditInstalledSmoke","BIMT")]
public sealed class AuditInstalledSmoke : AddInPlugin
{
    public override int Execute(params string[] args)
    {
        StringBuilder log=new StringBuilder();
        try
        {
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BIM-Tools","FederationBaselines");
            string[] before=Directory.Exists(folder)?Directory.GetFiles(folder):new string[0];
            CommandHandlerPlugin commands=NavisApplication.Plugins.FindPlugin("BIM.Tools.Commands.BIMT").LoadPlugin() as CommandHandlerPlugin;
            if(commands==null||commands.GetType().Assembly.GetName().Version.ToString()!="1.2.1.0")throw new Exception("Installed command assembly is not 1.2.1.");
            log.AppendLine("PASS Installed BIM Tools command assembly is 1.2.1");
            EventWatcherPluginRecord watcher=NavisApplication.Plugins.FindPlugin("BIM.FederationAudit.Watcher.BIMT") as EventWatcherPluginRecord;
            if(watcher==null||watcher.LoadedPlugin==null)throw new Exception("Installed automatic watcher did not load at startup.");
            log.AppendLine("PASS Installed Federation Audit watcher loads at startup");
            int modelCount=NavisApplication.ActiveDocument.Models.Count;
            if(commands.ExecuteCommand("BIM_OPEN_FEDERATION_AUDIT")!=0)throw new Exception("Federation Audit command failed.");
            Application.DoEvents();
            Form form=Application.OpenForms.Cast<Form>().Single(f=>f.Text=="BIM Tools · Federation Audit");
            if(form.Controls.Count==0)throw new Exception("Federation Audit window is empty.");
            if(!Descendants(form).OfType<Button>().Any(b=>b.Text=="Watched views…"))throw new Exception("Installed audit window has no watched views action.");
            log.AppendLine("PASS Installed audit window provides watched view management");
            form.Close();
            if(NavisApplication.ActiveDocument.Models.Count!=modelCount)throw new Exception("Opening audit changed the models.");
            string[] after=Directory.Exists(folder)?Directory.GetFiles(folder):new string[0];
            if(!before.OrderBy(x=>x).SequenceEqual(after.OrderBy(x=>x)))throw new Exception("Opening audit unexpectedly created or replaced a baseline.");
            log.AppendLine("PASS Installed Health command opens the real window without changing models or saving a baseline");
            File.WriteAllText(args[0],log.ToString());return 0;
        }
        catch(Exception error){log.AppendLine(error.ToString());File.WriteAllText(args[0],log.ToString());return 1;}
    }
    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {foreach(Control child in root.Controls){yield return child;foreach(Control nested in Descendants(child))yield return nested;}}
}
