# 0.3.2 beta

- Enabled 2nd Chapter installation for the inspected Steam 1.3.2.0 executable with an edition-specific loose-file package. The user selects the 2nd-specific sora2looseload DLL once; the studio then backs up and copies only that DLL and the generated MDL. The 1st Chapter ED9Loader files are never copied into 2nd.
- Added an isolated 2nd install/restore check that covers file hashes, proxy conflicts, interruption rollback, unsupported executables and unchanged PAC files. In-game model appearance and animation still require a user run.
- Replaced the full-panel image texture shown during extraction with a model loading state, elapsed time and activity indicator. Superseded previews are cancelled, and model processing has a four-minute limit with a local log.

# 0.3.1 beta

- Renamed the player executable and ZIP to SkyCharacterStudio so their filenames cover both Chapters.
- Aligned reused mesh groups in the live preview through their bone bind matrices and skin weights; the reported child Estelle hair now sits on the face.
- Distinguished definition labels from scene aliases and disclosed reused part IDs. Expanded sourced minor status, explicit child age, and identity-unverified status in the 2nd metadata.
- Added regression checks for the reported models and an installed-game preview alignment audit. 2nd Chapter game loading remains unverified.

# 0.3.0 beta

- Added 2nd Chapter FPAC scanning, MDL v4/v5 preview and width export, including LZ4 image decoding and flat chr-prefixed models.
- Added a separate 2nd Chapter adult eligibility catalog and per-game model, portrait, and export caches. Unknown ages remain ineligible for chest editing.
- Kept 2nd Chapter installation and F8 disabled while the game-specific loader is unverified. Its button saves an offline MDL under `exports/second` and shows the output path.
- Added an installed-game MDL round-trip audit and an isolated WPF scan, preview, portrait, export, and installer-gate check. Neither writes to the actual game.

# 0.2.2 beta

- Made F8 test summon optional throughout model generation: a missing script or summon builder failure now disables the summon and installs the generated model normally on supported game builds.
- Unsupported executables and a running game can still produce a local model file; installation remains blocked, and the UI displays the output path and reason.
- Added an isolated missing-script fixture and end-to-end checks for fallback installation and offline-only export in the portable release smoke test.

# 0.2.1 beta

- Fixed an install failure after clean ZIP extraction: create the generated model directory before replacing or removing an existing model.
- The one-click release build now tests the final compressed artifact from a separate extraction directory before promoting it to a player ZIP.

# 0.2.0 beta (source-only candidate; not a player release)

- Added a Chinese/English UI switch in the header; Chinese remains the default and the choice is remembered per user.
- Localized scan/install/restore status, model metadata, age and bone-detection details, contour preview labels, comparison images and generated package notes.
- Added English character-name mappings for the main cast and safe `Character chrXXXX` fallbacks for unknown Chinese resource names.
- Added language coverage alongside installer diagnostics and a left/right deformation correction.
- Corrected the mirrored left/right deformation frame so gravity shaping uses the same up direction on both sides.
- Check game compatibility and optional summon script files before model generation; missing files now have actionable paths.
- Renamed the install button “Save & apply to game” and clarified the English quick start.
- Corrected the player package age catalog to match the reviewed source; unlisted ages no longer default to adult chest eligibility.
- Added the locally inspected CLE Steam 1.0.7.0 executable hash after offline model, script and installer checks. In-game validation for 1.0.7.0 remains open.

# 0.1.3 beta

- Runtime-only player package for Nexus/manual installation; development source and native build inputs are no longer copied into the portable ZIP.
- Updated release metadata and player documentation to match the current generic chest deformation and support-load behavior.
- Retained the same verified CLE Steam 1.0.5.0 compatibility guard and offline validation suite.

# 0.1.0 beta

- Automatic game-resource scan and model/texture preview.
- Overall width and adult-character chest/torso adjustment, with skeletal and skinning-surface detection.
- Matching preview/export positions and normals; model-adaptive displacement bounds.
- Game-folder selection, installation backups and undo-last-install support.
- Optional F8 selected-character summon and F9 original/edited comparison, disabled by default.
- “还原原模型” button beside the strength slider resets the current preview to 0% without installing anything.
- Bundled Windows x64 .NET and Python runtimes; no extracted game assets.
- Install compatibility restricted to the tested CLE Steam executable 1.0.5.0.
- Unlisted characters scanned from the original game model archive now default to adult eligibility; explicit minor records still take priority.
- Added an embedded application icon from the supplied artwork; the portable package launches directly through Sky1stCharacterStudio.exe.
- Reworked the generic chest deformation with oriented lobe falloff, front-depth progression, smoothed skinning masks and a softened extreme-strength response.
