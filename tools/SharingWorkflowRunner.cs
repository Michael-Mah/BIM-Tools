using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api.Automation;

internal static class SharingWorkflowRunner
{
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length != 4 && args.Length != 5) { Console.Error.WriteLine("Usage: runner <Navisworks folder> <disposable model> <isolated probe dll> <output folder> [preview]"); return 2; }
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender,ResolveEventArgs e)
        { string file = Path.Combine(args[0],new AssemblyName(e.Name).Name + ".dll"); return File.Exists(file) ? Assembly.LoadFrom(file) : null; };
        Environment.SetEnvironmentVariable("PATH",args[0] + ";" + Environment.GetEnvironmentVariable("PATH"));
        try { return Run(args); } catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Run(string[] args)
    {
        Directory.CreateDirectory(args[3]);
        using (NavisworksApplication app = new NavisworksApplication())
        {
            app.Visible = false; app.DisableProgress(); app.OpenFile(args[1],new string[0]); app.AddPluginAssembly(args[2]);
            Console.WriteLine("Disposable model opened · " + args[0]);
            int result = app.ExecuteAddInPlugin("BIM.SharingWorkflowProbe.BIMT",args.Length == 5 ? new[] { args[3],args[4] } : new[] { args[3] });
            Console.WriteLine("Workflow=" + result); if (result != 0) return result;
            app.OpenFile(Path.Combine(args[3],"auto-scope.ifc"),new string[0]);
            result = app.ExecuteAddInPlugin("BIM.ScopedSharingVerifyProbe.BIMT",new[] {args[3],"auto-scope.json"});
            Console.WriteLine("Automatic clipped IFC reopened=" + result); if (result != 0) return result;
            app.OpenFile(Path.Combine(args[3],"auto-scope.obj"),new string[0]);
            result = app.ExecuteAddInPlugin("BIM.ScopedSharingVerifyProbe.BIMT",new[] {args[3],"auto-scope.json"});
            File.Copy(Path.Combine(args[3],"root-reopen-report.txt"),Path.Combine(args[3],"obj-reopen-report.txt"),true);
            Console.WriteLine("Strict clipped OBJ reopened=" + result); if(result!=0) return result;
            foreach(string extension in new[]{"nwc","nwd"})
            {
                app.OpenFile(Path.Combine(args[3],"auto-scope-native."+extension),new string[0]);
                result=app.ExecuteAddInPlugin("BIM.ScopedSharingVerifyProbe.BIMT",new[]{args[3],"auto-scope.json"});
                File.Copy(Path.Combine(args[3],"root-reopen-report.txt"),Path.Combine(args[3],extension+"-reopen-report.txt"),true);
                Console.WriteLine("Strict clipped "+extension.ToUpperInvariant()+" reopened="+result); if(result!=0) return result;
            }
            app.OpenFile(args[1],new string[0]);
            result = app.ExecuteAddInPlugin("BIM.DpiUiProbe.BIMT",new[] {Path.Combine(args[3],"dpi")});
            Console.WriteLine("DPI layout=" + result); return result;
        }
    }
}
