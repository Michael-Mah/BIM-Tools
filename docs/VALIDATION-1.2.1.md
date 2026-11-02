# BIM Tools 1.2.1 — selectable watched views

Validated on 2026-11-02 against the installed Manage 2026 and 2027 APIs. Federation Audit accepts arbitrary user-registered views; no project or view names are embedded in the product.

## Completed checks

- All 44 core comparison and persistence checks pass. Added cases cover ACC view identity across display-name and folder changes, separation by account/project/view, local-file fallback, arbitrary registrations, independently enabled views, one active baseline per view, retained historical lists, opt-out, unregistered views and unsaved-document rejection.
- Full native workflows pass in Manage 2026 and 2027 with disposable IFC/NWF/NWD fixtures. Source discovery, NWF reopen, packed NWD source identities, dropped models, renamed-file suggestions and moved-source detection pass in both hosts.
- In both hosts, opening another registered document switches an already-open audit window to its own baseline and automatic results. A healthy second view does not inherit the first view's missing-model warning. An unregistered document does not use another view's expectations. Returning to an enabled view restores its own missing-model warning. Disabling one view stops its checks while another remains enabled, and all saved expectations are retained. Unchanged issues do not repeatedly notify.
- The native window paints correctly at client sizes 1100×760, 850×580 and 760×540. Wrapped action and filter rows remain visible and the details pane stays inside the viewport. A standalone harness also verifies the watched-views manager at 800×450 and 640×360 and saves checkbox changes without altering model lists.
- Release builds for both years complete without warnings. All 32 packaged ribbon icon references decode at their intended WPF sizes and 96 DPI, with Health first. The existing generated blue Federation Audit icon is retained.
- The EXE installer was extracted into staging. All 71 staged files match the release bundle by SHA256; installer checksums pass.

## Installation

The 1.2.1 package is staged for a local update. Installation is waiting for the user's running Navisworks session to close; its installed 1.2.0 bundle has not been replaced. The updater checks for running Navisworks processes, backs up the existing bundle, verifies copied files and restores the previous bundle if replacement fails.

## Live ACC observation and limits

A read-only probe of an actual user-opened ACC live view verified that Navisworks exposes an `nwacc` document address containing account, project and aggregate/view identifiers, and readable loaded-model source metadata. The probe did not change models, selection or saved baselines. It did not establish whether that federation was complete.

Controlled view-switching tests use local disposable documents. They do not claim to create, rename or delete remote ACC views. The identity-normalization tests use synthetic cloud addresses with the observed structure. The tool checks the view loaded in Navisworks and does not poll ACC or enumerate unopened remote views. Each monitored view needs a user-confirmed complete list before missing files can be identified. Exposed file identities do not prove geometry completeness, latest cloud versions or unexposed embedded contents. 2024/2025 remain skipped.

No customer project names, cloud identifiers, model files, probe reports or Autodesk SDK assemblies are included in tracked sources or installer payloads.
