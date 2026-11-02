# BIM Tools 1.2.0 — Federation Audit validation

Validated on 2026-11-02. The generic product adds Health → Federation Audit before the existing Coordination panel. The Health tool uses a newly generated blue cube/checklist icon, with 16/32/64 px transparent assets normalized to 96 DPI.

## Completed checks

- 31 independent persistence and comparison checks pass. These cover missing assemblies/children, an entirely empty current federation, equal counts hiding removal/addition, source relocation, renamed-file suggestions, explicit mapping, duplicate names, unresolved-parent coverage, protected baseline versions, corrupt/unsupported XML, external entities, source URL sanitation and CSV output. A synthetic 64-assembly, 512-child inventory reconciles correctly.
- Manage 2026 and 2027 compile successfully against their matching installed APIs. Final release builds have no warnings.
- Isolated native workflows in both hosts pass: read actual IFC source filenames without changing loaded models/selections, save a separate baseline, reopen NWF, read sources inside a packed NWD, identify a dropped source, suggest a renamed source and flag a moved same-name source. Opt-in monitoring reports the missing model after settling and does not repeatedly notify the same unchanged issue set.
- The final 2027 workflow repeats the native checks after UI refinements. At client sizes 1100×760, 850×580 and 760×540, the actual window paints its header/controls, its details pane remains inside the viewport, and wrapped toolbar/filter controls remain visible. No draft labels are included in the production window. A standalone layout harness also exercises resizing without changing Windows display settings.
- All 32 packaged ribbon references across both years and English resource folders decode to their intended WPF size. Health is the first panel and Federation Audit the first button.
- The EXE installer is extracted into staging and its 71 files are compared by SHA256 with the distribution. The installed bundle matches all 71 files; the previous 1.1.1 bundle is retained in a separate local backup directory.
- A fresh Manage 2026 instance loads the actual installed 1.2.0 command assembly and its automatic watcher. The real Federation Audit command opens without changing the model count or creating/replacing a baseline.

## Limits

Native tests use disposable synthetic IFC/NWF/NWD sources, not a complete live ACC project. No performance claim is made for the user's full federation. The first complete baseline still requires the user to verify all expected models are loaded. The tool checks exposed source-file identities; it does not establish geometry completeness, latest cloud versions or unexposed embedded contents, and it does not repair ACC references. 2024/2025 remain skipped.

No source data, Navisworks project files or Autodesk assemblies are shipped in the installer. Test fixtures and reports stay outside tracked source assets.
