using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api.Automation;

internal static class ClashWorkflowRunner
{
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length != 5) { Console.Error.WriteLine("Usage: runner <Navisworks folder> <fixture A> <fixture B> <probe dll> <output folder>"); return 2; }
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
            string path = Path.Combine(args[0], new AssemblyName(e.Name).Name + ".dll"); return File.Exists(path) ? Assembly.LoadFrom(path) : null; };
        Environment.SetEnvironmentVariable("PATH", args[0] + ";" + Environment.GetEnvironmentVariable("PATH"));
        try { return Run(args); } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Run(string[] args)
    {
        Directory.CreateDirectory(args[4]);
        using (NavisworksApplication app = new NavisworksApplication())
        {
            app.Visible = false; app.DisableProgress(); app.OpenFile(args[1], new string[0]); app.AddPluginAssembly(args[3]);
            int result = app.ExecuteAddInPlugin("BIM.ClashWorkflowProbe.BIMT", new[] { args[4], "initial", args[2] });
            Console.WriteLine("Initial=" + result); if (result != 0) return result;
            app.OpenFile(Path.Combine(args[4], "saved-fixture.nwf"), new string[0]);
            result = app.ExecuteAddInPlugin("BIM.ClashWorkflowProbe.BIMT", new[] { args[4], "reopen" });
            Console.WriteLine("Reopen=" + result); if (result != 0) return result;
            string renamed = Path.Combine(Path.GetDirectoryName(args[1]), "GEN-STR-Z01-001-R02" + Path.GetExtension(args[1])); File.Copy(args[1], renamed, true);
            app.OpenFile(renamed, new string[0]);
            result = app.ExecuteAddInPlugin("BIM.ClashWorkflowProbe.BIMT", new[] { args[4], "rename", args[2] });
            Console.WriteLine("Rename=" + result); return result;
        }
    }
}
