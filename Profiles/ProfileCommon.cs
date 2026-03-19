using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBotProfiles
{
    public static class ProfileCommon
    {
        public static bool TryRunPureLearningPlayExecutor(Board board, ProfileParameters p)
        {
            try
            {
                EnsureDecisionSupportAssemblyLoaded();
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = assemblies.Length - 1; i >= 0; i--)
                {
                    var assembly = assemblies[i];
                    var executorType = assembly.GetType("SmartBotProfiles.DecisionPlayExecutor", false);
                    if (executorType == null)
                        continue;

                    var method = executorType.GetMethod(
                        "TryRunPureLearningPlayExecutor",
                        new[] { typeof(Board), typeof(ProfileParameters) });
                    if (method == null)
                        continue;

                    try
                    {
                        object result = method.Invoke(null, new object[] { board, p });
                        if (result is bool)
                            return (bool)result;
                    }
                    catch
                    {
                        // ignore and try older loaded versions
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static void EnsureDecisionSupportAssemblyLoaded()
        {
            string[] candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "compilecheck_decisionplayexecutor", "bin", "Debug", "net8.0", "compilecheck_decisionplayexecutor.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "compilecheck_decisionplayexecutor", "bin", "Release", "net8.0", "compilecheck_decisionplayexecutor.dll")
            };

            foreach (string candidate in candidates)
            {
                try
                {
                    if (!File.Exists(candidate))
                        continue;

                    Assembly.LoadFrom(candidate);
                    return;
                }
                catch
                {
                    // ignore
                }
            }
        }

        public static void AddLog(ref string log, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            if (log == null)
                log = string.Empty;

            if (log.Length > 0)
                log += "\r\n";

            log += line;
        }

        public static string CardName(Card.Cards id)
        {
            try
            {
                CardTemplate template = CardTemplate.LoadFromId(id);
                if (template != null)
                {
                    if (!string.IsNullOrWhiteSpace(template.NameCN))
                        return template.NameCN + "(" + id + ")";
                    if (!string.IsNullOrWhiteSpace(template.Name))
                        return template.Name + "(" + id + ")";
                }
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
            if (p == null || table == null)
                return;

            foreach (KeyValuePair<Card.Cards, int> kv in table)
                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(kv.Key, new Modifier(kv.Value));
        }

        public static HashSet<Card.Cards> GetEnemyMinionCardIds(Board board)
        {
            HashSet<Card.Cards> ids = new HashSet<Card.Cards>();
            if (board == null || board.MinionEnemy == null)
                return ids;

            foreach (Card enemy in board.MinionEnemy)
            {
                if (enemy == null || enemy.Template == null)
                    continue;

                ids.Add(enemy.Template.Id);
            }

            return ids;
        }

        public static Dictionary<Card.Cards, int> BuildMaxValueById(IEnumerable<KeyValuePair<Card.Cards, int>> table)
        {
            Dictionary<Card.Cards, int> maxById = new Dictionary<Card.Cards, int>();
            if (table == null)
                return maxById;

            foreach (KeyValuePair<Card.Cards, int> kv in table)
            {
                int current;
                if (maxById.TryGetValue(kv.Key, out current))
                    maxById[kv.Key] = Math.Max(current, kv.Value);
                else
                    maxById[kv.Key] = kv.Value;
            }

            return maxById;
        }

        public static void ApplyThreatTableIfPresent(
            ProfileParameters p,
            HashSet<Card.Cards> presentEnemyIds,
            IEnumerable<KeyValuePair<Card.Cards, int>> table)
        {
            if (p == null || presentEnemyIds == null || presentEnemyIds.Count == 0 || table == null)
                return;

            foreach (KeyValuePair<Card.Cards, int> kv in table)
            {
                if (!presentEnemyIds.Contains(kv.Key))
                    continue;

                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(kv.Key, new Modifier(kv.Value));
            }
        }

        public static void ApplyGoFaceOverrideWithThreatPullback(
            ProfileParameters p,
            Board board,
            Dictionary<Card.Cards, int> threatMaxById,
            int goFaceOverrideValue,
            int criticalThreatOverrideThreshold)
        {
            if (p == null || board == null || board.MinionEnemy == null || board.MinionEnemy.Count == 0)
                return;

            foreach (Card enemy in board.MinionEnemy)
            {
                if (enemy == null || enemy.Template == null || enemy.IsTaunt)
                    continue;

                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(goFaceOverrideValue));
            }

            if (threatMaxById == null || threatMaxById.Count == 0)
                return;

            foreach (Card enemy in board.MinionEnemy)
            {
                int threatValue;
                if (enemy == null || enemy.Template == null || enemy.IsTaunt)
                    continue;

                if (threatMaxById.TryGetValue(enemy.Template.Id, out threatValue)
                    && threatValue >= criticalThreatOverrideThreshold)
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(threatValue));
                }
            }
        }

        public static void ApplyDynamicEnemyThreat(Board board, ProfileParameters p, bool enableLogs, Action<string> addLog)
        {
            if (board == null || p == null || board.MinionEnemy == null || board.MinionEnemy.Count == 0)
                return;

            foreach (Card enemy in board.MinionEnemy)
            {
                if (enemy == null || enemy.Template == null)
                    continue;

                if (enemy.IsTaunt)
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(150));
                    if (enableLogs && addLog != null)
                        addLog("[Threat] " + CardName(enemy.Template.Id) + " taunt");
                }

                if (enemy.CurrentAtk >= 6)
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(80));
                    if (enableLogs && addLog != null)
                        addLog("[Threat] " + CardName(enemy.Template.Id) + " atk=" + enemy.CurrentAtk);
                }
            }
        }

        public static bool ApplyExactAttackModifier(ProfileParameters p, Card source, Card target, int modifier)
        {
            if (source == null || source.Template == null || target == null || target.Template == null)
                return false;

            return ApplyExactAttackModifier(
                p,
                source.Id,
                source.Template.Id,
                target.Id,
                target.Template.Id,
                modifier);
        }

        public static bool ApplyExactAttackModifier(
            ProfileParameters p,
            int sourceEntityId,
            Card.Cards sourceCardId,
            int targetEntityId,
            Card.Cards targetCardId,
            int modifier)
        {
            if (p == null || modifier == 0)
                return false;

            if (sourceEntityId > 0 && targetEntityId > 0)
            {
                p.MinionsAttackModifiers.AddOrUpdate(sourceEntityId, modifier, targetEntityId);
                return true;
            }

            if (sourceEntityId > 0 && targetCardId != default(Card.Cards))
            {
                p.MinionsAttackModifiers.AddOrUpdate(sourceEntityId, modifier, targetCardId);
                return true;
            }

            if (sourceCardId != default(Card.Cards) && targetEntityId > 0)
            {
                p.MinionsAttackModifiers.AddOrUpdate(sourceCardId, modifier, targetEntityId);
                return true;
            }

            if (sourceCardId != default(Card.Cards) && targetCardId != default(Card.Cards))
            {
                p.MinionsAttackModifiers.AddOrUpdate(sourceCardId, modifier, targetCardId);
                return true;
            }

            return false;
        }

        public static bool ApplyExactWeaponAttackModifier(
            ProfileParameters p,
            int sourceEntityId,
            Card.Cards sourceCardId,
            int targetEntityId,
            Card.Cards targetCardId,
            int modifier)
        {
            if (p == null || modifier == 0)
                return false;

            if (sourceEntityId > 0 && targetEntityId > 0)
            {
                p.WeaponsAttackModifiers.AddOrUpdate(sourceEntityId, modifier, targetEntityId);
                return true;
            }

            if (sourceEntityId > 0 && targetCardId != default(Card.Cards))
            {
                p.WeaponsAttackModifiers.AddOrUpdate(sourceEntityId, modifier, targetCardId);
                return true;
            }

            if (sourceCardId != default(Card.Cards) && targetEntityId > 0)
            {
                p.WeaponsAttackModifiers.AddOrUpdate(sourceCardId, modifier, targetEntityId);
                return true;
            }

            if (sourceCardId != default(Card.Cards) && targetCardId != default(Card.Cards))
            {
                p.WeaponsAttackModifiers.AddOrUpdate(sourceCardId, modifier, targetCardId);
                return true;
            }

            return false;
        }

        public static bool ApplyExactCastModifier(ProfileParameters p, Card source, Card target, int modifier)
        {
            if (source == null || source.Template == null || target == null || target.Template == null)
                return false;

            return ApplyExactCastModifier(
                p,
                source.Type,
                source.Id,
                source.Template.Id,
                target.Id,
                target.Template.Id,
                modifier);
        }

        public static bool ApplyExactCastModifier(
            ProfileParameters p,
            Card.CType sourceType,
            int sourceEntityId,
            Card.Cards sourceCardId,
            int targetEntityId,
            Card.Cards targetCardId,
            int modifier)
        {
            if (p == null || modifier == 0)
                return false;

            try
            {
                if (sourceType == Card.CType.MINION)
                {
                    if (sourceEntityId > 0 && targetEntityId > 0)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(sourceEntityId, new Modifier(modifier, targetEntityId));
                        return true;
                    }

                    if (sourceEntityId > 0 && targetCardId != default(Card.Cards))
                    {
                        p.CastMinionsModifiers.AddOrUpdate(sourceEntityId, new Modifier(modifier, targetCardId));
                        return true;
                    }

                    if (sourceCardId != default(Card.Cards) && targetEntityId > 0)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(sourceCardId, new Modifier(modifier, targetEntityId));
                        return true;
                    }

                    if (sourceCardId != default(Card.Cards) && targetCardId != default(Card.Cards))
                    {
                        p.CastMinionsModifiers.AddOrUpdate(sourceCardId, new Modifier(modifier, targetCardId));
                        return true;
                    }
                }
                else if (sourceType == Card.CType.SPELL)
                {
                    if (sourceEntityId > 0 && targetEntityId > 0)
                    {
                        p.CastSpellsModifiers.AddOrUpdate(sourceEntityId, new Modifier(modifier, targetEntityId));
                        return true;
                    }

                    if (sourceEntityId > 0 && targetCardId != default(Card.Cards))
                    {
                        p.CastSpellsModifiers.AddOrUpdate(sourceEntityId, new Modifier(modifier, targetCardId));
                        return true;
                    }

                    if (sourceCardId != default(Card.Cards) && targetEntityId > 0)
                    {
                        p.CastSpellsModifiers.AddOrUpdate(sourceCardId, new Modifier(modifier, targetEntityId));
                        return true;
                    }

                    if (sourceCardId != default(Card.Cards) && targetCardId != default(Card.Cards))
                    {
                        p.CastSpellsModifiers.AddOrUpdate(sourceCardId, new Modifier(modifier, targetCardId));
                        return true;
                    }
                }
                else if (sourceType == Card.CType.WEAPON)
                {
                    if (sourceEntityId > 0 && targetEntityId > 0)
                    {
                        p.CastWeaponsModifiers.AddOrUpdate(sourceEntityId, new Modifier(modifier, targetEntityId));
                        return true;
                    }

                    if (sourceEntityId > 0 && targetCardId != default(Card.Cards))
                    {
                        p.CastWeaponsModifiers.AddOrUpdate(sourceEntityId, new Modifier(modifier, targetCardId));
                        return true;
                    }

                    if (sourceCardId != default(Card.Cards) && targetEntityId > 0)
                    {
                        p.CastWeaponsModifiers.AddOrUpdate(sourceCardId, new Modifier(modifier, targetEntityId));
                        return true;
                    }

                    if (sourceCardId != default(Card.Cards) && targetCardId != default(Card.Cards))
                    {
                        p.CastWeaponsModifiers.AddOrUpdate(sourceCardId, new Modifier(modifier, targetCardId));
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        public static bool ApplyExactHeroPowerModifier(
            ProfileParameters p,
            Card.Cards heroPowerId,
            int targetEntityId,
            Card.Cards targetCardId,
            int modifier)
        {
            if (p == null || heroPowerId == default(Card.Cards) || modifier == 0)
                return false;

            try
            {
                if (targetEntityId > 0)
                {
                    p.CastHeroPowerModifier.AddOrUpdate(heroPowerId, new Modifier(modifier, targetEntityId));
                    return true;
                }

                if (targetCardId != default(Card.Cards))
                {
                    p.CastHeroPowerModifier.AddOrUpdate(heroPowerId, new Modifier(modifier, targetCardId));
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        public static bool ApplyLiveMemoryBias(Board board, ProfileParameters p)
        {
            return false;
        }

        public static bool ApplyLiveMemoryBias(Board board, ProfileParameters p, bool enableLogs, Action<string> addLog)
        {
            return false;
        }
    }
}
