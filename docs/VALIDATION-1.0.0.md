# BIM Tools 1.0.0 validation

Built separately against Navisworks Manage 2026/API 23 and 2027/API 24 using .NET Framework 4.8. Packages contain only 2026/2027; unavailable 2024/2025 installations are skipped.

Both source workflow tests pass: explicit box confirmation without automatic scanning, Detect, automatic model checks, manual exclusions, incremental Refresh preserving exclusions, cache reuse, numeric and identifier property reading, nested Saved Viewpoints, four format estimates, actual export popup/footer progress and cancellation. Relative layout scales 100/125/150/200% pass.

OBJ, IFC4, NWC and NWD exports from a disposable 8.7 MB fixture are reopened with clipping disabled. All inspected vertices remain inside the rotated Section Box. The fixture contains 1,433 intersecting geometry items. Native conversion uses clipped IFC and a separate hidden Navisworks process. NWD uses 2026 format.

The generic product has its own installation path, plugin IDs, settings folder and Saved Viewpoint prefix. Its source and history do not contain the company-specific model configuration or clash workflow. All 43 installed files match release staging SHA-256 hashes.

Full large-project timing, recipient-side Revizto alignment, 2027 NWC backward compatibility and real mixed-monitor transitions remain unverified. Cut ends are open; original CAD properties/textures are omitted. OBJ import units must be metres and the MTL must remain beside it.
