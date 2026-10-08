namespace BIM.Tools.Models
{
    public sealed class SharingExportProgress
    {
        public string Stage { get; private set; }
        public string Detail { get; private set; }
        public long Completed { get; private set; }
        public long Total { get; private set; }
        public SharingExportProgress(string stage,string detail,long completed,long total)
        { Stage=stage; Detail=detail; Completed=completed; Total=total; }
    }
}
