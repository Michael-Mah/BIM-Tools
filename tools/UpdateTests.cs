using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
using BIM.Tools.Updates;

#if CURRENT_VERSION
[assembly: AssemblyVersion("1.4.1.0")]
#else
[assembly: AssemblyVersion("1.4.0.0")]
#endif

internal static class UpdateTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
        Console.WriteLine("PASS " + message);
    }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { Check(true, message); return; }
        throw new Exception(message);
    }
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request)
        {
            string path = Path.Combine(args[0], new AssemblyName(request.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { return Run(args); }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        Check(UpdateClient.ParseVersion("v1.4.0") == new Version(1, 4, 0, 0), "GitHub tags compare with assembly versions");
        Check(UpdateClient.ParseVersion("1.10.0") > UpdateClient.ParseVersion("1.9.9"), "Version ordering is numeric");
        Reject(() => UpdateClient.ParseVersion("v1.5.0-beta"), "Prerelease labels cannot masquerade as stable versions");
        UpdateManifest manifest = new JavaScriptSerializer().Deserialize<UpdateManifest>(
            File.ReadAllText(args[1]).TrimStart('\uFEFF'));
        Version version = UpdateClient.ParseVersion(manifest.version);
        ReleaseAsset asset = new ReleaseAsset { id = 1, name = manifest.installerName, size = manifest.size };
        UpdateClient.ValidateManifest(manifest, version, asset, 2024);
        Check(true, "Built installer manifest includes the 2024 host");
        Reject(() => UpdateClient.ValidateManifest(manifest, version, asset, 2026), "Unsupported host year cannot download this release");
        asset.size++;
        Reject(() => UpdateClient.ValidateManifest(manifest, version, asset, 2024), "Mismatched installer size is rejected");
        asset.size--;
        Reject(() => UpdateClient.ValidateManifest(manifest, new Version(9, 0, 0, 0), asset, 2024), "Release tag and manifest must agree");
        string hash = manifest.sha256;
        manifest.sha256 = new string('z', 64);
        Reject(() => UpdateClient.ValidateManifest(manifest, version, asset, 2024), "Malformed checksums are rejected");
        manifest.sha256 = hash;
        Reject(() => UpdateClient.ValidateManifest(manifest, version, null, 2024), "Missing installer cannot trigger an update");
        string token = Environment.GetEnvironmentVariable("BIM_UPDATE_TEST_TOKEN");
        if (args.Length > 2 && args[2] == "live")
        {
            AvailableUpdate update = UpdateClient.Check(token);
#if CURRENT_VERSION
            Check(update == null, "The current version does not offer the same release again");
#else
            Check(update != null && update.Version == version, "An older computer discovers the latest GitHub release");
            string installer = UpdateClient.Download(update, token);
            Check(File.Exists(installer) && new FileInfo(installer).Length == manifest.size,
                "Release download passes SHA256 and length validation");
            string original = update.Manifest.sha256;
            update.Manifest.sha256 = new string('0', 64);
            Reject(() => UpdateClient.Download(update, token), "Tampered download checksum prevents installation");
            update.Manifest.sha256 = original;
            Check(new FileInfo(installer).Length == manifest.size, "Failed download retains the previously verified installer");
#endif
        }
        Console.WriteLine("PASS " + _checks + " update checks");
        return 0;
    }
}
