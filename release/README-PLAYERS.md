# Sky Character Studio — 0.3.3 beta

Windows x64 character shape editor for the Clouded Leopard Entertainment Steam editions of Trails in the Sky the 1st and Trails in the Sky 2nd Chapter. Installation is limited to inspected executable hashes: 1st 1.0.5.0/1.0.7.0 and 2nd 1.3.2.0 (Steam build 25386012). The studio backs up installed files and supports Undo last installation for both games. The 2nd Chapter loader and generated model still need an in-game appearance/animation check.

## 使用方法

1. 解压整个压缩包到一个你有写入权限的文件夹，双击 SkyCharacterStudio.exe。不需要另装 Python 或 .NET。
2. 选择包含 sora_1st.exe 或 sora_2nd.exe 的游戏目录，扫描并选择角色。原始 PAC 只读。
3. 选择整体宽度或胸部／胸廓调整，拖动滑条查看实际模型；需要回到原模型时点击滑条旁的“还原原模型”（强度 0%）。胸部调整仅对年龄目录已确认成年的角色开放；未成年、身份或年龄未核实的模型仍可调整整体宽度。2nd 的资源名称可能是场景称呼，也可能复用其他编号的部件，请结合预览和详情核对。表面估计定位会明确标注。
4. 目录旁会显示当前目标是 the 1st 还是 2nd Chapter；换目录后需重新扫描。退出游戏后点“保存并应用到游戏”。完整便携包已包含分别核对的 1st 和 2nd 加载器，会按游戏自动选择；无需另行下载或选择 DLL。2nd 只安装专用加载器和当前模型，不会安装 1st 的 ED9Loader。若游戏版本未通过校验，只生成本地模型并显示路径。2nd 的模型输出也保存在 `exports/second`。
5. F8 测试召唤仅适用于 1st。安装前勾选后，可以在自由移动场景按 F8，按 F9 切换原版／修改版再按 F8 刷新。2nd 中该选项禁用。2nd 安装后请重启游戏，在角色正常出场时检查外观和动画。

## 恢复

安装后，退出游戏并在工具中选择相同目录，点“撤销上次安装”。它恢复该次安装前的文件；多次安装可以依次撤销。请保留 install-backups。检测到其他程序改过已安装文件时会停止恢复。

## English quick start

Extract the archive into a writable folder and run SkyCharacterStudio.exe. Switch the UI to English in the upper-right corner if desired. Select the 1st or 2nd Chapter game folder; the target edition appears beside the folder label. Scan, choose a character and adjustment mode, then move the slider. On inspected builds, **Save & apply to game** installs the generated model after the game is closed. Both edition-specific loaders are included and selected automatically, so no separate DLL download is needed. Undo last installation works for both games. Optional F8 testing applies only to the 1st Chapter. A 2nd Chapter in-game appearance and animation check remains open.

## Scope and known limitations

- 54 adult models passed offline detection, WPF preview/export comparison and 432 export round trips at four strengths. This does not certify every in-game animation or costume collision.
- Scherazard and Julia have user-confirmed in-game tests. Generic summons for every other model have script-level checks, not complete in-game coverage.
- Estimated chest/torso locations are inferred from skeleton landmarks, skinning weights and nearby surfaces. The deformation uses an oriented elliptical region, inner-edge falloff, front-surface progression and smoothed skinning masks rather than a rigid scale. Values beyond ±100% use a softened response and model-specific safety limits to reduce local inversions.
- Some preview materials differ from the game renderer, especially transparency and special shaders. English covers the main editor workflow; a few low-level tool diagnostics may still include source-process text.
- Loader/proxy conflicts with other mods are possible. Installations make backups; do not remove them before restoring.
- No original game models, textures or scripts are distributed. The tool reads the player's own installed game and generates local outputs.
- The installed 2nd Chapter Steam build 25386012 (sora_2nd.exe 1.3.2.0) passed offline width export and round-trip checks for all 170 scanned models (88 v4, 82 v5), plus 62 extreme-strength chest exports across 31 catalogued adults. WPF scan, v4/v5 preview, loading state, local export, and isolated install/rollback checks passed. This is structural and installer evidence; no generated 2nd Chapter model has been visually checked in game through this tool.
- The 2nd Chapter preview now aligns reused parts through the mesh groups' bind skeletons. The reported `chr5344` hair offset is corrected in an offscreen WPF capture; `chr5710` visibly reuses `chr5500` parts and its identity/age are not verified. Resource names should not be treated as proof of identity.
- The application icon uses the artwork supplied for this build by the project owner. Confirm that you have permission to redistribute it before a public upload; if the artwork is AI-generated, disclose that in the Nexus form. It is not an official Falcom or Clouded Leopard Entertainment icon.

Credits and component licenses: THIRD-PARTY-NOTICES.md, licenses/, tools/vendor/LICENSE, and runtime/python/. The player ZIP contains runtime files only; project/build source is kept in the source repository.
