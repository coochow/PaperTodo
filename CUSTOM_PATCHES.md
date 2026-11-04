# PaperTodo 自定义补丁台账（dev 分支）

> 目的：记录所有相对上游 (snownico0722/PaperTodo) 的自定义修改，让 `git rebase upstream/main` 同步时的冲突可以**按此文档机械地解决**，无需逐处确认。
>
> 维护规则：每次改动后必须同步更新本文件。rebase 冲突时先查本表，按「冲突裁决规则」处理。

## 一、修改原则（所有补丁必须遵守）

1. **新功能尽量放独立文件**（前缀 `PaperWindow.CustomPatches.cs` 这类分部类/新文件），不动上游方法体；
2. **必须改上游文件时**，改动保持「单点、加粗注释锚定」——每处上游文件内的修改都带 `// Custom patch (dev branch):` 注释锚，rebase 时以此识别；
3. **字符串**优先放 `Strings.cs` 的 Supplemental 字典（四语言数组），不动 resx；
4. **禁止**重命名/移动上游符号；禁止改上游方法签名；
5. **数据字段**新增走 `Models.cs` 且默认值必须 `false`/`0`/空（`JsonIgnore(WhenWritingDefault)`），保证旧 data.json 无缝加载。

## 二、当前补丁清单（相对 upstream/main @ 411078b）

| # | 功能 | 涉及文件 | 改动性质 | 状态 |
|---|---|---|---|---|
| P1 | 纸片常驻半透明（设置-实验室「常驻透明」组） | `Models.cs`(State 2字段) `AppController.SettingsApi.Catalog.cs`(2 setting) `AppController.Settings.cs`(UI+2 handler) `PaperWindow.Experimental.cs`(RefreshExperimentalOpacity 插一层) `Strings.cs`(4键) | 上游文件小改，均有 patch 注释 | ✅ 已部署 |
| P2 | 右键隐藏/显示顶栏（沉浸编辑，按纸片持久化；最大化时保持隐藏、chrome 负 Margin 上移补偿，模式跨窗口状态一致） | `Models.cs`(PaperData.TopBarHidden) `PaperWindow.ExperimentalFocusPresentation.cs`(独立方法组+最大化判定) `PaperWindow.cs`(右键菜单 1 项+BuildShell 1 行初始化) `PaperWindow.Capsule.cs`(折叠清理+展开恢复 2 处) `Strings.cs`+4 resx(2键) | 上游文件中改 | ✅ 已部署 |
| P3 | 标题长度按全宽当量计（半角=0.5），默认/上限 32 | `PaperTitles.cs`(重写核心计数，含 EastAsianWidthCategory 新类) `PaperWindow.cs`(MaxLength×2) | 上游文件中改 | ✅ 已部署 |
| P4 | 窗口拖动双入口：①预览态左键按住直接拖（自闭环手势：按下挂 up/move/lostCapture 三处理器，移动>6px→原生 caption 拖动，静止松开→回调原进编辑路径；链接不劫持）②Alt+左键任意态拖（复用上游标题栏会话） | `PaperWindow.CustomPatches.cs`(BeginPreviewPressDragOrClick) `PaperWindow.Note.cs`(noteMouseDown 预览分支单点插入，带锚) `PaperWindow.cs`(BuildShell 挂 Alt 手势) `PaperWindow.Lifecycle.cs`(session.CaptureSource 字段) | 独立文件为主 | ✅ 已部署 |
| P5 | 正文空白处双击 → 最大化/还原（TextView 可视行判定空白；Alt 修饰排除） | `PaperWindow.CustomPatches.cs` `PaperWindow.cs`(BuildBody 3 行挂载) | 独立文件为主 | ✅ 已部署 |
| P6 | 顶栏「◐」按钮弹透明度滑杆（开关+30%~100% 实时步进，走设置管线刷新全部窗口） | `PaperWindow.CustomPatches.cs`(Popup 构建) `PaperWindow.cs`(固定按钮区 1 行插入) `AppController.Settings.cs`(2 个 FromPaper 入口) | 独立文件为主+2 单点 | ✅ 已部署 |

已撤销的尝试（勿再引入）：~~顶栏「◐」透明度滑杆按钮~~（首版挂 pinned 活动区被自适应收走→已改挂固定按钮区复活为 P6）、编辑态右键菜单沉浸切换（用户裁决不需要：失焦后预览态右键即可恢复顶栏）、右键长按拖动三代方案全部废弃（v1 手动位移：caption 循环只认左键；v2 窗口级 Mouse.Capture：与右键菜单死锁卡死 10s+；v3 复用 chrome 会话：chrome 的 move/up 带 OriginalSource 过滤，box 触发的事件无法收尾→会话僵死+单击难进编辑）。

**拖动手势设计原理（rebase 重推导依据）**：鼠标捕获是「责任」不是「权限」——捕获落在谁身上，谁就必须有完整 up/move/lostCapture 收尾。上游 chrome 安全是因为它有全套处理器；任何「触发者与收尾者分离」的借用（box 触发+chrome 收尾）都会因事件路由过滤而僵死。P4 的自闭环结构（谁触发谁收尾、结束时立即摘除全部处理器）是唯一与上游零耦合的安全形态。

## 三、冲突裁决规则（rebase upstream/main 时按序执行）

1. **`PaperWindow.CustomPatches.cs` / 新文件**：几乎不会冲突。若上游新建了同名文件（概率≈0），重命名我们的文件即可。挂载点（BuildShell/BuildBody 尾部 1-3 行）冲突时：保双方——上游行在前、`// Custom patch` 行在后；
2. **`PaperTitles.cs`**：上游若改标题逻辑，**优先采上游**，然后把我们的两个改动重新套上：`DefaultMaxTitleLength=32`、全宽当量计数（ElementWidth/FullWidthEquivalents 系列整段可独立粘贴，AddScreenCandidates 式不依赖上游私有符号，只依赖 `StringInfo`）；
3. **`RefreshExperimentalOpacity`（PaperWindow.Experimental.cs）**：上游若重构透明度链，采上游结构后重插「persistent 优先级分支」：条件链为 `IsAdvancedPaperTransparent > persistent(不折叠) > inactive 旧逻辑`；
4. **`Models.cs` / `AppController.Settings*.cs` / `Strings.cs`**：纯增量（新字段/新 setting/新键），冲突时全保留（union）。`Strings.cs` 的 Supplemental 字典冲突时按 key 合并；
5. **`PaperWindow.cs` / `Capsule.cs` 内的挂载/菜单项**：均为带 `// Custom patch` 注释的独立小块，冲突时保双方；上游把挂载方法改名时，把我们的行移到新方法同名位置；
6. **resx（4 语言）**：纯新增 `<data>` 块，冲突时 union。若上游也加了 `MenuHideTopBar/MenuShowTopBar`（issue #384 已提交，作者可能实现），**删我们的同名块用上游的**，菜单引用键名不变则代码无需改。

## 四、用户侧事实（排障速查）

- 使用 exe：`D:\userdata\shiwenbin\software\portable\paperTodo\PaperTodo-dev-custom.exe`（快捷方式 `shortcut\paper.lnk` 指向它，更新只换文件不动 lnk）；
- 官方原版归档：`official-backup\`；数据备份：`backup-20261103\`；
- `data.json` 持久化会**覆盖代码默认值**——升级后「设置不生效」先查 data.json 里存的旧值（实例：maxTitleLength 6 盖掉默认 32，2026-11-03 已修）；
- 胶囊上下磁吸：上游实验功能已支持（AddScreenCandidates 生成四边候选），开关=设置-实验室-「边缘磁吸胶囊」（已在 data.json 开启，拖普通悬浮胶囊靠近任意屏幕边/窗口边生效；贴边队列胶囊仍为左右）；
- 重建：`dotnet publish ./PaperTodo.csproj -c Release -r win-x64 --self-contained true -o ./release_local -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:EmbedAllSources=false -p:EmbedUntrackedSources=false -p:DebugType=none -p:DebugSymbols=false`；
- 同步上游：`git fetch upstream && git rebase upstream/main dev` → 按上表裁决冲突 → `dotnet build -c Release` 验证 → push `origin HEAD:refs/heads/dev` → publish 替换 exe → 重启。

## 五、待办 / 已知问题

- [ ] 上游若发布 4.0 正式版：执行首次 rebase 同步（预期冲突点：`PaperTitles.cs`、`RefreshExperimentalOpacity`、`Note.cs` noteMouseDown，按裁决规则 2/3/5 处理）；
- [ ] issue #383/#384 上游答复后：若作者实现了同类功能，评估撤销对应补丁（P3 优先候选；#383 作者已答复：整队换边已进主线待发版，上下停靠「难点是交互逻辑没想法」，故不自研）；
- [ ] P4 预览态拖动与「按下-拖动-松开」快速循环：自闭环结构无残留，观察长期使用。
