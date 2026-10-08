using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Globalization;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Automation;
using Autodesk.Navisworks.Api.Plugins;

internal static class NativeSharingConverter
{
    [STAThread] private static int Main(string[] args)
    {
        if(args.Length!=5) return 2;
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e)
        { string dll=Path.Combine(args[0],new AssemblyName(e.Name).Name+".dll"); return File.Exists(dll) ? Assembly.LoadFrom(dll) : null; };
        Environment.SetEnvironmentVariable("PATH",args[0]+";"+Environment.GetEnvironmentVariable("PATH"));
        try { return Convert(args); }
        catch(OperationCanceledException) { File.WriteAllText(args[3],"Conversion cancelled"); return 3; }
        catch(Exception error) { File.WriteAllText(args[3],error.ToString()); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Convert(string[] args)
    {
        Action check=delegate { if(File.Exists(args[4])) throw new OperationCanceledException(); };
        check(); File.WriteAllText(args[3],"Starting a separate Navisworks converter");
        Stopwatch timing=Stopwatch.StartNew(); double startup=0,conversion=0;
        // Always own a new automation instance; never attach to the user's project.
        using(NavisworksApplication app=new NavisworksApplication())
        {
            app.Visible=false; app.DisableProgress(); check();
            // Measure launch separately: otherwise a tiny first sample would
            // incorrectly scale the entire process startup by model size.
            string warmup=Path.Combine(Path.GetDirectoryName(args[3]),"warmup.obj");
            File.WriteAllText(warmup,"v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            app.OpenFile(warmup,new string[0]); check();
            startup=timing.Elapsed.TotalSeconds; timing.Restart();
            if(Path.GetExtension(args[2]).Equals(".nwc",StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(args[3],"Building NWC from permanently clipped geometry");
                app.CreateCache(args[1]); check();
                string cache=Path.ChangeExtension(args[1],"nwc");
                if(!File.Exists(cache)) throw new IOException("Navisworks did not create the NWC cache.");
                File.Copy(cache,args[2],false);
            }
            else
            {
                File.WriteAllText(args[3],"Loading permanently clipped geometry");
                app.OpenFile(args[1],new string[0]); check();
                app.AddPluginAssembly(Assembly.GetExecutingAssembly().Location);
                File.WriteAllText(args[3],"Saving NWD in Navisworks 2026 format");
                if(app.ExecuteAddInPlugin("BIM.NativeSharingSave.BIMT",new[]{args[2]})!=0) throw new IOException("Navisworks did not save the NWD.");
                check();
            }
            conversion=timing.Elapsed.TotalSeconds; timing.Restart();
        }
        startup+=timing.Elapsed.TotalSeconds;
        File.WriteAllText(args[3]+".metrics",startup.ToString("R",CultureInfo.InvariantCulture)+","+conversion.ToString("R",CultureInfo.InvariantCulture)+","+new FileInfo(args[1]).Length.ToString(CultureInfo.InvariantCulture));
        File.WriteAllText(args[3],"Native conversion complete"); return 0;
    }
}

[Plugin("BIM.NativeSharingSave","BIMT")]
public sealed class NativeSharingSave : AddInPlugin
{
    public override int Execute(params string[] args)
    {
        // This document contains only the generated, already clipped IFC.
        Document doc=Autodesk.Navisworks.Api.Application.ActiveDocument;
        return doc.TrySaveFile(args[0],DocumentFileVersion.Navisworks2026) ? 0 : -1;
    }
}
