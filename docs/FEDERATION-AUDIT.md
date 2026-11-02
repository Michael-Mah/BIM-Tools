# Federation Audit

Open **BIM Tools → Health → Federation Audit**. This is the first ribbon tool, ahead of the Coordination tools. It compares a remembered complete source-file list with the model files currently loaded in Navisworks Manage 2026/2027.

## Save the complete list

1. Open the live federation, wait for loading to finish and verify that all expected assemblies are present.
2. Open Federation Audit and choose **Save complete model list**.
3. Give the list a name and confirm the assembly and contained-file counts. A new list is created by default. Replacing a selected list requires an explicit checkbox and confirmation, and keeps its previous version.

The list is saved independently of the NWF under `%LOCALAPPDATA%/BIM-Tools/FederationBaselines`. A first list cannot identify files that were already absent when it was saved. The default list name comes from the currently opened view. Local files are associated by document path. ACC live views are associated by account, project and aggregate/view identifiers, so the same view can retain its association after a display-name or folder change. Unrecognized cloud addresses use their full path. Another view's list requires an explicit confirmation before a manual check.

## Check after updates

Choose the complete list and click **Check Now**. Use **Assemblies** or **Contained models**, search, and **Problems only**. Missing entries remain under their original parent assembly. Select a row to inspect previous/current sources and related loaded files. File count alone is never the matching criterion.

- **Missing**: the expected file is absent; an absent parent retains its child expectations.
- **Changed source**: the same filename is loaded from another readable source location.
- **Review match**: a name is ambiguous or a related filename may be a replacement.
- **Cannot verify**: the identity or parent is unresolved, or an embedded child-file list is not exposed.
- **New**: a loaded file is not mapped to the saved expectations.
- **Loaded**: the expected file identity is found in its expected assembly.

**Review mapping** requires selecting a related loaded file and confirming the old and new reference. It updates the saved list only; it does not relink ACC references or append models. Other expected files remain recorded. Duplicate file names are compared within their assembly and by available source evidence, never by row position.

**Export CSV** includes all rows, their parent assemblies, source evidence, check time and coverage limitations. After models change, check again before exporting or changing a mapping.

## Automatic checks

Save a complete model list for each view you want to register. There is no fixed list of view names or projects. Optionally enable **Check this view automatically on open / refresh** when saving, or use **Check on open / refresh** on the main window afterward.

**Watched views…** lists registered views and lets you enable or disable each independently. It uses one active complete list per view. To use a different saved list for that view, select it on the main window and enable its automatic checks. Older complete lists are retained. Unchecking a view does not delete its saved model expectations.

On opening or refreshing an enabled view, the watcher waits for the model collection to settle, compares that view's own saved list, and gives a nonmodal notification for changes or incomplete coverage. An open audit window switches to the current view's matching list and automatic results. Unregistered views are not checked against another view's expectations. The same unchanged issue set is not repeatedly notified. Checks never save or replace the baseline. Automatic checks are off until explicitly enabled. This works with the view loaded in Navisworks; it does not poll ACC in the background or list unopened remote views.

## Scope and performance

The reader inspects source-file roots and file-named embedded containers. It does not traverse every geometry element. A cancellable progress window shows the current file; cancelled or interrupted reads do not overwrite the saved list. Embedded child lists that Navisworks does not expose remain explicitly unverified.

File presence does not prove that all geometry is complete or that the latest ACC version is loaded. This tool does not authenticate to ACC, inspect its entire remote package manifest or repair a broken cloud link. It does not classify hidden geometry as a missing source file.

Version 1.2.1 is built for the installed 2026/2027 hosts. 2024/2025 remain skipped until matching installed hosts/API files are available.
