using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SmartBot.Plugins.API;
using SmartBotAPI.Stats;

namespace BotMain
{
    /// <summary>
    /// 澶勭悊 SBAPI Bot 绫荤殑闈欐€佸瓧娈佃疆璇㈠拰鐘舵€佸悓姝?    /// Bot 绫荤殑鏂规硶閫氳繃璁剧疆闈欐€?flag 瀛楁閫氱煡瀹夸富锛屽涓昏疆璇㈠悗鎵ц骞舵竻闄?flag
    /// </summary>
    public class BotApiHandler
    {
        private readonly Action<string> _log;
        private readonly BotService _service;
        private const bool ForwardProfileBotLogs = false;
        private readonly Dictionary<string, FieldInfo> _botFields = new();

        public BotApiHandler(BotService service, Action<string> log)
        {
            _service = service;
            _log = log;
            CacheBotFields();
            InitBotLog();
        }

        private void CacheBotFields()
        {
            var botType = typeof(Bot);
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in botType.GetFields(flags))
                _botFields[field.Name] = field;
        }

        private T GetField<T>(string name, T fallback = default)
        {
            if (_botFields.TryGetValue(name, out var fi))
            {
                try { return (T)fi.GetValue(null); }
                catch { }
            }
            return fallback;
        }

        private void SetField<T>(string name, T value)
        {
            if (_botFields.TryGetValue(name, out var fi))
            {
                try { fi.SetValue(null, value); }
                catch { }
            }
        }

        private bool ConsumeFlag(string flagName)
        {
            if (!GetField(flagName, false)) return false;
            SetField(flagName, false);
            return true;
        }

        private void InitBotLog()
        {
            // Bot.Log() polling is handled in PollLogs().
        }

        /// <summary>
        /// 鍚屾寤惰繜鏁版嵁鍒?Bot._averageLatency/_minLatency/_maxLatency
        /// </summary>
        public void SetLatency(int avg, int min, int max)
        {
            SetField("_averageLatency", avg);
            SetField("_minLatency", min);
            SetField("_maxLatency", max);
        }

        /// <summary>
        /// 鍚屾瀵规墜 ID 鍒?Bot._currentOpponent/_lastOpponent
        /// </summary>
        public void SetOpponentId(long current, long previous)
        {
            SetField("_currentOpponent", current);
            SetField("_lastOpponent", previous);
        }

        /// <summary>
        /// 鏇存柊 Bot 绫荤殑鍙鐘舵€佸瓧娈碉紙渚涙煡璇㈡柟娉曚娇鐢級
        /// </summary>
        public void UpdateBotState(
            bool isRunning,
            string currentProfile,
            string currentMulligan,
            string currentDeck,
            int modeIndex,
            List<string> profileNames,
            List<string> mulliganNames,
            List<string> discoverNames)
        {
            SetField("_botRunning", isRunning);
            SetField("_currentProfile", currentProfile ?? string.Empty);
            SetField("_currentMulligan", currentMulligan ?? string.Empty);

            var mode = ModeFromIndex(modeIndex);
            SetField("_currentMode", mode);

            if (profileNames != null)
                SetField("_profiles", profileNames);
            if (mulliganNames != null)
                SetField("_mulliganProfiles", mulliganNames);
            if (discoverNames != null)
                SetField("_discoverProfiles", discoverNames);
        }

        /// <summary>
        /// 鍚屾褰撳墠妫嬬洏鍒?Bot._currentBoard锛堝甫閿侊級
        /// </summary>
        public void SetCurrentBoard(Board board)
        {
            var locker = GetField<object>("Boardlocker");
            if (locker != null)
            {
                lock (locker) { SetField("_currentBoard", board); }
            }
            else
            {
                SetField("_currentBoard", board);
            }
        }

        /// <summary>
        /// 鍚屾褰撳墠鍦烘櫙鍒?Bot._currentScene
        /// </summary>
        public void SetCurrentScene(Bot.Scene scene)
        {
            SetField("_currentScene", scene);
        }

        /// <summary>
        /// 鍚屾鎻掍欢鍒楄〃鍒?Bot._plugins
        /// </summary>
        public void SetPlugins(List<SmartBot.Plugins.Plugin> plugins)
        {
            SetField("_plugins", plugins);
        }

        /// <summary>
        /// 鍚屾鍗＄粍鍒楄〃鍒?Bot._decks
        /// </summary>
        public void SetDecks(List<Deck> decks)
        {
            SetField("_decks", decks);
        }

        /// <summary>
        /// 鍚屾鍖烘湇鍒?Bot._region
        /// </summary>
        public void SetRegion(SmartBotAPI.Stats.BnetRegion region)
        {
            SetField("_region", region);
            SetField("_regionset", true);
        }

        /// <summary>
        /// 鍚屾 Archetype 鍒楄〃
        /// </summary>
        public void SetArchetypes(List<SmartBotAPI.Plugins.API.Archetype> archetypes)
        {
            SetField("_Archetypes", archetypes);
        }

        /// <summary>
        /// 鍚屾 Arena 绛栫暐鍒楄〃
        /// </summary>
        public void SetArenaProfiles(List<string> names)
        {
            SetField("_arenaProfiles", names);
        }

        /// <summary>
        /// 杞 Bot 绫荤殑鎵€鏈?flag 瀛楁锛屾墽琛屽搴旀搷浣?        /// 鍦?MainLoop 姣忔杩唬涓皟鐢?        /// </summary>
        public void Poll()
        {
            PollLogs();
            PollControlFlags();
            PollConfigFlags();
            PollRefreshFlags();
            PollSettingFlags();
            PollSocialFlags();
            PollEmoteFlags();
        }

        private void PollLogs()
        {
            if (!ConsumeFlag("_log")) return;
            var queue = GetField<Queue<string>>("_logValue");
            if (queue == null) return;
            lock (queue)
            {
                while (queue.Count > 0)
                {
                    var msg = queue.Dequeue();
                    if (ForwardProfileBotLogs)
                        _log?.Invoke($"[Bot.Log] {msg}");
                }
            }
        }

        private void PollControlFlags()
        {
            if (ConsumeFlag("_startBot"))
            {
                _log?.Invoke("[BotAPI] StartBot requested");
                _service.Start();
            }

            if (ConsumeFlag("_stopBot"))
            {
                _log?.Invoke("[BotAPI] StopBot requested");
                _service.Stop();
            }

            if (ConsumeFlag("_suspendBot"))
            {
                _log?.Invoke("[BotAPI] SuspendBot");
                _service.Suspend();
            }

            if (ConsumeFlag("_resumeBot"))
            {
                _log?.Invoke("[BotAPI] ResumeBot");
                _service.Resume();
            }

            if (ConsumeFlag("_finishBot"))
            {
                _log?.Invoke("[BotAPI] Finish requested");
                _service.FinishAfterGame();
            }

            if (ConsumeFlag("_closeHs"))
            {
                _log?.Invoke("[BotAPI] CloseHs");
                _service.CloseHs();
            }

            if (ConsumeFlag("_closeBot"))
            {
                _log?.Invoke("[BotAPI] CloseBot requested");
                _service.Stop();
            }

            if (ConsumeFlag("_concede"))
            {
                _log?.Invoke("[BotAPI] Concede requested");
                _service.RequestConcede();
            }
        }

        private void PollConfigFlags()
        {
            if (ConsumeFlag("_changeDeck"))
            {
                var val = GetField("_changeDeckValue", string.Empty);
                _log?.Invoke($"[BotAPI] ChangeDeck: {val}");
                _service.SetDeckByName(val);
            }

            if (ConsumeFlag("_changeProfile"))
            {
                var val = GetField("_changeProfileValue", string.Empty);
                _log?.Invoke($"[BotAPI] ChangeProfile: {val}");
                _service.SetProfileByName(val);
            }

            if (ConsumeFlag("_changeMulligan"))
            {
                var val = GetField("_changeMulliganValue", string.Empty);
                _log?.Invoke($"[BotAPI] ChangeMulligan: {val}");
                _service.SetMulliganByName(val);
            }

            if (ConsumeFlag("_changeMode"))
            {
                var val = GetField("_changeModeValue", default(Bot.Mode));
                _log?.Invoke($"[BotAPI] ChangeMode: {val}");
                _service.SetModeFromApi(val);
            }

            if (ConsumeFlag("_changeAfterArenaMode"))
            {
                var val = GetField("_changeAfterArenaModeValue", default(Bot.Mode));
                _log?.Invoke($"[BotAPI] ChangeAfterArenaMode: {val}");
                _service.SetAfterArenaMode(val);
            }

            if (ConsumeFlag("_changeArenaProfile"))
            {
                var val = GetField("_changeArenaProfileValue", string.Empty);
                _log?.Invoke($"[BotAPI] ChangeArenaProfile: {val}");
                _service.SetArenaProfileByName(val);
            }

            if (ConsumeFlag("_changeDiscoverProfile"))
            {
                var val = GetField("_changeDiscoverProfileValue", string.Empty);
                _log?.Invoke($"[BotAPI] ChangeDiscoverProfile: {val}");
                _service.SetDiscoverByName(val);
            }
        }

        private void PollRefreshFlags()
        {
            if (ConsumeFlag("_refreshDecks"))
            {
                _log?.Invoke("[BotAPI] RefreshDecks");
                _service.RefreshDecks();
            }

            if (ConsumeFlag("_refreshProfiles"))
            {
                _log?.Invoke("[BotAPI] RefreshProfiles");
                _service.RefreshProfiles();
            }

            if (ConsumeFlag("_refreshMulligans"))
            {
                _log?.Invoke("[BotAPI] RefreshMulliganProfiles");
                _service.RefreshMulliganProfiles();
            }

            if (ConsumeFlag("_refresArenaProfiles"))
            {
                _log?.Invoke("[BotAPI] RefreshArenaProfiles");
                _service.RefreshArenaProfiles();
            }

            if (ConsumeFlag("_refresDiscoverProfiles"))
            {
                _log?.Invoke("[BotAPI] RefreshDiscoverProfiles");
                _service.RefreshDiscoverProfiles();
            }

            if (ConsumeFlag("_refreshArchetypes"))
            {
                _log?.Invoke("[BotAPI] RefreshArchetypes");
                _service.RefreshArchetypes();
            }

            if (ConsumeFlag("_reloadPlugins"))
            {
                _log?.Invoke("[BotAPI] ReloadPlugins");
                _service.ReloadPlugins();
            }
        }

        private void PollSettingFlags()
        {
            if (ConsumeFlag("_setMinRank"))
                _service.SetMinRank(GetField("_setMinRankValue", 0));

            if (ConsumeFlag("_setMaxRank"))
                _service.SetMaxRank(GetField("_setMaxRankValue", 0));

            if (ConsumeFlag("_setMaxWins"))
                _service.SetMaxWins(GetField("_setMaxWinsValue", 0));

            if (ConsumeFlag("_setMaxLosses"))
                _service.SetMaxLosses(GetField("_setMaxLossesValue", 0));

            if (ConsumeFlag("_setMaxHours"))
                _service.SetMaxHours(GetField("_setMaxHoursValue", 0.0));

            if (ConsumeFlag("_setCloseHs"))
                _service.SetCloseHs(GetField("_setCloseHsValue", false));

            if (ConsumeFlag("_setAutoConcede"))
                _service.SetAutoConcede(GetField("_setAutoConcedeValue", false));

            if (ConsumeFlag("_setAutoConcedeAlternative"))
                _service.SetAutoConcedeAlternativeMode(GetField("_setAutoConcedeAlternativeValue", false));

            if (ConsumeFlag("_setAutoConcedeMaxRank"))
                _service.SetAutoConcedeMaxRank(GetField("_setAutoConcedeMaxRankValue", 0));

            if (ConsumeFlag("_setConcedeWhenLethal"))
                _service.SetConcedeWhenLethal(GetField("_setConcedeWhenLethalValue", false));

            if (ConsumeFlag("_setThinkingRoutineEnabled"))
                _service.SetThinkingRoutineEnabled(GetField("_setThinkingRoutineEnabledValue", false));

            if (ConsumeFlag("_setHoverRoutineEnabled"))
                _service.SetHoverRoutineEnabled(GetField("_setHoverRoutineEnabledValue", false));

            if (ConsumeFlag("_setLatencySamplingRate"))
                _service.SetLatencySamplingRate(GetField("_setLatencySamplingRateValue", 20000));
        }

        private void PollSocialFlags()
        {
            if (ConsumeFlag("_acceptRequest"))
                _log?.Invoke("[BotAPI] AcceptFriendRequest (stub)");

            if (ConsumeFlag("_declineRequest"))
                _log?.Invoke("[BotAPI] DeclineFriendRequest (stub)");

            if (ConsumeFlag("_removeFriend"))
                _log?.Invoke("[BotAPI] RemoveFriend (stub)");

            if (ConsumeFlag("_whisperFriend"))
                _log?.Invoke("[BotAPI] WhisperToFriend (stub)");

            if (ConsumeFlag("_switchAccount"))
                _log?.Invoke("[BotAPI] SwitchAccount (stub - not supported)");

            if (ConsumeFlag("_cancelQuest"))
                _log?.Invoke("[BotAPI] CancelQuest (stub)");

            if (ConsumeFlag("_startRelogger"))
                _log?.Invoke("[BotAPI] StartRelogger (stub - not supported)");

            if (ConsumeFlag("_stopRelogger"))
                _log?.Invoke("[BotAPI] StopRelogger (stub - not supported)");
        }

        private void PollEmoteFlags()
        {
            if (ConsumeFlag("_sendEmote"))
            {
                var val = GetField("_sendEmoteValue", default(Bot.EmoteType));
                _log?.Invoke($"[BotAPI] SendEmote: {val} (stub)");
            }

            if (ConsumeFlag("_squelch"))
                _log?.Invoke("[BotAPI] Squelch (stub)");

            if (ConsumeFlag("_unsquelch"))
                _log?.Invoke("[BotAPI] Unsquelch (stub)");

            if (ConsumeFlag("_hover"))
                _log?.Invoke("[BotAPI] Hover (stub)");

            if (ConsumeFlag("_arrow"))
                _log?.Invoke("[BotAPI] Arrow (stub)");

            if (ConsumeFlag("_sendRandomArrowFromHand"))
                _log?.Invoke("[BotAPI] SendRandomArrowFromHand (stub)");

            if (ConsumeFlag("_sendRandomArrowFromBoard"))
                _log?.Invoke("[BotAPI] SendRandomArrowFromBoard (stub)");

            if (ConsumeFlag("_sendRandomHoverOnHand"))
                _log?.Invoke("[BotAPI] SendRandomHoverOnHand (stub)");

            if (ConsumeFlag("_sendRandomHoverOnFriendlyMinions"))
                _log?.Invoke("[BotAPI] SendRandomHoverOnFriendlyMinions (stub)");

            if (ConsumeFlag("_sendRandomHoverOnEnemyMinions"))
                _log?.Invoke("[BotAPI] SendRandomHoverOnEnemyMinions (stub)");
        }

        private static Bot.Mode ModeFromIndex(int idx) => idx switch
        {
            0 => Bot.Mode.Standard,
            1 => Bot.Mode.Wild,
            2 => Bot.Mode.ArenaAuto,
            3 => Bot.Mode.Casual,
            _ => Bot.Mode.Standard
        };
    }
}

