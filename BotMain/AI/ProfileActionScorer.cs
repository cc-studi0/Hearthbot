using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Plugins.API;
using SmartBotProfiles;

namespace BotMain.AI
{
    public sealed class ProfileActionScore
    {
        public float Bonus { get; set; }
        public bool HardBlocked { get; set; }
        public string Detail { get; set; } = string.Empty;
    }

    public class ProfileActionScorer
    {
        private static readonly Card.Cards[] HeroPrefixCardIds = Enum.GetValues(typeof(Card.Cards))
            .Cast<Card.Cards>()
            .Where(IsHeroPrefixCardId)
            .ToArray();

        public ProfileActionScore Evaluate(SimBoard board, GameAction action, ProfileParameters param)
        {
            var score = new ProfileActionScore();
            if (board == null || action == null || param == null || action.Type == ActionType.EndTurn)
                return score;

            var source = ResolveSource(board, action);
            var target = ResolveTarget(board, action);
            var details = new List<string>();

            switch (action.Type)
            {
                case ActionType.PlayCard:
                    EvaluatePlayCard(param, source, target, score, details);
                    break;
                case ActionType.HeroPower:
                    EvaluateHeroPower(board, param, source, target, score, details);
                    break;
                case ActionType.Attack:
                    EvaluateAttack(board, param, source, target, score, details);
                    break;
                case ActionType.UseLocation:
                    EvaluateUseLocation(param, source, target, score, details);
                    break;
            }

            if (!score.HardBlocked && source != null && param.PlayBanList != null)
            {
                var sourceCardInt = (int)source.CardId;
                if (param.PlayBanList.Contains(source.EntityId) || param.PlayBanList.Contains(sourceCardInt))
                {
                    score.HardBlocked = true;
                    details.Add($"PlayBanList[{source.EntityId}/{sourceCardInt}]");
                }
            }

            score.Detail = string.Join("; ", details.Where(d => !string.IsNullOrWhiteSpace(d)));
            return score;
        }

        private void EvaluatePlayCard(
            ProfileParameters param,
            SimEntity source,
            SimEntity target,
            ProfileActionScore score,
            List<string> details)
        {
            if (source == null)
                return;

            RulesSet castRules = null;
            Modifier globalCast = null;
            var castLabel = "CastOther";

            switch (source.Type)
            {
                case Card.CType.MINION:
                    castRules = param.CastMinionsModifiers;
                    globalCast = param.GlobalCastMinionsModifier;
                    castLabel = "CastMinions";
                    break;
                case Card.CType.SPELL:
                case Card.CType.LOCATION:
                case Card.CType.HERO:
                    castRules = param.CastSpellsModifiers;
                    globalCast = param.GlobalCastSpellsModifier;
                    castLabel = "CastSpells";
                    break;
                case Card.CType.WEAPON:
                    castRules = param.CastWeaponsModifiers;
                    castLabel = "CastWeapons";
                    break;
            }

            ApplyGlobalPropensity(score, details, $"{castLabel}:global", globalCast);
            ApplyPropensityRules(score, details, castRules, castLabel, source, target);
            ApplyOrderRules(score, details, param.PlayOrderModifiers, "PlayOrder", source, target);
        }

        private void EvaluateUseLocation(
            ProfileParameters param,
            SimEntity source,
            SimEntity target,
            ProfileActionScore score,
            List<string> details)
        {
            if (source == null) return;
            ApplyPropensityRules(score, details, param.LocationsModifiers, "UseLocation", source, target);
            ApplyOrderRules(score, details, param.PlayOrderModifiers, "PlayOrder", source, target);
        }

        private void EvaluateHeroPower(
            SimBoard board,
            ProfileParameters param,
            SimEntity source,
            SimEntity target,
            ProfileActionScore score,
            List<string> details)
        {
            var heroPower = source ?? board.HeroPower;
            if (heroPower == null)
                return;

            ApplyHeroPowerPropensityRules(score, details, param.CastHeroPowerModifier, "CastHeroPower", heroPower, target);
            ApplyHeroPowerOrderRules(score, details, param.PlayOrderModifiers, "PlayOrder", heroPower, target);
        }

        private void EvaluateAttack(
            SimBoard board,
            ProfileParameters param,
            SimEntity source,
            SimEntity target,
            ProfileActionScore score,
            List<string> details)
        {
            if (source == null || target == null)
                return;

            var isHeroAttack = board.FriendHero != null && source.EntityId == board.FriendHero.EntityId;
            if (isHeroAttack && board.FriendWeapon != null)
            {
                var weapon = board.FriendWeapon;
                ApplyGlobalPropensity(score, details, "WeaponsAttack:global", param.GlobalWeaponsAttackModifier);
                ApplyPropensityRules(score, details, param.WeaponsAttackModifiers, "WeaponsAttack", weapon, target);

                // SmartBot 的武器攻击规则用 AddOrUpdate(targetId, modifier) 写入，目标也需要单独匹配一次
                ApplyPropensityNoTargetRules(score, details, param.WeaponsAttackModifiers, "WeaponsAttack(target)", target);
            }

            if (!isHeroAttack
                && source.Type == Card.CType.MINION
                && target.Type == Card.CType.MINION)
            {
                ApplyPropensityRules(score, details, param.TradeModifiers, "Trade", source, target);
            }

            ApplyOrderRules(score, details, param.AttackOrderModifiers, "AttackOrder", source, target);
        }

        private static SimEntity ResolveSource(SimBoard board, GameAction action)
        {
            if (action.Source != null)
                return action.Source;

            if (action.Type == ActionType.HeroPower)
                return board.HeroPower;

            if (action.SourceEntityId <= 0)
                return null;

            if (action.Type == ActionType.PlayCard)
                return board.Hand.FirstOrDefault(c => c.EntityId == action.SourceEntityId);

            if (action.Type == ActionType.Attack)
            {
                return board.FriendMinions.FirstOrDefault(m => m.EntityId == action.SourceEntityId)
                    ?? (board.FriendHero != null && board.FriendHero.EntityId == action.SourceEntityId ? board.FriendHero : null);
            }

            if (action.Type == ActionType.UseLocation)
                return board.FriendMinions.FirstOrDefault(m => m.EntityId == action.SourceEntityId);

            return null;
        }

        private static SimEntity ResolveTarget(SimBoard board, GameAction action)
        {
            if (action.Target != null)
                return action.Target;

            if (action.TargetEntityId <= 0)
                return null;

            var id = action.TargetEntityId;
            if (board.FriendHero != null && board.FriendHero.EntityId == id) return board.FriendHero;
            if (board.EnemyHero != null && board.EnemyHero.EntityId == id) return board.EnemyHero;

            return board.FriendMinions.FirstOrDefault(m => m.EntityId == id)
                ?? board.EnemyMinions.FirstOrDefault(m => m.EntityId == id);
        }

        private static void ApplyGlobalPropensity(ProfileActionScore score, List<string> details, string label, Modifier modifier)
        {
            if (modifier == null)
                return;

            ApplyPropensityValue(score, details, label, modifier.Value);
        }

        private static void ApplyPropensityRules(
            ProfileActionScore score,
            List<string> details,
            RulesSet rules,
            string label,
            SimEntity source,
            SimEntity target)
        {
            if (rules == null || source == null)
                return;

            if (TryGetNoTargetModifier(rules, source.CardId, source.EntityId, out var baseModifier, out var baseHit))
                ApplyPropensityValue(score, details, $"{label}[{baseHit}]", baseModifier.Value);

            if (target != null && TryGetTargetModifier(rules, source, target, out var targetModifier, out var targetHit))
                ApplyPropensityValue(score, details, $"{label}[{targetHit}]", targetModifier.Value);
        }

        private static void ApplyHeroPowerPropensityRules(
            ProfileActionScore score,
            List<string> details,
            RulesSet rules,
            string label,
            SimEntity source,
            SimEntity target)
        {
            if (rules == null || source == null)
                return;

            if (TryGetNoTargetModifierWithHeroPrefixFallback(rules, source.CardId, source.EntityId, out var baseModifier, out var baseHit))
                ApplyPropensityValue(score, details, $"{label}[{baseHit}]", baseModifier.Value);

            if (target != null && TryGetTargetModifierWithHeroPrefixFallback(rules, source, target, out var targetModifier, out var targetHit))
                ApplyPropensityValue(score, details, $"{label}[{targetHit}]", targetModifier.Value);
        }

        private static void ApplyPropensityNoTargetRules(
            ProfileActionScore score,
            List<string> details,
            RulesSet rules,
            string label,
            SimEntity source)
        {
            if (rules == null || source == null)
                return;

            if (TryGetNoTargetModifier(rules, source.CardId, source.EntityId, out var modifier, out var hit))
                ApplyPropensityValue(score, details, $"{label}[{hit}]", modifier.Value);
        }

        private static void ApplyOrderRules(
            ProfileActionScore score,
            List<string> details,
            RulesSet rules,
            string label,
            SimEntity source,
            SimEntity target)
        {
            if (rules == null || source == null)
                return;

            if (TryGetNoTargetModifier(rules, source.CardId, source.EntityId, out var baseModifier, out var baseHit))
                ApplyOrderValue(score, details, $"{label}[{baseHit}]", baseModifier.Value);

            if (target != null && TryGetTargetModifier(rules, source, target, out var targetModifier, out var targetHit))
                ApplyOrderValue(score, details, $"{label}[{targetHit}]", targetModifier.Value);
        }

        private static void ApplyHeroPowerOrderRules(
            ProfileActionScore score,
            List<string> details,
            RulesSet rules,
            string label,
            SimEntity source,
            SimEntity target)
        {
            if (rules == null || source == null)
                return;

            if (TryGetNoTargetModifierWithHeroPrefixFallback(rules, source.CardId, source.EntityId, out var baseModifier, out var baseHit))
                ApplyOrderValue(score, details, $"{label}[{baseHit}]", baseModifier.Value);

            if (target != null && TryGetTargetModifierWithHeroPrefixFallback(rules, source, target, out var targetModifier, out var targetHit))
                ApplyOrderValue(score, details, $"{label}[{targetHit}]", targetModifier.Value);
        }

        private static void ApplyPropensityValue(ProfileActionScore score, List<string> details, string label, int value)
        {
            // 与 SmartBot 示例语义对齐：0=中性，负数更爱用，正数更不爱用。
            score.Bonus -= value;
            details.Add($"{label}={value}");
        }

        private static void ApplyOrderValue(ProfileActionScore score, List<string> details, string label, int value)
        {
            score.Bonus += value;
            details.Add($"{label}={value}");
        }

        private static bool TryGetNoTargetModifier(
            RulesSet rules,
            Card.Cards sourceCardId,
            int sourceEntityId,
            out Modifier modifier,
            out string hit)
        {
            modifier = null;
            hit = null;

            Rule rule;
            if (sourceEntityId > 0)
            {
                rule = rules.RulesIntIds?[sourceEntityId];
                if (rule?.CardModifier != null)
                {
                    modifier = rule.CardModifier;
                    hit = $"srcId:{sourceEntityId}";
                    return true;
                }
            }

            rule = rules.RulesCardIds?[sourceCardId];
            if (rule?.CardModifier != null)
            {
                modifier = rule.CardModifier;
                hit = $"srcCard:{sourceCardId}";
                return true;
            }

            var sourceCardInt = (int)sourceCardId;
            rule = rules.RulesIntIds?[sourceCardInt];
            if (rule?.CardModifier != null)
            {
                modifier = rule.CardModifier;
                hit = $"srcIntCard:{sourceCardInt}";
                return true;
            }

            return false;
        }

        private static bool TryGetNoTargetModifierWithHeroPrefixFallback(
            RulesSet rules,
            Card.Cards sourceCardId,
            int sourceEntityId,
            out Modifier modifier,
            out string hit)
        {
            if (TryGetNoTargetModifier(rules, sourceCardId, sourceEntityId, out modifier, out hit))
                return true;

            foreach (var heroCardId in HeroPrefixCardIds)
            {
                if (TryGetNoTargetModifier(rules, heroCardId, 0, out modifier, out hit))
                {
                    hit = $"{hit}(heroPrefix:{heroCardId})";
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetTargetModifier(
            RulesSet rules,
            SimEntity source,
            SimEntity target,
            out Modifier modifier,
            out string hit)
        {
            if (source == null || target == null)
            {
                modifier = null;
                hit = null;
                return false;
            }

            return TryGetTargetModifier(
                rules,
                source.CardId,
                source.EntityId,
                target.CardId,
                target.EntityId,
                out modifier,
                out hit);
        }

        private static bool TryGetTargetModifier(
            RulesSet rules,
            Card.Cards sourceCardId,
            int sourceEntityId,
            Card.Cards targetCardId,
            int targetEntityId,
            out Modifier modifier,
            out string hit)
        {
            modifier = null;
            hit = null;

            Rule rule;

            if (sourceEntityId > 0 && targetEntityId > 0)
            {
                rule = rules.RulesIntIdsTargetIntInds?[sourceEntityId]?[targetEntityId];
                if (rule?.CardModifier != null)
                {
                    modifier = rule.CardModifier;
                    hit = $"srcId:{sourceEntityId}->tgtId:{targetEntityId}";
                    return true;
                }
            }

            if (sourceEntityId > 0)
            {
                rule = rules.RulesIntIdsTargetCardIds?[sourceEntityId]?[targetCardId];
                if (rule?.CardModifier != null)
                {
                    modifier = rule.CardModifier;
                    hit = $"srcId:{sourceEntityId}->tgtCard:{targetCardId}";
                    return true;
                }
            }

            if (targetEntityId > 0)
            {
                rule = rules.RulesCardIdsTargetIntInds?[sourceCardId]?[targetEntityId];
                if (rule?.CardModifier != null)
                {
                    modifier = rule.CardModifier;
                    hit = $"srcCard:{sourceCardId}->tgtId:{targetEntityId}";
                    return true;
                }
            }

            rule = rules.RulesCardIdsTargetCardIds?[sourceCardId]?[targetCardId];
            if (rule?.CardModifier != null)
            {
                modifier = rule.CardModifier;
                hit = $"srcCard:{sourceCardId}->tgtCard:{targetCardId}";
                return true;
            }

            var sourceCardInt = (int)sourceCardId;
            if (targetEntityId > 0)
            {
                rule = rules.RulesIntIdsTargetIntInds?[sourceCardInt]?[targetEntityId];
                if (rule?.CardModifier != null)
                {
                    modifier = rule.CardModifier;
                    hit = $"srcIntCard:{sourceCardInt}->tgtId:{targetEntityId}";
                    return true;
                }
            }

            rule = rules.RulesIntIdsTargetCardIds?[sourceCardInt]?[targetCardId];
            if (rule?.CardModifier != null)
            {
                modifier = rule.CardModifier;
                hit = $"srcIntCard:{sourceCardInt}->tgtCard:{targetCardId}";
                return true;
            }

            return false;
        }

        private static bool TryGetTargetModifierWithHeroPrefixFallback(
            RulesSet rules,
            SimEntity source,
            SimEntity target,
            out Modifier modifier,
            out string hit)
        {
            if (TryGetTargetModifier(rules, source, target, out modifier, out hit))
                return true;

            if (source == null || target == null)
            {
                modifier = null;
                hit = null;
                return false;
            }

            foreach (var heroCardId in HeroPrefixCardIds)
            {
                if (TryGetTargetModifier(
                    rules,
                    heroCardId,
                    source.EntityId,
                    target.CardId,
                    target.EntityId,
                    out modifier,
                    out hit))
                {
                    hit = $"{hit}(heroPrefix:{heroCardId})";
                    return true;
                }
            }

            modifier = null;
            hit = null;
            return false;
        }

        private static bool IsHeroPrefixCardId(Card.Cards cardId)
        {
            var name = cardId.ToString();
            return name.StartsWith("HERO_", StringComparison.OrdinalIgnoreCase);
        }
    }
}
