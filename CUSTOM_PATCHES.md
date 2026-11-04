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
| P2 | 沉浸顶栏（右键菜单切换，按纸片持久化；最大化时保持隐藏+chrome 负 Margin 上移补偿） | `Models.cs`(TopBarHidden 字段+锚) `PaperWindow.ExperimentalFocusPresentation.cs`(独立方法组：Toggle/Refresh/LayoutUpdated/Extent+最大化判定) `PaperWindow.cs`(右键菜单 1 项) `PaperWindow.Capsule.cs`(折叠清理+锚、展开恢复+锚) `Strings.cs`+4 resx(MenuHideTopBar/MenuShowTopBar) | 上游文件中改 | ✅ 已确认 |
| P3 | 标题长度按全宽当量计（半角=0.5），默认/上限 32 | `PaperTitles.cs`(重写计数+EastAsianWidthCategory 新类) `PaperWindow.cs`(标题编辑框 MaxLength×2) | 上游文件中改 | ✅ 已确认 |
| P4 | 预览态左键直接拖动窗口（自闭环手势：按下挂三处理器，移动>6px→原生 caption 拖动，静止松开→回调原进编辑路径；链接不劫持） | `PaperWindow.CustomPatches.cs`(ShouldPreviewPressStartWindowDrag+BeginPreviewPressDragOrClick) `PaperWindow.Note.cs`(noteMouseDown 预览分支单点插入+锚) `PaperWindow.cs`(BuildShell 挂 StateChanged reconciler+锚) | 独立文件为主 | ✅ 已确认 |
| P5 | F11 全屏/还原（沉浸顶栏状态跨最大化一致） | `PaperWindow.Todo.cs`(OnWindowPreviewKeyDown 头部插入+锚) `PaperWindow.CustomPatches.cs`(ToggleWindowStateForBlankDoubleClick) | 独立文件为主 | ✅ 已确认 |
| P6 | 透明度快捷调节双入口（顶栏固定按钮区「◐」弹滑杆 Popup；Shift+滚轮 30%~100%） | `PaperWindow.CustomPatches.cs`(Popup 构建+滑杆) `PaperWindow.cs`(固定按钮区 1 行插入+锚) `PaperWindow.Note.cs`(OnWindowPreviewMouseWheel 头部 Shift 分支+锚) `Strings.cs`(ToolTipOpacityQuickSlider 键) | 独立文件为主 | ✅ 已确认 |

已撤销并清理干净的尝试（勿再引入，含死代码已删）：
- 右键长按拖动三代（手动位移 / 窗口级捕获死锁 / 借 chrome 会话僵死）；
- Alt+左键拖动（预览态直拖覆盖后冗余，连同 captureSource 参数体系、session.CaptureSource 字段、MoveWindowDevicePositionAsync 一并删除）；
- 空白双击全屏（与拖动/进编辑共享按下入口的结构性竞争，改用 F11）；
- 编辑态右键菜单沉浸切换（失焦后预览态右键即可）；
- 背景独立透明通道（SkinBorder 合成渲染多层叠加，覆盖底层画刷无视觉效果；等上游 4.0 材质透明度，届时 Alt+滚轮可作其快捷键预留）。

## 三、冲突裁决规则（rebase upstream/main 时按序执行）

1. **`PaperWindow.CustomPatches.cs` / 新文件**：几乎不会冲突。挂载点（BuildShell/BuildBody 尾部 1-3 行、Todo.cs 键盘头部、Note.cs 预览分支）冲突时保双方——上游行在前、`// Custom patch` 行在后；
2. **`PaperTitles.cs`**：上游若改标题逻辑，**优先采上游**，然后重套两个改动：`DefaultMaxTitleLength=32`、全宽当量计数（ElementWidth/FullWidthEquivalents/EastAsianWidthCategory 整段可独立粘贴，只依赖 `StringInfo`）；
3. **`RefreshExperimentalOpacity`（PaperWindow.Experimental.cs）**：上游若重构透明度链，采上游结构后重插 persistent 分支：优先级 `IsAdvancedPaperTransparent > persistent(不折叠) > inactive 旧逻辑`；
4. **`Models.cs` / `AppController.Settings*.cs` / `Strings.cs` / resx**：纯增量（新字段/新 setting/新键/新 data 块），冲突时 union 全保留。若上游也加了 `MenuHideTopBar/MenuShowTopBar`（issue #384），**删我们的同名块用上游的**；
5. **`PaperWindow.ExperimentalFocusPresentation.cs`**：上游若重构失焦呈现，我们的方法组（Manual TopBar 系列）可整段搬移，仅依赖 `_paperChrome.SetHeaderOpacity/SetHeaderExtent`、`_shell.RowDefinitions[0]`、`_topBarHost` 三个稳定接口；最大化补偿块（hidden 判定+负 Margin）在 RefreshManualTopBarHiddenPresentation 内，整体搬；
6. **`PaperWindow.cs` / `Capsule.cs` / `Note.cs` / `Todo.cs` 内的插入块**：均为带锚注释的独立小块，冲突时保双方；上游方法改名时把我们的块移到新方法同名位置。Note.cs 的插入点在 noteMouseDown 预览分支 `TryGetOpenableLinkFromTextViewPoint` 之后、进编辑之前——上游若重排该函数，按此逻辑位置重新插入。

## 四、用户侧事实（排障速查）

- 使用 exe：`D:\userdata\shiwenbin\software\portable\paperTodo\PaperTodo-dev-custom.exe`（快捷方式 `shortcut\paper.lnk` 指向它，更新只换文件不动 lnk）；
- 官方原版归档：`official-backup\`；数据备份：`backup-20261103\`；
- `data.json` 持久化会**覆盖代码默认值**——升级后「设置不生效」先查 data.json 存的旧值（实例：maxTitleLength 6 盖掉默认 32）；
- 更新流程（严格顺序）：改 dev 分支 → `dotnet publish`（官方参数）→ `--exit` 退出并确认进程消失 → 替换 exe → 启动 → 验证。**改 data.json 必须在进程退出后**（运行中会写回内存值）；
- 胶囊上下磁吸：上游实验功能已支持（拖悬浮胶囊靠任意屏幕边/窗口边吸附），开关=设置-实验室-「边缘磁吸胶囊」；贴边队列仍为左右（#383 作者：上下「难点是交互逻辑没想法」）；
- 重建命令：`dotnet publish ./PaperTodo.csproj -c Release -r win-x64 --self-contained true -o ./release_local -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:EmbedAllSources=false -p:EmbedUntrackedSources=false -p:DebugType=none -p:DebugSymbols=false`；
- 同步上游：`git fetch upstream && git rebase upstream/main dev` → 按上表裁决 → `dotnet build -c Release` 验证 → push `origin HEAD:refs/heads/dev` → publish 替换 exe → 重启。

## 五、待办 / 已知问题

- [ ] 上游发布 4.0 正式版：执行首次 rebase（fork 点 411078b 落后上游已较多；预期冲突点 `PaperTitles.cs`/`RefreshExperimentalOpacity`/`Note.cs` noteMouseDown，按裁决规则 2/3/6 处理）；
- [ ] issue #384 若作者实现同类标题逻辑：撤 P3；#383 上下停靠若作者想通交互：直接用上游；
- [ ] 上游 4.0 材质透明度落地后：评估撤 P1/P6（改用材质透明度），并把 Alt+滚轮接为其快捷键。
