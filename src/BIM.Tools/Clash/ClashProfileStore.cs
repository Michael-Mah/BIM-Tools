using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace BIM.Tools.Clash
{
    internal sealed class ClashProfileStore
    {
        private readonly string _folder;
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(ClashProfile));
        internal ClashProfileStore() : this(Path.Combine(EditionBrand.SettingsRoot, "ClashProfiles")) { }
        internal ClashProfileStore(string folder) { _folder = Path.GetFullPath(folder); }
        internal List<ClashProfile> LoadAll(IList<string> warnings)
        {
            List<ClashProfile> result = new List<ClashProfile>();
            if (!Directory.Exists(_folder)) return result;
            foreach (string file in Directory.GetFiles(_folder, "*.xml"))
                try { result.Add(Read(file)); }
                catch (Exception error) { warnings.Add(Path.GetFileName(file) + ": " + error.Message); }
            return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        internal void Save(ClashProfile profile)
        {
            Validate(profile); profile.UpdatedUtc = DateTime.UtcNow.ToString("o");
            Directory.CreateDirectory(_folder); WriteAtomic(Path.Combine(_folder, Guid.Parse(profile.Id).ToString("D") + ".xml"), profile);
        }
        internal void Export(string path, ClashProfile profile) { Validate(profile); WriteAtomic(path, profile); }
        internal ClashProfile Import(string path)
        {
            ClashProfile profile = Read(path); profile.Id = Guid.NewGuid().ToString("D");
            // Imported setups must never claim ownership of an unrelated native test.
            profile.NativeTestId = null; profile.NativeTestName = null; return profile;
        }
        internal static ClashProfile Read(string path)
        {
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Clash setup exceeds the 4 MB limit.");
            using (XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                ClashProfile profile = (ClashProfile)Serializer.Deserialize(reader); Validate(profile); return profile;
            }
        }
        private static void WriteAtomic(string path, ClashProfile profile)
        {
            string target = Path.GetFullPath(path), temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { Serializer.Serialize(stream, profile); stream.Flush(true); }
                if (File.Exists(target))
                {
                    // File.Replace fails if the requested backup path already exists.
                    // Keep the first convenient .bak name, then give later snapshots
                    // unique names so repeated saves/restores remain atomic.
                    string backup = target + ".bak";
                    if (File.Exists(backup)) backup += "." + Guid.NewGuid().ToString("N");
                    File.Replace(temporary, target, backup);
                }
                else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal static void Validate(ClashProfile profile)
        {
            Guid id;
            if (profile == null || profile.SchemaVersion != 1 || !Guid.TryParse(profile.Id, out id)) throw new InvalidDataException("Unsupported or invalid clash setup.");
            if (string.IsNullOrWhiteSpace(profile.Name)) throw new InvalidDataException("Enter a clash setup name.");
            if (double.IsNaN(profile.ToleranceMillimetres) || double.IsInfinity(profile.ToleranceMillimetres) || profile.ToleranceMillimetres < 0 || profile.ToleranceMillimetres > 1000000)
                throw new InvalidDataException("Tolerance must be between 0 and 1,000,000 mm.");
            if (!Enum.IsDefined(typeof(ClashKind), profile.Kind)) throw new InvalidDataException("Unsupported clash type.");
            if (profile.UseSectionBox)
            {
                ClashBox box = profile.Box;
                if (box == null) throw new InvalidDataException("Capture a Section Box for this setup first.");
                double[] values = { box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ, box.A, box.B, box.C, box.D };
                if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v)) || box.MinX >= box.MaxX || box.MinY >= box.MaxY || box.MinZ >= box.MaxZ
                    || box.A * box.A + box.B * box.B + box.C * box.C + box.D * box.D < 1e-20)
                    throw new InvalidDataException("The saved Section Box is invalid.");
            }
            if (profile.SelectionA == null || profile.SelectionB == null || profile.Replacements == null) throw new InvalidDataException("The saved selections are incomplete.");
            foreach (ClashModelIdentity model in profile.SelectionA.Concat(profile.SelectionB))
                if (model == null || !Guid.TryParse(model.Id, out id) || string.IsNullOrWhiteSpace(model.FileName)) throw new InvalidDataException("A saved model identity is invalid.");
            foreach (var group in profile.SelectionA.Concat(profile.SelectionB).GroupBy(m => m.Id))
                if (group.Any(m => !ClashModelMatcher.Same(m.FileName, group.First().FileName) || !ClashModelMatcher.Same(m.Container, group.First().Container)
                    || !ClashModelMatcher.Same(m.SourcePath, group.First().SourcePath))) throw new InvalidDataException("A saved identity has conflicting definitions.");
            if (profile.SelectionA.GroupBy(m => m.Id).Any(g => g.Count() > 1) || profile.SelectionB.GroupBy(m => m.Id).Any(g => g.Count() > 1))
                throw new InvalidDataException("A selection contains duplicate model identities.");
        }
    }
}
