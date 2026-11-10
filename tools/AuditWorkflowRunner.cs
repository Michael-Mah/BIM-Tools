using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api.Automation;

internal static class AuditWorkflowRunner
{
    [STAThread] private static int Main(string[] args)
    {
        if(args.Length<5||args.Length>6)return 2;
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string file=Path.Combine(args[0],new AssemblyName(e.Name).Name+".dll");return File.Exists(file)?Assembly.LoadFrom(file):null;};
        Environment.SetEnvironmentVariable("PATH",args[0]+";"+Environment.GetEnvironmentVariable("PATH"));
        try{return Run(args);}catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    [MethodImpl(MethodImplOptions.NoInlining)]private static int Run(string[] args)
    {
        Directory.CreateDirectory(args[4]);
        using(NavisworksApplication app=new NavisworksApplication())
        {
            app.Visible=false;app.DisableProgress();app.OpenFile(args[1],new string[0]);app.AddPluginAssembly(args[3]);
            if(args.Length==6&&args[5]=="interaction")return Phase(app,args,"interaction")?0:1;
            if(!Phase(app,args,"initial"))return 1;
            if(args.Length==6&&args[5]=="preview")return 0;
            app.OpenFile(Path.Combine(args[4],"live.nwf"),new string[0]);if(!Phase(app,args,"reopen"))return 1;
            app.OpenFile(Path.Combine(args[4],"packed.nwd"),new string[0]);if(!Phase(app,args,"packed"))return 1;
            string renamed=Path.Combine(args[4],"GEN-STR-Z01-001-R02.ifc");File.Copy(args[1],renamed);
            app.OpenFile(renamed,new string[0]);if(!Phase(app,args,"rename"))return 1;
            string moved=Path.Combine(args[4],"moved");Directory.CreateDirectory(moved);moved=Path.Combine(moved,Path.GetFileName(args[1]));File.Copy(args[1],moved);
            app.OpenFile(moved,new string[0]);if(!Phase(app,args,"moved"))return 1;
            app.OpenFile(args[1],new string[0]);if(!Phase(app,args,"missing"))return 1;
            return 0;
        }
    }
    private static bool Phase(NavisworksApplication app,string[] args,string phase)
    {int code=app.ExecuteAddInPlugin("BIM.AuditWorkflowProbe.BIMT",new[]{args[4],phase,args[2]});Console.WriteLine(phase+"="+code);return code==0;}
}
