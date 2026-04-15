# 设计文档：MainWindow XAML对齐smartbot布局（只换皮）

## 目标
将BotMain的MainWindow.xaml布局和标签文本对齐smartbot的UI风格，不改动任何C#逻辑和功能。

## 范围
- **只改文件**：`BotMain/MainWindow.xaml`
- **不碰**：MainViewModel.cs、MainWindow.xaml.cs、任何其他C#文件

## 具体变更

### 1. Debug子Tab拆分（核心变更）

**当前结构：**
```
Debug
  └─ tabControl2
       ├─ Board (TabBoardDEbu)
       │    └─ 嵌套TabControl
       │         ├─ Current → DebugTextBox
       │         ├─ Before  → DebugTextBoxBefore
       │         └─ After   → DebugTextBoxAfter
       ├─ Mulligan
       ├─ Discover
       └─ Advanced (hidden)
```

**改为：**
```
Debug
  └─ tabControl2
       ├─ Board   → DebugTextBox (原Current内容)
       ├─ Before  → DebugTextBoxBefore
       ├─ After   → DebugTextBoxAfter
       ├─ Mulligan
       ├─ Discover
       └─ Advanced (hidden)
```

即：把Board内的三级嵌套TabControl移除，Current/Before/After提升为tabControl2的直接子Tab。Board Tab直接显示DebugTextBox（原Current的内容）。

### 2. 标签文本微调

| 控件 | 当前 | 改为 |
|------|------|------|
| LogFilterCurrentGame | "Current game" | "Current Game" |
| LogFilterLastGame | "Last game" | "Last Game" |

### 3. 不变的部分

- 主Tab页顺序（Debug/Changelog/Stats/Misplays/Logs/Board/Plugins）已与smartbot一致，不改
- 顶栏按钮（Inject/Start/Stop/Finish）和选择器（Mode/Profile/Deck/Mulligan/Discover）已一致
- 中间栏功能按钮（中控/更新/Settings/Plugins）保留
- 所有Binding和Command绑定不动
- Board主Tab（可视化面板）、Plugins主Tab保留
- Stats子Tab结构保留
- Misplays子Tab结构保留

## 风险评估

- **风险级别**：低
- **影响范围**：仅XAML布局
- **回滚方式**：git revert
- **需要验证**：Debug子Tab拆分后DebugTextBox/Before/After的数据绑定仍然正常工作（这些控件是x:Name引用，code-behind直接赋值，拆分层级不影响引用）
