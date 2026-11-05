using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIM.Tools.Clash;

namespace BIM.Tools.Health
{
    internal enum AuditStatus { Missing, Review, SourceChanged, Unverifiable, New, Loaded }
    internal sealed class AuditCandidate
    {
        internal AuditFile File; internal double Score;
        public override string ToString() { return File.Name + " · " + (string.IsNullOrEmpty(File.Source) ? "Source unavailable" : File.Source); }
    }
    internal sealed class AuditRow
    {
        internal AuditFile Expected, Current;
        internal string Parent, Detail;
        internal AuditStatus Status;
        internal List<AuditCandidate> Candidates = new List<AuditCandidate>();
        internal AuditFile File { get { return Expected ?? Current; } }
        internal string Label { get { return Status == AuditStatus.SourceChanged ? "Changed source" : Status == AuditStatus.Unverifiable ? "Cannot verify" : Status == AuditStatus.Review ? "Review match" : Status.ToString(); } }
    }
    internal static class AuditComparison
    {
        internal static List<AuditRow> Compare(AuditSnapshot expected, AuditSnapshot current)
        {
            AuditStore.ValidateSnapshot(expected); AuditStore.ValidateSnapshot(current, true);
            List<AuditRow> rows = new List<AuditRow>(); HashSet<string> used = new HashSet<string>();
            CompareLevel(expected, current, null, null, true, false, rows, used);
            foreach (AuditFile file in current.Files.Where(f => !used.Contains(f.Id)))
                rows.Add(new AuditRow { Current = file, Parent = AuditNames.ParentName(current, file), Status = file.Readable ? AuditStatus.New : AuditStatus.Unverifiable, Detail = file.Readable ? "This file is loaded but is not in the saved complete list." : "A readable source-file name is unavailable." });
            return rows;
        }
        private static void CompareLevel(AuditSnapshot old, AuditSnapshot now, string oldParent, string newParent, bool parentVisible, bool parentUncertain, List<AuditRow> rows, HashSet<string> used)
        {
            List<AuditFile> saved = old.Files.Where(f => AuditNames.Same(f.ParentId, oldParent)).ToList();
            List<AuditFile> loaded = parentVisible ? now.Files.Where(f => AuditNames.Same(f.ParentId, newParent)).ToList() : new List<AuditFile>();
            List<AuditRow> level = new List<AuditRow>();
            AuditFile currentParent = string.IsNullOrEmpty(newParent) ? null : now.Files.First(f => f.Id == newParent);
            foreach (AuditFile file in saved)
            {
                AuditRow row = new AuditRow { Expected = file, Parent = AuditNames.ParentName(old, file) };
                List<AuditFile> same = loaded.Where(f => f.Readable && AuditNames.Same(f.Name, file.Name)).ToList();
                List<AuditFile> strong = same.Where(f => !string.IsNullOrEmpty(file.Source) && AuditNames.Same(AuditNames.Clean(file.Source), AuditNames.Clean(f.Source))).ToList();
                if (file.Readable && strong.Count == 1) row.Current = strong[0];
                else if (file.Readable && same.Count == 1 && saved.Count(f => AuditNames.Same(f.Name, file.Name)) == 1) row.Current = same[0];
                else if (file.Readable && same.Count > 1 && !string.IsNullOrEmpty(file.NativeId))
                { strong = same.Where(f => AuditNames.Same(f.NativeId, file.NativeId)).ToList(); if (strong.Count == 1) row.Current = strong[0]; }
                if (!file.Readable || (!parentVisible && parentUncertain) || (currentParent != null && currentParent.LimitedChildren && row.Current == null))
                { row.Status = AuditStatus.Unverifiable; row.Detail = "This file list is not exposed by the loaded container. Its internal completeness cannot be confirmed."; }
                else if (row.Current != null)
                {
                    bool changed = !string.IsNullOrEmpty(file.Source) && !string.IsNullOrEmpty(row.Current.Source) && !AuditNames.Same(AuditNames.Clean(file.Source), AuditNames.Clean(row.Current.Source));
                    row.Status = changed ? AuditStatus.SourceChanged : AuditStatus.Loaded;
                    row.Detail = changed ? "The file name matches, but its source location changed. Review before updating the baseline."
                        : "The expected file is loaded in the expected assembly.";
                    if (row.Current.LimitedChildren) row.Detail += " Embedded child-file coverage is incomplete.";
                    if (!string.IsNullOrEmpty(file.Source) && string.IsNullOrEmpty(row.Current.Source)) { row.Status = AuditStatus.Unverifiable; row.Detail = "The file name is loaded, but its previous source location cannot be verified."; }
                }
                else { row.Status = same.Count > 0 ? AuditStatus.Review : AuditStatus.Missing; row.Detail = parentVisible ? "The expected file is not uniquely identifiable in this assembly." : "The parent assembly is missing or unresolved."; }
                level.Add(row);
            }
            foreach (var collision in level.Where(r => r.Current != null).GroupBy(r => r.Current.Id).Where(g => g.Count() > 1))
                foreach (AuditRow row in collision) { row.Current = null; row.Status = AuditStatus.Review; row.Detail = "Several saved files point to one loaded file. Select the intended source."; }
            foreach (AuditRow row in level.Where(r => r.Current != null)) used.Add(row.Current.Id);
            foreach (AuditRow row in level)
            {
                if (row.Current == null && row.Expected.Readable && parentVisible && row.Status != AuditStatus.Unverifiable)
                {
                    row.Candidates = loaded.Where(f => f.Readable && !used.Contains(f.Id)).Select(f => new AuditCandidate { File = f, Score = AuditNames.Same(f.Name, row.Expected.Name) ? 1 : ClashModelMatcher.Similarity(f.Name, row.Expected.Name) })
                        .Where(c => c.Score >= 0.55).OrderByDescending(c => c.Score).Take(12).ToList();
                    if (row.Candidates.Count > 0) row.Status = AuditStatus.Review;
                }
                rows.Add(row);
                CompareLevel(old, now, row.Expected.Id, row.Current == null ? null : row.Current.Id, row.Current != null, row.Status != AuditStatus.Missing, rows, used);
            }
        }
        internal static AuditBaseline Map(AuditBaseline baseline, AuditSnapshot current, string expectedId, string currentId)
        {
            List<AuditRow> rows = Compare(baseline.Snapshot, current);
            AuditRow row = rows.Single(r => r.Expected != null && r.Expected.Id == expectedId);
            AuditFile replacement = current.Files.Single(f => f.Id == currentId);
            if (!replacement.Readable) throw new InvalidOperationException("The selected file has no readable identity.");
            if (rows.Any(r => r.Expected != null && r.Expected.Id != expectedId && r.Current != null && r.Current.Id == currentId)) throw new InvalidOperationException("That loaded file already belongs to another saved entry.");
            string requiredParent = null;
            if (!string.IsNullOrEmpty(row.Expected.ParentId))
            {
                AuditRow parent = rows.Single(r => r.Expected != null && r.Expected.Id == row.Expected.ParentId);
                if (parent.Current == null) throw new InvalidOperationException("Resolve the parent assembly first.");
                requiredParent = parent.Current.Id;
            }
            if (!AuditNames.Same(requiredParent, replacement.ParentId)) throw new InvalidOperationException("Select a replacement in the same parent assembly.");
            AuditBaseline copy = baseline.Copy(); AuditFile target = copy.Snapshot.Files.Single(f => f.Id == expectedId);
            target.Name = replacement.Name; target.Source = replacement.Source; target.NativeId = replacement.NativeId; target.Readable = replacement.Readable;
            copy.Revision++; return copy;
        }
        internal static void Export(string path, AuditBaseline baseline, AuditSnapshot snapshot, IList<AuditRow> rows)
        {
            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("Baseline,Revision,Checked UTC,Level,Status,Model,Parent assembly,Expected source,Current model,Current source,Details");
                foreach (AuditRow row in rows)
                    writer.WriteLine(string.Join(",", new[] { baseline.Name, baseline.Revision.ToString(), snapshot.CapturedUtc, row.File.Assembly ? "Assembly" : "Contained model", row.Label, row.File.Name, row.Parent, row.Expected == null ? "" : row.Expected.Source, row.Current == null ? "" : row.Current.Name, row.Current == null ? "" : row.Current.Source, row.Detail }.Select(Csv)));
                foreach (AuditFile file in snapshot.Files.Where(f => f.LimitedChildren)) writer.WriteLine(string.Join(",", new[] { baseline.Name, baseline.Revision.ToString(), snapshot.CapturedUtc, "Coverage", "Cannot verify", file.Name, AuditNames.ParentName(snapshot,file), "", "", "", "Embedded child-file list is not exposed; internal completeness is unverified." }.Select(Csv)));
                foreach (AuditFile file in baseline.Snapshot.Files.Where(f => f.LimitedChildren)) writer.WriteLine(string.Join(",", new[] { baseline.Name, baseline.Revision.ToString(), snapshot.CapturedUtc, "Saved coverage", "Cannot verify", file.Name, AuditNames.ParentName(baseline.Snapshot,file), "", "", "", "The saved complete list had limited embedded-file coverage for this container." }.Select(Csv)));
            }
        }
        internal static string Csv(string value)
        { value = value ?? ""; if (value.TrimStart().StartsWith("=") || value.TrimStart().StartsWith("+") || value.TrimStart().StartsWith("-") || value.TrimStart().StartsWith("@")) value = "'" + value; return "\"" + value.Replace("\"", "\"\"") + "\""; }
    }
}
