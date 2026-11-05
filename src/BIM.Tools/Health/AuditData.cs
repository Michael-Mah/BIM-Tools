using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace BIM.Tools.Health
{
    public sealed class AuditFile
    {
        public string Id { get; set; }
        public string ParentId { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }
        public string NativeId { get; set; }
        public bool Readable { get; set; }
        public bool LimitedChildren { get; set; }
        public AuditFile() { Id = Guid.NewGuid().ToString("D"); Readable = true; }
        public AuditFile Copy() { return (AuditFile)MemberwiseClone(); }
        [XmlIgnore] public bool Assembly { get { return string.IsNullOrEmpty(ParentId); } }
        public override string ToString() { return Name; }
    }
    public sealed class AuditSnapshot
    {
        public string DocumentPath { get; set; }
        public string CapturedUtc { get; set; }
        public List<AuditFile> Files { get; set; }
        public AuditSnapshot() { Files = new List<AuditFile>(); CapturedUtc = DateTime.UtcNow.ToString("o"); }
        public AuditSnapshot Copy() { return new AuditSnapshot { DocumentPath = DocumentPath, CapturedUtc = CapturedUtc, Files = Files.ConvertAll(f => f.Copy()) }; }
    }
    [XmlRoot("FederationBaseline")]
    public sealed class AuditBaseline
    {
        public int SchemaVersion { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public int Revision { get; set; }
        public bool AutoCheck { get; set; }
        public string SavedUtc { get; set; }
        public AuditSnapshot Snapshot { get; set; }
        [XmlIgnore] public string DisplayModelName { get; set; }
        public AuditBaseline() { SchemaVersion = 1; Id = Guid.NewGuid().ToString("D"); Revision = 1; }
        public AuditBaseline Copy() { return new AuditBaseline { SchemaVersion = SchemaVersion, Id = Id, Name = Name, Revision = Revision, AutoCheck = AutoCheck, SavedUtc = SavedUtc, Snapshot = Snapshot.Copy() }; }
        public override string ToString() { return (string.IsNullOrEmpty(DisplayModelName)?AuditNames.ModelName(Snapshot==null?null:Snapshot.DocumentPath,Name):DisplayModelName) + " · v" + Revision; }
    }
    internal static class AuditNames
    {
        internal static string ModelName(string path,string fallback)
        {
            string clean=Clean(path);int slash=clean.LastIndexOf('/');string name=clean.Substring(slash+1);
            try{name=Uri.UnescapeDataString(name);}catch{}
            return string.IsNullOrWhiteSpace(name)?fallback:name;
        }
        private static readonly Regex FilePattern = new Regex(@"[^/\\]+?\.(?:nwf|nwd|nwc|rvt|ifc|dwg|dgn|dxf|rfa|3ds|fbx|skp|obj|stp|step|igs|iges)(?=$|[\s?#])", RegexOptions.IgnoreCase);
        internal static string FileName(params string[] texts)
        {
            foreach (string text in texts)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                string decoded; try { decoded = Uri.UnescapeDataString(text); } catch { decoded = text; }
                Match match = FilePattern.Match(decoded); if (match.Success) return match.Value.Trim();
            }
            return null;
        }
        internal static string Clean(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            text=text.Trim();
            if(Regex.IsMatch(text,@"^[a-z]+%3a",RegexOptions.IgnoreCase))try{text=Uri.UnescapeDataString(text);}catch{}
            int encodedQuery=text.IndexOf("%3f",StringComparison.OrdinalIgnoreCase);
            if(encodedQuery>=0)text=text.Substring(0,encodedQuery);
            Uri uri;
            if(Uri.TryCreate(text,UriKind.Absolute,out uri)&&!uri.IsFile&&text.Contains("://"))
            {
                UriBuilder builder=new UriBuilder(uri){UserName="",Password="",Query="",Fragment=""};
                return builder.Uri.AbsoluteUri;
            }
            bool opaque=Regex.IsMatch(text,@"^[a-z][a-z0-9+.-]*:",RegexOptions.IgnoreCase)&&!Regex.IsMatch(text,@"^[a-z]:[\\/]",RegexOptions.IgnoreCase);
            int cut=opaque?text.IndexOfAny(new[]{'?','#'}):text.IndexOf('?'); if(cut>=0)text=text.Substring(0,cut);
            // Preserve escaped URI path characters and literal # in local file/folder names.
            if(!opaque)try{text=Uri.UnescapeDataString(text);}catch{}
            cut=text.IndexOf('?');if(cut>=0)text=text.Substring(0,cut);
            return text.Trim().Replace('\\', '/');
        }
        internal static bool Same(string a, string b) { return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase); }
        internal static string DocumentKey(string path)
        {
            string clean=Clean(path); Uri uri;
            if(Uri.TryCreate(clean,UriKind.Absolute,out uri)&&Same(uri.Scheme,"nwacc"))
            {
                string[] parts=uri.AbsolutePath.Split(new[]{'/'},StringSplitOptions.RemoveEmptyEntries);
                Guid account,project,view;
                if(Guid.TryParse(Segment(parts,"account"),out account)&&account!=Guid.Empty
                    &&Guid.TryParse(Segment(parts,"project"),out project)&&project!=Guid.Empty
                    &&Guid.TryParse(Segment(parts,"aggregate"),out view)&&view!=Guid.Empty)
                    return "acc-view:"+account.ToString("D")+"/"+project.ToString("D")+"/"+view.ToString("D");
            }
            return clean;
        }
        private static string Segment(string[] parts,string name)
        { for(int i=0;i<parts.Length-1;i++)if(Same(parts[i],name))return Uri.UnescapeDataString(parts[i+1]);return ""; }
        internal static bool SameDocument(string first,string second)
        { string key=DocumentKey(first);return key.Length>0&&Same(key,DocumentKey(second)); }
        internal static string ParentName(AuditSnapshot snapshot, AuditFile file)
        {
            List<string> names = new List<string>(); string parent = file.ParentId;
            while (!string.IsNullOrEmpty(parent)) { AuditFile item = snapshot.Files.FirstOrDefault(f => f.Id == parent); if (item == null) break; names.Add(item.Name); parent = item.ParentId; }
            names.Reverse(); return names.Count == 0 ? "Top-level assembly" : string.Join(" / ", names);
        }
    }
}
