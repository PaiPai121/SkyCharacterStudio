# Sky 1st Character Studio — 0.2.1 beta

Windows x64 character shape editor for the Clouded Leopard Entertainment Steam edition of Trails in the Sky the 1st, executable versions 1.0.5.0 and 1.0.7.0 (specific verified file hashes). Other editions/builds are not verified; installation checks the executable fingerprint and stops on a mismatch. Version 1.0.7.0 has completed offline model and installer checks; gameplay was not exercised on that build.

## 使用方法

1. 解压整个压缩包到一个你有写入权限的文件夹，双击 Sky1stCharacterStudio.exe。不需要另装 Python 或 .NET。
2. 选择包含 sora_1st.exe 的游戏目录，扫描并选择角色。原始 PAC 只读。
3. 选择整体宽度或胸部／胸廓调整，拖动滑条查看实际模型；需要回到原模型时点击滑条旁的“还原原模型”（强度 0%）。胸部调整仅对年龄目录已确认成年的角色开放；年龄未知的角色仍可调整整体宽度。表面估计定位会明确标注。
4. 退出游戏后点“保存并应用到游戏”。这就是保存并安装按钮。默认关闭测试召唤，角色正常出场时使用修改模型。
5. 如需快速测试，安装前勾选“启用 F8 测试召唤”；进入能自由移动的场景按 F8。F9 切换原版／修改版，再按 F8 刷新。召唤的是临时角色，不会加入队伍。

## 恢复

退出游戏，在工具中选择相同游戏目录，点“撤销上次安装”。它恢复的是该次安装前的文件；多次安装可以依次撤销。请保留工具目录中的 install-backups。检测到其他程序改过已安装文件时会停止恢复，防止覆盖其他 Mod 的修改。

## English quick start

Extract the entire archive into a writable folder and run Sky1stCharacterStudio.exe. The UI opens in Chinese by default; use the language selector in the upper-right corner to switch to English. Select your game folder, scan, select a character and adjustment mode, and move the slider. Close the game, then click **Save & apply to game**; that button generates, saves and installs the model. Optional F8 test summons are disabled by default. Enable the checkbox before applying if needed; F8 creates the installed character in a free-roaming field scene, and F9 followed by F8 switches original/edited appearances. If the optional summon reports a missing script_sc.pac, check that the selected game folder is complete or disable F8 and retry. Only the verified CLE Steam 1.0.5.0/1.0.7.0 executable files can be installed to. The restore button undoes the latest installation; retain install-backups.

## Scope and known limitations

- 54 adult models passed offline detection, WPF preview/export comparison and 432 export round trips at four strengths. This does not certify every in-game animation or costume collision.
- Scherazard and Julia have user-confirmed in-game tests. Generic summons for every other model have script-level checks, not complete in-game coverage.
- Estimated chest/torso locations are inferred from skeleton landmarks, skinning weights and nearby surfaces. The deformation uses an oriented elliptical region, inner-edge falloff, front-surface progression and smoothed skinning masks rather than a rigid scale. Values beyond ±100% use a softened response and model-specific safety limits to reduce local inversions.
- Some preview materials differ from the game renderer, especially transparency and special shaders. English covers the main editor workflow; a few low-level tool diagnostics may still include source-process text.
- Loader/proxy conflicts with other mods are possible. Installations make backups; do not remove them before restoring.
- No original game models, textures or scripts are distributed. The tool reads the player's own installed game and generates local outputs.
- The application icon uses the artwork supplied for this build by the project owner. Confirm that you have permission to redistribute it before a public upload; if the artwork is AI-generated, disclose that in the Nexus form. It is not an official Falcom or Clouded Leopard Entertainment icon.

Credits and component licenses: THIRD-PARTY-NOTICES.md, licenses/, tools/vendor/LICENSE, and runtime/python/. The player ZIP contains runtime files only; project/build source is kept in the source repository.
