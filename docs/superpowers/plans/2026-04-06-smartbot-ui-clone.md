# SmartBot UI 布局复刻实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Hearthbot 主窗口从 DockPanel 布局改为 SmartBot 风格的 3 行 Grid 布局，保留所有现有后端绑定，SmartBot 独有控件以空壳占位。

**Architecture:** 纯 UI 重写。MainWindow.xaml 完全重写为 Grid 3行布局。MainViewModel.cs 新增 3 个按钮可见性属性以支持 Inject/Start/Stop 三按钮叠放。MainWindow.xaml.cs 适配新控件名（LogBox→保留，PluginCanvas→保留）。

**Tech Stack:** WPF (.NET 8), XAML, C# MVVM, 无新增 NuGet 依赖（IntegerUpDown 用普通 TextBox 替代以避免引入 Xceed）

---

## 文件清单

| 操作 | 文件 | 职责 |
|------|------|------|
| 重写 | `BotMain/MainWindow.xaml` | 主窗口 XAML — 从 DockPanel 改为 Grid 3行 |
| 修改 | `BotMain/MainWindow.xaml.cs` | 适配新控件名引用 |
| 修改 | `BotMain/MainViewModel.cs` | 新增按钮可见性属性 |

---

### Task 1: ViewModel — 新增按钮可见性属性

**Files:**
- Modify: `BotMain/MainViewModel.cs:283-301`

- [ ] **Step 1: 在 MainViewModel 中新增三个可见性属性**

在 `MainButtonText` 属性附近（约第299行），新增以下属性：

```csharp
public System.Windows.Visibility InjectButtonVisibility =>
    _bot.State == BotState.Idle && !_bot.IsPrepared ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
public System.Windows.Visibility StartButtonVisibility =>
    _bot.State == BotState.Idle && _bot.IsPrepared ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
public System.Windows.Visibility StopButtonVisibility =>
    _bot.State != BotState.Idle ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
```

- [ ] **Step 2: 在 OnStatusChanged 回调中通知新属性**

在 `_bot.OnStatusChanged` 回调（约第87-94行）中，在已有的 `Notify(nameof(MainButtonText))` 后面追加：

```csharp
Notify(nameof(InjectButtonVisibility));
Notify(nameof(StartButtonVisibility));
Notify(nameof(StopButtonVisibility));
```

- [ ] **Step 3: 在 Prepare 完成后也通知可见性变化**

在 `OnMainButton()` 方法中，`_bot.Prepare()` 调用后（约第821行），追加：

```csharp
_bot.Prepare();
Notify(nameof(InjectButtonVisibility));
Notify(nameof(StartButtonVisibility));
Notify(nameof(StopButtonVisibility));
```

注意：`_bot.Prepare()` 是异步的，状态变化通过 `OnStatusChanged` 通知。但在 Idle→Prepared 转换时 State 不变（仍然是 Idle），所以需要在 `_bot.OnPrepared` 或其他回调中通知。检查 `BotService` 是否有 Prepare 完成的回调。如果没有，可以在 `_bot.Prepare()` 后加一个延迟通知或者在状态轮询中处理。

**简化方案**：由于 `_bot.IsPrepared` 状态变化已经触发了 `OnStatusChanged`（通过 pipe 连接状态变化），现有的 `OnStatusChanged` 回调中新增的 Notify 就足够了。

- [ ] **Step 4: 构建验证**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: 编译成功，无错误

- [ ] **Step 5: 提交**

```bash
git add BotMain/MainViewModel.cs
git commit -m "feat(ui): 新增 Inject/Start/Stop 按钮可见性属性"
```

---

### Task 2: MainWindow.xaml — Row 0 顶栏

**Files:**
- Modify: `BotMain/MainWindow.xaml` — 完全重写

- [ ] **Step 1: 替换整个 MainWindow.xaml 的 Window 声明和 Row 0**

把整个文件内容替换为以下开头（后续 Task 会接续填充 Row 1 和 Row 2）：

```xml
<Window x:Class="BotMain.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:BotMain"
        Title="Hearthbot" MinWidth="400" MinHeight="450"
        SnapsToDevicePixels="True"
        WindowStartupLocation="Manual" Background="#F0F0F0"
        FontFamily="Segoe UI" FontSize="12">
    <Window.DataContext>
        <local:MainViewModel/>
    </Window.DataContext>
    <Grid Margin="10,0,10,10">
        <Grid.RowDefinitions>
            <RowDefinition Height="100"/>
            <RowDefinition Height="140"/>
            <RowDefinition Height="300*"/>
        </Grid.RowDefinitions>

        <!-- ===== Row 0: 顶栏 — 标题 + 按钮 + 选择器 ===== -->
        <StackPanel Grid.Row="0" VerticalAlignment="Center">
            <Label x:Name="TitleLabel" Content="{Binding TopStatusText}"
                   VerticalContentAlignment="Center" FontSize="11" Margin="0,0,0,2"/>
            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                <!-- 左侧：按钮区 -->
                <StackPanel Margin="0">
                    <Grid>
                        <Button x:Name="ButtonInject" Content="Inject" FontSize="12"
                                MinWidth="100" MinHeight="32" Padding="10,4"
                                Command="{Binding MainCmd}"
                                Visibility="{Binding InjectButtonVisibility}"/>
                        <Button x:Name="ButtonStart" Content="Start" FontSize="12"
                                MinWidth="75" MinHeight="32" Padding="10,4"
                                Command="{Binding MainCmd}"
                                Visibility="{Binding StartButtonVisibility}"/>
                        <Button x:Name="ButtonStop" Content="Stop" FontSize="12"
                                MinWidth="75" MinHeight="32" Padding="10,4"
                                Command="{Binding MainCmd}"
                                Visibility="{Binding StopButtonVisibility}"/>
                    </Grid>
                    <Button x:Name="ButtonFinish" Content="Finish" FontSize="12"
                            MinWidth="75" MinHeight="32" Margin="0,5,0,0" Padding="10,4"
                            Command="{Binding FinishCmd}"/>
                </StackPanel>
                <!-- 右侧：选择器区 -->
                <StackPanel Margin="10,0,0,0" VerticalAlignment="Center">
                    <!-- 第一行：Mode + Profile -->
                    <StackPanel Orientation="Horizontal" MinHeight="30">
                        <Label Content="Mode :" Margin="0,0,5,0" Padding="2,4,3,4"
                               VerticalContentAlignment="Center" HorizontalContentAlignment="Right" MinWidth="40"/>
                        <ComboBox x:Name="ComboBoxMode" MinWidth="120" MaxWidth="75" MinHeight="25"
                                  FontSize="12" Padding="0"
                                  VerticalAlignment="Center" VerticalContentAlignment="Center"
                                  HorizontalContentAlignment="Center"
                                  SelectedIndex="{Binding ModeIndex}" Margin="0,0,29,0">
                            <ComboBoxItem Content="Standard"/>
                            <ComboBoxItem Content="Wild"/>
                            <ComboBoxItem Content="Battlegrounds"/>
                            <ComboBoxItem Content="Test"/>
                            <ComboBoxItem Content="Arena"/>
                        </ComboBox>
                        <Label Content="Profile :" Margin="0,0,5,0" Padding="2,4,6,4"
                               VerticalContentAlignment="Center" HorizontalContentAlignment="Right" MinWidth="56"/>
                        <ComboBox x:Name="ComboBoxProfile" MinWidth="120" MaxWidth="110" MinHeight="25"
                                  FontSize="12" Padding="0" Margin="0,0,5,0"
                                  VerticalAlignment="Center" VerticalContentAlignment="Center"
                                  HorizontalContentAlignment="Center"
                                  ItemsSource="{Binding ProfileNames}"
                                  SelectedIndex="{Binding SelectedProfileIndex}"
                                  IsEnabled="{Binding LocalRecommendationControlsEnabled}"/>
                        <Button x:Name="ButtonRefreshProfiles" Content="↻" Width="23" Height="23"
                                FontSize="12" VerticalAlignment="Center"
                                HorizontalContentAlignment="Center"
                                Command="{Binding RefreshProfilesCmd}"/>
                        <!-- Battleground Profile — 占位 Collapsed -->
                        <Label Content="Profile :" Visibility="Collapsed" Margin="0,0,5,0"
                               Padding="2,4,6,4" VerticalContentAlignment="Center" MinWidth="56"/>
                        <ComboBox x:Name="ComboBoxProfileBattleground" Visibility="Collapsed"
                                  MinWidth="120" MaxWidth="110" MinHeight="25" FontSize="12"/>
                        <Button x:Name="ButtonRefreshProfilesBattleground" Content="↻"
                                Visibility="Collapsed" Width="23" Height="23"/>
                    </StackPanel>
                    <!-- ��二行：Deck + Mulligan -->
                    <StackPanel Orientation="Horizontal" MinHeight="30" Margin="0,10,0,0">
                        <Label Content="Deck :" Margin="0,0,5,0" Padding="2,4,3,4"
                               VerticalContentAlignment="Center" HorizontalContentAlignment="Right" MinWidth="40"/>
                        <ComboBox x:Name="ComboBoxDeck" MinWidth="120" MaxWidth="110" MinHeight="25"
                                  FontSize="12" Padding="0" Margin="0,0,5,0"
                                  VerticalAlignment="Center" VerticalContentAlignment="Center"
                                  HorizontalContentAlignment="Center"
                                  ItemsSource="{Binding DeckNames}"
                                  SelectedIndex="{Binding SelectedDeckIndex}"/>
                        <Button x:Name="ButtonRefreshDeck" Content="↻" Width="23" Height="23"
                                FontSize="12" VerticalAlignment="Center"
                                Command="{Binding RefreshDecksCmd}" Margin="0,0,1,0"/>
                        <Label Content="Mulligan :" Margin="0,0,5,0" Padding="2,4"
                               VerticalContentAlignment="Center" HorizontalContentAlignment="Right" MinWidth="56"/>
                        <ComboBox x:Name="ComboBoxMulligan" MinWidth="120" MaxWidth="110" MinHeight="25"
                                  FontSize="12" Padding="0" Margin="0,0,5,0"
                                  VerticalAlignment="Center" VerticalContentAlignment="Center"
                                  HorizontalContentAlignment="Center"
                                  ItemsSource="{Binding MulliganNames}"
                                  SelectedIndex="{Binding MulliganProfileIndex}"
                                  IsEnabled="{Binding LocalRecommendationControlsEnabled}"/>
                        <Button x:Name="ButtonRefreshMulligan" Content="↻" Width="23" Height="23"
                                FontSize="12" VerticalAlignment="Center"
                                Command="{Binding RefreshMulliganCmd}"/>
                    </StackPanel>
                </StackPanel>
            </StackPanel>
        </StackPanel>

        <!-- Row 1 和 Row 2 在后续 Task 中填充 -->
    </Grid>
</Window>
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: 编译成功（此时 LogBox 和 PluginCanvas 引用已移除，xaml.cs 中会有编译错误 — 这在 Task 4 中修复）

- [ ] **Step 3: 提交**

```bash
git add BotMain/MainWindow.xaml
git commit -m "feat(ui): Row 0 顶栏 — Inject/Start/Stop 按钮 + Mode/Profile/Deck/Mulligan 选择器"
```

---

### Task 3: MainWindow.xaml — Row 1 中间栏

**Files:**
- Modify: `BotMain/MainWindow.xaml` — 在 Row 0 的 `</StackPanel>` 之后、`</Grid></Window>` 之前插入

- [ ] **Step 1: 插入 Row 1 内容**

在 Row 0 顶栏的 `</StackPanel>` 结束标签之后，替换 `<!-- Row 1 和 Row 2 在后续 Task 中填充 -->` 为：

```xml
        <!-- ===== Row 1: 中间栏 — 统计 + 选项 + 功能按钮 ===== -->
        <Grid Grid.Row="1" Margin="0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="50*"/>
                <ColumnDefinition Width="150"/>
                <ColumnDefinition Width="100"/>
            </Grid.ColumnDefinitions>

            <!-- Col 0: 统计面板 -->
            <Grid Grid.Column="0" Margin="0">
                <GroupBox Header="Statistics" Margin="0" Padding="8,4,8,6">
                    <Grid Margin="0">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="61*"/>
                            <RowDefinition Height="16*"/>
                        </Grid.RowDefinitions>
                        <Label x:Name="Statslabel" Grid.RowSpan="2" Margin="0"
                               VerticalContentAlignment="Center">
                            <Label.Content>
                                <TextBlock TextWrapping="Wrap" FontSize="11" Foreground="#333">
                                    <Run Text="Runtime: "/><Run Text="{Binding RuntimeText, Mode=OneWay}"/>
                                    <LineBreak/>
                                    <Run Text="W: "/><Run Text="{Binding Wins, Mode=OneWay}"/>
                                    <Run Text="  L: "/><Run Text="{Binding Losses, Mode=OneWay}"/>
                                    <Run Text="  C: "/><Run Text="{Binding Concedes, Mode=OneWay}"/>
                                    <LineBreak/>
                                    <Run Text="WR: "/><Run Text="{Binding WinRate, Mode=OneWay}"/><Run Text="%"/>
                                    <Run Text="  Rank: "/><Run Text="{Binding CurrentRankText, Mode=OneWay}"/>
                                </TextBlock>
                            </Label.Content>
                        </Label>
                        <Button Content="Reset stats" Grid.Row="1" Width="75"
                                HorizontalAlignment="Right" Margin="0"
                                VerticalContentAlignment="Stretch"
                                Command="{Binding ResetStatsCmd}"/>
                        <Button x:Name="LegendsButton" Content="Legends" Grid.Row="1" Width="75"
                                HorizontalAlignment="Right" Margin="0,0,75,0"
                                VerticalContentAlignment="Stretch"
                                HorizontalContentAlignment="Center"
                                IsEnabled="False"/>
                    </Grid>
                </GroupBox>
            </Grid>

            <!-- Col 1: 复选框区 -->
            <StackPanel Grid.Column="1" Margin="15,5,0,0">
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <CheckBox x:Name="SessionEnabled" Content="Task" Margin="0"
                              VerticalAlignment="Center" Padding="4,-1,0,0"
                              Visibility="Collapsed"/>
                    <ComboBox x:Name="taskCombo" MinWidth="80" MaxWidth="80" Height="21"
                              Margin="3,0,0,0" VerticalAlignment="Center"
                              Visibility="Collapsed"/>
                </StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,6,0,0">
                    <CheckBox x:Name="CheckBoxCoach" Content="Coach" FontSize="12"
                              Margin="0" VerticalAlignment="Center"
                              VerticalContentAlignment="Center" IsEnabled="False"/>
                    <CheckBox x:Name="CheckBoxOverlay" Content="Overlay" FontSize="12"
                              Margin="6,0,0,0" VerticalAlignment="Center"
                              VerticalContentAlignment="Center" IsEnabled="False"/>
                </StackPanel>
                <CheckBox x:Name="CheckBoxResetStats" Content="Reset stats on start"
                          FontSize="12" Margin="0,8,0,0" Padding="4,-1,0,0"
                          VerticalAlignment="Center" VerticalContentAlignment="Center"
                          IsEnabled="False"/>
                <StackPanel Orientation="Horizontal" Margin="0,3,0,0">
                    <CheckBox x:Name="FPSbox" Content="FPS Lock" Margin="0,0,10,0"
                              VerticalAlignment="Center" IsEnabled="False"/>
                    <TextBox x:Name="FPSValue" Text="60" Width="45" Height="22"
                             VerticalAlignment="Bottom" Margin="0,2,0,0" IsEnabled="False"/>
                </StackPanel>
                <CheckBox x:Name="CutConAuth" Content="Cut con auth" Visibility="Collapsed"/>
                <CheckBox x:Name="CutGameCon" Content="Cut game con" Visibility="Collapsed"/>
                <StackPanel Orientation="Horizontal" Margin="0,6,0,0">
                    <Label Content="Proxy :" Padding="2,4,5,4"/>
                    <ComboBox x:Name="proxyDropBox" Width="92" IsEnabled="False">
                        <ComboBoxItem Content="smartbot.ws" IsSelected="True"/>
                        <ComboBoxItem Content="cnproxy.smartbot.ws"/>
                    </ComboBox>
                </StackPanel>
                <CheckBox x:Name="CheckBoxUseWebSocket" Content="WebSocket"
                          Margin="0,4,0,0" VerticalContentAlignment="Center"
                          FontSize="11" Padding="4,0,0,0" IsChecked="True" IsEnabled="False"/>
            </StackPanel>

            <!-- Col 2: 功能按钮 -->
            <StackPanel Grid.Column="2" Margin="0,5,0,0" HorizontalAlignment="Center">
                <Button x:Name="taskEditorButton" Content="Task Editor"
                        Margin="0,0,0,9" Visibility="Collapsed"/>
                <Button x:Name="MissplayButton" Content="Missplay"
                        Margin="0,0,0,6" Padding="10,4" IsEnabled="False"/>
                <Button Content="Settings" Margin="0,0,0,6" Width="93" Padding="10,4"
                        Command="{Binding SettingsCmd}"/>
                <Button Content="Plugins" Margin="0,0,0,4" Padding="10,4" IsEnabled="False"/>
            </StackPanel>
        </Grid>

        <!-- Row 2 在后续 Task 中填充 -->
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: 可能有 LogBox/PluginCanvas 缺失警告，Task 4 会修复

- [ ] **Step 3: 提交**

```bash
git add BotMain/MainWindow.xaml
git commit -m "feat(ui): Row 1 中间栏 — 统计面板 + 复选框 + 功能按钮"
```

---

### Task 4: MainWindow.xaml — Row 2 TabControl（所有标签页）

**Files:**
- Modify: `BotMain/MainWindow.xaml` — 替换 `<!-- Row 2 在后续 Task 中填充 -->` 为完整 TabControl

- [ ] **Step 1: 插入 Row 2 TabControl**

替换 `<!-- Row 2 在后续 Task 中填充 -->` 为以下内容：

```xml
        <!-- ===== Row 2: 主内容区 — TabControl ===== -->
        <Grid Grid.Row="2" Margin="0">
            <TabControl x:Name="TabControlHeader" Margin="0"
                        VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto"
                        HorizontalContentAlignment="Stretch" VerticalContentAlignment="Stretch"
                        SelectedIndex="4">

                <!-- === Tab: Debug === -->
                <TabItem x:Name="TabDebug" Header="Debug">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="4*"/>
                            <RowDefinition Height="23*"/>
                        </Grid.RowDefinitions>
                        <!-- 调试工具栏 -->
                        <StackPanel Orientation="Horizontal" Margin="0">
                            <Label x:Name="DebugLabel" Content="Seed :" VerticalAlignment="Center"/>
                            <TextBox x:Name="DebugInput" Width="50" VerticalAlignment="Center" MaxLines="1"/>
                            <Button x:Name="DebugButton" Content="Execute" Width="50"
                                    VerticalAlignment="Center" IsEnabled="False"/>
                            <Button x:Name="DebugClearButton" Content="Clear" Width="50"
                                    VerticalAlignment="Center" Margin="0" IsEnabled="False"/>
                            <Button x:Name="DebugClearLogButton" Content="Clear Log" Width="70"
                                    VerticalAlignment="Center" Margin="0" IsEnabled="False"/>
                            <ComboBox x:Name="ComboBoxProfileDebug" Margin="5,0,0,0" FontSize="12"
                                      VerticalAlignment="Center" MinHeight="23" IsEnabled="False"
                                      HorizontalContentAlignment="Center" VerticalContentAlignment="Center"/>
                            <ComboBox x:Name="ComboBoxModeDebug" Margin="5,0,0,0" FontSize="12"
                                      VerticalAlignment="Center" IsEnabled="False"
                                      HorizontalContentAlignment="Center" VerticalContentAlignment="Center"/>
                            <Button x:Name="ButtonRefreshProfilesDebug" Content="↻" FontSize="12"
                                    Margin="5,7,0,7" Width="20" Height="20" IsEnabled="False"/>
                        </StackPanel>
                        <!-- 调试子标签 -->
                        <TabControl x:Name="tabControl2" Grid.Row="1" Margin="0">
                            <!-- Board 调试 -->
                            <TabItem x:Name="TabBoardDEbu" Header="Board">
                                <Grid Margin="0">
                                    <TabControl>
                                        <TabItem Header="Current">
                                            <Grid>
                                                <TextBox x:Name="DebugTextBox" Margin="0" TextWrapping="Wrap"
                                                         IsReadOnly="True" Text="{Binding DebugText, Mode=OneWay}"/>
                                            </Grid>
                                        </TabItem>
                                        <TabItem Header="Before">
                                            <Grid>
                                                <TextBox x:Name="DebugTextBoxBefore" Margin="0"
                                                         TextWrapping="Wrap" IsReadOnly="True"/>
                                            </Grid>
                                        </TabItem>
                                        <TabItem Header="After">
                                            <Grid>
                                                <TextBox x:Name="DebugTextBoxAfter" Margin="0"
                                                         TextWrapping="Wrap" IsReadOnly="True"/>
                                            </Grid>
                                        </TabItem>
                                    </TabControl>
                                </Grid>
                            </TabItem>
                            <!-- Mulligan 测试 (空壳) -->
                            <TabItem Header="Mulligan">
                                <Grid IsEnabled="False">
                                    <ComboBox x:Name="mullTestOne" HorizontalAlignment="Left" Margin="10,10,0,0"
                                              VerticalAlignment="Top" Width="131" IsEditable="True"/>
                                    <ComboBox x:Name="mullTestTwo" HorizontalAlignment="Left" Margin="10,36,0,0"
                                              VerticalAlignment="Top" Width="131" IsEditable="True"/>
                                    <ComboBox x:Name="mullTestThree" HorizontalAlignment="Left" Margin="10,63,0,0"
                                              VerticalAlignment="Top" Width="131" IsEditable="True"/>
                                    <ComboBox x:Name="mullTestFour" HorizontalAlignment="Left" Margin="10,90,0,0"
                                              VerticalAlignment="Top" Width="131" IsEditable="True"/>
                                    <Label Content="Own hero :" HorizontalAlignment="Left" Margin="155,11,0,0"
                                           VerticalAlignment="Top"/>
                                    <Label Content="Enemy hero :" HorizontalAlignment="Left" Margin="156,47,0,0"
                                           VerticalAlignment="Top"/>
                                    <ComboBox x:Name="mullOwnHero" HorizontalAlignment="Left" Margin="239,16,0,0"
                                              VerticalAlignment="Top" Width="70"/>
                                    <ComboBox x:Name="mullEnemyHero" HorizontalAlignment="Left" Margin="239,47,0,0"
                                              VerticalAlignment="Top" Width="70"/>
                                    <Button x:Name="mulltest" Content="Test" HorizontalAlignment="Left"
                                            Margin="228,86,0,0" VerticalAlignment="Top" Width="81"/>
                                    <TextBox x:Name="mullTestRes" HorizontalAlignment="Left" Margin="317,0,0,0"
                                             TextWrapping="Wrap" VerticalAlignment="Center" Width="174" Height="90"/>
                                </Grid>
                            </TabItem>
                            <!-- Discover 测试 (空壳) -->
                            <TabItem Header="Discover">
                                <Grid IsEnabled="False">
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="32*"/>
                                        <RowDefinition Height="178*"/>
                                    </Grid.RowDefinitions>
                                    <StackPanel Orientation="Horizontal" Margin="0">
                                        <Label Content="Choices :" VerticalAlignment="Center"/>
                                        <TextBox x:Name="discoChoiceOne" MinWidth="40" VerticalAlignment="Center"/>
                                        <TextBox x:Name="discoChoiceTwo" MinWidth="40" Margin="5,0,0,0"
                                                 VerticalAlignment="Center"/>
                                        <TextBox x:Name="discoChoiceThree" MinWidth="40" Margin="5,0,0,0"
                                                 VerticalAlignment="Center"/>
                                        <Label Content="Origin :" VerticalAlignment="Center"/>
                                        <TextBox x:Name="discoChoiceOrigin" MinWidth="40" Margin="5,3.82,0,3.78"
                                                 VerticalAlignment="Center"/>
                                        <Button x:Name="discoverDebugFromClipboard" Content="From clipboard"
                                                Width="123" VerticalAlignment="Center" Margin="20,0,0,0"/>
                                    </StackPanel>
                                    <TextBox x:Name="debugDiscoOut" Grid.Row="1" Margin="0" TextWrapping="Wrap"/>
                                </Grid>
                            </TabItem>
                            <!-- Hidden 调试 (空壳) -->
                            <TabItem x:Name="hiddentab" Header="Advanced" Visibility="Hidden">
                                <Grid IsEnabled="False">
                                    <Label Content="Debug tools" HorizontalAlignment="Left"
                                           Margin="10,10,0,0" VerticalAlignment="Top"/>
                                    <Button x:Name="DebugObjectsButton" Content="Objects" Height="20"
                                            Margin="10,53,0,93.4" HorizontalAlignment="Left"/>
                                    <TextBox x:Name="xcoord" Text="xcoord" Margin="10,86,0,62.4"
                                             HorizontalAlignment="Left" VerticalAlignment="Center" MinWidth="20"/>
                                    <TextBox x:Name="ycoord" Text="ycoord" Margin="52,86,0,62.4"
                                             HorizontalAlignment="Left" VerticalAlignment="Center" MinWidth="20"/>
                                    <Button x:Name="sendcoords" Content="Send" Margin="100,86,0,61.4"
                                            HorizontalAlignment="Left" VerticalAlignment="Center"/>
                                </Grid>
                            </TabItem>
                        </TabControl>
                    </Grid>
                </TabItem>

                <!-- === Tab: Changelog (空壳) === -->
                <TabItem x:Name="TabChangelog" Header="Changelog">
                    <TabControl x:Name="TabControlChangelog"
                                HorizontalContentAlignment="Stretch" VerticalContentAlignment="Stretch"
                                VerticalScrollBarVisibility="Auto">
                        <TabItem x:Name="TabChangelogAI" Header="AI">
                            <Grid>
                                <FlowDocumentScrollViewer x:Name="RichTextChangelogAI"
                                    VerticalScrollBarVisibility="Auto" IsToolBarVisible="False" Margin="0"/>
                            </Grid>
                        </TabItem>
                        <TabItem x:Name="TabChangelogBot" Header="Bot">
                            <Grid>
                                <FlowDocumentScrollViewer x:Name="RichTextChangelog"
                                    VerticalScrollBarVisibility="Auto" IsToolBarVisible="False" Margin="0"/>
                            </Grid>
                        </TabItem>
                    </TabControl>
                </TabItem>

                <!-- === Tab: Stats === -->
                <TabItem x:Name="TabStats" Header="Stats">
                    <Grid>
                        <TabControl Margin="0" Grid.RowSpan="2">
                            <!-- 时间维度统计 -->
                            <TabItem Header="Overview">
                                <Grid Margin="10,0,0,0">
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="5*"/>
                                        <RowDefinition Height="33*"/>
                                    </Grid.RowDefinitions>
                                    <StackPanel Orientation="Horizontal" Margin="0,2,0,0" HorizontalAlignment="Left">
                                        <Label Content="Period :" VerticalAlignment="Center"/>
                                        <RadioButton x:Name="StatsOneHour" Content="1h" IsChecked="True"
                                                     VerticalContentAlignment="Center" Margin="0,0,10,0"
                                                     VerticalAlignment="Center" IsEnabled="False"/>
                                        <RadioButton x:Name="StatsTwelveHours" Content="12h"
                                                     VerticalContentAlignment="Center" Margin="0,0,10,0"
                                                     VerticalAlignment="Center" IsEnabled="False"/>
                                        <RadioButton x:Name="StatsTwentyFourHours" Content="24h"
                                                     VerticalContentAlignment="Center" Margin="0,0,10,0"
                                                     VerticalAlignment="Center" IsEnabled="False"/>
                                        <RadioButton x:Name="StatsSeventyTwoHours" Content="72h"
                                                     VerticalContentAlignment="Center" Margin="0,0,10,0"
                                                     VerticalAlignment="Center" IsEnabled="False"/>
                                        <RadioButton x:Name="StatsMonthly" Content="Monthly"
                                                     VerticalContentAlignment="Center" Margin="0,0,10,0"
                                                     VerticalAlignment="Center" IsEnabled="False"/>
                                        <RadioButton x:Name="StatsLastMonthly" Content="Last month"
                                                     VerticalContentAlignment="Center" Margin="0,0,10,0"
                                                     VerticalAlignment="Center" IsEnabled="False"/>
                                    </StackPanel>
                                    <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0">
                                        <TextBox x:Name="ArenaStatsTextbox" TextWrapping="Wrap" Margin="0,10,12,0"
                                                 VerticalAlignment="Top" IsReadOnly="True" FontFamily="Courier New"
                                                 Padding="6,4" Text="{Binding StatsDetailText, Mode=OneWay}"/>
                                        <TextBox x:Name="RankedStdTextbox" TextWrapping="Wrap" Margin="0,10,12,0"
                                                 VerticalAlignment="Top" IsReadOnly="True" FontFamily="Courier New"
                                                 Padding="6,4"/>
                                        <TextBox x:Name="RankedWTextbox" TextWrapping="Wrap" Margin="0,10,12,0"
                                                 VerticalAlignment="Top" IsReadOnly="True" FontFamily="Courier New"
                                                 Padding="6,4"/>
                                        <TextBox x:Name="RankedCTextbox" TextWrapping="Wrap" Margin="0,10,12,0"
                                                 VerticalAlignment="Top" IsReadOnly="True" FontFamily="Courier New"
                                                 Padding="6,4"/>
                                    </StackPanel>
                                </Grid>
                            </TabItem>
                            <!-- Profile维度统计 (空壳) -->
                            <TabItem Header="By profile">
                                <Grid Margin="0" IsEnabled="False">
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="13*"/>
                                        <RowDefinition Height="87*"/>
                                    </Grid.RowDefinitions>
                                    <Label Content="Profile :" HorizontalAlignment="Left" Margin="10,0,0,0"
                                           VerticalAlignment="Top"/>
                                    <ComboBox x:Name="profStatComboBox" HorizontalAlignment="Left"
                                              Margin="100,0,0,0" VerticalAlignment="Top" Width="120"/>
                                    <RadioButton x:Name="profStatClassicBut" Content="Classic"
                                                 HorizontalAlignment="Left" Margin="225,0,0,0"
                                                 VerticalAlignment="Top" Height="21" IsChecked="True"
                                                 VerticalContentAlignment="Center"/>
                                    <RadioButton x:Name="profStatStandardBut" Content="Standard"
                                                 HorizontalAlignment="Left" Margin="284,0,0,0"
                                                 VerticalAlignment="Top" Height="21"
                                                 VerticalContentAlignment="Center"/>
                                    <RadioButton x:Name="profStatWildBut" Content="Wild"
                                                 HorizontalAlignment="Left" Margin="355,0,0,0"
                                                 VerticalAlignment="Top" Height="21"
                                                 VerticalContentAlignment="Center"/>
                                    <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0">
                                        <TextBox x:Name="profStatsTextBoxStd" TextWrapping="Wrap"
                                                 FontSize="11" IsReadOnly="True" TextAlignment="Right"/>
                                        <TextBox x:Name="profStatsTextBoxStd_Copy" TextWrapping="Wrap"
                                                 FontSize="11" IsReadOnly="True"/>
                                    </StackPanel>
                                </Grid>
                            </TabItem>
                        </TabControl>
                    </Grid>
                </TabItem>

                <!-- === Tab: Misplays === -->
                <TabItem x:Name="TabMissplay" Header="Misplays">
                    <Grid>
                        <TabControl x:Name="tabControl" Margin="0,0,0,-1">
                            <!-- Info -->
                            <TabItem x:Name="MissplayInfosTab" Header="Info">
                                <Grid>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="30"/>
                                        <RowDefinition Height="213*"/>
                                    </Grid.RowDefinitions>
                                    <StackPanel x:Name="PanelMissplay" Orientation="Horizontal">
                                        <Label x:Name="labelMissplays"/>
                                        <Button x:Name="button1" Content="Refresh" IsEnabled="False"/>
                                    </StackPanel>
                                    <TextBox Grid.Row="1" Text="{Binding MisplayText, Mode=OneWay}"
                                             IsReadOnly="True" FontFamily="Consolas" FontSize="11"
                                             TextWrapping="Wrap" VerticalScrollBarVisibility="Auto"/>
                                </Grid>
                            </TabItem>
                            <!-- Leaderboard (空壳) -->
                            <TabItem x:Name="MissplayLeaderboard" Header="Leaderboard">
                                <Grid IsEnabled="False">
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="45"/>
                                        <RowDefinition Height="106*"/>
                                    </Grid.RowDefinitions>
                                    <Label x:Name="LabelMissplayStats" Content=""
                                           VerticalContentAlignment="Center" MinHeight="30"/>
                                    <TabControl x:Name="tabControl1" Grid.Row="1" Margin="0">
                                        <TabItem Header="This month">
                                            <Grid>
                                                <RichTextBox x:Name="Leaderboard" IsReadOnly="True"
                                                             FontFamily="Courier New" AllowDrop="False"
                                                             IsUndoEnabled="False" AcceptsReturn="False"
                                                             Margin="0">
                                                    <FlowDocument><Paragraph/></FlowDocument>
                                                </RichTextBox>
                                            </Grid>
                                        </TabItem>
                                        <TabItem Header="Last month">
                                            <Grid>
                                                <RichTextBox x:Name="LeaderboardMonth" IsReadOnly="True"
                                                             FontFamily="Courier New" AllowDrop="False"
                                                             IsUndoEnabled="False" AcceptsReturn="False"
                                                             Margin="0">
                                                    <FlowDocument><Paragraph/></FlowDocument>
                                                </RichTextBox>
                                            </Grid>
                                        </TabItem>
                                    </TabControl>
                                </Grid>
                            </TabItem>
                            <!-- Edit (隐藏空壳) -->
                            <TabItem x:Name="MissplayEditTab" Header="Edit" Visibility="Hidden">
                                <Grid IsEnabled="False">
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="30"/>
                                        <RowDefinition Height="208*"/>
                                    </Grid.RowDefinitions>
                                    <StackPanel Orientation="Horizontal">
                                        <RadioButton Content="Ranked" GroupName="Buttons" VerticalAlignment="Center"/>
                                        <RadioButton Content="Arena" GroupName="Buttons" VerticalAlignment="Center"/>
                                        <RadioButton Content="Practice" GroupName="Buttons" VerticalAlignment="Center"/>
                                        <RadioButton Content="Secret" GroupName="Buttons" VerticalAlignment="Center"/>
                                        <RadioButton Content="Wild" GroupName="Buttons" VerticalAlignment="Center"/>
                                    </StackPanel>
                                    <ListBox x:Name="MissplayEditListBox" Grid.Row="1"
                                             HorizontalContentAlignment="Stretch" VerticalContentAlignment="Stretch"/>
                                </Grid>
                            </TabItem>
                        </TabControl>
                    </Grid>
                </TabItem>

                <!-- === Tab: Logs === -->
                <TabItem x:Name="TabLog" Header="Logs">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="*"/>
                        </Grid.RowDefinitions>
                        <!-- 搜索和筛选工具栏 -->
                        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="2,2,2,0">
                            <TextBox x:Name="LogSearchBox" Width="150" Margin="2"
                                     VerticalContentAlignment="Center" FontSize="11" Padding="4,2"
                                     IsEnabled="False"/>
                            <Button x:Name="LogFilterAll" Content="All" Margin="2" Padding="6,2"
                                    FontSize="11" FontWeight="Bold" IsEnabled="False"/>
                            <Button x:Name="LogFilterErrors" Content="Errors" Margin="2" Padding="6,2"
                                    FontSize="11" IsEnabled="False"/>
                            <Button x:Name="LogFilterActions" Content="Actions" Margin="2" Padding="6,2"
                                    FontSize="11" IsEnabled="False"/>
                            <Button x:Name="LogFilterCurrentGame" Content="Current game" Margin="2"
                                    Padding="6,2" FontSize="11" IsEnabled="False"/>
                            <Button x:Name="LogFilterLastGame" Content="Last game" Margin="2"
                                    Padding="6,2" FontSize="11" IsEnabled="False"/>
                        </StackPanel>
                        <!-- 日志内容 -->
                        <TextBox x:Name="LogBox" Grid.Row="1"
                                 Text="{Binding LogText, Mode=OneWay}" IsReadOnly="True"
                                 Background="White" Foreground="#222" BorderThickness="0"
                                 FontFamily="Consolas" FontSize="11" TextWrapping="Wrap"
                                 VerticalScrollBarVisibility="Auto"/>
                    </Grid>
                </TabItem>

                <!-- === Tab: Board === -->
                <TabItem Header="Board">
                    <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="6">
                        <StackPanel>
                            <Border Background="#FFEBEE" Padding="6,3" Margin="0,0,0,4">
                                <TextBlock Text="{Binding EnemyHeroInfo, StringFormat='Enemy Hero: {0}'}"
                                           FontSize="11" Foreground="#C62828"/>
                            </Border>
                            <TextBlock Text="Enemy Minions:" FontSize="10" Foreground="#888" Margin="0,0,0,2"/>
                            <ItemsControl ItemsSource="{Binding EnemyMinionList}" Margin="0,0,0,6">
                                <ItemsControl.ItemsPanel><ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate></ItemsControl.ItemsPanel>
                                <ItemsControl.ItemTemplate><DataTemplate>
                                    <Border Background="#FFCDD2" CornerRadius="2" Padding="5,2" Margin="1">
                                        <TextBlock Text="{Binding}" FontSize="10"/></Border>
                                </DataTemplate></ItemsControl.ItemTemplate>
                            </ItemsControl>
                            <Separator Margin="0,2"/>
                            <TextBlock Text="Friendly Minions:" FontSize="10" Foreground="#888" Margin="0,4,0,2"/>
                            <ItemsControl ItemsSource="{Binding FriendMinionList}" Margin="0,0,0,6">
                                <ItemsControl.ItemsPanel><ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate></ItemsControl.ItemsPanel>
                                <ItemsControl.ItemTemplate><DataTemplate>
                                    <Border Background="#C8E6C9" CornerRadius="2" Padding="5,2" Margin="1">
                                        <TextBlock Text="{Binding}" FontSize="10"/></Border>
                                </DataTemplate></ItemsControl.ItemTemplate>
                            </ItemsControl>
                            <Border Background="#E8F5E9" Padding="6,3" Margin="0,0,0,4">
                                <TextBlock Text="{Binding FriendHeroInfo, StringFormat='Friendly Hero: {0}'}"
                                           FontSize="11" Foreground="#2E7D32"/>
                            </Border>
                            <TextBlock Text="Hand:" FontSize="10" Foreground="#888" Margin="0,2,0,2"/>
                            <ItemsControl ItemsSource="{Binding HandCardList}">
                                <ItemsControl.ItemsPanel><ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate></ItemsControl.ItemsPanel>
                                <ItemsControl.ItemTemplate><DataTemplate>
                                    <Border Background="#BBDEFB" CornerRadius="2" Padding="5,2" Margin="1">
                                        <TextBlock Text="{Binding}" FontSize="10"/></Border>
                                </DataTemplate></ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </StackPanel>
                    </ScrollViewer>
                </TabItem>

                <!-- === Tab: Plugins === -->
                <TabItem Header="Plugins">
                    <Canvas x:Name="PluginCanvas" Background="White" ClipToBounds="True"/>
                </TabItem>
            </TabControl>

            <!-- SaveLog 按钮浮在 TabControl 上方 -->
            <Button x:Name="SaveLogButton" Content="Save log as..."
                    HorizontalAlignment="Left" VerticalAlignment="Top"
                    Margin="0,10,0,0" Command="{Binding SaveLogCmd}"/>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: 编译成功 — LogBox 和 PluginCanvas 引用都已恢复

- [ ] **Step 3: 提交**

```bash
git add BotMain/MainWindow.xaml
git commit -m "feat(ui): Row 2 TabControl — Debug/Changelog/Stats/Misplays/Logs/Board/Plugins 标签页"
```

---

### Task 5: MainWindow.xaml.cs — 适配新布局

**Files:**
- Modify: `BotMain/MainWindow.xaml.cs`

- [ ] **Step 1: 检查 xaml.cs 中对控件的引用**

当前 `MainWindow.xaml.cs` 引用了：
- `LogBox` — 保留，名称未变
- `PluginCanvas` — 保留，名称未变

两个都在新 XAML 中保留了相同的 `x:Name`，所以 **xaml.cs 不需要改动**。

验证: 检查编译是否通���。

- [ ] **Step 2: 构建验证**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: 编译成功，零错误零警告

- [ ] **Step 3: 运行测试**

Run: `dotnet build BotMain/BotMain.csproj -c Release`
Expected: Release 也能编译通过

- [ ] **Step 4: 提交（如有改动）**

如果 xaml.cs 需要修改：
```bash
git add BotMain/MainWindow.xaml.cs
git commit -m "fix(ui): 适配主窗口新布局的控件引用"
```

---

### Task 6: 最终验证与清理

**Files:**
- All modified files

- [ ] **Step 1: 完整构建**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: 零错误

- [ ] **Step 2: 检查所有绑定是否保留**

在新 XAML 中手动确认以下绑定存在：
- `{Binding TopStatusText}` — TitleLabel
- `{Binding MainCmd}` — ButtonInject/Start/Stop
- `{Binding FinishCmd}` — ButtonFinish
- `{Binding ModeIndex}` — ComboBoxMode
- `{Binding SelectedProfileIndex}` — ComboBoxProfile
- `{Binding ProfileNames}` — ComboBoxProfile.ItemsSource
- `{Binding RefreshProfilesCmd}` — ButtonRefreshProfiles
- `{Binding DeckNames}` — ComboBoxDeck.ItemsSource
- `{Binding SelectedDeckIndex}` — ComboBoxDeck
- `{Binding RefreshDecksCmd}` — ButtonRefreshDeck
- `{Binding MulliganNames}` — ComboBoxMulligan.ItemsSource
- `{Binding MulliganProfileIndex}` — ComboBoxMulligan
- `{Binding RefreshMulliganCmd}` — ButtonRefreshMulligan
- `{Binding RuntimeText}` / `{Binding Wins}` / `{Binding Losses}` etc — Statslabel
- `{Binding ResetStatsCmd}` — Reset stats button
- `{Binding SettingsCmd}` — Settings button
- `{Binding SaveLogCmd}` — SaveLogButton
- `{Binding DebugText}` — DebugTextBox
- `{Binding StatsDetailText}` — ArenaStatsTextbox
- `{Binding MisplayText}` — Misplays Info tab
- `{Binding LogText}` — LogBox
- `{Binding LocalRecommendationControlsEnabled}` — Profile/Mulligan IsEnabled
- Board tab 所有绑定: EnemyHeroInfo, EnemyMinionList, FriendMinionList, FriendHeroInfo, HandCardList
- `{Binding InjectButtonVisibility}` / `{Binding StartButtonVisibility}` / `{Binding StopButtonVisibility}`

- [ ] **Step 3: 检查 Discover 下拉框绑定**

原来 Hearthbot 有 Discover 选择器（Profile/Deck/Mulligan/Discover 四个），但 SmartBot 布局中只有 Mode/Profile + Deck/Mulligan 两行。Discover 下拉需要保留在某处。

**方案**：在 Row 0 的第二行 Mulligan 之后追加 Discover 选择器。

在 `ButtonRefreshMulligan` 之后添加：
```xml
                        <Label Content="Discover :" Margin="0,0,5,0" Padding="2,4"
                               VerticalContentAlignment="Center" HorizontalContentAlignment="Right" MinWidth="56"
                               IsEnabled="{Binding LocalRecommendationControlsEnabled}"/>
                        <ComboBox x:Name="ComboBoxDiscover" MinWidth="100" MaxWidth="100" MinHeight="25"
                                  FontSize="12" Padding="0" Margin="0,0,5,0"
                                  VerticalAlignment="Center" VerticalContentAlignment="Center"
                                  HorizontalContentAlignment="Center"
                                  ItemsSource="{Binding DiscoverNames}"
                                  SelectedIndex="{Binding DiscoverProfileIndex}"
                                  IsEnabled="{Binding LocalRecommendationControlsEnabled}"/>
                        <Button x:Name="ButtonRefreshDiscover" Content="↻" Width="23" Height="23"
                                FontSize="12" VerticalAlignment="Center"
                                Command="{Binding RefreshDiscoverCmd}"/>
```

- [ ] **Step 4: 检查原有"中控"和"更新"按钮**

原 Hearthbot 有"中控"和"更新"按钮。在新布局中把它们放在 Row 1 Col 2 的功能按钮区，Settings 按钮上方：

在 Col 2 StackPanel 的 Settings 按钮之前添加：
```xml
                <Button Content="中控" Margin="0,0,0,6" Padding="10,4"
                        Command="{Binding OpenAccountControllerCmd}" ToolTip="多账号中控"/>
                <Button Content="更新" Margin="0,0,0,6" Padding="10,4"
                        Command="{Binding CheckUpdateCmd}" ToolTip="检查更新"/>
```

- [ ] **Step 5: 构建并提交**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: 零错误

```bash
git add BotMain/MainWindow.xaml BotMain/MainViewModel.cs
git commit -m "fix(ui): 补全 Discover 选择器和中控/更新按钮"
```
