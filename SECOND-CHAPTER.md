# Trails in the Sky 2nd Chapter compatibility

This branch adds **offline** support for the Clouded Leopard Entertainment Steam build checked on 2026-09-27: Steam build 25386012, `sora_2nd.exe` file version 1.3.2.0, SHA-256 `D8B2911D1576216BDC22D070550E4F531E105DE7ED2981885849669F4ACF8AAF`.

## Verified on the installed game

- The FPAC scanner found 170 chr-prefixed models. The read-only audit exported every model at width 100%, reparsed the result, and checked finite positions, triangle indices, skin-weight sums, material order, mesh counts, and unchanged model size. All 170 passed: 88 MDL v4 and 82 MDL v5. Three flat planes initially failed a height assumption; width editing now uses their nonzero axis span. See `smoke/second_assets.py`.
- The separate 2nd age catalog contains 31 adults and 7 minors with stated evidence; `chr5345` is explicitly labeled 11 years old in the game's name table. `chr5710` is labeled as a bridal outfit but reuses `chr5500` face and hair parts, so its identity and age remain unverified. Exact 2nd ages were not inferred from 1st data. Unverified IDs cannot use chest editing. Each of the 31 adults passed chest export at strengths −500 and +1000 with round-trip, topology, finite-value, Jacobian, and face-direction checks. See `smoke/second_chest.py`.
- The first visual report exposed a missing bind-pose transform: `chr5344` hair floated above the child face. The preview now skins each visible mesh group from its own bind matrices into the model skeleton, including weighted facial and hair bones. `smoke/second_preview_alignment.py` audits all 170 models, and the WPF regression reopens the reported `chr5344` and `chr5710` inputs. Diagnostic captures are kept outside the release package.
- An isolated WPF check scanned the 170 models, previewed v4 and v5 models, decoded a LZ4 portrait, exported an MDL locally, and confirmed the installation guard rejected 2nd. The 2nd executable and selected model hashes, PAC sizes/times, and game-root XInput proxy state did not change. See `smoke/SecondChapterCheck.cs`.
- The 1st Chapter `chr5002` model still passed width preview and export with the updated Python tools.

To rerun the model audits, set `SKY_STUDIO_TOOLCHAIN` to a local checkout containing Kuro MDL Tool, then run the two scripts with `--game` pointing to the installed 2nd directory and `--report` pointing to a writable local JSONL path. The WPF check also requires `SKY2ND_GAME_ROOT`. Tests write their outputs under the studio workspace; they do not write to the game.

## Game installation remains disabled

The bundled ED9Loader, EventStarter, and SceneRedirect components target specific 1st Chapter executable addresses. They must not be used in `sora_2nd.exe`. This branch never installs its generated 2nd model, loader, or F8 script to the game directory. The 2nd button saves a loose MDL at `exports/second/<model>_<mode>_<strength>/asset/common/model/<model>.mdl`.

The separate [sora2looseload project](https://github.com/lmaple0/sora2looseload) documents a 2nd-specific loose-file loader and static checks for build 25386012. Its maintainer still requests a fresh in-game smoke test for this build. We have not integrated or tested that loader with this studio's generated MDL. No game executable or third-party mod resources are included in this branch's tests or reports.

**Remaining game check:** with an independently verified 2nd loader and a backup/removal plan, test one generated model in a free-roaming scene, inspect its materials and animations, and confirm that removing the loose file restores the original. Additional costumes and extreme slider values need representative in-game checks. Structural checks above do not establish these results.
