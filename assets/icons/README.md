# Ribbon icons

The three original transparent blue masters were generated with the built-in image_gen tool on 2026-10-13. Prompts are recorded in `generation-prompts.json`. Section Box uses a cube, clipping plane and crop corners; Model Sharing uses a cube and export arrow; Clash Setup uses overlapping cubes and an orange intersection.

`masters/` is the authoritative artwork. `tools/Render-RibbonIcons.cjs` creates transparent 16, 32 and 64 px PNGs with **96 DPI** metadata. The ribbon uses 16 and 32 px, and the sharing window uses the embedded 64 px mark. Run the renderer with Node.js and sharp available.

The older SVG sources are retained for history; the renderer now uses the generated PNG masters. Do not regenerate the new artwork from those SVGs.

Navisworks uses WPF image dimensions: `DIP = pixels * 96 / DPI`. The previous 384 DPI metadata caused 32 px icons to display at only 8 DIP. `tools/Test-RibbonAssets.ps1` decodes the actual ribbon references through WPF and checks their visible dimensions. `Build-Release.ps1` validates both source icons and the staged 2024/2025/2026/2027 and English ribbon assets before packaging.

Federation Audit was added on 2026-11-02 using a newly generated blue cube/checklist master. Its prompt is recorded in federation-audit-prompt.json. All four families use the same 96 DPI output policy.
