using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBotProfiles
{
    // Profiles 公共工具：尽量只放“通用逻辑”，避免绑死某个卡组。
    // tip: 项目是 c#6，不要用新语法
    public static class ProfileCommon
    {
        public static void AddLog(ref string log, string line)
        {
            if (log == null) log = "";
            if (log.Length > 0)
                log += "\r\n";
            log += line;
        }

        public static string CardName(Card.Cards id)
        {
            try
            {
                var t = CardTemplate.LoadFromId(id);
                var cn = t != null ? t.NameCN : null;
                if (!string.IsNullOrWhiteSpace(cn))
                    return cn + "(" + id + ")";
            }
            catch
            {
                // ignore
            }

            return id.ToString();
        }

        public static Card.Cards GetFriendAbilityId(Board board, Card.Cards fallback)
        {
            try
            {
                if (board != null && board.Ability != null && board.Ability.Template != null)
                    return board.Ability.Template.Id;
            }
            catch
            {
                // ignore
            }

            return fallback;
        }

        public static void ApplyEnemyThreatTable(ProfileParameters p, IEnumerable<KeyValuePair<Card.Cards, int>> table)
        {
            if (p == null || table == null) return;

            foreach (var kv in table)
                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(kv.Key, new Modifier(kv.Value));
        }

        // ===== 攻击优先 / 卡牌威胁：通用辅助 =====
        // 注意：威胁表本身应由具体卡组维护（不同卡组对同一随从的威胁口径可能不同）。

        public static HashSet<Card.Cards> GetEnemyMinionCardIds(Board board)
        {
            var ids = new HashSet<Card.Cards>();
            if (board == null || board.MinionEnemy == null) return ids;

            foreach (var m in board.MinionEnemy)
            {
                if (m == null || m.Template == null) continue;
                ids.Add(m.Template.Id);
            }
            return ids;
        }

        // 构建“每个随从 id 的最大威胁值”，用于后续做“走脸覆盖”后回拉关键威胁。
        public static Dictionary<Card.Cards, int> BuildMaxValueById(IEnumerable<KeyValuePair<Card.Cards, int>> table)
        {
            var maxById = new Dictionary<Card.Cards, int>();
            if (table == null) return maxById;

            foreach (var kv in table)
            {
                int cur;
                if (maxById.TryGetValue(kv.Key, out cur))
                    maxById[kv.Key] = Math.Max(cur, kv.Value);
                else
                    maxById[kv.Key] = kv.Value;
            }
            return maxById;
        }

        // 只对“当前对面场上存在的随从”应用威胁表，减少无意义的 AddOrUpdate。
        public static void ApplyThreatTableIfPresent(ProfileParameters p, HashSet<Card.Cards> presentEnemyIds, IEnumerable<KeyValuePair<Card.Cards, int>> table)
        {
            if (p == null || presentEnemyIds == null || presentEnemyIds.Count == 0 || table == null) return;

            foreach (var kv in table)
            {
                if (!presentEnemyIds.Contains(kv.Key))
                    continue;
                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(kv.Key, new Modifier(kv.Value));
            }
        }

        // “走脸覆盖”：当你决定在无嘲讽窗口尽量走脸时，降低所有非嘲讽随从的被攻击优先级；
        // 但保留对关键威胁随从的回拉（例如威胁值>=阈值）。
        public static void ApplyGoFaceOverrideWithThreatPullback(ProfileParameters p, Board board, Dictionary<Card.Cards, int> threatMaxById, int goFaceOverrideValue, int criticalThreatOverrideThreshold)
        {
            if (p == null || board == null || board.MinionEnemy == null || board.MinionEnemy.Count == 0)
                return;

            foreach (var em in board.MinionEnemy)
            {
                if (em == null || em.Template == null) continue;
                if (em.IsTaunt) continue;
                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(em.Template.Id, new Modifier(goFaceOverrideValue));
            }

            if (threatMaxById == null || threatMaxById.Count == 0)
                return;

            foreach (var em in board.MinionEnemy)
            {
                if (em == null || em.Template == null) continue;
                if (em.IsTaunt) continue;

                int t;
                if (threatMaxById.TryGetValue(em.Template.Id, out t) && t >= criticalThreatOverrideThreshold)
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(em.Template.Id, new Modifier(t));
            }
        }

        // 通用动态威胁评估：
        // - 嘲讽优先解（避免卡住节奏/斩杀）
        // - 高攻优先解（保命口径）
        // 说明：这是“兜底逻辑”，更细的对局/职业特化可以在具体策略里再加。
        public static void ApplyDynamicEnemyThreat(Board board, ProfileParameters p, bool enableLogs, Action<string> addLog)
        {
            if (board == null || p == null || board.MinionEnemy == null || board.MinionEnemy.Count == 0)
                return;

            foreach (var m in board.MinionEnemy)
            {
                if (m == null || m.Template == null) continue;

                if (m.IsTaunt)
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(m.Template.Id, new Modifier(150));
                    if (enableLogs && addLog != null)
                        addLog("[威胁] " + CardName(m.Template.Id) + " 嘲讽优先解");
                }

                if (m.CurrentAtk >= 6)
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(m.Template.Id, new Modifier(80));
                    if (enableLogs && addLog != null)
                        addLog("[威胁] " + CardName(m.Template.Id) + " 高攻" + m.CurrentAtk + "点（优先控场）");
                }
            }
        }
    }
}
