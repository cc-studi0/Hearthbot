using System;
using System.Collections.Generic;
using System.Linq;
using BotMain.AI.CardEffectsScripts;
using SmartBot.Plugins.API;

namespace BotMain.AI
{
    public interface IHeuristicRule
    {
        bool TryPrune(SimBoard board, GameAction action, out string reason);
    }

    public static class HeuristicRuleFactory
    {
        public static List<IHeuristicRule> CreateDefault(CardEffectDB db, bool includeLegacyRules)
        {
            var rules = new List<IHeuristicRule>
            {
                new AvoidPureHealOnFullFriendlyHeroRule(db),
                new AvoidPureHealWithoutInjuredFriendlyRule(db),
                new AvoidDamageCoreFriendlyMinionRule(db),
                new AvoidLowValueSilenceBattlecryTargetRule(db),
            };

            // 赤红深渊（REV_990 / CORE_REV_990）：对敌方随从使用时，
            // 除非能击杀（1血），否则给敌人 +2 攻击力的代价远大于 1 点伤害的收益，
            // 始终应该剪枝。这不是"遗留"行为，而是普遍正确的规则。
            rules.Add(new LegacyCrimsonAbyssRule());

            return rules;
        }
    }

    internal sealed class AvoidPureHealOnFullFriendlyHeroRule : IHeuristicRule
    {
        private readonly CardEffectDB _db;

        public AvoidPureHealOnFullFriendlyHeroRule(CardEffectDB db)
        {
            _db = db;
        }

        public bool TryPrune(SimBoard board, GameAction action, out string reason)
        {
            reason = null;
            if (action?.Type != ActionType.PlayCard || action.Source == null || action.Target == null)
                return false;
            if (action.Source.Type != Card.CType.SPELL) return false;
            if (!action.Target.IsFriend || action.Target.Type != Card.CType.HERO) return false;
            if (!HeuristicRuleUtils.IsAtFullHealth(action.Target)) return false;

            var kinds = HeuristicRuleUtils.GetSpellKinds(_db, action.Source.CardId);
            if (!HeuristicRuleUtils.IsPureHealEffect(kinds)) return false;

            reason = "heal full-health hero";
            return true;
        }
    }

    internal sealed class AvoidPureHealWithoutInjuredFriendlyRule : IHeuristicRule
    {
        private readonly CardEffectDB _db;

        public AvoidPureHealWithoutInjuredFriendlyRule(CardEffectDB db)
        {
            _db = db;
        }

        public bool TryPrune(SimBoard board, GameAction action, out string reason)
        {
            reason = null;
            if (action?.Type != ActionType.PlayCard || action.Source == null)
                return false;
            if (action.Source.Type != Card.CType.SPELL) return false;
            if (action.Target != null) return false;

            var kinds = HeuristicRuleUtils.GetSpellKinds(_db, action.Source.CardId);
            if (!HeuristicRuleUtils.IsPureHealEffect(kinds)) return false;
            if (HeuristicRuleUtils.HasAnyInjuredFriendlyCharacter(board)) return false;

            reason = "pure-heal while all friendly are full health";
            return true;
        }
    }

    internal sealed class AvoidDamageCoreFriendlyMinionRule : IHeuristicRule
    {
        private readonly CardEffectDB _db;

        public AvoidDamageCoreFriendlyMinionRule(CardEffectDB db)
        {
            _db = db;
        }

        public bool TryPrune(SimBoard board, GameAction action, out string reason)
        {
            reason = null;
            if (action?.Type != ActionType.PlayCard || action.Source == null || action.Target == null)
                return false;
            if (action.Source.Type != Card.CType.SPELL) return false;
            if (!action.Target.IsFriend || action.Target.Type != Card.CType.MINION) return false;

            var kinds = HeuristicRuleUtils.GetSpellKinds(_db, action.Source.CardId);
            if ((kinds & (EffectKind.Damage | EffectKind.Destroy)) == 0) return false;
            if (!HeuristicRuleUtils.IsCoreFriendlyMinion(action.Target)) return false;
            if (HeuristicRuleUtils.HasSelfDamageException(_db, action.Target)) return false;

            reason = "damage/destroy core friendly minion";
            return true;
        }
    }

    internal sealed class AvoidLowValueSilenceBattlecryTargetRule : IHeuristicRule
    {
        private readonly CardEffectDB _db;

        public AvoidLowValueSilenceBattlecryTargetRule(CardEffectDB db)
        {
            _db = db;
        }

        public bool TryPrune(SimBoard board, GameAction action, out string reason)
        {
            reason = null;
            if (action?.Type != ActionType.PlayCard || action.Source == null || action.Target == null)
                return false;
            if (action.Source.Type != Card.CType.MINION) return false;
            if (action.Target.Type != Card.CType.MINION || action.Target.IsFriend) return false;

            var kinds = HeuristicRuleUtils.GetBattlecryKinds(_db, action.Source.CardId);
            if (!HeuristicRuleUtils.IsSilenceBattlecryEffect(kinds)) return false;

            if (action.Target.IsSilenced)
            {
                reason = "silence already-silenced enemy minion";
                return true;
            }

            var targetValue = HeuristicRuleUtils.EstimateSilenceTargetValue(action.Target);
            var bestEnemyValue = 0f;
            if (board?.EnemyMinions != null)
            {
                foreach (var enemy in board.EnemyMinions)
                {
                    if (enemy == null || enemy.Health <= 0 || enemy.IsStealth) continue;
                    var value = HeuristicRuleUtils.EstimateSilenceTargetValue(enemy);
                    if (value > bestEnemyValue) bestEnemyValue = value;
                }
            }

            var floor = bestEnemyValue >= 8f ? 4f : bestEnemyValue >= 5f ? 3f : 2f;
            if (bestEnemyValue >= 4f && targetValue + 1.4f < bestEnemyValue)
            {
                reason = $"low-value silence target ({targetValue:0.0} < best {bestEnemyValue:0.0})";
                return true;
            }

            if (targetValue < floor)
            {
                reason = $"low-value silence target ({targetValue:0.0} < floor {floor:0.0})";
                return true;
            }

            return false;
        }
    }

    internal sealed class LegacyCrimsonAbyssRule : IHeuristicRule
    {
        public bool TryPrune(SimBoard board, GameAction action, out string reason)
        {
            reason = null;
            if (action?.Source == null) return false;
            if (action.Type != ActionType.UseLocation && action.Type != ActionType.PlayCard) return false;
            if (action.Source.CardId != Card.Cards.REV_990 && action.Source.CardId != Card.Cards.CORE_REV_990) return false;
            if (action.Target == null || action.Target.IsFriend) return false;

            bool lethal = !action.Target.IsImmune && !action.Target.IsDivineShield && action.Target.Health <= 1;
            if (lethal) return false;

            reason = "REV_990 on enemy minion without lethal";
            return true;
        }
    }

    internal static class HeuristicRuleUtils
    {
        public static EffectKind GetSpellKinds(CardEffectDB db, Card.Cards cardId)
        {
            if (db == null) return EffectKind.None;
            return db.GetEffectKinds(cardId, EffectTrigger.Spell);
        }

        public static EffectKind GetBattlecryKinds(CardEffectDB db, Card.Cards cardId)
        {
            if (db == null) return EffectKind.None;
            return db.GetEffectKinds(cardId, EffectTrigger.Battlecry);
        }

        public static bool IsSilenceBattlecryEffect(EffectKind kinds)
        {
            if ((kinds & EffectKind.Silence) == 0) return false;
            if ((kinds & (EffectKind.Damage | EffectKind.Destroy)) != 0) return false;
            return true;
        }

        public static bool IsPureHealEffect(EffectKind kinds)
        {
            if ((kinds & EffectKind.Heal) == 0) return false;
            var nonHeal = kinds & ~EffectKind.Heal;
            return nonHeal == EffectKind.None;
        }

        public static float EstimateSilenceTargetValue(SimEntity minion)
        {
            if (minion == null || minion.Type != Card.CType.MINION || minion.Health <= 0) return 0f;
            if (minion.IsSilenced) return 0f;

            var score = 0f;

            if (minion.IsTaunt) score += 4f;
            if (minion.IsDivineShield) score += 3.5f;
            if (minion.HasPoison) score += 5f;
            if (minion.IsLifeSteal) score += 2.2f + minion.Atk * 0.3f;
            if (minion.IsWindfury) score += 2.2f + minion.Atk * 0.6f;
            if (minion.HasReborn) score += 2.5f;
            if (minion.SpellPower > 0) score += minion.SpellPower * 2.4f;
            if (minion.HasDeathrattle) score += 1.6f;
            if (minion.EnrageBonusActive) score += 1.2f;

            if (minion.Atk >= 8) score += 3f;
            else if (minion.Atk >= 6) score += 2f;
            else if (minion.Atk >= 4) score += 1f;

            var bodyPressure = minion.Atk * 1.15f + minion.Health * 0.8f;
            if (bodyPressure >= 14f) score += 2.8f;
            else if (bodyPressure >= 10f) score += 1.2f;

            var baseAtk = minion.Atk;
            var baseHp = minion.Health;
            try
            {
                baseAtk = Math.Max(0, CardEffectScriptHelpers.GetBaseAtk(minion.CardId, minion.Atk));
                baseHp = Math.Max(1, CardEffectScriptHelpers.GetBaseHealth(minion.CardId, minion.Health));
            }
            catch
            {
            }

            var buffAtk = Math.Max(0, minion.Atk - baseAtk);
            var buffHp = Math.Max(0, minion.Health - baseHp);
            score += buffAtk * 1.2f + buffHp * 0.85f;
            if (buffAtk + buffHp >= 3) score += 1.2f;

            return score;
        }

        public static bool HasAnyInjuredFriendlyCharacter(SimBoard board)
        {
            if (board == null) return false;
            if (board.FriendHero != null && board.FriendHero.Health < board.FriendHero.MaxHealth) return true;
            return board.FriendMinions.Any(m => m != null && m.Health < m.MaxHealth);
        }

        public static bool HasSelfDamageException(CardEffectDB db, SimEntity minion)
        {
            if (minion == null) return false;
            if (minion.EnrageBonusActive) return true;
            return db != null && db.Has(minion.CardId, EffectTrigger.AfterDamaged);
        }

        public static bool IsCoreFriendlyMinion(SimEntity minion)
        {
            if (minion == null || minion.Type != Card.CType.MINION) return false;

            if (minion.SpellPower > 0
                || minion.IsWindfury
                || minion.HasPoison
                || minion.IsLifeSteal
                || minion.HasDeathrattle
                || minion.HasReborn
                || minion.IsTaunt
                || minion.IsDivineShield)
            {
                return true;
            }

            var value = minion.Atk * 1.4f + minion.Health;
            return value >= 7.5f;
        }

        public static bool IsAtFullHealth(SimEntity entity)
            => entity != null && entity.Health >= entity.MaxHealth;
    }
}
