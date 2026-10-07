# Sky Character Studio — 0.3.18 beta

Windows x64 character shape editor for the Clouded Leopard Entertainment Steam editions of Trails in the Sky the 1st and Trails in the Sky 2nd Chapter. The 2nd Chapter installer checks the executable and model resources without an exact game EXE hash gate; the 1st Chapter runtime selects components by inspected EXE hash. The studio backs up replaced files and supports Undo last installation for both games. A completed installation means the file bytes were written and checked; in-game appearance and animation must be checked separately.

## 使用方法

1. 解压整个压缩包到一个你有写入权限的文件夹，双击 SkyCharacterStudio.exe。不需要另装 Python 或 .NET。
   打包脚本已按资源管理器默认的同名文件夹解压路径检查长度。若将 ZIP 移到更深的目录后遇到 Windows“路径太长”，请选择较浅的解压位置。
2. 选择包含 sora_1st.exe 或 sora_2nd.exe 的游戏目录，扫描并选择角色。工作台会读取 `pac/steam` 中的 PAC，也会读取 `asset/common/model` 和贴图目录中的散装资源；同路径散装文件优先。原始游戏文件保持只读。
3. 选择整体宽度或胸部／胸廓调整，拖动滑条查看实际模型；需要回到原模型时点击滑条旁的“还原原模型”（强度 0%）。胸部调整对年龄目录已确认成年的角色、以及目录未登记的模型（按默认成年处理，界面标注“年龄目录未登记 · 默认成年”）开放；目录已确认的未成年角色只可使用整体宽度。服装模型还须由游戏名称表的定义行确认属于该角色，并通过骨骼检测。2nd 的资源名称可能是场景称呼，也可能复用其他编号的部件，请结合预览和详情核对。表面估计定位会明确标注。
   同一角色的服装按独立模型编号显示。2nd 的下拉列表会从当前游戏的服装表和物品表读取对应的游戏内服装名称；“（游戏服装）”表示找到了唯一对应，“（资源名称）”表示没有找到唯一的游戏内服装名称。列表始终保留 `chr` 编号，安装结果也会显示具体的 MDL 文件。游戏只有在使用该服装模型时才会显示相应改动；安装一个编号不会自动修改其他服装。
   如果 2nd 的游戏目录中已有加载器日志，扫描后选择同一角色时会显示上次游戏过程实际请求的模型，并可点击跳转到那个编号。提示附有日志时间，只代表那次游戏过程；工作台不会自动开启日志，也不会在没有日志时猜测当前服装。
4. 目录旁会显示当前目标是 the 1st 还是 2nd Chapter；换目录后需重新扫描。退出游戏后点“保存并应用到游戏”。2nd 会检查 64 位游戏程序和角色模型资源，不按 EXE 版本号或哈希限制安装；1st 只允许已核对哈希的 1.0.5.0／1.0.7.0，并为两版选择不同的模型重定向组件。完整便携包已包含两款游戏的加载器，会按游戏自动选择；无需另行下载或选择 DLL。2nd 只安装专用加载器和当前模型，不会安装 1st 的 ED9Loader。安装前会备份将被替换的文件；若目录或运行组件检查失败，只生成本地模型并显示路径。2nd 的模型输出也保存在 `exports/second`。扫描、预览和导出均在本机完成，首次及之后使用都不需要联网。
   若安装时 Windows 拒绝写入，界面会保留原始错误与备份位置，并提示先退出游戏、关闭工作台，再尝试右键以管理员身份运行。若提示回滚不完整，请先处理备份与未恢复的文件，不要直接重复安装。
5. 普通 1st 模型安装不附带 F8 召唤组件。对 1st 1.0.7.0，安装时会识别并备份、停用旧工作台留下的两个已知不兼容插件；撤销安装会恢复原文件。F8 测试召唤目前仅在已核对的 1st 1.0.5.0 开放；安装前勾选后，可以在自由移动场景按 F8，按 F9 切换原版／修改版再按 F8 刷新。1st 1.0.7.0 与 2nd 中该选项禁用。安装后请重启游戏，在角色正常出场时检查外观和动画。
   如果你以前单独安装过 `ScherazardSummon`，其 `BracerMentorHud.dll` 仍可能显示“模型召唤终端”。普通工作台安装不会再加入这个 HUD，也不会自动移除独立的旧模组。要关闭旧终端，请先退出游戏，再禁用 `ED9Loader/plugins/BracerMentorHud.dll`；若不再使用旧召唤模组，也在模组管理器中禁用 `ScherazardSummon`。保留工作台所需的 `xinput1_4.dll` 和 ED9Loader。

## 恢复

安装后，退出游戏并在工具中选择相同目录，点“撤销上次安装”。它恢复该次安装前的文件；多次安装可以依次撤销。请保留 install-backups。检测到其他程序改过已安装文件时会停止恢复。

## English quick start

Extract the archive into a writable folder and run SkyCharacterStudio.exe. Switch the UI to English in the upper-right corner if desired. Select the 1st or 2nd Chapter game folder; the target edition appears beside the folder label. Scan, choose a character and adjustment mode, then move the slider. The studio reads model PAC archives and loose game-relative MDL/DDS files; a loose file at the same path takes priority. If the original PAC has been renamed, a single readable matching archive can still be used. Scanning, previewing and exporting work entirely offline, including the first run. After closing the game, **Save & apply to game** backs up replaced files. For 2nd Chapter, it checks the x64 executable and character resources without requiring a particular game EXE hash; the 1st Chapter runtime selects components for inspected 1.0.5.0 or 1.0.7.0 executable hashes. Both edition-specific loaders are included and selected automatically, so no separate DLL download is needed. Undo last installation works for both games. Ordinary 1st model installs do not include F8 summon components; on 1st 1.0.7.0, the installer also backs up and retires exact known copies of two incompatible old Studio plugins. Undo restores them. Optional F8 testing is available only for 1st 1.0.5.0. The current 1st 1.0.7.0 model redirect and 2nd 1.4.0.0 installation still need a representative in-game appearance and animation check. If preview fails, keep the status text and `cache/models/<edition>/<model>/<mode>/model-process.log` for diagnosis.

An older, separately installed `ScherazardSummon` mod can still show its summon terminal through `ED9Loader/plugins/BracerMentorHud.dll`. A normal Studio model install does not add that HUD or remove the separate mod. To hide the old terminal, close the game and disable that plugin; also disable `ScherazardSummon` in the mod manager if you no longer use it. Keep the loader and XInput proxy needed for ordinary model installs.

The package is checked at the default Windows Explorer extraction path. If you move the ZIP into a much deeper folder and Windows reports a path-length error, choose a shallower extraction destination.

If Windows denies a write during installation, the studio keeps the original error and backup path and suggests closing the game and retrying after launching the studio as administrator. If rollback is incomplete, inspect the backup and unrecovered files before retrying.

Costumes are separate model IDs. In 2nd Chapter, the selector reads outfit names from the installed game's costume and item tables; "(in-game outfit)" identifies a direct match, while "(resource name)" means no unique outfit item was found. The `chr` ID stays visible, and the installation message names the exact MDL file. Installing one ID does not alter the others, and only appears when the game uses that outfit model. Chest editing is available for catalogued adults and for models the catalog does not list, which are treated as defaulted adults and labelled "Not listed in the age catalog · adult by default"; catalogued minors stay width-only. A costume inherits a character's age status only when a game definition row identifies that catalogued character and the model passes bone detection. A resource label does not establish which outfit a particular scene uses.

If a 2nd Chapter loader log already exists in the game folder, the selector offers a shortcut to the model that the last logged game session requested for that character. The tooltip shows the log time. This is an observation from that session, not a promise that another save or scene uses the same outfit. The studio does not enable logging automatically or guess an active outfit without a log.

## Scope and known limitations

- 54 adult models passed offline detection, WPF preview/export comparison and 432 export round trips at four strengths. This does not certify every in-game animation or costume collision.
- Earlier Scherazard and Julia builds had user-confirmed in-game tests on older game versions. This build's 1st 1.0.7.0 redirect and F8 behavior have not been confirmed in game.
- Estimated chest/torso locations are inferred from skeleton landmarks, skinning weights and nearby surfaces. The deformation uses an oriented elliptical region, inner-edge falloff, front-surface progression and smoothed skinning masks rather than a rigid scale. Values beyond ±100% use a softened response and model-specific safety limits to reduce local inversions.
- Some preview materials differ from the game renderer, especially transparency and special shaders. English covers the main editor workflow; a few low-level tool diagnostics may still include source-process text.
- Loader/proxy conflicts with other mods are possible. Installations make backups; do not remove them before restoring.
- No original game models, textures or scripts are distributed. The tool reads the player's own installed game and generates local outputs.
- The installed 2nd Chapter Steam build 25386012 (sora_2nd.exe 1.3.2.0) passed the earlier offline width export and round-trip audit for 170 chr-prefixed models (88 v4, 82 v5), plus 62 extreme-strength chest exports across 31 catalogued adults. The current scanner lists 169 base and 301 complete costume models; all 301 costume models passed a separate structural width-export and round-trip audit (141 v4, 160 v5). WPF scan, both Estelle outfit previews, loading state, local export, and isolated install/rollback checks passed. This is structural and installer evidence; no generated 2nd Chapter model has been visually checked in game through this tool.
- The 2nd Chapter preview now aligns reused parts through the mesh groups' bind skeletons. The reported `chr5344` hair offset is corrected in an offscreen WPF capture; `chr5710` visibly reuses `chr5500` parts and its identity/age are not verified. Resource names should not be treated as proof of identity.
- Loose-model, renamed-PAC and PAC-plus-loose override layouts passed isolated scan and preview checks with the installed 1.3.2.0 game assets. Loose DDS override and 2nd Chapter installation/restore with a changed but valid x64 executable hash passed in isolated copies. This removes the 2nd version gate; it does not establish that the reported 1.03.1 build works in game, because that build was unavailable for direct testing.
- The application icon uses the artwork supplied for this build by the project owner. Confirm that you have permission to redistribute it before a public upload; if the artwork is AI-generated, disclose that in the Nexus form. It is not an official Falcom or Clouded Leopard Entertainment icon.

Credits and component licenses: THIRD-PARTY-NOTICES.md, licenses/, tools/vendor/LICENSE, and runtime/python/. The player ZIP contains runtime files only; project/build source is kept in the source repository.
