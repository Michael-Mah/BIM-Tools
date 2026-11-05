# BIM Tools 1.2.2

Prepared on 2026-11-05. Complete-list display names follow the associated model filename, including older lists with unrelated custom labels. The selector filters by the open model identity; it is synchronized when reopening the tool and before a manual check. Other models' lists are excluded instead of offering a cross-model comparison prompt. Source-file expectations remain unchanged.

Both 2026 and 2027 release builds pass against matching installed APIs. All 49 comparison/persistence checks pass. Tests include model filename labels, historical lists restricted to the current identity and an empty selector for an unregistered model. All 32 packaged ribbon image references pass.

The Manage 2027 native workflow passes source discovery, all three window sizes, NWF reopen, NWD source discovery, renamed/moved sources, missing-source detection, switching between registered/unregistered documents and opt-out without changing saved expectations. Manage 2026 was compiled; its native automation attempt did not finish and was stopped. No new claim is made for a completed 2026 native workflow in this patch.

The layout harness checks client sizes 1100×760, 850×580 and 760×540, and independently stresses 9, 11.25, 13.5 and 18 pt fonts at 1100×800. Button heights, toolbar wrapping and watched-view toggles pass. This font test is a local stress test, not a claim to have tested every physical display or Windows DPI setting. Native validation is recorded separately on completion.

Review-mapping height follows its preferred text size. Metadata checkbox width and table headings follow the host font. Save-list fields use content layout, and watched-view buttons resize to text. Saved timestamps are displayed in local readable form. The installer is staged and its 71 files are checked against the release. The existing user-opened Navisworks session remains untouched; installation awaits closure.
