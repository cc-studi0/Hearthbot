# SmartBot UI 布局复刻设计

## 目标

将 Hearthbot 的 BotMain 主窗口 UI 布局改为与 SmartBot 一致的 3 行 Grid 布局，仅改 UI 外观和布局，后端逻辑不变。SmartBot 有但 Hearthbot 没有后端支撑的控件以 disabled 空壳占位。

## 参考文件

- SmartBot 主窗口 XAML: `references/smartbot反编译后源码/XAML_clean/MainWindow.xaml` (1442行)
- SmartBot 本地化字符串: `references/smartbot反编译后源码/XAML_clean/../XAML/localization/strings.zh-cn.xaml`
- 当前 Hearthbot 主窗口: `BotMain/MainWindow.xaml` (171行)
- 当前 ViewModel: `BotMain/MainViewModel.cs`

## 布局结构

```
Window (MinWidth=400, MinHeight=450, SnapsToDevicePixels=True)
└── Grid (Margin=10,0,10,10)
    ├── Row 0 (Height=100) — 顶栏：标题 + 控制按钮 + 选择器
    ├── Row 1 (Height=140) — 中间栏：统计 + 选项 + 功能按钮
    └── Row 2 (Height=300*) — 主内容区：TabControl
```

### Row 0 — 顶栏

```
StackPanel (垂直)
├── TitleLabel — 绑定 TopStatusText（替代原蓝色状态栏）
└── StackPanel (水平)
    ├── 左侧 StackPanel (按钮区)
    │   ├── Grid (Inject/Start/Stop 三按钮叠放，根据状态切换可见性)
    │   │   ├── ButtonInject (MinWidth=100, MinHeight=32) — 绑定现有 MainCmd
    │   │   ├── ButtonStart (MinWidth=75, MinHeight=32) — 绑定现有 MainCmd
    │   │   └── ButtonStop (MinWidth=75, MinHeight=32) — 绑定现有 MainCmd
    │   └── ButtonFinish (MinWidth=75, MinHeight=32) — 绑定现有 FinishCmd
    │
    └── 右侧 StackPanel (选择器区, Margin=10,0,0,0)
        ├── 第一行 StackPanel (水平, MinHeight=30)
        │   ├── Label "Mode :" + ComboBox (ComboBoxMode) — 绑定 ModeIndex
        │   ├── Label "Profile :" + ComboBox (ComboBoxProfile) — 绑定 SelectedProfileIndex
        │   ├── Button ↻ (ButtonRefreshProfiles, 23x23) — 绑定 RefreshProfilesCmd
        │   ├── Label "Profile :" + ComboBox (ComboBoxProfileBattleground) — Collapsed 占位
        │   └── Button ↻ (ButtonRefreshProfilesBattleground) — Collapsed 占位
        │
        └── 第二行 StackPanel (水平, MinHeight=30, Margin=0,10,0,0)
            ├── Label "Deck :" + CheckComboBox/ComboBox (ComboBoxDeck) — 绑定 DeckNames/SelectedDeckIndex
            ├── Button ↻ (ButtonRefreshDeck, 23x23) — 绑定 RefreshDecksCmd
            ├── Label "Mulligan :" + ComboBox (ComboBoxMulligan) — 绑定 MulliganNames/MulliganProfileIndex
            └── Button ↻ (ButtonRefreshMulligan, 23x23) — 绑定 RefreshMulliganCmd
```

### Row 1 — 中间栏

```
Grid (3列: 50*, 150, 100)
├── Col 0: GroupBox "Statistics"
│   ├── Statslabel — 绑定统计信息 (Runtime/W/L/C/WR/Rank)
│   ├── Button "Reset stats" (宽75, 靠右下) — 绑定 ResetStatsCmd
│   └── Button "Legends" (宽75, 靠右下偏左) — disabled 占位
│
├── Col 1: StackPanel (复选框区, Margin=15,5,0,0)
│   ├── StackPanel (水平): CheckBox "Task" (Collapsed占位) + ComboBox taskCombo (Collapsed占位)
│   ├── StackPanel (水平): CheckBox "Coach" (disabled占位) + CheckBox "Overlay" (disabled占位)
│   ├── CheckBox "Reset stats on start" (disabled占位)
│   ├── StackPanel (水平): CheckBox "FPS Lock" (disabled占位) + IntegerUpDown (Value=60, 5-200)
│   ├── CheckBox "CutConAuth" (Collapsed)
│   ├── CheckBox "CutGameCon" (Collapsed)
│   ├── StackPanel (水平): Label "Proxy :" + ComboBox (smartbot.ws/cnproxy) — disabled占位
│   └── CheckBox "WebSocket" (disabled占位)
│
└── Col 2: StackPanel (功能按钮, 居中)
    ├── Button "Task Editor" (Collapsed占位)
    ├── Button "Missplay" — disabled占位
    ├── Button "Settings" — 绑定现有 SettingsCmd
    └── Button "Plugins" — disabled占位
```

注意：Hearthbot 原有的"中控"和"更新"按钮移到这里或保留在 Settings 窗口中。

### Row 2 — 主内容区 TabControl

```
TabControl (Name=TabControlHeader, SelectedIndex=4 即默认选中Logs)
├── TabItem "Debug" (TabDebug)
│   ├── 顶部 StackPanel (水平): DebugLabel + DebugInput(宽50) + Execute按钮 + Clear按钮 + ClearLog按钮 + Profile下拉 + Mode下拉 + 刷新按钮
│   └── 子 TabControl (tabControl2)
│       ├── "Board" 子标签 (TabBoardDEbu)
│       │   └── 再嵌套TabControl: Current/Before/After 三个只读TextBox — 绑定 DebugText
│       ├── "Mulligan" 子标签 — 4个ComboBox(mullTestOne~Four) + OwnHero/EnemyHero ComboBox + Test按钮 + 结果TextBox — 空壳占位
│       ├── "Discover" 子标签 — 3个TextBox(choice1~3) + Origin TextBox + FromClipboard按钮 + 输出TextBox — 空壳占位
│       └── "Hidden" 子标签 (Visibility=Hidden) — 坐标调试工具 — 空壳占位
│
├── TabItem "Changelog" (TabChangelog) — 空壳
│   └── 子 TabControl
│       ├── "AI" 子标签 — FlowDocumentScrollViewer (只读)
│       └── "Bot" 子标签 — FlowDocumentScrollViewer (只读)
│
├── TabItem "Stats" (TabStats)
│   └── 子 TabControl
│       ├── 第一子标签 (时间维度统计)
│       │   ├── 顶部: 时间筛选 RadioButton 组 (1h/12h/24h/72h/Monthly/LastMonth) — 空壳占位
│       │   └── 横排4个TextBox (Arena/RankedStd/RankedW/RankedC) — 绑定 StatsDetailText 到第一个，其余空壳
│       └── 第二子标签 (Profile维度统计)
│           ├── Profile下拉 + Classic/Standard/Wild RadioButton — 空壳占位
│           └── 2个TextBox — 空壳占位
│
├── TabItem "Misplays" (TabMissplay)
│   └── 子 TabControl
│       ├── "Info" 子标签 (MissplayInfosTab)
│       │   ├── 顶部: labelMissplays + 按钮
│       │   └── MissplayListBox — 绑定现有 MisplayText（改用ListBox）
│       ├── "Leaderboard" 子标签 (MissplayLeaderboard) — 空壳
│       │   ├── LabelMissplayStats
│       │   └── 子TabControl: 当月/上月 两个RichTextBox
│       └── "Edit" 子标签 (MissplayEditTab, Visibility=Hidden) — 空壳
│           ├── RadioButton 组 (Ranked/Arena/等11个)
│           └── MissplayEditListBox
│
├── TabItem "Logs" (TabLog)
│   ├── 顶部 StackPanel (水平): 搜索框(LogSearchBox) + 筛选按钮(All/Errors/Actions/CurrentGame/LastGame) — 空壳占位
│   └── RichTextBox (RichTextBoxLog) — 绑定现有 LogText
│
├── TabItem "Board" — 保留现有 Hearthbot 棋盘展示（ScrollViewer + 敌方/友方随从 + 手牌）
│
└── TabItem "Plugins" — 保留现有 Canvas
```

## 不变部分

- `MainViewModel.cs` — 所有现有绑定属性和命令不变
- `MainWindow.xaml.cs` — 代码后置逻辑不变（日志滚动、插件渲染等）
- `SettingsWindow.xaml` / `AccountControllerWindow.xaml` — 不改
- 所有后端服务（BotService, NotificationService 等）— 不改

## 需要调整的 ViewModel 绑定

现有绑定全部保留，新增空壳控件不需要新增绑定。但以下需要适配：

1. **MainButtonText → 拆分为三个按钮可见性**：原来用一个按钮动态切换文本，SmartBot 用三个按钮(Inject/Start/Stop)叠放切换 Visibility。需要在 ViewModel 中新增 `InjectButtonVisible`/`StartButtonVisible`/`StopButtonVisible` 属性，或在 XAML 中用 DataTrigger 转换。
2. **LogText → RichTextBox**：原来绑定到 TextBox.Text，改为 RichTextBox 需要通过代码后置设置 FlowDocument。可以保持简单实现——先用 TextBox 包在 RichTextBox 的 TabItem 里。
3. **统计信息格式**：原来用多个 Run 拼接，现在改为单个 Label.Content，需要调整格式化方式或保持 TextBlock with Runs。

## 空壳控件清单

以下控件在 UI 上显示但 IsEnabled=False 或 Visibility=Collapsed：

| 控件 | 状态 | 说明 |
|------|------|------|
| SessionEnabled CheckBox | Collapsed | 任务系统未实现 |
| taskCombo ComboBox | Collapsed | 任务系统未实现 |
| CheckBoxCoach | disabled | Coach 模式未实现 |
| CheckBoxOverlay | disabled | Overlay 未实现 |
| CheckBoxResetStats | disabled | 自动重置统计未实现 |
| FPSbox + FPSValue | disabled | FPS 锁定未实现 |
| CutConAuth / CutGameCon | Collapsed | 网络控制未实现 |
| proxyDropBox | disabled | 代理选择未实现 |
| CheckBoxUseWebSocket | disabled | WebSocket 未实现 |
| taskEditorButton | Collapsed | 任务编辑器未实现 |
| MissplayButton | disabled | Missplay 管理未实现 |
| LegendsButton | disabled | 传说排行未实现 |
| Changelog 整个标签页 | 内容为空 | 无数据源 |
| Stats 时间筛选 RadioButtons | disabled | 统计分片未实现 |
| Stats Profile 维度 | disabled | 未实现 |
| Misplays Leaderboard/Edit | 内容为空 | 未实现 |
| Logs 搜索框和筛选按钮 | disabled | 日志筛选未实现 |
| Debug 子标签 Mulligan/Discover/Hidden | 内容为空 | 调试工具未实现 |

## 不需要的 SmartBot 元素

- SmartBot Remote UI 链接和 WebUiLink — 不加入
- SaveLogButton 位置改动 — 移入统计 GroupBox 或保留在原位
- news Label — 不加入
