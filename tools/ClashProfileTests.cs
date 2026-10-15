using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIM.Tools.Clash;

internal static class ClashProfileTests
{
    private static int _checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); _checks++; Console.WriteLine("PASS " + message); }
    private static ClashModelIdentity Model(string name, string source, string container)
    { return new ClashModelIdentity { FileName = name, SourcePath = source, Container = container, ModelGuid = Guid.NewGuid().ToString(), RootGuid = Guid.NewGuid().ToString() }; }
    private static int Main(string[] args)
    {
        try
        {
            string folder = args[0]; Directory.CreateDirectory(folder); ClashProfileStore store = new ClashProfileStore(folder);
            ClashModelIdentity structure = Model("GEN-STR-Z01-001-R01.ifc", "C:/Models/STR.ifc", "Building A");
            ClashModelIdentity services = Model("GEN-MEP-Z01-002-R01.ifc", "C:/Models/MEP.ifc", "Building A");
            ClashProfile original = new ClashProfile { Name = "Structure vs services", ToleranceMillimetres = 2.5,
                NativeTestId = Guid.NewGuid().ToString(), NativeTestName = "Native test" };
            original.SelectionA.Add(structure); original.SelectionB.Add(services); store.Save(original); store.Save(original);
            List<string> warnings = new List<string>(); ClashProfile saved = store.LoadAll(warnings).Single();
            Check(warnings.Count == 0 && saved.SelectionA.Count == 1 && saved.SelectionB.Count == 1 && saved.ToleranceMillimetres == 2.5, "Atomic profile round-trip and overwrite");
            Check(File.Exists(Path.Combine(folder, original.Id + ".xml.bak")), "Previous saved profile retained as backup");
            List<ClashModelIdentity> updated = new List<ClashModelIdentity> { Model(structure.FileName, structure.SourcePath, structure.Container), Model(services.FileName, services.SourcePath, services.Container) };
            Check(ClashModelMatcher.Resolve(saved, updated).All(m => m.Resolved), "Same file names rebind after all native identities change");
            List<ClashMatch> missing = ClashModelMatcher.Resolve(saved, updated.Take(1).ToList());
            Check(missing.Count(m => !m.Resolved) == 1 && saved.SelectionB.Count == 1, "Missing model stays in the saved intention");
            ClashModelIdentity renamed = Model("GEN-STR-Z01-001-R02.ifc", "C:/Models/STR-R02.ifc", "Building A");
            updated[0] = renamed; ClashMatch review = ClashModelMatcher.Resolve(saved, updated).Single(m => !m.Resolved);
            Check(review.Candidates[0].Model.Id == renamed.Id && review.Current == null, "Revision-name comparison suggests a candidate without auto-accepting");
            ClashProfile cancelledDraft = saved.Copy(); ClashModelMatcher.Confirm(cancelledDraft, structure.Id, renamed);
            Check(saved.SelectionA[0].FileName == structure.FileName && saved.Replacements.Count == 0, "Cancelling a rename draft leaves the original unchanged");
            store.Save(cancelledDraft); ClashProfile confirmed = store.LoadAll(new List<string>()).Single();
            Check(ClashModelMatcher.Resolve(confirmed, updated).All(m => m.Resolved) && confirmed.Replacements.Single().PreviousName == structure.FileName, "Confirmed replacement persists and rebinds next time");
            ClashModelIdentity guidRename = structure.Copy(); guidRename.Id = Guid.NewGuid().ToString(); guidRename.FileName = "renamed.ifc";
            ClashMatch guidReview = ClashModelMatcher.Resolve(original, new List<ClashModelIdentity> { guidRename, services }).Single(m => !m.Resolved);
            Check(guidReview.Candidates[0].Score == 1 && !guidReview.Resolved, "Even same-GUID renames require confirmation");
            ClashModelIdentity duplicateA = Model("SAME.ifc", "C:/A/SAME.ifc", "Federation A"), duplicateB = Model("SAME.ifc", "C:/B/SAME.ifc", "Federation B");
            ClashProfile duplicates = new ClashProfile { Name = "Same basename, different sources" }; duplicates.SelectionA.Add(duplicateA); duplicates.SelectionB.Add(duplicateB);
            List<ClashModelIdentity> duplicateLoaded = new List<ClashModelIdentity> { Model("SAME.ifc", duplicateA.SourcePath, duplicateA.Container), Model("SAME.ifc", duplicateB.SourcePath, duplicateB.Container) };
            Check(ClashModelMatcher.Resolve(duplicates, duplicateLoaded).All(m => m.Resolved), "Duplicate names disambiguated by source and container after update");
            duplicateLoaded[1].SourcePath = duplicateA.SourcePath; duplicateLoaded[1].Container = duplicateA.Container;
            Check(ClashModelMatcher.Resolve(duplicates, duplicateLoaded).Any(m => !m.Resolved), "Indistinguishable duplicate names never chosen by position");
            ClashProfile collapsed = new ClashProfile { Name = "Two intended copies" }; collapsed.SelectionA.Add(duplicateA.Copy()); collapsed.SelectionB.Add(duplicateB.Copy());
            Check(ClashModelMatcher.Resolve(collapsed, duplicateLoaded.Take(1).ToList()).All(m => !m.Resolved), "Several saved files cannot silently collapse into one loaded file");
            string exported = Path.Combine(folder, "portable.setup"); store.Export(exported, confirmed); ClashProfile imported = store.Import(exported);
            Check(imported.Id != confirmed.Id && imported.NativeTestId == null && imported.SelectionA.Count == 1, "Portable import gets its own ownership and preserves model intentions");
            File.WriteAllText(Path.Combine(folder, "corrupt.xml"), "<ClashSetup>"); warnings.Clear(); store.LoadAll(warnings);
            Check(warnings.Count == 1 && File.Exists(Path.Combine(folder, "corrupt.xml")), "Unreadable setup reported and retained");
            ClashProfile unsupported = original.Copy(); unsupported.SchemaVersion = 99;
            bool blocked = false; try { store.Save(unsupported); } catch (InvalidDataException) { blocked = true; }
            Check(blocked, "Unsupported schema cannot overwrite a stored profile");
            ClashProfile invalid = original.Copy(); invalid.ToleranceMillimetres = double.NaN; blocked = false;
            try { store.Save(invalid); } catch (InvalidDataException) { blocked = true; } Check(blocked, "Invalid tolerance rejected");
            invalid = original.Copy(); invalid.UseSectionBox = true; blocked = false;
            try { store.Save(invalid); } catch (InvalidDataException) { blocked = true; } Check(blocked, "Box mode requires a captured valid scope");
            string malicious = Path.Combine(folder, "dtd.setup");
            File.WriteAllText(malicious, "<!DOCTYPE ClashSetup [<!ENTITY ext SYSTEM 'file:///unread'>]><ClashSetup>&ext;</ClashSetup>"); blocked = false;
            try { store.Import(malicious); } catch (InvalidOperationException) { blocked = true; } catch (System.Xml.XmlException) { blocked = true; }
            Check(blocked, "Imported setup cannot expand external XML entities");
            Console.WriteLine("PASS " + _checks + " persistence / reconciliation checks"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
