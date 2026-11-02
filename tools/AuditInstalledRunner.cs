using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api.Automation;

internal static class AuditInstalledRunner
{
    [STAThread]private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string file=Path.Combine(args[0],new AssemblyName(e.Name).Name+".dll");return File.Exists(file)?Assembly.LoadFrom(file):null;};
        Environment.SetEnvironmentVariable("PATH",args[0]+";"+Environment.GetEnvironmentVariable("PATH"));
        try{return Run(args);}catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    [MethodImpl(MethodImplOptions.NoInlining)]private static int Run(string[] args)
    {
        using(NavisworksApplication application=new NavisworksApplication())
        {application.Visible=false;application.DisableProgress();application.OpenFile(args[1],new string[0]);application.AddPluginAssembly(args[2]);int result=application.ExecuteAddInPlugin("BIM.AuditInstalledSmoke.BIMT",new[]{args[3]});Console.WriteLine("Installed smoke="+result);return result;}
    }
}
