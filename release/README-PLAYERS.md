# Sky 1st Character Studio — 0.1.0 beta

Windows x64 character shape editor for the Clouded Leopard Entertainment Steam edition of Trails in the Sky the 1st, executable version 1.0.5.0. Other editions/builds are not verified; installation checks the executable fingerprint and stops on a mismatch.

## 使用方法

1. 解压整个压缩包到一个你有写入权限的文件夹，双击 Sky1stCharacterStudio.exe。不需要另装 Python 或 .NET。
2. 选择包含 sora_1st.exe 的游戏目录，扫描并选择角色。原始 PAC 只读。
3. 选择整体宽度或胸部／胸廓调整，拖动滑条查看实际模型；需要回到原模型时点击滑条旁的“还原原模型”（强度 0%）。胸部调整仅对已有成年资料的角色开放，表面估计定位会明确标注。
4. 退出游戏后点“应用到游戏”。默认关闭测试召唤，角色正常出场时使用修改模型。
5. 如需快速测试，安装前勾选“启用 F8 测试召唤”；进入能自由移动的场景按 F8。F9 切换原版／修改版，再按 F8 刷新。召唤的是临时角色，不会加入队伍。

## 恢复

退出游戏，在工具中选择相同游戏目录，点“撤销上次安装”。它恢复的是该次安装前的文件；多次安装可以依次撤销。请保留工具目录中的 install-backups。检测到其他程序改过已安装文件时会停止恢复，防止覆盖其他 Mod 的修改。

## English quick start

Extract the entire archive into a writable folder and run Sky1stCharacterStudio.exe. The UI is currently Chinese. Select your game folder, scan, select a character and adjustment mode, and move the slider. Close the game before applying. Optional F8 test summons are disabled by default. Enable the checkbox before applying if needed; F8 creates the installed character in a free-roaming field scene, and F9 followed by F8 switches original/edited appearances. The restore button undoes the latest installation; retain install-backups.

## Scope and known limitations

- 54 adult models passed offline detection, WPF preview/export comparison and 432 export round trips at four strengths. This does not certify every in-game animation or costume collision.
- Scherazard and Julia have user-confirmed in-game tests. Generic summons for every other model have script-level checks, not complete in-game coverage.
- Estimated chest/torso locations are inferred from skeleton landmarks, skinning weights and nearby surfaces. Shape intensity adapts to each model to reduce local inversions.
- Some preview materials differ from the game renderer, especially transparency and special shaders. The UI is Chinese-only in this beta.
- Loader/proxy conflicts with other mods are possible. Installations make backups; do not remove them before restoring.
- No original game models, textures or scripts are distributed. The tool reads the player's own installed game and generates local outputs.

Credits and component licenses: THIRD-PARTY-NOTICES.md, licenses/, tools/vendor/LICENSE, runtime-source/vendor/ed9modmanager/LICENSE, and runtime/python/.
