# Model Sharing

Open a model in Navisworks Manage 2026 or 2027 and select **BIM Tools → Model Sharing**.

1. Use **Read Current Box** to capture the enabled native Section Box, or **Use Existing Box** to choose a Saved Viewpoint containing a box. Confirming a box does not start scanning.
2. Click **Detect Section Box Content**. Models intersecting the box are checked automatically. Uncheck models to exclude them; double-click a model to inspect its detected elements.
3. After adjusting the box, use **Refresh Content**. It reads the adjusted box, reuses applicable detection results, discovers new contents and preserves model exclusions. Export is unavailable until the detected contents match the selected box.
4. Choose OBJ, IFC4, NWC or NWD. Estimates appear below the format buttons after detection. Export progress includes the active task and a live estimate. Estimates are approximate and depend on geometry complexity and native conversion startup.

All formats permanently clip geometry at the box boundary, including rotated boxes. Cut ends remain open. Coordinates and colours are retained; original CAD properties and textures are omitted. The source document is not overwritten.

OBJ is a geometry delivery option. Extract the ZIP and keep the MTL next to the OBJ. Import units are metres. Confirm coordinates and alignment in the recipient's tool before coordination. IFC includes coordination identifiers. NWC/NWD are converted from the clipped IFC using a separate Navisworks process and can take longer. NWD uses 2026 format; backward compatibility of a 2027-generated NWC is unverified.

Close Navisworks before installing. The generic bundle installs separately from company editions and uses its own settings and Saved Viewpoint prefix. The package currently includes only 2026/2027; 2024/2025 are skipped pending matching installed APIs and runtime verification.
