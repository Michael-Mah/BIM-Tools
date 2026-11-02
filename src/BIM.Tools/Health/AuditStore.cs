using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace BIM.Tools.Health
{
    internal sealed class AuditStore
    {
        private readonly string _folder;
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(AuditBaseline));
        internal AuditStore() : this(Path.Combine(EditionBrand.SettingsRoot, "FederationBaselines")) { }
        internal AuditStore(string folder) { _folder = Path.GetFullPath(folder); }
        internal List<AuditBaseline> Load(IList<string> warnings)
        {
            List<AuditBaseline> result = new List<AuditBaseline>();
            if (!Directory.Exists(_folder)) return result;
            foreach (string file in Directory.GetFiles(_folder, "*.xml"))
                try { result.Add(Read(file)); } catch (Exception e) { warnings.Add(Path.GetFileName(file) + ": " + e.Message); }
            return result.OrderByDescending(b => b.SavedUtc).ToList();
        }
        internal static AuditBaseline Read(string file)
        {
            if (new FileInfo(file).Length > 16 * 1024 * 1024) throw new InvalidDataException("This baseline exceeds the supported size.");
            using (XmlReader reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                AuditBaseline value = (AuditBaseline)Serializer.Deserialize(reader); Validate(value);
                value.Snapshot.DocumentPath=AuditNames.Clean(value.Snapshot.DocumentPath);
                foreach(AuditFile fileEntry in value.Snapshot.Files)fileEntry.Source=AuditNames.Clean(fileEntry.Source);
                return value;
            }
        }
        internal void Save(AuditBaseline baseline)
        {
            Validate(baseline);
            // Work on a copy: a failed disk write cannot modify the caller's current baseline.
            AuditBaseline copy = baseline.Copy(); copy.SavedUtc = DateTime.UtcNow.ToString("o");
            copy.Snapshot.DocumentPath = AuditNames.Clean(copy.Snapshot.DocumentPath);
            foreach (AuditFile entry in copy.Snapshot.Files) entry.Source = AuditNames.Clean(entry.Source);
            Directory.CreateDirectory(_folder);
            string target = Path.Combine(_folder, Guid.Parse(copy.Id).ToString("D") + ".xml");
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { Serializer.Serialize(stream, copy); stream.Flush(true); }
                if (File.Exists(target)) File.Replace(temporary, target, target + "." + Guid.NewGuid().ToString("N") + ".bak");
                else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            baseline.SavedUtc = copy.SavedUtc;
        }
        internal static void Validate(AuditBaseline baseline)
        {
            Guid id;
            if (baseline == null || baseline.SchemaVersion != 1 || !Guid.TryParse(baseline.Id, out id) || string.IsNullOrWhiteSpace(baseline.Name) || baseline.Name.Length > 200 || baseline.Revision < 1)
                throw new InvalidDataException("The federation baseline is invalid or unsupported.");
            ValidateSnapshot(baseline.Snapshot);
        }
        internal static void ValidateSnapshot(AuditSnapshot snapshot, bool allowEmpty = false)
        {
            if (snapshot == null || snapshot.Files == null || (!allowEmpty && snapshot.Files.Count == 0) || snapshot.Files.Count > 50000)
                throw new InvalidDataException("The model list is empty or exceeds the supported size.");
            Dictionary<string, AuditFile> seen = new Dictionary<string, AuditFile>(); Dictionary<string,int> depth=new Dictionary<string,int>(); Guid id;
            // Parent-before-child order is the persisted schema; prevents cycles and dangling parents.
            foreach (AuditFile file in snapshot.Files)
            {
                if (file == null || !Guid.TryParse(file.Id, out id) || seen.ContainsKey(file.Id) || string.IsNullOrWhiteSpace(file.Name)) throw new InvalidDataException("The model list contains an invalid or duplicate identity.");
                if (!string.IsNullOrEmpty(file.ParentId) && !seen.ContainsKey(file.ParentId)) throw new InvalidDataException("A model has an invalid parent assembly.");
                int level=string.IsNullOrEmpty(file.ParentId)?0:depth[file.ParentId]+1;
                if(level>32)throw new InvalidDataException("The saved file hierarchy is too deep.");
                depth.Add(file.Id,level);
                seen.Add(file.Id, file);
            }
        }
    }
}
