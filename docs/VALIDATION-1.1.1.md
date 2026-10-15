# Ribbon icon repair — 1.1.1

2026-10-13: three new transparent blue icon families were generated for Section Box, Model Sharing and Clash Setup. The masters and prompts are under `assets/icons/`; 16/32/64 px outputs are created by `tools/Render-RibbonIcons.cjs`.

The installed 1.1.0 ribbon was inspected in Manage 2027. Model Sharing displayed normally; Section Box and Clash Setup were reduced to small marks. Their PNGs contained 384 DPI metadata, while the working image contained 96 DPI. A 32 px WPF image at 384 DPI has a natural size of only 8 DIP.

Validation completed:

- The WPF decoder regression check rejects the installed 384 DPI icon.
- All six new source ribbon references decode to the intended 16/32 DIP size at 96 DPI.
- Release builds for Manage 2026 and 2027 succeed.
- All 24 packaged ribbon references across both years and their English resource directories pass the WPF size check.
- The EXE installer was extracted into an isolated staging folder; all 59 files match the distribution by SHA256, and its ribbon assets pass the same checks.
- The distributed ZIP and EXE match the recorded SHA256SUMS.

This release changes ribbon artwork, image rendering and packaging validation. It does not change clash or model export behavior. Full model workflow tests were not repeated for this asset update. Manage 2024/2025 remain skipped because the matching installed hosts/API files are unavailable.

## GitHub publication checks — 2026-10-15

The previous published release was 1.0.1, so this release also includes the new remembered Clash Setup workflow that was installed locally as 1.1.0.

- All 17 profile persistence and model reconciliation checks pass, including renamed-model confirmation, missing and duplicate models, portable import ownership, invalid input and external XML entity rejection.
- The isolated native workflow probes compile against both Manage 2026 and 2027 APIs. Compilation is not a successful native workflow run.
- The prepared release hashes and its 24 ribbon references were checked again before publication. The packaged assemblies match the release build outputs.
- The installed 1.1.1 bundle previously matched all 59 release files and both host versions.

Native clash execution, preservation of existing result statuses, and recovery across actual model reopen/reload operations still require complete end-to-end validation on the installed hosts. Do not interpret the profile tests as verification of those native operations. No project models or company-specific configuration are included in the source commit or release assets.
