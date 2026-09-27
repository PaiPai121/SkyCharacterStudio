# Sky Character Studio — 0.3.0 beta

Windows x64 character shape editor for the Clouded Leopard Entertainment Steam editions of Trails in the Sky the 1st and Trails in the Sky 2nd Chapter. For the 1st, installation is limited to specific verified 1.0.5.0 and 1.0.7.0 executable hashes. The 2nd Chapter supports offline scanning, preview, and local model export; its in-game loader is not verified, so this tool does not install to the 2nd Chapter game directory.

## 使用方法

1. 解压整个压缩包到一个你有写入权限的文件夹，双击 Sky1stCharacterStudio.exe。不需要另装 Python 或 .NET。
2. 选择包含 sora_1st.exe 或 sora_2nd.exe 的游戏目录，扫描并选择角色。原始 PAC 只读。
3. 选择整体宽度或胸部／胸廓调整，拖动滑条查看实际模型；需要回到原模型时点击滑条旁的“还原原模型”（强度 0%）。胸部调整仅对年龄目录已确认成年的角色开放；年龄未知的角色仍可调整整体宽度。表面估计定位会明确标注。
4. 使用 1st 的受支持版本时，退出游戏后点“保存并应用到游戏”。如果版本未通过检查，工具只生成本地模型并显示路径。选择 2nd 时，按钮显示“保存模型到本地”，输出位于工具目录的 exports/second，游戏文件不会被修改。导出文件只是离线结果，尚未确认能在 2nd 游戏内正常加载和动画。
5. F8 测试召唤仅适用于 1st。安装前勾选后，可以在自由移动场景按 F8，按 F9 切换原版／修改版再按 F8 刷新。2nd 中该选项禁用。

## 恢复

1st 安装后，退出游戏并在工具中选择相同目录，点“撤销上次安装”。它恢复该次安装前的文件；多次安装可以依次撤销。请保留 install-backups。检测到其他程序改过已安装文件时会停止恢复。2nd 没有安装步骤，因此撤销按钮禁用。

## English quick start

Extract the archive into a writable folder and run Sky1stCharacterStudio.exe. Switch the UI to English in the upper-right corner if desired. Select the 1st or 2nd Chapter game folder, scan, choose a character and adjustment mode, then move the slider. On the verified 1st Chapter 1.0.5.0/1.0.7.0 executable files, **Save & apply to game** installs the generated model after the game is closed. Optional F8 testing and restore apply only to the 1st Chapter. For the 2nd Chapter, **Save model locally** writes an MDL under `exports/second` and never changes the game directory. The 2nd Chapter loader and in-game appearance still need validation.

## Scope and known limitations

- 54 adult models passed offline detection, WPF preview/export comparison and 432 export round trips at four strengths. This does not certify every in-game animation or costume collision.
- Scherazard and Julia have user-confirmed in-game tests. Generic summons for every other model have script-level checks, not complete in-game coverage.
- Estimated chest/torso locations are inferred from skeleton landmarks, skinning weights and nearby surfaces. The deformation uses an oriented elliptical region, inner-edge falloff, front-surface progression and smoothed skinning masks rather than a rigid scale. Values beyond ±100% use a softened response and model-specific safety limits to reduce local inversions.
- Some preview materials differ from the game renderer, especially transparency and special shaders. English covers the main editor workflow; a few low-level tool diagnostics may still include source-process text.
- Loader/proxy conflicts with other mods are possible. Installations make backups; do not remove them before restoring.
- No original game models, textures or scripts are distributed. The tool reads the player's own installed game and generates local outputs.
- The installed 2nd Chapter Steam build 25386012 (sora_2nd.exe 1.3.2.0) passed offline width export and round-trip checks for all 170 scanned models (88 v4, 82 v5), plus 62 extreme-strength chest exports across 31 catalogued adults. The WPF scan, v4/v5 preview, LZ4 portrait, and local export check passed. This is structural evidence; no 2nd Chapter model has been tested in game through this tool.
- The application icon uses the artwork supplied for this build by the project owner. Confirm that you have permission to redistribute it before a public upload; if the artwork is AI-generated, disclose that in the Nexus form. It is not an official Falcom or Clouded Leopard Entertainment icon.

Credits and component licenses: THIRD-PARTY-NOTICES.md, licenses/, tools/vendor/LICENSE, and runtime/python/. The player ZIP contains runtime files only; project/build source is kept in the source repository.
