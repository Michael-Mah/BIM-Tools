# BIM Tools

Company-neutral Navisworks **Section Box**, **Model Sharing** and **Clash Setup**, initially built and verified for Manage 2026/2027. 2024/2025 require matching installed hosts and remain skipped.

Download the installer from [1.1.1 Releases](https://github.com/Michael-Mah/BIM-Tools/releases/tag/v1.1.1). See the [model sharing guide](docs/MODEL-SHARING.md), [Clash Setup guide](docs/CLASH-SETUP.md) and [1.1.1 validation record](docs/VALIDATION-1.1.1.md). Close Navisworks before installation.

`Shared/` is the authoritative source for datum/box geometry, content discovery, clipping, export, estimates and their UI. Products import `Shared/BIM.Tools.Shared.projitems`; each product supplies its own `EditionBrand.cs`. This generic product includes a model-name based clash setup that remembers selections outside the NWF; company editions retain their separate model rules, workflow and history.

The generic product has no fixed company model list or project-specific package classification. Its plugin IDs, settings (`%LOCALAPPDATA%/BIM-Tools`), Saved Viewpoint prefix (`[BIM]`) and installation (`%APPDATA%/Autodesk/ApplicationPlugins/BIM.Tools.bundle`) are distinct from company editions. Both repositories remain private. Providing an installer does not require providing repository access.

## Build

```powershell
./tools/Build-Release.ps1 -Versions 2026,2027
```

Requires matching authentic Autodesk assemblies and .NET Framework 4.8 reference assemblies. No Autodesk assemblies or model fixtures are distributed.

## Model sharing

Confirm the current Section Box or choose a Saved Viewpoint, then explicitly **Detect Section Box Content**. Models inside the box are checked automatically; uncheck exclusions. After moving/resizing the box, **Refresh Content** updates affected contents and preserves exclusions. Four format buttons show measured time ranges.

All OBJ, IFC4, NWC and NWD output is permanently clipped, including rotated boxes. Cut ends are open. Native outputs are converted only from clipped IFC in a separate owned Navisworks instance. Extract the transfer ZIP before import. OBJ needs its MTL companion and explicit metre import units; IFC/native formats carry unit information. NWD is saved as 2026 format; backward compatibility of 2027 NWC is unverified. Original CAD properties/textures are omitted.

## Clash setup

See [Clash Setup](docs/CLASH-SETUP.md) for model discovery, saved selections, restoring a missing native test and confirming renamed-model replacements.

Run probes on disposable models. Full-federation timings and recipient coordinate alignment require their own validation. This repository starts with clean history containing only generic sources.
