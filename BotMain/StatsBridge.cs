using System;
using System.Reflection;
using SmartBot.Plugins.API;

namespace BotMain
{
    /// <summary>
    /// 管理 SBAPI Statistics 静态字段的读写和重置轮询
    /// </summary>
    public class StatsBridge
    {
        private readonly Action<string> _log;
        private readonly FieldInfo _resetField;
        private readonly FieldInfo _winsField;
        private readonly FieldInfo _lossesField;
        private readonly FieldInfo _concededField;
        private readonly FieldInfo _concededTotalField;
        private readonly FieldInfo _elapsedField;
        private readonly FieldInfo _goldField;
        private readonly FieldInfo _xpLevelField;
        private readonly FieldInfo _xpProgressField;
        private readonly FieldInfo _arenaWinsField;
        private readonly FieldInfo _arenaLossesField;

        private DateTime _startTimeUtc = DateTime.UtcNow;

        public int Wins { get; private set; }
        public int Losses { get; private set; }
        public int Concedes { get; private set; }
        public int ConcedesTotal { get; private set; }
        public TimeSpan Elapsed => DateTime.UtcNow - _startTimeUtc;

        public StatsBridge(Action<string> log)
        {
            _log = log;
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var t = typeof(Statistics);
            _resetField = t.GetField("_reset", flags);
            _winsField = t.GetField("_wins", flags);
            _lossesField = t.GetField("_losses", flags);
            _concededField = t.GetField("_conceded", flags);
            _concededTotalField = t.GetField("_concededTotal", flags);
            _elapsedField = t.GetField("_elapsedTime", flags);
            _goldField = t.GetField("_gold", flags);
            _xpLevelField = t.GetField("_xplevel", flags);
            _xpProgressField = t.GetField("_xpprogress", flags);
            _arenaWinsField = t.GetField("_Arenawins", flags);
            _arenaLossesField = t.GetField("_Arenalosses", flags);
            SyncToSbapi();
        }

        public void RecordWin()
        {
            Wins++;
            SyncToSbapi();
        }

        public void RecordLoss()
        {
            Losses++;
            SyncToSbapi();
        }

        public void RecordConcede()
        {
            Concedes++;
            ConcedesTotal++;
            SyncToSbapi();
        }

        public void SetGold(int gold) => SetInt(_goldField, gold);
        public void SetXpLevel(int level) => SetInt(_xpLevelField, level);
        public void SetXpProgress(string progress) => SetString(_xpProgressField, progress);

        /// <summary>
        /// 轮询 Statistics._reset 标志，如果被插件触发则重置计数
        /// </summary>
        public bool PollReset()
        {
            try
            {
                var reset = (bool?)_resetField?.GetValue(null) ?? false;
                if (!reset) return false;
                _resetField.SetValue(null, false);

                Wins = 0;
                Losses = 0;
                Concedes = 0;
                _startTimeUtc = DateTime.UtcNow;
                SyncToSbapi();
                _log?.Invoke("[Stats] Reset by plugin");
                return true;
            }
            catch { }

            return false;
        }

        /// <summary>
        /// 更新运行时间到 Statistics._elapsedTime
        /// </summary>
        public void UpdateElapsed()
        {
            try { _elapsedField?.SetValue(null, DateTime.UtcNow - _startTimeUtc); }
            catch { }
        }

        public void ResetAll()
        {
            Wins = 0;
            Losses = 0;
            Concedes = 0;
            ConcedesTotal = 0;
            _startTimeUtc = DateTime.UtcNow;
            SyncToSbapi();
        }

        private void SyncToSbapi()
        {
            SetInt(_winsField, Wins);
            SetInt(_lossesField, Losses);
            SetInt(_concededField, Concedes);
            SetInt(_concededTotalField, ConcedesTotal);
        }

        private static void SetInt(FieldInfo fi, int val)
        {
            try { fi?.SetValue(null, val); } catch { }
        }

        private static void SetString(FieldInfo fi, string val)
        {
            try { fi?.SetValue(null, val); } catch { }
        }
    }
}
