using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SmartBot.Plugins;
using SmartBot.Plugins.API;
using SbAction = SmartBot.Plugins.API.Actions.Action;

namespace BotMain
{
    /// <summary>
    /// 加载和管理 SBAPI Plugin 实例的生命周期，触发事件回调
    /// </summary>
    public class PluginSystem
    {
        private readonly Action<string> _log;
        private readonly List<Plugin> _plugins = new();
        private bool _started;

        public PluginSystem(Action<string> log)
        {
            _log = log;
        }

        public List<Plugin> Plugins => _plugins;

        /// <summary>
        /// 从 Plugins 目录加载所有插件脚本
        /// </summary>
        public void LoadPlugins(string pluginDir, string[] compilerRefs)
        {
            _plugins.Clear();
            if (!Directory.Exists(pluginDir))
            {
                _log?.Invoke($"[PluginSystem] Plugin directory not found: {pluginDir}");
                return;
            }

            try
            {
                var loader = new ProfileLoader(pluginDir, compilerRefs);
                var assemblies = loader.CompileAll();

                if (loader.Errors.Count > 0)
                {
                    foreach (var err in loader.Errors)
                        _log?.Invoke($"  [PluginCompileError] {err}");
                }

                var instances = loader.LoadInstances<Plugin>(assemblies);
                _plugins.AddRange(instances);
                _log?.Invoke($"[PluginSystem] Loaded {_plugins.Count} plugin(s).");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[PluginSystem] Load failed: {ex.Message}");
            }
        }

        public void FireOnPluginCreated()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnPluginCreated", () => p.OnPluginCreated());
        }

        public void FireOnStarted()
        {
            _started = true;
            foreach (var p in _plugins)
                SafeCall(p, "OnStarted", () => p.OnStarted());
        }

        public void FireOnStopped()
        {
            if (!_started) return;
            _started = false;
            foreach (var p in _plugins)
                SafeCall(p, "OnStopped", () => p.OnStopped());
        }

        public void FireOnTick()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnTick", () => p.OnTick());
        }

        public void FireOnTurnBegin()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnTurnBegin", () => p.OnTurnBegin());
        }

        public void FireOnTurnEnd()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnTurnEnd", () => p.OnTurnEnd());
        }

        public void FireOnSimulation()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnSimulation", () => p.OnSimulation());
        }

        public void FireOnGameBegin()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnGameBegin", () => p.OnGameBegin());
        }

        public void FireOnGameEnd()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnGameEnd", () => p.OnGameEnd());
        }

        public void FireOnLethal()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnLethal", () => p.OnLethal());
        }

        public void FireOnVictory()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnVictory", () => p.OnVictory());
        }

        public void FireOnDefeat()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnDefeat", () => p.OnDefeat());
        }

        public void FireOnConcede()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnConcede", () => p.OnConcede());
        }

        public void FireOnGoldAmountChanged()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnGoldAmountChanged", () => p.OnGoldAmountChanged());
        }

        public void FireOnArenaEnd()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnArenaEnd", () => p.OnArenaEnd());
        }

        public void FireOnAllQuestsCompleted()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnAllQuestsCompleted", () => p.OnAllQuestsCompleted());
        }

        public void FireOnQuestCompleted()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnQuestCompleted", () => p.OnQuestCompleted());
        }

        public void FireOnDecklistUpdate()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnDecklistUpdate", () => p.OnDecklistUpdate());
        }

        public void FireOnGameResolutionUpdate(int w, int h)
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnGameResolutionUpdate", () => p.OnGameResolutionUpdate(w, h));
        }

        public void FireOnHandleMulligan(
            List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnHandleMulligan",
                    () => p.OnHandleMulligan(choices, opponentClass, ownClass));
        }

        public void FireOnMulliganCardsReplaced(List<Card.Cards> replaced)
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnMulliganCardsReplaced",
                    () => p.OnMulliganCardsReplaced(replaced));
        }

        public void FireOnReceivedEmote(Bot.EmoteType emote)
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnReceivedEmote", () => p.OnReceivedEmote(emote));
        }

        public void FireOnDataContainerUpdated()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnDataContainerUpdated", () => p.OnDataContainerUpdated());
        }

        public void FireOnActionExecute(SbAction action)
        {
            if (action == null) return;
            foreach (var p in _plugins)
                SafeCall(p, "OnActionExecute", () => p.OnActionExecute(action));
        }

        public void FireOnActionStackReceived(List<SbAction> actions)
        {
            if (actions == null || actions.Count == 0) return;
            foreach (var p in _plugins)
                SafeCall(p, "OnActionStackReceived", () => p.OnActionStackReceived(actions));
        }

        public void FireOnInjection()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnInjection", () => p.OnInjection());
        }

        public void FireOnArenaTicketPurchaseFailed()
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnArenaTicketPurchaseFailed", () => p.OnArenaTicketPurchaseFailed());
        }

        public void FireOnWhisperReceived(Friend friend, string message)
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnWhisperReceived", () => p.OnWhisperReceived(friend, message));
        }

        public void FireOnFriendRequestReceived(FriendRequest request)
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnFriendRequestReceived", () => p.OnFriendRequestReceived(request));
        }

        public void FireOnFriendRequestAccepted(Friend friend)
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnFriendRequestAccepted", () => p.OnFriendRequestAccepted(friend));
        }

        public void FireOnSharedDataQueryResult(Dictionary<int, Dictionary<string, string>> results)
        {
            foreach (var p in _plugins)
                SafeCall(p, "OnSharedDataQueryResult", () => p.OnSharedDataQueryResult(results));
        }

        public void Dispose()
        {
            foreach (var p in _plugins)
                SafeCall(p, "Dispose", () => p.Dispose());
            _plugins.Clear();
        }

        private void SafeCall(Plugin p, string method, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                _log?.Invoke($"[Plugin:{p.Name ?? p.GetType().Name}] {method} error: {ex.Message}");
            }
        }
    }
}
