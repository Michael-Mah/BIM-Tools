using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BIM.Tools.Clash
{
    public enum ClashKind { Hard, HardConservative, Clearance, Duplicate }

    // These are durable file identities, never serialized ModelItem pointers.
    public sealed class ClashModelIdentity
    {
        public string Id { get; set; }
        public string FileName { get; set; }
        public string Container { get; set; }
        public string SourcePath { get; set; }
        public string ModelGuid { get; set; }
        public string RootGuid { get; set; }
        public ClashModelIdentity() { Id = Guid.NewGuid().ToString("D"); }
        public ClashModelIdentity Copy() { return (ClashModelIdentity)MemberwiseClone(); }
        public override string ToString() { return FileName; }
    }

    public sealed class ClashReplacement
    {
        public string ModelId { get; set; }
        public string PreviousName { get; set; }
        public string ReplacementName { get; set; }
        public string ConfirmedUtc { get; set; }
    }

    public sealed class ClashBox
    {
        public double MinX { get; set; } public double MinY { get; set; } public double MinZ { get; set; }
        public double MaxX { get; set; } public double MaxY { get; set; } public double MaxZ { get; set; }
        public double A { get; set; } public double B { get; set; } public double C { get; set; } public double D { get; set; }
        public ClashBox Copy() { return (ClashBox)MemberwiseClone(); }
    }

    [XmlRoot("ClashSetup")]
    public sealed class ClashProfile
    {
        public int SchemaVersion { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string ProjectPath { get; set; }
        public string NativeTestId { get; set; }
        public string NativeTestName { get; set; }
        public ClashKind Kind { get; set; }
        public double ToleranceMillimetres { get; set; }
        public bool UseSectionBox { get; set; }
        public string SectionBoxJson { get; set; }
        public ClashBox Box { get; set; }
        public string UpdatedUtc { get; set; }
        public List<ClashModelIdentity> SelectionA { get; set; }
        public List<ClashModelIdentity> SelectionB { get; set; }
        public List<ClashReplacement> Replacements { get; set; }
        public ClashProfile()
        {
            SchemaVersion = 1; Id = Guid.NewGuid().ToString("D"); Name = "New clash setup";
            SelectionA = new List<ClashModelIdentity>(); SelectionB = new List<ClashModelIdentity>();
            Replacements = new List<ClashReplacement>();
        }
        public ClashProfile Copy()
        {
            ClashProfile copy = (ClashProfile)MemberwiseClone();
            copy.Box = Box == null ? null : Box.Copy();
            copy.SelectionA = SelectionA.ConvertAll(m => m.Copy());
            copy.SelectionB = SelectionB.ConvertAll(m => m.Copy());
            copy.Replacements = Replacements.ConvertAll(r => new ClashReplacement {
                ModelId = r.ModelId, PreviousName = r.PreviousName,
                ReplacementName = r.ReplacementName, ConfirmedUtc = r.ConfirmedUtc });
            return copy;
        }
        public override string ToString() { return Name; }
    }
}
