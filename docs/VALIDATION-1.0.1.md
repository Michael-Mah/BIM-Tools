# BIM Tools 1.0.1 validation

This patch removes the remaining company wordmark from the Section Box SVG and regenerates its 16/32 px PNGs in blue. The installer and manifest advance to 1.0.1. No shared runtime geometry, detection, export, settings or progress code changes from 1.0.0.

The 2026/API 23 and 2027/API 24 builds use their matching installed Autodesk APIs and .NET Framework 4.8. Packages include only those years. 2024/2025 remain skipped.

The 1.0.0 source workflow, four-format rotated-box reopen and layout checks remain the functional baseline; see [1.0.0 validation](VALIDATION-1.0.0.md). This patch checks regenerated icon resources, complete installed file hashes and installed command/coexistence behavior on both hosts. The generic product retains separate settings, Saved Viewpoint identities, plugin IDs and bundle paths.

Full large-project timing, recipient Revizto alignment, real mixed-monitor transitions and 2027 NWC backward compatibility remain unverified. Cut ends are open; original CAD properties/textures are omitted. OBJ uses metre import units and its MTL companion. NWD uses 2026 format.
