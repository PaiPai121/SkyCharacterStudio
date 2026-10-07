# 0.3.18 beta

- Prepare a clean-source, reproducible player package for the combined 1st/2nd Chapter studio. The release archive contains no extracted game models, textures, scripts, local exports, or backups.
- Clarify a migration limit: an independently installed older `ScherazardSummon` mod can remain enabled, and its `BracerMentorHud.dll` still displays the summon terminal. An ordinary Studio model install does not add this HUD or remove the separate mod. Disable the older HUD/mod separately if it is unwanted.
- The 1st 1.0.7.0 model redirect and the 2nd Chapter modified costume's appearance and animation still need representative in-game checks. Offline installation and package checks do not establish visual compatibility.

# 0.3.17 beta

- Fix Windows Explorer error `0x80010135` when extracting a local preview from the project's `release-artifacts/local-previews` folder. The old ZIP stem made the bundled OpenBLAS DLL's default destination path 277 characters long.
- Use a shorter, unique `SkyCharacterStudio-<version>-local-<id>.zip` name for local previews. Keep the complete beta version and source state in the release manifest. Build QA now checks every ZIP entry against Explorer's default same-name extraction directory, requires the longest path to stay below 240 characters, and runs its clean-extraction tests in that directory.
- No game resources or user game directories are changed by this packaging fix. In-game model replacement remains to be verified separately.

# 0.3.16 beta

- Read the installed 2nd Chapter model PAC and preview the exact `chr5000_c00` model requested in the observed prologue session. The model parses and exports locally; the previous no-effect report involved installs to `chr5000` and `chr5000_c52`, neither of which that session requested.
- When a 2nd Chapter loader log already exists, identify the last complete costume-model request for each character from the latest logged session. Show its exact model ID beside the selector and offer a one-click jump, even when the list is filtered to another costume. Label the log time and do not infer an active outfit when no log is available.
- Check log-session reset, animation-name exclusion, filtered-selector navigation, actual `c00` preview, and isolated install/Undo. Real-game appearance after installing the matching slot remains unverified.

# 0.3.15 beta

- Retire byte-identified legacy `ScriptInject.dll` and `EventStarter.dll` when installing into the inspected 1st 1.0.7.0 build. Back up their exact bytes, remove them within the installation transaction, and restore them on Undo. Leave differently hashed third-party plugins untouched.
- Reproduce the reported 1st crash from its minidump: the old ScriptInject plugin hooks a location that is no longer a valid function entry on 1.0.7.0. This release fixes the old-plugin migration in isolated copies; an in-game retry is still required.

# 0.3.14 beta

- Stage ordinary 1st model edits as their own `SkyCharacterStudio` mod. Unchecked F8 exports no longer include the summon DLLs or script mod; a previous Studio summon selection is explicitly disabled during migration.
- Use a unique game file-open signature to redirect 1st 1.0.7 merged assets. Install it under a separate plugin name so existing mods are not overwritten; keep the original SceneRedirect plugin for the verified 1.0.5 path. F8 stays unavailable on 1.0.7 until its address-dependent plugin has an in-game check.
- Check whether the selected game directory itself is running before installation. A same-named game process launched from another path no longer blocks installation into an isolated copy.
- State that a successful file copy has not verified the in-game appearance. Runtime testing of both current game builds remains required.

# 0.3.13 beta

- When an installation fails because Windows denies a file write, keep the original error and backup path visible and suggest retrying after closing the game with the studio run as administrator. Other failure types retain their specific messages; an incomplete rollback is not presented as a simple retry.
- Reproduce denied writes to the 1st loader and a 2nd model in isolated game copies, and check the on-screen advice and rollback without changing the installed games.

# 0.3.12 beta

- Label 2nd Chapter models with the matching in-game outfit name by joining the installed game's `t_costume.tbl` model entry to its localized `t_item.tbl` item name. Keep the `chr` model ID visible; resources without a unique outfit item remain marked as resource names.
- Show the selected outfit and exact MDL filename after a 2nd Chapter installation, so the result is not mistaken for a change to every costume of that character.
- Check the Chinese and English item labels against the installed 2nd Chapter tables, and run isolated scan, preview, export, install, and Undo checks without writing to the real game directory. In-game appearance still needs a player check.

# 0.3.11 beta

- Remove the extra write-and-delete probe in the game root before installation. Installation now tests only the files it actually needs to update; an existing identical loader can remain untouched while a model under `asset/common/model` is updated.
- If an actual target write fails, report the underlying error and backup path after rollback instead of suggesting a sandbox or other unverified cause.
- Add an isolated 2nd Chapter regression where Windows denies new files in the game root but permits a model update in its existing model folder. Install and Undo both pass without changing real game files. The reported user's normal-launch environment still needs a retry with this build.

# 0.3.10 beta

- Reset to overall-width preview when selecting a different character or costume, then enable chest editing only if that exact model passes geometry detection. This prevents unsupported 2nd Chapter costumes such as `chr5002_c74` from inheriting the previous model's chest mode and failing to preview.
- Show the final model-processing error line in the preview pane and keep the complete traceback in `model-process.log`. Run bundled Python in UTF-8 mode so diagnostic text stays readable on Windows.
- Skip installation and Undo writes for files whose contents already match the requested state. This avoids overwriting an unchanged, write-protected loader while applying or restoring a different model. Keep backups and rollback for files that do change.
- Exclude generated cache files from the WPF project's source glob so local diagnostic builds cannot introduce duplicate assembly attributes.
- These fixes have isolated preview and installation regression checks; appearance and animation in the running games remain to be checked.

# 0.3.9 beta

- Treat the age catalogs as a deny-list of minors in both editions: a model the catalog does not list is now a "defaulted adult" and can use chest editing, labelled "年龄目录未登记 · 默认成年" / "Not listed in the age catalog · adult by default". Every catalogued minor (Estelle, Joshua, Kloe, Tita, Josette, the child models, `chr5345` at 11) stays restricted to width editing.
- Apply the same rule to a catalogued-but-unverified identity: `chr5710` is now eligible and shown as "身份与年龄未核实 · 默认成年", keeping its "the resource label does not establish the identity" basis text.
- Keep costume inheritance intact: a costume still copies a character's status only when a game definition row names that catalogued character; otherwise it falls back to the defaulted-adult rule instead of being refused.
- Change eligibility in `CharacterAgeCatalog.cs` and `tools/character_age.py` together so the studio UI, the preview and the export metadata (`adult_eligible`) always agree. The catalog JSON files are unchanged, so both reviewed catalog hashes still match.

# 0.3.8 beta

- Let a complete costume model inherit a character's age status only when the selected game's name table has a definition row identifying that character. Numeric model IDs and scene aliases alone do not establish identity.
- Pass the same definition evidence to preview and export so the UI and bundled Python tool agree on chest-edit eligibility. Adult costumes can be edited where bone detection supports it; minor and unverified costumes remain restricted to width editing.
- Add real 1st/2nd costume preview and chest-export regression checks, including rejection when the definition is missing or names someone else.

# 0.3.7 beta

- Show complete `chr####_c##` costume models as separate selectable resources instead of filtering out every underscored MDL. Animation and mesh-part MDLs remain excluded.
- Use the 2nd Chapter game's definition label when it identifies a specific outfit. `chr5000` is marked as Estelle's 1st Chapter outfit, while `chr5000_c11` is the separately selectable 2nd Chapter outfit. English labels retain this distinction.
- Add real-resource preview, round-trip export, isolated installation and restore checks for Estelle's 2nd Chapter outfit. All 301 complete 2nd Chapter costume models also pass the structural width-export audit. Whether an outfit is active in a particular scene and how an edited model behaves in game still require in-game verification.

# 0.3.6 beta

- Removed the exact game-executable hash gate for 2nd Chapter. Installation checks for an x64 Windows executable and readable character model resources; loader integrity, conflict, backup, rollback, and restore checks remain in place.
- Kept the inspected 1st Chapter hashes in a separate manifest because its plugins target executable-specific addresses. A different but valid 2nd executable hash now passes isolated install/restore checks; the 1st hash gate still rejects an altered executable.
- The reported 2nd Chapter 1.03.1 executable was not available, so its in-game loader and model behavior remain unverified.

# 0.3.5 beta

- Read loose game-relative MDL and DDS resources alongside PAC entries, with loose files taking priority. A single renamed PAC can be read when the original filename is absent.
- Pass the selected model and image sources through the preview/export pipeline, and release WPF texture file handles so a changed loose texture can be refreshed without stale cache or a locked PNG.
- Keep scanning and local export available for uninspected executable hashes while retaining the exact-hash installation gate. Add isolated regression fixtures for loose-only, renamed-PAC, model and texture overrides, and offline-only export.
- Clarify that the portable editor needs no internet connection or separate loader download. The reported 2nd Chapter 1.03.1 build and game-internal appearance remain unverified.

# 0.3.4 beta

- Rebuilt the portable package from a fresh release stage after a manually repacked 0.3.3 archive included generated cache, exports, and installation backups.
- Release ZIP creation now requires an exact match to the stage manifest and rejects user data and game-model files.

# 0.3.3 beta

- Included the inspected 2nd Chapter loader in the portable package and made release builds reject a missing or mismatched loader. 2nd installation selects the bundled loader automatically, without asking players to download a DLL.
- Added a persistent target-game indicator beside the game folder. Changing folders immediately clears the previous game's scan and preview until the new folder is scanned.
- The clean-extraction install check now uses the bundled 2nd loader, proving that the final ZIP does not depend on a developer download or cache.

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
