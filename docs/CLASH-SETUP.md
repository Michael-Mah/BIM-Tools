# Clash Setup

Open a federated model in Navisworks Manage and select **BIM Tools → Clash Setup**. The tool reads the model files currently loaded in that document; it does not use a company-specific model list.

For a new setup, create a name, then check the models for Selection A and Selection B. A model may be used on either or both sides. Choose the clash type and tolerance, then select **Save Setup**. The plugin stores model identities and side assignments under `%LOCALAPPDATA%/BIM-Tools/ClashProfiles`; it does not copy model geometry or clash results. Export/import setup XML when another user needs the same selection recipe.

When the same NWF is reopened, the most recently saved setup for that document is selected automatically. Click **Restore Clash Group** to reread loaded model files and restore the native Clash Detective test's selections. If the native test was removed, the tool creates it again. Existing native test results, groups and statuses are preserved when the test still exists. Restoring selections does not run clash detection; use **Run Clash** when you want Navisworks to calculate results.

If a saved model is absent, its selection remains stored and the restore is blocked until you load it, remove it from A/B, or resolve a replacement. A changed filename is compared with loaded names and source/container information. The candidate list shows likely matches as suggestions only. Select the intended replacement and confirm it; the mapping is saved when you save or restore the setup. No renamed file is substituted automatically.

**Refresh Models** rereads the current Navisworks document and rebinds saved selections where a unique name/source match exists. It does not run clash detection. Use **Clear Selections** to remove the checked assignments in the current draft.

The optional Section Box setting narrows which elements enter the native test and filters results by clash centre. Navisworks Clash Detective tests whole elements, so this setting does not geometrically cut elements at the box boundary. Section Box setup is independent of the remembered model identities.

The setup is stored per Windows user on this computer. XML export/import carries the selection recipe to another workstation. Files must still be loaded in Navisworks before the setup can be restored.
