# PaperTodo 自定义补丁台账（dev 分支）

> 目的：记录所有相对上游 (snownico0722/PaperTodo) 的自定义修改，让 `git rebase upstream/main` 同步时的冲突可以**按此文档机械地解决**，无需逐处确认。
>
> 维护规则：每次「功能完成+用户确认」后同步更新本文件；中间迭代不提交。rebase 冲突时先查本表，按「冲突裁决规则」处理。

## 一、修改原则（所有补丁必须遵守）

1. **新功能尽量放独立文件**（`PaperWindow.CustomPatches.cs` 等分部类），不动上游方法体；
2. **必须改上游文件时**，改动保持「单点、加粗注释锚定」——每处上游文件内的修改都带 `// Custom patch (dev branch):` 注释锚，rebase 时以此识别（锚缺失=代码债，发现即补）；
3. **字符串**优先放 `Strings.cs` 的 Supplemental 字典（四语言数组），resx 仅在需要出现在正式翻译资源时使用；
4. **禁止**重命名/移动上游符号；禁止改上游方法签名；**不留死代码**（功能撤销时其支撑设施一并删除）；
5. **数据字段**新增走 `Models.cs` 且默认值必须 `false`/`0`/空（`JsonIgnore(WhenWritingDefault)`），保证旧 data.json 无缝加载；
6. **鼠标捕获铁律**：捕获落在谁身上，谁必须有完整的 up/move/lostCapture 收尾闭环。禁止「触发者与收尾者分离」的借用（历史事故×3：窗口根捕获死锁右键、编辑框借 chrome 会话僵死、原始按下无回退难进编辑）。

## 二、当前补丁清单（基于 fork 点 411078b，实际 diff 见 `git diff 411078b..dev -- src Resources`）

| # | 功能 | 涉及文件（带锚处） | 改动性质 | 状态 |
|---|---|---|---|---|
| P1 | 常驻透明（整窗，设置→实验室「常驻透明」组） | `Models.cs`(State 2 字段+锚) `AppController.SettingsApi.Catalog.cs`(2 setting+锚) `AppController.Settings.cs`(UI+2 handler+2 FromPaper 入口+锚) `PaperWindow.Experimental.cs`(RefreshExperimentalOpacity 插 persistent 优先级分支) `Strings.cs`(5 键+锚) | 上游文件小改 | ✅ 已确认 |
| P2 | 沉浸顶栏（右键菜单切换，按纸片持久化；最大化时保持隐藏+chrome 负 Margin 上移补偿；隐藏顶栏时保留顶部窄边缘缩放） | `Models.cs`(TopBarHidden 字段+锚) `PaperWindow.ExperimentalFocusPresentation.cs`(独立方法组：Toggle/Refresh/LayoutUpdated/Extent+最大化判定) `PaperWindow.cs`(右键菜单 1 项) `PaperWindow.Capsule.cs`(折叠清理+锚、展开恢复+锚) `Strings.cs`+4 resx(MenuHideTopBar/MenuShowTopBar) | 上游文件中改 | ✅ 已确认 |
| P3 | 标题长度按全宽当量计（半角=0.5），默认/上限 32 | `PaperTitles.cs`(重写计数+EastAsianWidthCategory 新类) `PaperWindow.cs`(标题编辑框 MaxLength×2) | 上游文件中改 | ✅ 已确认 |
| P4 | 预览态左键直接拖动窗口（自闭环手势：按下挂三处理器，移动>6px→原生 caption 拖动，静止松开→回调原进编辑路径；链接不劫持） | `PaperWindow.CustomPatches.cs`(ShouldPreviewPressStartWindowDrag+BeginPreviewPressDragOrClick) `PaperWindow.Note.cs`(noteMouseDown 预览分支单点插入+锚) `PaperWindow.cs`(BuildShell 挂 StateChanged reconciler+锚) | 独立文件为主 | ✅ 已确认 |
| P5 | F11 全屏/还原（沉浸顶栏状态跨最大化一致） | `PaperWindow.Todo.cs`(OnWindowPreviewKeyDown 头部插入+锚) `PaperWindow.CustomPatches.cs`(ToggleWindowStateForBlankDoubleClick) | 独立文件为主 | ✅ 已确认 |
| P6 | 透明度调节：Shift+滚轮 30%~100%（唯一快捷入口；◐ 按钮已按用户裁决删除，设置页保留开关+步进器） | `PaperWindow.Note.cs`(OnWindowPreviewMouseWheel 头部 Shift 分支+锚) `Strings.cs` | 单点小改 | ✅ 已确认 |
| P7 | 设置→「关于」页（版本号+定制构建说明+使用手册单链接，指向 fork 的 CUSTOM_FEATURES.md；预留未来检查更新/OTA） | `AppController.Settings.About.cs`(全新分部文件) `AppController.Settings.cs`(枚举+About+锚) `AppController.SettingsSidebar.cs`(导航项+路由+锚) | 独立文件为主 | ✅ 已确认 |
| P8 | 编辑器右键「表格视图（临时）」：光标在 GFM 管道表格内可用（菜单 Opened 时按光标动态置灰），转换为 CJK 感知等宽对齐代码块，Ctrl+Z 一步还原原始表格；粘贴永远原样（数据零损失） | `MarkdownTablePaste.cs`(全新纯函数静态类) `PaperWindow.Note.cs`(编辑菜单尾+锚、OpenCapsuleForEditing 改 internal+锚) `PaperWindow.CustomPatches.cs`(IsTableViewAvailableAtCaret+ToggleNoteTableView) 4 resx(MenuTableView) | 独立文件为主 | ✅ 已确认 |
| P9 | Alt+O「收起/展开全部纸片（胶囊不动）」全局命令（可配置；快照只记本命令收起的纸片 id，中途手动操作被尊重；复用上游 SetCollapsedState/OpenCapsuleForEditing） | `AppController.CollapsePapers.cs`(全新分部文件) `GlobalHotkeys.cs`(enum+catalog+锚) `AppController.ExperimentalPassive.cs`(分发 1 case+锚) `Strings.cs`(标签键) | 独立文件为主 | ✅ 已确认 |
| P10 | 版本发布+检查更新/OTA：GitHub Release=custom-release.yml（push v* tag 触发，自动构建嵌套 ZIP+manifest+delta）；GitLab common_tools/PaperTodo 镜像+latest.json 指针；客户端 GitLab latest.json 优先（6s 探测）→GitHub 兜底；关于页手动检查+启动静默气泡；增量升级=本地 SHA256 比对→delta 或全量→校验→`.old` 换文件→重启。v1.0.2 已发布（00ada91d），v1.0.0/1.0.1 已退役（旧客户端要求 exe 资产对现布局全盲） | 文件清单见 v1.0.2 提交（UpdateService/About/workflow 等） | 独立文件为主 | ✅ v1.0.2 双端核验通过 |
| P11 | 默认值定制：待办提醒默认启用+提醒声音默认启用+已完成待办默认移底（新装开箱即得；「恢复默认」同步对齐 fork 默认；用户本地实例按「配置=新用户默认」运维原则清理漂移，快捷分钟数 10 与提醒音 beep 属知情豁免） | `Models.cs`(3 字段初始化器+锚) `AppController.Settings.TodoPage.cs`(恢复默认 3 处+锚) | 上游文件小改 | ✅ 已确认 |
| P12 | OTA 可靠性修复批次（v1.0.5 已发布 2026-11-26）：①版本自报号——删掉 csproj 钉死的 `<InformationalVersion>1.0.3</...>`（v1.0.4 二进制是 1.0.4 却自报 1.0.3 → 关于页永远旧号、检查更新永远把已装版本当新版），改由 `<Version>` 派生 + 运行时兜底（Informational 落后于 AssemblyVersion 时取后者）+ CI 断言发布 exe 的 ProductVersion==tag；②升级后自动重启——OTA 换文件后改经 `AppController.Exit()` 真实关闭路径退出（原直排 `Application.Current.Shutdown()` 被 PaperWindow.OnClosing「点×=隐藏」的一律 e.Cancel 中止，App.Exit 不触发→重启永不发生，v1.0.3→v1.0.4 卡死根因）；③升级失败可诊断——分阶段记录（Manifest/Download/Extract/Apply/Restart/AlreadyCurrent）+ update-trace.log 落盘本地应用数据目录 + 关于页文案带阶段名；顺修镜像「已是最新」分支误 `return null` 致调用方 NRE、把权威最新判决误报成检查失败；④下载逐资产通道回退——delta/全量各带备用通道 URL（镜像↔GitHub 字节一致），失败自动换源并可从 delta 降级全量 | `PaperTodo.csproj`(删 pin+锚) `src/UpdateService.cs` `src/AppController.Settings.About.cs` `src/AppController.Tray.cs` `.github/workflows/custom-release.yml` | 独立文件为主+上游小改 | ✅ v1.0.5 双端核验通过 |
| P13 | Markdown 导入导出（全量+单张）：设置→通用页「数据与备份」可导出全部为 ZIP、从 ZIP 或 Markdown 导入；纸片右键可导出单张。笔记正文原样，图片导出到同名 .assets 并在导入时回写内部引用；待办导出为 GFM 任务清单；文件首行用不可见 HTML 注释保存类型和标题元数据；导入只追加新建、不覆盖，普通 md 按笔记导入，最多 200 张；导入待办按当前自动置底设置整理顺序；导入后纸片默认折叠为胶囊、不批量展开窗口；导入导出汇总弹窗强制置顶。 | `src/PaperTransferFormat.cs` `src/PaperMarkdownExportService.cs` `src/PaperMarkdownImportService.cs` `src/PaperMarkdownImageImporter.cs` `src/AppController.DataTransfer.cs` `src/PaperWindow.DataTransfer.cs`（全新）；`src/AppController.Settings.GeneralPage.cs`(1 行+锚) `src/PaperWindow.cs`(右键 1 项+锚) `src/PaperNoticeDialog.cs`(可选 forceTopmost+锚) `src/Strings.cs`(键) `src/AssemblyInfo.Tests.cs`(测试可见性) `tools/testing/Run-Checks.ps1`(注册 DataTransferChecks) | 独立文件为主+上游小改 | ✅ 用户验收 2026-11-28 |
| P14 | 待办完成项排序：①启动/重启时检查全部待办，顺序不符合则整理（窗口恢复前，不产生中间态）；②打开「已完成待办自动置底」或待办页「恢复默认」时，立即把历史完成项整理到底部（O(n) 稳定分组）；③新勾选的完成项插到已完成组最上方，越早完成越靠下，取消完成回未完成组末尾；④开关提示同步说明新行为。 | `src/AppController.cs`(StartAsync 调用 1 行+锚) `src/TodoRules.cs`(ApplyDoneTransitionOrdering 插入点 1 处+锚) `src/AppController.Settings.cs`(toggle 方法体+锚) `src/AppController.Settings.TodoPage.cs`(恢复默认 1 行+锚) `src/Strings.cs`(ToolTip) `src/AppController.CompletedTodos.cs`（全新） | 上游文件单点小改+独立文件 | ✅ 用户验收 2026-11-28 |
| P15 | 恢复默认对齐 fork 新装默认：常规页顶栏固定按钮数 1→4；视觉页主题 `system`→`light`、缩放把手 → 隐藏（与 Models 新装默认一致） | `src/AppController.Settings.GeneralPage.cs`(1 行+锚) `src/AppController.Settings.cs`(Visual restore 2 行+锚) | 上游文件小改 | ✅ 用户验收 2026-11-28 |
| P16 | 恢复启动纸片四边缩放失效修复：`SourceInitialized` 时 `PresentationSource.FromVisual(this)` 不是 `HwndSource`，消息钩子未挂，只有 WPF 右下角把手能缩放；改为 `TryAttachWindowMessageHook()` 在 SourceInitialized/Loaded/Activated 幂等重试，恢复启动的纸片四边/四角缩放正常；隐藏顶栏时纸面下移 HeaderExtent，顶部判定带改为跟随可见纸面顶边（`chromeMargin + HeaderExtent*(1-HeaderOpacity) + 5`），修复“只有看不见的窗口顶部能拖、可见纸边拖不动”；`ResizeMode` 守卫必须同时允许 `CanResize` 与 `CanResizeWithGrip`。 | `src/PaperWindow.cs`(字段+TryAttachWindowMessageHook+Loaded/Activated 调用+顶部窄带判断，均带锚) | 上游文件小改 | ✅ 用户验收 2026-11-28 |
| P17 | 缩放把手默认隐藏：`ResizeGripMode` 默认 Soft→Hidden（新装/恢复默认为无点阵），保留 Standard/Soft/Hidden 设置与透明握手作为兜底；四边/四角缩放由自定义 hit-test 负责。 | `src/Models.cs`(初始化器 1 行+锚) `src/AppController.Settings.cs`(Visual restore 1 行+锚) | 上游文件小改 | ✅ 用户验收 2026-11-28 |
| P18 | 一键恢复所有默认设置：设置→关于→维护→「恢复所有默认设置…」，二次确认后按手册顺序调用各页恢复默认（实验室→快捷键→常规→待办→笔记→视觉）；纸片/待办/笔记内容与位置不变，插件启用状态与插件数据不变；实验室恢复会关闭 MCP 与实验功能。 | `src/AppController.RestoreAllSettings.cs`（全新） `src/AppController.Settings.About.cs`（维护节+2 方法） | 独立文件+上游零改 | ⏳ 待用户验收 |

已撤销并清理干净的尝试（勿再引入，含死代码已删）：
- 右键长按拖动三代（手动位移 / 窗口级捕获死锁 / 借 chrome 会话僵死）；
- Alt+左键拖动（预览态直拖覆盖后冗余，连同 captureSource 参数体系、session.CaptureSource 字段、MoveWindowDevicePositionAsync 一并删除）；
- 空白双击全屏（与拖动/进编辑共享按下入口的结构性竞争，改用 F11）；
- 编辑态右键菜单沉浸切换（失焦后预览态右键即可）；
- 背景独立透明通道（SkinBorder 合成渲染多层叠加，覆盖底层画刷无视觉效果；等上游 4.0 材质透明度，届时 Alt+滚轮可作其快捷键预留）；
- 顶栏「◐」透明度按钮（Shift+滚轮为主入口后冗余，含 Popup 机制与 ToolTip 字符串全删）；
- 粘贴自动转表格（有损单向变换：原始 GFM 表格源码丢失、导出链路断；改为 P8 显式临时转换，粘贴原样）；
- 占位胶囊背景色区分（上游默认皮肤下无材质差异，但 DockedActive 表面常驻右侧 × 已可区分——上游现成行为，不补丁）；
- **待办纸空白区拖动窗口**（2026-11-12 两轮实验后撤）：待办正文无预览态、左键语义饱和——行=选中/排序、＋底栏=新增（在 _todoPanel 子树内）、滚动条，唯一"空白"仅剩行间隙数像素，结构上无安全劫持区；窗口级隧道 handler+CaptureMouse 会连坐吞掉顶栏/新增/胶囊全部点击。待办纸移动走顶栏（上游原生）。教训刻进：任何窗口级手势武装前必须先证明按下源 ∈ 预期交互区域。

## 三、冲突裁决规则（rebase upstream/main 时按序执行）

1. **`PaperWindow.CustomPatches.cs` / 新文件**：几乎不会冲突。挂载点（BuildShell/BuildBody 尾部 1-3 行、Todo.cs 键盘头部、Note.cs 预览分支）冲突时保双方——上游行在前、`// Custom patch` 行在后；
2. **`PaperTitles.cs`**：上游若改标题逻辑，**优先采上游**，然后重套两个改动：`DefaultMaxTitleLength=32`、全宽当量计数（ElementWidth/FullWidthEquivalents/EastAsianWidthCategory 整段可独立粘贴，只依赖 `StringInfo`）；
3. **`RefreshExperimentalOpacity`（PaperWindow.Experimental.cs）**：上游若重构透明度链，采上游结构后重插 persistent 分支：优先级 `IsAdvancedPaperTransparent > persistent(不折叠) > inactive 旧逻辑`；
3b. **`.github/workflows/release.yml`**：上游若改其触发结构，保留我们的禁用锚（tag 触发改为永不满 足的 `upstream-release-disabled` 模式）——fork 的发布只走 `custom-release.yml`；上游的 workflow 内容变化照常合入，不影响禁用锚。
4. **`Models.cs` / `AppController.Settings*.cs` / `Strings.cs` / resx**：纯增量（新字段/新 setting/新键/新 data 块），冲突时 union 全保留。若上游也加了 `MenuHideTopBar/MenuShowTopBar`（issue #384），**删我们的同名块用上游的**；
5. **`PaperWindow.ExperimentalFocusPresentation.cs`**：上游若重构失焦呈现，我们的方法组（Manual TopBar 系列）可整段搬移，仅依赖 `_paperChrome.SetHeaderOpacity/SetHeaderExtent`、`_shell.RowDefinitions[0]`、`_topBarHost` 三个稳定接口；最大化补偿块（hidden 判定+负 Margin）在 RefreshManualTopBarHiddenPresentation 内，整体搬；
6. **`PaperWindow.cs` / `Capsule.cs` / `Note.cs` / `Todo.cs` 内的插入块**：均为带锚注释的独立小块，冲突时保双方；上游方法改名时把我们的块移到新方法同名位置。Note.cs 的插入点在 noteMouseDown 预览分支 `TryGetOpenableLinkFromTextViewPoint` 之后、进编辑之前——上游若重排该函数，按此逻辑位置重新插入。
7. **`TodoRules.ApplyDoneTransitionOrdering`**：保留我们的单点改动——插入点取 `remaining.FindIndex(item => item.Done)`（无已完成项时取列表末尾），使新完成项落在已完成组顶部、恢复项落在未完成组末尾；上游若重写该函数，按此语义重新落点。`done` 参数若上游已使用则保留签名。
8. **`PaperNoticeDialog.cs`**：保留可选 `forceTopmost` 参数与 `Topmost = forceTopmost || owner.Topmost`；导入导出调用点保持 `forceTopmost: true`。
9. **P13/P14 新增文件**（`PaperTransfer*`、`AppController.DataTransfer.cs`、`PaperWindow.DataTransfer.cs`、`AppController.CompletedTodos.cs`）：独立文件几乎不冲突；若上游实现同类导入导出或完成项排序，按原则优先撤我们的补丁改用上游。
10. **`AppController.StartAsync`**：保留 `ReorderCompletedTodosToBottom()` 在恢复纸片窗口之前的一行调用；上游若重排启动流程，按逻辑位置放在恢复 papers 之前。
11. **`PaperWindow.TryGetPaperResizeHitTest`**：三条必须同时保留——①`ResizeMode` 守卫是 `!= CanResize && != CanResizeWithGrip`（勿简化成两个 `CanResize`，否则展开纸片走 CanResizeWithGrip 会整体跳过自定义 hit-test，只剩右下角把手）；②顶部窄带在所有头栏状态可缩放，且跟随可见纸面顶边：`topResizeBorder = (chromeMargin + HeaderExtent*(1-HeaderOpacity) + 5) * dpiScale`（隐藏顶栏时纸面下移，不能用窗口顶部常量）；③`overGrip` 右下角带保留，正文其余区域仍为普通客户区。
12. **`PaperWindow.TryAttachWindowMessageHook`**：保留 SourceInitialized/Loaded/Activated 的幂等重试，确保恢复启动的纸片也能挂上 `OnWindowMessage`；上游若修复同问题，采上游并撤本条。
13. **`ResizeGripMode`（P17）**：保留 Models 默认 `Hidden` 与视觉页恢复默认 `Hidden`；上游若移除缩放把手设置/点阵，采上游并撤 P17。
14. **一键恢复所有默认设置（P18）**：入口在设置→关于→维护；保持调用各页既有 `Restore*Defaults` 方法，新增默认值只需改对应页方法；若上游提供全局重置，采上游并撤 P18。

## 四、用户侧事实（排障速查）

- 使用 exe：`D:\userdata\shiwenbin\software\portable\paperTodo\PaperTodo.exe`（快捷方式 `shortcut\paper.lnk` 指向它，更新只换文件不动 lnk）；
- 官方原版归档：`official-backup\`；数据备份：`backup-20261103\`；
- `data.json` 持久化会**覆盖代码默认值**——升级后「设置不生效」先查 data.json 存的旧值（实例：maxTitleLength 6 盖掉默认 32）；
- **数据备份/迁移（P13）**：入口=设置→通用→「数据与备份」。全量导出为 ZIP（`notes/`、`todos/` 每张 .md + 图片 + `manifest.json`），单张在纸片右键导出 `.md`；文件首行是不可见 HTML 注释元数据，普通编辑器打开无感，若被删除则导入时按笔记处理；导入只追加、不覆盖，导入后默认折叠为胶囊。
- **完成项排序（P14）**：`autoMoveCompletedTodosToBottom` 在旧实例中常为 `false`（P11 前默认被持久化）；升级不会改它，需在设置→待办打开一次或点恢复默认，打开时会把历史完成项整理到底部。新完成项落在已完成组顶部；历史项首次整理保持相对顺序（旧数据无完成时间）。触发点：启动/重启、勾选/取消完成、追加/粘贴待办、导入、打开开关/恢复默认、外部或插件批量写入；启动时在窗口恢复前检查，顺序合规则不写盘；手动拖动后尊重手动顺序直到下一次触发或重启。
- **待办默认显示（开关开启时）**：未完成在前、已完成在后；已完成组内最新完成的在最上面。导入待办时也按此规则整理，未完成在前、已完成在后。
- **恢复默认（P15 对齐）**：常规页恢复后顶栏固定 4 个按钮、视觉页恢复后主题为浅色、待办页恢复后自动置底开启并整理现有项；恢复默认不会删除纸片内容（待办顺序会按规则变化）。
- **四边缩放（P16）**：启动恢复的纸片若只有右下角把手能缩放，原因是源初始化时 HwndSource 未就绪、消息钩子未挂；P16 在 Loaded/Activated 重试挂载。隐藏顶栏时顶部判定带跟随**可见纸面顶边**（不是窗口上方透明区），从纸片顶边即可拖动改高度；顶栏显示时行为不变。
- **缩放把手默认隐藏（P17）**：`ResizeGripMode` 默认和视觉页恢复默认都是 `Hidden`，右下角不再显示点阵；透明抓手仍作兜底，四边/四角由自定义 hit-test 负责。
- **一键恢复所有默认设置（P18）**：设置→关于→维护。重置全部设置页；纸片/待办/笔记内容与位置不变，插件启用状态与插件数据不变；待办完成项会归底，实验室页会关闭 MCP 与实验功能。
- 更新流程（严格顺序，2026-11-05 两次事故后固化）：改 dev 分支 → `dotnet publish`（官方参数）→ `--exit` 退出并**确认进程消失** → 替换 exe → **哈希校验 exe 真的换了**（时间戳/字节不可靠，cp 可能被进程锁静默吞掉）→ 启动 → 验证；
- **data.json 纪律**（三层）：①改值必须在进程退出后（运行中会写回内存值盖掉）；②**新增命令/设置键必须在「含该键代码的 exe」启动前写入**（旧 exe 的 NormalizeBindings 归一化保存会把不认识的键从字典剔除——静默丢配置）；③写完等一个保存周期回读确认存活（启动后 ≥10s 回查）；
- 胶囊上下磁吸：上游实验功能已支持（拖悬浮胶囊靠任意屏幕边/窗口边吸附），开关=设置-实验室-「边缘磁吸胶囊」；贴边队列仍为左右（#383 作者：上下「难点是交互逻辑没想法」）；
- 重建命令：`dotnet publish ./PaperTodo.csproj -c Release -r win-x64 --self-contained true -o ./release_local -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:EmbedAllSources=false -p:EmbedUntrackedSources=false -p:DebugType=none -p:DebugSymbols=false`；
- 同步上游：`git fetch upstream && git rebase upstream/main dev` → 按上表裁决 → `dotnet build -c Release` 验证 → push `origin HEAD:refs/heads/dev` → publish 替换 exe → 重启。

## 五、待办 / 已知问题

- [x] v1.0.3 待发布内容集（攒批）：①镜像「已是最新」判定权威化——镜像 up-to-date 与镜像失败共用 null 返回值，内网+GitHub 匿名配额耗尽时误报「检查失败」（v1.0.2 tag 溯源实锤，注释写权威实现未权威）②升级 apply 成功却报「失败」的 UI 误报（2026-11-12 实测）③检查失败时提示具体通道 ④关于页手册链接 GitLab/GitHub 探测定向（已在工作区实现待发）⑤OtaTestMarker.cs 删除；✅ v1.0.3 已发布（2026-11-16，GitHub tag=d1b6453f + GitLab acd5db6，delta-from-v1.0.2=8 文件/1.6MB，双端在线核验通过）；
- [x] 2026-11-13 新修待发：胶囊队列换边崩溃——渲染回调内同步 Dispatcher.Invoke 抛 suspended，MasterCapsuleWindow.cs 与 EdgeCapsuleDragWindow.cs 两处渲染屏障改 BeginInvoke（带锚）；上游 beta1 原生 bug，合并队列换边动画完成帧触发；✅ 已随 v1.0.3 发布（上游 #456）；
- [x] v1.0.4 已发布（2026-11-17）：P11 三默认值——待办提醒默认启用+提醒声音默认启用+已完成待办默认移底（Models.cs 字段初始化器+TodoPage 恢复默认同步，均带锚）；GitHub tag=v1.0.4（567d3cb6）三资产在线核验 + GitLab master=d501dcc latest.json 在线核验，delta-from-v1.0.3=8文件/1.6MB；发布前 fresh 验收实证三默认落盘；audit-defaults.ps1 对齐审计脚本留存可复跑；已知现象：CI 现编 lmdb 链接时间戳每次漂移（大小恒 164352B），delta 永含 lmdb 无害；踩坑记录：本仓 remote.origin.push=*:refs/for/* 会改写 tag/分支引用（tag 曾误推 refs/for/refs/tags/）——推送必须显式 refs/tags/…或 refs/heads/…全路径；
- [x] v1.0.5 已发布（2026-11-26，P12 OTA 修复批次）：GitHub tag=v1.0.5（073d5579）三资产在线核验（zip 338/338 哈希吻合、exe ProductVersion=1.0.5、delta-from-v1.0.4=11 文件/8.0MB），GitLab master=81408bc（zip/manifest/增量均 200、latest.json 指向 1.0.5）；首次 tag 推送因 workflow 卡口步骤缩进错误（6 空格截断 run 块）致 CI 秒败，修复后重打 tag 成功；用户端 v1.0.4 升 1.0.5 仍由旧进程执行换文件：如未自动重启需手动重启一次，此后自报号/检查更新/自启全转正
- [ ] P13–P17 发布（代码已提交并推送 dev：419b3bd6..523c5d84，2026-11-28；用户已验收）：P13 导入导出+弹窗置顶，P14 完成项排序，P15 恢复默认对齐，P16/P17 四边缩放与点阵默认隐藏；待发 v1.0.6。P13 目前同步执行文件 I/O，超大导出集可能短暂卡 UI，后续可改异步；导入每张纸片一次同步保存，200 张上限内有界。
- [ ] v1.0.6 发版时同步 GitLab `latest.json`：`ReleaseNote.zh-CN` 必须改为 1.0.6 文案（客户端优先读它，显示前截断 600 字）；GitHub Release 正文由 workflow 固定模板生成，不含逐版本清单。
- [ ] 真实实例 `autoMoveCompletedTodosToBottom` 仍为 `false`：P14 部署后需用户打开一次开关（或经同意改 data.json）才会启用新排序。
- [ ] 真实实例 `resizeGripMode` 仍为 `soft`：升级 v1.0.6 后需在 设置→视觉 选「隐藏」或点恢复默认，右下角点阵才会消失。
- [ ] P18 一键恢复所有默认设置：待用户验收，随 v1.0.6 发布。
- [ ] 上游发布 4.0 正式版：执行首次 rebase（fork 点 411078b 落后上游已较多；预期冲突点 `PaperTitles.cs`/`RefreshExperimentalOpacity`/`Note.cs` noteMouseDown，按裁决规则 2/3/6 处理）；
- [ ] issue #384 若作者实现同类标题逻辑：撤 P3；#383 上下停靠若作者想通交互：直接用上游；
- [ ] 上游 4.0 材质透明度落地后：评估撤 P1/P6（改用材质透明度），并把 Alt+滚轮接为其快捷键。

**已完成（存档）**：

- [x] P10 发布流程首次实跑验证 ✅（2026-11-06）：v1.0.0/v1.0.1 已发布；端到端升级实测通过（delta 1.54MB vs 全量 77.6MB，升级后 338 文件零失配，`.old`/staging 清理干净，升级版启动稳定）；

