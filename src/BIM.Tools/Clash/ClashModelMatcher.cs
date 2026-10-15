using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BIM.Tools.Clash
{
    internal sealed class ClashMatch
    {
        internal ClashModelIdentity Saved;
        internal ClashModelIdentity Current;
        internal List<ClashCandidate> Candidates = new List<ClashCandidate>();
        internal bool Resolved { get { return Current != null; } }
        internal string Reason;
    }
    internal sealed class ClashCandidate
    {
        internal ClashModelIdentity Model;
        internal double Score;
        internal string Evidence;
        public override string ToString() { return Model.FileName + " — " + Model.Container; }
    }

    internal static class ClashModelMatcher
    {
        internal static List<ClashMatch> Resolve(ClashProfile profile, IList<ClashModelIdentity> current)
        {
            List<ClashMatch> result = new List<ClashMatch>();
            foreach (ClashModelIdentity saved in profile.SelectionA.Concat(profile.SelectionB).GroupBy(m => m.Id).Select(g => g.First()))
            {
                List<ClashModelIdentity> exact = current.Where(m => Same(saved.FileName, m.FileName)).ToList();
                ClashModelIdentity found = null;
                if (exact.Count == 1) found = exact[0];
                else if (exact.Count > 1)
                {
                    // A duplicate name is never resolved by list position.
                    List<ClashModelIdentity> strong = exact.Where(m => RootIdentity(saved, m)).ToList();
                    if (strong.Count == 1) found = strong[0];
                    if (found == null)
                    {
                        strong = exact.Where(m => !string.IsNullOrWhiteSpace(saved.SourcePath) && Same(saved.SourcePath, m.SourcePath)
                            && Same(saved.Container, m.Container)).ToList();
                        if (strong.Count == 1) found = strong[0];
                    }
                }
                ClashMatch match = new ClashMatch { Saved = saved, Current = found,
                    Reason = exact.Count > 1 ? "More than one loaded model has this name. Choose the intended source."
                        : "The saved file name is absent. Confirm a replacement, or load the missing model." };
                if (!match.Resolved)
                {
                    foreach (ClashModelIdentity model in current)
                    {
                        bool sameRoot = RootIdentity(saved, model), sameName = Same(saved.FileName, model.FileName);
                        double score = sameRoot || sameName ? 1 : Similarity(saved.FileName, model.FileName);
                        match.Candidates.Add(new ClashCandidate { Model = model, Score = score,
                            Evidence = sameRoot ? "Same stored model/root GUID; file name changed — confirmation required."
                                : sameName ? "Exact file name; duplicated source — confirmation required."
                                : string.Format("Name similarity {0:P0}{1}; suggestion only.", score,
                                    Same(saved.Container, model.Container) ? "; same container" : "") });
                    }
                    match.Candidates = match.Candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Model.FileName).ToList();
                }
                result.Add(match);
            }
            // Two intended files must not silently collapse into a single current file.
            foreach (var group in result.Where(m => m.Resolved).GroupBy(m => m.Current.Id).Where(g => g.Count() > 1))
                foreach (ClashMatch match in group)
                {
                    ClashModelIdentity candidate = match.Current; match.Current = null;
                    match.Reason = "Several saved entries resolve to the same loaded file. Confirm the intended mapping.";
                    match.Candidates.Add(new ClashCandidate { Model = candidate, Score = 1, Evidence = match.Reason });
                }
            return result;
        }

        internal static void Confirm(ClashProfile profile, string savedId, ClashModelIdentity replacement)
        {
            if (replacement == null) throw new ArgumentNullException("replacement");
            List<ClashModelIdentity> entries = profile.SelectionA.Concat(profile.SelectionB).Where(m => m.Id == savedId).ToList();
            if (entries.Count == 0) throw new InvalidOperationException("The saved model entry no longer exists.");
            string previous = entries[0].FileName;
            foreach (ClashModelIdentity entry in entries)
            {
                entry.FileName = replacement.FileName; entry.Container = replacement.Container;
                entry.SourcePath = replacement.SourcePath; entry.ModelGuid = replacement.ModelGuid; entry.RootGuid = replacement.RootGuid;
            }
            profile.Replacements.Add(new ClashReplacement { ModelId = savedId, PreviousName = previous,
                ReplacementName = replacement.FileName, ConfirmedUtc = DateTime.UtcNow.ToString("o") });
        }
        internal static bool Same(string first, string second)
        { return string.Equals((first ?? "").Trim(), (second ?? "").Trim(), StringComparison.OrdinalIgnoreCase); }
        private static bool RootIdentity(ClashModelIdentity a, ClashModelIdentity b)
        {
            Guid root, model;
            return Guid.TryParse(a.RootGuid, out root) && root != Guid.Empty && Same(a.RootGuid, b.RootGuid)
                && Guid.TryParse(a.ModelGuid, out model) && model != Guid.Empty && Same(a.ModelGuid, b.ModelGuid);
        }
        internal static double Similarity(string first, string second)
        {
            string a = Path.GetFileNameWithoutExtension(first ?? "").ToLowerInvariant();
            string b = Path.GetFileNameWithoutExtension(second ?? "").ToLowerInvariant();
            if (a.Length == 0 || b.Length == 0) return 0;
            HashSet<string> ta = new HashSet<string>(Regex.Split(a, @"[\W_]+")), tb = new HashSet<string>(Regex.Split(b, @"[\W_]+"));
            ta.Remove(""); tb.Remove("");
            double tokens = 2.0 * ta.Intersect(tb).Count() / Math.Max(1, ta.Count + tb.Count);
            int[] previous = Enumerable.Range(0, b.Length + 1).ToArray(), next = new int[b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
            {
                next[0] = i;
                for (int j = 1; j <= b.Length; j++) next[j] = Math.Min(Math.Min(next[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                int[] swap = previous; previous = next; next = swap;
            }
            double characters = 1.0 - (double)previous[b.Length] / Math.Max(a.Length, b.Length);
            return 0.55 * characters + 0.45 * tokens;
        }
    }
}
