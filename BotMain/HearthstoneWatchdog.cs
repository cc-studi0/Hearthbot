using System;
using System.Diagnostics;
using System.Threading;

namespace BotMain
{
    public class HearthstoneWatchdog : IDisposable
    {
        public enum WatchdogState
        {
            Disabled,
            NotRunning,
            Launching,
            WaitingPayload,
            Connected,
            Running,
            Recovering
        }

        // ── 配置 ──
        public int TickIntervalMs { get; set; } = 3000;
        public int LaunchTimeoutSeconds { get; set; } = 120;
        public int PayloadTimeoutSeconds { get; set; } = 120;
        public int NotRespondingTimeoutSeconds { get; set; } = 30;
        public int GameTimeoutSeconds { get; set; } = 300;
        public int MaxConsecutiveFailures { get; set; } = 5;
        public int RecoveryCooldownSeconds { get; set; } = 5;
        public int BattleNetRestartThreshold { get; set; } = 3;

        // ── 外部回调 ──
        public Func<bool> IsBotRunning { get; set; }
        public Func<bool> IsPipeConnected { get; set; }
        public Func<DateTime?> GetLastEffectiveAction { get; set; }
        public Action RequestBotStop { get; set; }
        public Action RequestBotStart { get; set; }
        public Action SuspendMonitors { get; set; }
        public Action ResumeMonitors { get; set; }
        public Action<string> Log { get; set; }

        // ── 事件 ──
        public event Action<WatchdogState> StateChanged;

        // ── 内部状态 ──
        private volatile bool _active;
        private Thread _thread;
        private WatchdogState _state = WatchdogState.Disabled;
        private DateTime _stateEnteredUtc;
        private DateTime? _notRespondingSinceUtc;
        private int _consecutiveFailures;
        private bool _wasRunningBeforeRecovery;

        public WatchdogState CurrentState => _state;

        public void Start()
        {
            if (_active) return;
            _active = true;
            _consecutiveFailures = 0;
            TransitionTo(WatchdogState.NotRunning);

            _thread = new Thread(TickLoop)
            {
                Name = "HearthstoneWatchdog",
                IsBackground = true
            };
            _thread.Start();
            Log?.Invoke("[Watchdog] 看门狗已启动");
        }

        public void Stop()
        {
            _active = false;
            TransitionTo(WatchdogState.Disabled);
            Log?.Invoke("[Watchdog] 看门狗已停止");
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// 外部通知：炉石已由其他逻辑启动（如 BotService.TryReconnectLoop），
        /// Watchdog 应同步状态而非重复启动。
        /// </summary>
        public void NotifyHearthstoneLaunched()
        {
            if (_state == WatchdogState.NotRunning || _state == WatchdogState.Launching)
                TransitionTo(WatchdogState.WaitingPayload);
        }

        private void TickLoop()
        {
            while (_active)
            {
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"[Watchdog] tick 异常: {ex.Message}");
                }

                Thread.Sleep(TickIntervalMs);
            }
        }

        private void Tick()
        {
            if (_state == WatchdogState.Disabled) return;

            bool hearthstoneAlive = IsHearthstoneAlive();

            // ── 1. WerFault 崩溃对话框检测 ──
            if (DetectWerFault())
            {
                Log?.Invoke("[Watchdog] 检测到 WerFault 崩溃弹窗");
                BattleNetWindowManager.KillWerFault(Log);
                if (_state != WatchdogState.NotRunning && _state != WatchdogState.Recovering)
                {
                    EnterRecovering("WerFault 崩溃弹窗");
                    return;
                }
            }

            // ── 2. 进程消失检测 ──
            if (!hearthstoneAlive && _state is WatchdogState.WaitingPayload
                                          or WatchdogState.Connected
                                          or WatchdogState.Running)
            {
                Log?.Invoke("[Watchdog] 炉石进程已消失");
                EnterRecovering("进程消失");
                return;
            }

            // ── 3. 进程无响应检测 ──
            if (hearthstoneAlive && !BattleNetWindowManager.IsHearthstoneResponding())
            {
                if (_notRespondingSinceUtc == null)
                {
                    _notRespondingSinceUtc = DateTime.UtcNow;
                }
                else if ((DateTime.UtcNow - _notRespondingSinceUtc.Value).TotalSeconds >= NotRespondingTimeoutSeconds)
                {
                    Log?.Invoke($"[Watchdog] 炉石持续无响应 {NotRespondingTimeoutSeconds}s");
                    EnterRecovering("进程无响应");
                    return;
                }
            }
            else
            {
                _notRespondingSinceUtc = null;
            }

            // ── 4. 状态机流转 ──
            double secondsInState = (DateTime.UtcNow - _stateEnteredUtc).TotalSeconds;

            switch (_state)
            {
                case WatchdogState.NotRunning:
                    if (hearthstoneAlive)
                    {
                        TransitionTo(WatchdogState.WaitingPayload);
                    }
                    else if (IsBotRunning?.Invoke() == true)
                    {
                        // Bot 已启动但炉石不在，自动拉起游戏
                        Log?.Invoke("[Watchdog] Bot 已启动但炉石未运行，自动启动炉石...");
                        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(LaunchTimeoutSeconds));
                        try
                        {
                            var result = BattleNetWindowManager.LaunchHearthstoneCmd(Log, cts.Token, LaunchTimeoutSeconds)
                                .GetAwaiter().GetResult();
                            if (result.Success)
                            {
                                Log?.Invoke("[Watchdog] 炉石启动成功");
                                TransitionTo(WatchdogState.WaitingPayload);
                            }
                            else
                            {
                                Log?.Invoke($"[Watchdog] 炉石启动失败: {result.Message}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log?.Invoke($"[Watchdog] 启动异常: {ex.Message}");
                        }
                        finally
                        {
                            cts.Dispose();
                        }
                    }
                    break;

                case WatchdogState.Launching:
                    if (hearthstoneAlive)
                    {
                        TransitionTo(WatchdogState.WaitingPayload);
                    }
                    else if (secondsInState >= LaunchTimeoutSeconds)
                    {
                        Log?.Invoke($"[Watchdog] 启动超时 ({LaunchTimeoutSeconds}s)");
                        EnterRecovering("启动超时");
                    }
                    break;

                case WatchdogState.WaitingPayload:
                    if (IsPipeConnected?.Invoke() == true)
                    {
                        TransitionTo(WatchdogState.Connected);
                    }
                    else if (secondsInState >= PayloadTimeoutSeconds)
                    {
                        Log?.Invoke($"[Watchdog] Payload 连接超时 ({PayloadTimeoutSeconds}s)");
                        EnterRecovering("Payload 连接超时");
                    }
                    break;

                case WatchdogState.Connected:
                    if (_wasRunningBeforeRecovery)
                    {
                        _wasRunningBeforeRecovery = false;
                        Log?.Invoke("[Watchdog] 恢复完成，自动重启 Bot");
                        try { RequestBotStart?.Invoke(); } catch { }
                    }
                    if (IsBotRunning?.Invoke() == true)
                    {
                        _consecutiveFailures = 0;
                        TransitionTo(WatchdogState.Running);
                    }
                    break;

                case WatchdogState.Running:
                    if (IsBotRunning?.Invoke() != true)
                    {
                        TransitionTo(WatchdogState.Connected);
                    }
                    else
                    {
                        var lastAction = GetLastEffectiveAction?.Invoke();
                        if (lastAction.HasValue &&
                            (DateTime.UtcNow - lastAction.Value).TotalSeconds >= GameTimeoutSeconds)
                        {
                            Log?.Invoke($"[Watchdog] 游戏内超时: {GameTimeoutSeconds}s 无有效操作");
                            EnterRecovering("游戏内超时");
                        }
                    }
                    break;

                case WatchdogState.Recovering:
                    break;
            }
        }

        /// <summary>
        /// 供外部监控器（MemoryMonitor / NetworkMonitor）触发恢复流程。
        /// </summary>
        public void TriggerRecovery(string reason)
        {
            if (_state == WatchdogState.Recovering || _state == WatchdogState.Disabled)
                return;
            EnterRecovering(reason);
        }

        private void EnterRecovering(string reason)
        {
            // Bot 未运行时不走完整恢复流程，仅清理残留后回到 NotRunning
            if (IsBotRunning?.Invoke() != true)
            {
                Log?.Invoke($"[Watchdog] Bot 未运行，跳过恢复流程 (原因: {reason})");
                BattleNetWindowManager.KillWerFault(Log);
                BattleNetWindowManager.KillHearthstone(Log);
                TransitionTo(WatchdogState.NotRunning);
                return;
            }

            TransitionTo(WatchdogState.Recovering);
            _consecutiveFailures++;
            Log?.Invoke($"[Watchdog] 开始恢复流程 (原因: {reason}, 连续失败: {_consecutiveFailures}/{MaxConsecutiveFailures})");

            if (_consecutiveFailures > MaxConsecutiveFailures)
            {
                Log?.Invoke($"[Watchdog] 连续失败 {_consecutiveFailures} 次，超过上限 {MaxConsecutiveFailures}，停止看门狗");
                Stop();
                return;
            }

            // 1. 暂停监控器，防止恢复期间误判触发二次恢复
            try { SuspendMonitors?.Invoke(); } catch { }

            // 2. 记录恢复前 Bot 是否在运行，恢复后自动重启
            _wasRunningBeforeRecovery = IsBotRunning?.Invoke() == true;
            try { RequestBotStop?.Invoke(); } catch { }

            // 3. 杀 WerFault
            BattleNetWindowManager.KillWerFault(Log);

            // 4. 杀 Hearthstone
            BattleNetWindowManager.KillHearthstone(Log);
            WaitForHearthstoneExit(15);

            // 5. 冷却
            Thread.Sleep(RecoveryCooldownSeconds * 1000);

            // 6. 连续失败多次则重启战网
            if (_consecutiveFailures >= BattleNetRestartThreshold)
            {
                Log?.Invoke("[Watchdog] 连续失败过多，重启 Battle.net");
                BattleNetWindowManager.KillBattleNet(Log);
                Thread.Sleep(10000);
            }

            // 7. 启动炉石
            Log?.Invoke("[Watchdog] 通过命令行启动炉石...");
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(LaunchTimeoutSeconds));
            try
            {
                var result = BattleNetWindowManager.LaunchHearthstoneCmd(Log, cts.Token, LaunchTimeoutSeconds)
                    .GetAwaiter().GetResult();

                if (result.Success)
                {
                    Log?.Invoke("[Watchdog] 炉石启动成功");
                    TransitionTo(WatchdogState.WaitingPayload);
                }
                else
                {
                    Log?.Invoke($"[Watchdog] 炉石启动失败: {result.Message}");
                    TransitionTo(WatchdogState.NotRunning);
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[Watchdog] 启动异常: {ex.Message}");
                TransitionTo(WatchdogState.NotRunning);
            }
            finally
            {
                cts.Dispose();
            }

            // 8. 恢复监控器（重置状态后再启动，避免残留计时器误判）
            try { ResumeMonitors?.Invoke(); } catch { }
        }

        private void TransitionTo(WatchdogState newState)
        {
            if (_state == newState) return;
            _state = newState;
            _stateEnteredUtc = DateTime.UtcNow;
            try { StateChanged?.Invoke(newState); } catch { }
        }

        private static bool IsHearthstoneAlive()
        {
            return Process.GetProcessesByName("Hearthstone").Length > 0;
        }

        private static bool DetectWerFault()
        {
            return Process.GetProcessesByName("WerFault").Length > 0;
        }

        private static void WaitForHearthstoneExit(int maxSeconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(maxSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (Process.GetProcessesByName("Hearthstone").Length == 0) return;
                Thread.Sleep(1000);
            }
        }
    }
}
