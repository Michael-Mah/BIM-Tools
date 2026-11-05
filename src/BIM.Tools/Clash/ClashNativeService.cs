using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using BIM.Tools.Models;
using NavisApplication = Autodesk.Navisworks.Api.Application;

namespace BIM.Tools.Clash
{
    internal sealed class PreparedClash
    {
        internal ModelItemCollection A = new ModelItemCollection(), B = new ModelItemCollection();
        internal SectionBoxScope Scope;
        internal Model[] Models;
    }
    internal sealed class ClashRunSummary
    {
        internal int Results, OutsideBoxRemoved;
        internal string TestName;
    }
    internal sealed class ClashNativeService
    {
        internal static ClashBox CaptureBox(Document document, out string json)
        {
            SectionBoxScope scope; string reason;
            if (!SectionBoxScope.TryRead(document.ActiveView, out scope, out reason)) throw new InvalidOperationException(reason);
            Viewpoint copy = document.ActiveView.CreateViewpointCopy();
            BoundingBox3D bounds = new BoundingBox3D(); Rotation3D rotation = new Rotation3D();
            ClipPlaneCompatibility.GetOrientedBox(copy, bounds, rotation); rotation = rotation.Normalize(); json = scope.RawJson;
            return new ClashBox { MinX = scope.Minimum.X, MinY = scope.Minimum.Y, MinZ = scope.Minimum.Z,
                MaxX = scope.Maximum.X, MaxY = scope.Maximum.Y, MaxZ = scope.Maximum.Z,
                A = rotation.A, B = rotation.B, C = rotation.C, D = rotation.D };
        }
        internal static SectionBoxScope Scope(ClashProfile profile)
        {
            if (!profile.UseSectionBox) return null;
            ClashBox b = profile.Box;
            return SectionBoxScope.FromBox(new Point3D(b.MinX, b.MinY, b.MinZ), new Point3D(b.MaxX, b.MaxY, b.MaxZ),
                new Rotation3D(b.A, b.B, b.C, b.D), profile.SectionBoxJson);
        }
        internal IEnumerable<SharingProgress> Prepare(Document document, ClashProfile profile, IList<LoadedClashModel> loaded, PreparedClash result)
        {
            ClashProfileStore.Validate(profile);
            if (profile.SelectionA.Count == 0 || profile.SelectionB.Count == 0) throw new InvalidOperationException("Select at least one model on each side.");
            List<ClashMatch> matches = ClashModelMatcher.Resolve(profile, loaded.Select(m => m.Identity).ToList());
            List<ClashMatch> missing = matches.Where(m => !m.Resolved).ToList();
            if (missing.Count > 0) throw new InvalidOperationException("Restore is blocked: resolve all " + missing.Count + " missing/ambiguous model(s) first. Saved selections are retained.");
            result.Models = document.Models.ToArray(); result.Scope = Scope(profile);
            int completed = 0, total = profile.SelectionA.Count + profile.SelectionB.Count, visited = 0;
            foreach (bool sideA in new[] { true, false })
            {
                ModelItemCollection target = sideA ? result.A : result.B;
                HashSet<ModelItem> seen = new HashSet<ModelItem>();
                foreach (ClashModelIdentity identity in sideA ? profile.SelectionA : profile.SelectionB)
                {
                    ClashMatch match = matches.First(m => m.Saved.Id == identity.Id);
                    ModelItem root = loaded.First(m => m.Identity.Id == match.Current.Id).Root;
                    if (root == null || root.IsDisposed) throw new InvalidOperationException("The loaded models changed. Refresh Models and try again.");
                    if (result.Scope == null) { if (seen.Add(root)) target.Add(root); }
                    else
                    {
                        Stack<ModelItem> pending = new Stack<ModelItem>(); pending.Push(root);
                        while (pending.Count > 0)
                        {
                            ModelItem item = pending.Pop(); bool intersects;
                            try { intersects = result.Scope.Intersects(item.BoundingBox()); }
                            catch { intersects = true; }
                            if (intersects)
                            {
                                if (item.HasGeometry)
                                {
                                    if (seen.Add(item)) target.Add(item);
                                    // Select geometry leaves only. Including a geometry
                                    // container and its descendants duplicates the same
                                    // objects in Clash Detective and makes restore slower.
                                }
                                else foreach (ModelItem child in item.Children)
                                {
                                    pending.Push(child);
                                    if (pending.Count % 128 == 0) yield return Step(identity, completed, total, ++visited);
                                }
                            }
                            if (++visited % 64 == 0) yield return Step(identity, completed, total, visited);
                        }
                    }
                    yield return Step(identity, ++completed, total, visited);
                }
            }
            if (result.A.Count == 0 || result.B.Count == 0) throw new InvalidOperationException("There is no geometry on one side in the saved Section Box. The native test was not changed.");
            CheckModels(document, result);
        }
        private static SharingProgress Step(ClashModelIdentity model, int done, int total, int visited)
        { return new SharingProgress("Prepare clash selections", model.FileName + " · " + visited.ToString("N0") + " tree items checked", done, total); }

        internal ClashTest Restore(Document document, ClashProfile profile, PreparedClash prepared)
        {
            CheckModels(document, prepared); DocumentClashTests tests = GetTests(document);
            ClashTest current = Find(tests, profile.NativeTestId);
            ClashTest editable = current == null ? new ClashTest() : (ClashTest)current.CreateCopy();
            // Restoring an imported native test must preserve the user's existing
            // test name. Only tests created by this tool receive the BIM prefix.
            string name = current == null ? "[BIM] " + profile.Name : current.DisplayName;
            if (current == null)
            {
                // Same-name tests are not ownership evidence. Give the new test a
                // unique name rather than overwriting someone else's existing test.
                List<string> names = AllTests(tests).Select(t => t.DisplayName).ToList();
                string baseName = name; int suffix = 2;
                while (names.Any(n => ClashModelMatcher.Same(n, name))) name = baseName + " (" + suffix++ + ")";
                editable.Guid = Guid.NewGuid();
                editable.SelectionA.PrimitiveTypes = PrimitiveTypes.Triangles;
                editable.SelectionB.PrimitiveTypes = PrimitiveTypes.Triangles;
            }
            editable.DisplayName = name; editable.TestType = (ClashTestType)Enum.Parse(typeof(ClashTestType), profile.Kind.ToString());
            editable.Tolerance = profile.ToleranceMillimetres * UnitConversion.ScaleFactor(Units.Millimeters, document.Units);
            editable.SelectionA.Selection.CopyFrom(prepared.A); editable.SelectionB.Selection.CopyFrom(prepared.B);
            Guid testId = editable.Guid;
            if (current == null)
            {
#if NAVISWORKS2027
                tests.TestsAddCopy(tests.Value.TestsRoot, editable);
#else
                tests.TestsAddCopy(editable);
#endif
            }
            else tests.TestsEditTestFromCopy(current, editable);
            // Every mutation may dispose the previous native wrapper.
            ClashTest persisted = Find(tests, testId.ToString("D"));
            if (persisted == null) throw new InvalidOperationException("Navisworks did not persist the clash test.");
            profile.NativeTestId = persisted.Guid.ToString("D"); profile.NativeTestName = persisted.DisplayName;
            return persisted;
        }
        internal ClashRunSummary Run(Document document, ClashProfile profile, PreparedClash prepared)
        {
            CheckModels(document, prepared); DocumentClashTests tests = GetTests(document);
            ClashTest test = Find(tests, profile.NativeTestId);
            if (test == null) throw new InvalidOperationException("Restore the test selections before running it.");
            using (Progress progress = NavisApplication.BeginProgress("BIM Tools · Run Clash", test.DisplayName))
            {
                progress.Update(0); tests.TestsRunTest(test);
                if (progress.IsCanceled) throw new OperationCanceledException("Native clash run cancelled. The saved setup is retained.");
                progress.Update(1);
            }
            test = Find(tests, profile.NativeTestId);
            if (test == null) throw new InvalidOperationException("The clash test disappeared during the run.");
            ClashRunSummary summary = new ClashRunSummary { TestName = test.DisplayName };
            if (prepared.Scope != null)
            {
                // The native engine clashes whole elements. Scope the plugin's
                // results by centre, including results inside nested result groups.
                List<Guid> outside = Results(test).Where(r => !prepared.Scope.Contains(r.Center)).Select(r => r.Guid).ToList();
                foreach (Guid id in outside)
                {
                    test = Find(tests, profile.NativeTestId); GroupItem parent; SavedItem item;
                    if (FindResult(test, id, out parent, out item)) { tests.TestsRemove(parent, item); summary.OutsideBoxRemoved++; }
                }
                test = Find(tests, profile.NativeTestId);
            }
            summary.Results = Results(test).Count(); return summary;
        }
        internal static DocumentClashTests GetTests(Document document)
        {
            DocumentClash clash = DocumentClash.ClashInstance(document);
            if (clash == null) throw new InvalidOperationException("Clash Detective requires Navisworks Manage.");
            return clash.TestsData;
        }
        internal static IEnumerable<ClashTest> AllTests(DocumentClashTests tests)
        {
#if NAVISWORKS2027
            return WalkTests(tests.Value.TestsRoot.Children);
#else
            return WalkTests(tests.Tests);
#endif
        }
        private static IEnumerable<ClashTest> WalkTests(SavedItemCollection items)
        {
            foreach (SavedItem item in items)
            {
                ClashTest test = item as ClashTest;
                if (test != null) yield return test;
                else
                {
                    GroupItem group = item as GroupItem;
                    if (group != null) foreach (ClashTest child in WalkTests(group.Children)) yield return child;
                }
            }
        }
        internal static ClashTest Find(DocumentClashTests tests, string id)
        { Guid guid; return Guid.TryParse(id, out guid) ? AllTests(tests).FirstOrDefault(t => t.Guid == guid) : null; }
        internal static IEnumerable<ClashResult> Results(GroupItem group)
        {
            foreach (SavedItem item in group.Children)
            {
                ClashResult result = item as ClashResult;
                if (result != null) yield return result;
                else { GroupItem nested = item as GroupItem; if (nested != null) foreach (ClashResult child in Results(nested)) yield return child; }
            }
        }
        private static bool FindResult(GroupItem group, Guid id, out GroupItem parent, out SavedItem item)
        {
            foreach (SavedItem child in group.Children)
            {
                if (child.Guid == id) { parent = group; item = child; return true; }
                GroupItem nested = child as GroupItem;
                if (nested != null && FindResult(nested, id, out parent, out item)) return true;
            }
            parent = null; item = null; return false;
        }
        private static void CheckModels(Document document, PreparedClash prepared)
        {
            if (document == null || document.IsDisposed || document.IsClear || prepared.Models == null || !document.Models.SequenceEqual(prepared.Models))
                throw new InvalidOperationException("The open models changed. Refresh Models and try again.");
        }
    }
}
