using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.Plugins;
using BIM.Tools.Clash;
using BIM.Tools.Models;
using BIM.Tools.UI;
using NavisApplication = Autodesk.Navisworks.Api.Application;

namespace BIM.Tools.ClashProbe
{
    [Plugin("BIM.ClashWorkflowProbe", "BIMT")]
    public sealed class ClashWorkflowProbePlugin : AddInPlugin
    {
        private readonly StringBuilder _log = new StringBuilder();
        private void Check(bool condition, string text) { if (!condition) throw new Exception(text); _log.AppendLine("PASS " + text); }
        private List<LoadedClashModel> Discover(Document doc)
        { List<LoadedClashModel> models = new List<LoadedClashModel>(); foreach (SharingProgress step in new ClashModelDiscovery().Discover(doc, models)) { } return models; }
        private PreparedClash Prepare(Document doc, ClashProfile profile, List<LoadedClashModel> models)
        { PreparedClash prepared = new PreparedClash(); foreach (SharingProgress step in new ClashNativeService().Prepare(doc, profile, models, prepared)) { } return prepared; }
        public override int Execute(params string[] args)
        {
            string folder = args[0], phase = args[1]; Directory.CreateDirectory(folder);
            try
            {
                Document doc = NavisApplication.ActiveDocument; ClashNativeService service = new ClashNativeService();
                ClashProfileStore store = new ClashProfileStore(Path.Combine(folder, "profiles"));
                DocumentClashTests tests = ClashNativeService.GetTests(doc);
                _log.AppendLine("YearAPI=" + typeof(Document).Assembly.GetName().Version + ";Phase=" + phase);
                if (phase == "initial")
                {
                    doc.AppendFile(args[2]); List<LoadedClashModel> loaded = Discover(doc);
                    Check(loaded.Count == 2, "File-only discovery lists the two actual loaded model files");
                    Check(ClashNativeService.AllTests(tests).Count() == 0, "Discovering files does not create or run native clash tests");
                    ClashProfile profile = new ClashProfile { Name = "Persistent fixture", ToleranceMillimetres = 1 };
                    profile.SelectionA.Add(loaded[0].Identity.Copy()); profile.SelectionB.Add(loaded[1].Identity.Copy()); store.Save(profile);
                    Check(ClashNativeService.AllTests(tests).Count() == 0, "Saving plugin profile does not create or run a native test");
                    PreparedClash prepared = Prepare(doc, profile, loaded); service.Restore(doc, profile, prepared); store.Save(profile);
                    ClashTest test = ClashNativeService.Find(tests, profile.NativeTestId);
                    Check(test.LastRun == null && test.SelectionA.Selection.GetSelectedItems(doc).Count == 1 && test.SelectionB.Selection.GetSelectedItems(doc).Count == 1,
                        "Restore creates root selections without starting detection");
                    service.Restore(doc, profile, prepared);
                    Check(ClashNativeService.AllTests(tests).Count() == 1, "Repeated restore updates owned GUID without duplicating test");
                    ClashRunSummary run = service.Run(doc, profile, prepared); _log.AppendLine("NativeResults=" + run.Results);
                    Check(run.Results >= 2, "Explicit Run uses native Clash Detective on real overlapping geometry");
                    test = ClashNativeService.Find(tests, profile.NativeTestId); ClashResult result = ClashNativeService.Results(test).First(); Guid resultId = result.Guid;
                    tests.TestsEditResultStatus(result, ClashResultStatus.Approved, null);
                    test = ClashNativeService.Find(tests, profile.NativeTestId);
                    tests.TestsAddCopy(test, new ClashResultGroup { DisplayName = "Reviewed group" });
                    test = ClashNativeService.Find(tests, profile.NativeTestId); ClashResultGroup group = test.Children.OfType<ClashResultGroup>().Single();
                    int resultIndex = -1; for (int i = 0; i < test.Children.Count; i++) if (test.Children[i].Guid == resultId) resultIndex = i;
                    tests.TestsMove(test, resultIndex, group, 0);
                    test = ClashNativeService.Find(tests, profile.NativeTestId); int beforeCount = ClashNativeService.Results(test).Count();
                    ClashTest broken = (ClashTest)test.CreateCopy(); broken.SelectionA.Selection.Clear(); broken.SelectionB.Selection.Clear();
                    tests.TestsEditTestFromCopy(test, broken); test = ClashNativeService.Find(tests, profile.NativeTestId);
                    Check(test.SelectionA.Selection.GetSelectedItems(doc).Count == 0, "Reproduced native selections disappearing");
                    service.Restore(doc, profile, Prepare(doc, profile, Discover(doc))); test = ClashNativeService.Find(tests, profile.NativeTestId);
                    Check(test.SelectionA.Selection.GetSelectedItems(doc).Count == 1 && test.SelectionB.Selection.GetSelectedItems(doc).Count == 1, "One-click service restore rebinds both missing selections");
                    Check(ClashNativeService.Results(test).Count() == beforeCount && ClashNativeService.Results(test).Single(r => r.Guid == resultId).Status == ClashResultStatus.Approved
                        && test.Children.OfType<ClashResultGroup>().Any(g => g.DisplayName == "Reviewed group"), "Restore preserves existing result GUIDs, groups and approved status");
                    store.Save(profile); doc.SaveFile(Path.Combine(folder, "saved-fixture.nwf"));
                    File.WriteAllText(Path.Combine(folder, "profile-id.txt"), profile.Id);
                    Preview(doc, store, folder);
                }
                else
                {
                    string id = File.ReadAllText(Path.Combine(folder, "profile-id.txt"));
                    ClashProfile profile = store.LoadAll(new List<string>()).Single(p => p.Id == id);
                    if (phase == "reopen")
                    {
                        List<LoadedClashModel> models = Discover(doc);
                        Check(ClashModelMatcher.Resolve(profile, models.Select(m => m.Identity).ToList()).All(m => m.Resolved), "Independent saved selections rebind after reopening NWF");
                        ClashTest test = ClashNativeService.Find(tests, profile.NativeTestId); Guid previous = test.Guid;
                        service.Restore(doc, profile, Prepare(doc, profile, models));
                        Check(ClashNativeService.AllTests(tests).Count() == 1 && ClashNativeService.Find(tests, profile.NativeTestId).Guid == previous, "Reopened test restored using its existing GUID");
#if NAVISWORKS2027
                        // Exercise the 2027 folder API using its actual public folder type.
                        Type folderType = typeof(ClashTest).Assembly.GetTypes().FirstOrDefault(t => typeof(GroupItem).IsAssignableFrom(t) && t.Name.IndexOf("Folder", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (folderType != null)
                        {
                            GroupItem nested = (GroupItem)Activator.CreateInstance(folderType); nested.DisplayName = "Nested setup tests";
                            tests.TestsAddCopy(tests.Value.TestsRoot, nested);
                            GroupItem storedFolder = tests.Value.TestsRoot.Children.OfType<GroupItem>().Single(g => g.DisplayName == nested.DisplayName);
                            test = ClashNativeService.Find(tests, profile.NativeTestId); int index = -1;
                            for (int i = 0; i < tests.Value.TestsRoot.Children.Count; i++) if (tests.Value.TestsRoot.Children[i].Guid == test.Guid) index = i;
                            tests.TestsMove(tests.Value.TestsRoot, index, storedFolder, 0);
                            service.Restore(doc, profile, Prepare(doc, profile, models));
                            Check(ClashNativeService.Find(tests, profile.NativeTestId) != null && ClashNativeService.AllTests(tests).Count() == 1, "Restore finds a test inside a Navisworks 2027 folder");
                        }
                        else throw new Exception("Could not exercise the 2027 test folder API.");
#endif
                        test = ClashNativeService.Find(tests, profile.NativeTestId);
                        ClashResult first = ClashNativeService.Results(test).First(); Point3D centre = first.Center;
                        double size = doc.GetBoundingBox(true).Size.Length * 0.06;
                        Viewpoint view = doc.ActiveView.CreateViewpointCopy(); view.ClipPlanes.Enabled = true; view.ClipPlanes.Mode = ClipPlaneSetMode.Box;
                        view.ClipPlanes.SetOrientedBox(new BoundingBox3D(new Point3D(centre.X - size, centre.Y - size, centre.Z - size), new Point3D(centre.X + size, centre.Y + size, centre.Z + size)), new Rotation3D(0, 0, Math.Sin(Math.PI / 14), Math.Cos(Math.PI / 14)));
                        doc.CurrentViewpoint.CopyFrom(view); string json; profile.Box = ClashNativeService.CaptureBox(doc, out json); profile.SectionBoxJson = json; profile.UseSectionBox = true;
                        string originalClip = doc.ActiveView.GetClippingPlanes(); PreparedClash boxPrepared = Prepare(doc, profile, models);
                        Check(doc.ActiveView.GetClippingPlanes() == originalClip, "Restoring a saved rotated box does not alter active clipping planes");
                        service.Restore(doc, profile, boxPrepared); ClashRunSummary scoped = service.Run(doc, profile, boxPrepared);
                        test = ClashNativeService.Find(tests, profile.NativeTestId);
                        Check(scoped.Results > 0 && ClashNativeService.Results(test).All(r => boxPrepared.Scope.Contains(r.Center)), "Scoped native run retains only clash centres in the saved rotated Section Box");
                        _log.AppendLine("ScopedResults=" + scoped.Results + ";OutsideCentresExcluded=" + scoped.OutsideBoxRemoved);
                    }
                    else if (phase == "rename")
                    {
                        doc.AppendFile(args[2]); List<LoadedClashModel> loaded = Discover(doc);
                        List<ClashMatch> matches = ClashModelMatcher.Resolve(profile, loaded.Select(m => m.Identity).ToList());
                        Check(matches.Count(m => !m.Resolved) == 1, "Real renamed model requires replacement confirmation");
                        int testsBefore = ClashNativeService.AllTests(tests).Count(); bool blocked = false;
                        try { Prepare(doc, profile, loaded); } catch (InvalidOperationException) { blocked = true; }
                        Check(blocked && ClashNativeService.AllTests(tests).Count() == testsBefore, "Unconfirmed renamed model blocks partial native restore");
                        ClashMatch renamed = matches.Single(m => !m.Resolved); ClashProfile cancelled = profile.Copy();
                        ClashModelMatcher.Confirm(cancelled, renamed.Saved.Id, renamed.Candidates[0].Model);
                        Check(store.LoadAll(new List<string>()).Single(p => p.Id == id).SelectionA[0].FileName == profile.SelectionA[0].FileName, "Cancelled comparison does not update the persisted profile");
                        profile = cancelled; store.Save(profile); service.Restore(doc, profile, Prepare(doc, profile, loaded)); store.Save(profile);
                        Check(ClashNativeService.AllTests(tests).Count() == 1 && ClashNativeService.Find(tests, profile.NativeTestId).LastRun == null, "Deleted native test recreated from plugin profile after confirmed rename without running it");
                        Check(store.LoadAll(new List<string>()).Single(p => p.Id == id).Replacements.Count == 1, "Confirmed real model rename is recorded independently");
                    }
                }
                File.WriteAllText(Path.Combine(folder, phase + "-report.txt"), _log.ToString()); return 0;
            }
            catch (Exception error) { _log.AppendLine(error.ToString()); File.WriteAllText(Path.Combine(folder, phase + "-report.txt"), _log.ToString()); return 1; }
        }
        private void Preview(Document doc, ClashProfileStore store, string folder)
        {
            int testsBefore = ClashNativeService.AllTests(ClashNativeService.GetTests(doc)).Count();
            using (ClashSetupForm form = new ClashSetupForm(doc, store))
            {
                form.ShowInTaskbar = false; form.Show(); System.Windows.Forms.Application.DoEvents();
                ((ComboBox)typeof(ClashSetupForm).GetField("_profiles", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form)).SelectedIndex = 0;
                form.PerformLayout();
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "clash-setup.png")); }
                Check(ClashNativeService.AllTests(ClashNativeService.GetTests(doc)).Count() == testsBefore, "Opening the actual UI reads files without starting a clash");
                foreach (float scale in new[] { 1F, 1.25F, 1.5F, 2F })
                {
                    using (ClashSetupForm layout = new ClashSetupForm(doc, store))
                    {
                        layout.Scale(new SizeF(scale, scale));
                        foreach (Size size in new[] { new Size(1366, 728), new Size(1920, 1040), new Size(3840, 2320) })
                        { Rectangle area = new Rectangle(Point.Empty, size); BimWindowLayout.FitToWorkingArea(layout, area); Check(area.Contains(layout.Bounds), "Window fits " + size + " at relative scale " + scale); }
                    }
                }
                form.Close();
            }
        }
    }
}
