namespace BIM.Tools.Models
{
    public sealed class SharingProgress
    {
        public string Stage { get; private set; }
        public string Detail { get; private set; }
        public int Completed { get; private set; }
        public int Total { get; private set; }
        public SharingProgress(string stage, string detail, int completed, int total)
        { Stage = stage; Detail = detail; Completed = completed; Total = total; }
    }
}
