# Trails in the Sky 2nd Chapter compatibility

This branch adds **offline** support for the Clouded Leopard Entertainment Steam build checked on 2026-09-27: Steam build 25386012, `sora_2nd.exe` file version 1.3.2.0, SHA-256 `D8B2911D1576216BDC22D070550E4F531E105DE7ED2981885849669F4ACF8AAF`.

## Verified on the installed game

- The FPAC scanner found 170 chr-prefixed models. The read-only audit exported every model at width 100%, reparsed the result, and checked finite positions, triangle indices, skin-weight sums, material order, mesh counts, and unchanged model size. All 170 passed: 88 MDL v4 and 82 MDL v5. Three flat planes initially failed a height assumption; width editing now uses their nonzero axis span. See `smoke/second_assets.py`.
- The separate 2nd age catalog contains 31 adults and 7 minors with stated evidence; `chr5345` is explicitly labeled 11 years old in the game's name table. `chr5710` is labeled as a bridal outfit but reuses `chr5500` face and hair parts, so its identity and age remain unverified. Exact 2nd ages were not inferred from 1st data. Unverified IDs cannot use chest editing. Each of the 31 adults passed chest export at strengths −500 and +1000 with round-trip, topology, finite-value, Jacobian, and face-direction checks. See `smoke/second_chest.py`.
- The first visual report exposed a missing bind-pose transform: `chr5344` hair floated above the child face. The preview now skins each visible mesh group from its own bind matrices into the model skeleton, including weighted facial and hair bones. `smoke/second_preview_alignment.py` audits all 170 models, and the WPF regression reopens the reported `chr5344` and `chr5710` inputs. Diagnostic captures are kept outside the release package.
- An isolated WPF check scanned the 170 models, previewed v4 and v5 models, exercised the loading state, and exported an MDL locally. The 2nd executable and selected model hashes, PAC sizes/times, and game-root XInput proxy state did not change. See `smoke/SecondChapterCheck.cs`.
- The 2nd installer now creates a package containing exactly `xinput1_4.dll` from sora2looseload and one generated `asset/common/model/<id>.mdl`. It rejects a different existing proxy, backs up replaced files, hashes installed bytes, rolls back an interrupted copy, and supports undo. These cases passed against an isolated copy of the 2nd executable and a generated model; the actual game directory was not written during this test. See `smoke/SecondInstallCheck.cs`.
- The 1st Chapter `chr5002` model still passed width preview and export with the updated Python tools.

To rerun the model audits, set `SKY_STUDIO_TOOLCHAIN` to a local checkout containing Kuro MDL Tool, then run the two scripts with `--game` pointing to the installed 2nd directory and `--report` pointing to a writable local JSONL path. The WPF check also requires `SKY2ND_GAME_ROOT`. Tests write their outputs under the studio workspace; they do not write to the game.

## Game installation

The bundled ED9Loader, EventStarter, and SceneRedirect components target specific 1st Chapter executable addresses. The 2nd path uses the separate sora2looseload XInput proxy and installs only a generated loose MDL at `asset/common/model/<id>.mdl`. The game EXE must match the inspected 1.3.2.0 hash. On first use the player selects the 2nd-specific DLL; the studio checks its PE architecture, XInput ordinal exports, loader markers, and the inspected 49,664-byte SHA-256 `E08A18068A482BB5D187A62023759C0E14AB69D76395B773EF0405D35E2AC8C7` before caching it. A different game-root XInput DLL is never overwritten. The original PAC and EXE stay untouched.

The [sora2looseload project](https://github.com/lmaple0/sora2looseload) documents static checks for build 25386012. Its v1.1.2 notes report that the affected Steam 1.3.2 system started normally with the proxy. This studio's generated MDL has not yet been checked visually in game. No game executable or third-party mod resources are included in source control or reports.

**Remaining game check:** install one generated model into the actual game, restart it, inspect the model's materials and animations in a free-roaming scene, and confirm that Undo restores the original. Additional costumes and extreme slider values need representative in-game checks. The isolated tests above do not establish these results.
