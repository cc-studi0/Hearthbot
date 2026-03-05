using System;
using System.Linq;
using SmartBot.Plugins.API;

namespace BotMain.AI
{
    public readonly struct ActionPriorityOptions
    {
        public bool LethalMode { get; init; }
        public bool PreferSurvival { get; init; }

        public static ActionPriorityOptions DefaultSearch => new ActionPriorityOptions
        {
            LethalMode = false,
            PreferSurvival = true
        };

        public static ActionPriorityOptions LethalSearch => new ActionPriorityOptions
        {
            LethalMode = true,
            PreferSurvival = false
        };
    }

    internal static class ActionPriorityModel
    {
        public static float Estimate(
            SimBoard board,
            GameAction action,
            ProfileActionScore profileScore,
            AggroInteractionContext aggroContext,
            ActionPriorityOptions options)
        {
            if (action == null) return 0f;
            if (aggroContext == null) aggroContext = AggroInteractionContext.FromAggroCoef(1f);

            float p = profileScore?.Bonus ?? 0f;
            float lethalScale = options.LethalMode ? 1.25f : 1f;
            float survivalScale = options.PreferSurvival ? 1f : 0.6f;

            switch (action.Type)
            {
                case ActionType.Attack:
                    p += EstimateAttackPriority(board, action, aggroContext, lethalScale, survivalScale);
                    break;

                case ActionType.PlayCard:
                    p += EstimatePlayCardPriority(board, action, aggroContext, lethalScale, survivalScale);
                    break;

                case ActionType.HeroPower:
                    p += EstimateHeroPowerPriority(board, action, aggroContext, lethalScale);
                    break;

                case ActionType.TradeCard:
                    p += board != null && board.Hand.Count >= 8 ? 1.8f : 0.4f;
                    break;

                case ActionType.UseLocation:
                    p += 1.2f;
                    if (action.Target != null)
                    {
                        p += action.Target.Atk * 0.25f * aggroContext.ThreatBias;
                        if (action.Target.IsTaunt) p += 0.8f;
                    }
                    break;
            }

            return p;
        }

        public static bool IsTacticalAction(SimBoard board, GameAction action, AggroInteractionContext aggroContext, bool lethalMode)
        {
            if (action == null) return false;
            if (aggroContext == null) aggroContext = AggroInteractionContext.FromAggroCoef(1f);

            switch (action.Type)
            {
                case ActionType.Attack:
                    {
                        var target = action.Target;
                        var source = action.Source;
                        if (target == null) return false;

                        if (board?.EnemyHero != null && target.EntityId == board.EnemyHero.EntityId)
                        {
                            if (source == null) return lethalMode;
                            int enemyEhp = (board.EnemyHero.Health + board.EnemyHero.Armor);
                            return source.Atk >= enemyEhp || (lethalMode && source.Atk >= Math.Max(1, enemyEhp - 2));
                        }

                        if (target.IsTaunt || target.HasPoison || target.IsWindfury || target.SpellPower > 0)
                            return true;

                        return target.Atk >= aggroContext.HighThreatAttackThreshold;
                    }

                case ActionType.PlayCard:
                    {
                        var src = action.Source;
                        var tgt = action.Target;
                        if (src == null) return false;

                        if (src.Type == Card.CType.WEAPON || (src.Type == Card.CType.MINION && src.HasCharge))
                            return true;

                        if (tgt != null && (tgt.IsTaunt || tgt.HasPoison || tgt.IsWindfury || tgt.SpellPower > 0))
                            return true;

                        if (board?.EnemyHero != null && tgt != null && tgt.EntityId == board.EnemyHero.EntityId)
                            return true;

                        return false;
                    }

                case ActionType.HeroPower:
                    if (action.Target == null) return lethalMode;
                    return !action.Target.IsFriend && (action.Target.Type == Card.CType.HERO || action.Target.IsTaunt || action.Target.HasPoison);

                default:
                    return false;
            }
        }

        public static float EstimateOptimisticGain(SimBoard board, int remainingDepth, AggroInteractionContext aggroContext)
        {
            if (board == null) return 0f;
            if (aggroContext == null) aggroContext = AggroInteractionContext.FromAggroCoef(1f);

            int attackerCount = 0;
            int attackSum = 0;
            foreach (var m in board.FriendMinions)
            {
                if (m == null || !m.CanAttack || m.Type == Card.CType.LOCATION) continue;
                attackerCount++;
                attackSum += Math.Max(0, m.Atk) * (m.IsWindfury ? 2 : 1);
            }
            if (board.FriendHero != null && board.FriendHero.CanAttack)
            {
                attackerCount++;
                attackSum += Math.Max(0, board.FriendHero.Atk);
            }

            float depthScale = Math.Max(0.3f, Math.Min(1.2f, remainingDepth * 0.18f + 0.35f));
            float resourceBoost = Math.Max(0f, board.Mana) * 0.55f + Math.Max(0, board.Hand?.Count ?? 0) * 0.22f;
            float tacticalBoost = attackerCount * 0.35f + attackSum * 0.12f * aggroContext.FaceBias;
            return (resourceBoost + tacticalBoost) * depthScale;
        }

        private static float EstimateAttackPriority(
            SimBoard board,
            GameAction action,
            AggroInteractionContext aggroContext,
            float lethalScale,
            float survivalScale)
        {
            var source = action.Source;
            var target = action.Target;
            if (target == null) return 0f;

            float p = 0f;
            bool isFace = board?.EnemyHero != null && target.EntityId == board.EnemyHero.EntityId;

            if (isFace)
            {
                p += Math.Max(0f, (source?.Atk ?? 0) * 0.45f) * aggroContext.FaceBias * lethalScale;
                int enemyEhp = (board?.EnemyHero?.Health ?? 0) + (board?.EnemyHero?.Armor ?? 0);
                if (enemyEhp > 0 && source != null && source.Atk >= enemyEhp)
                    p += 8f * lethalScale;
                else if (enemyEhp > 0 && enemyEhp <= aggroContext.SafeFaceEnemyEhpThreshold)
                    p += 2.2f * aggroContext.FaceBias * lethalScale;
                return p;
            }

            p += (target.Atk * 0.62f + target.Health * 0.18f) * aggroContext.ThreatBias;
            if (target.HasPoison) p += 3f * aggroContext.ThreatBias;
            if (target.IsWindfury) p += 2f * aggroContext.ThreatBias;
            if (target.SpellPower > 0) p += target.SpellPower * 1.4f * aggroContext.ThreatBias;
            if (target.IsTaunt) p += 1.5f * aggroContext.TradeBias;

            if (source != null)
            {
                if (source.HasPoison) p += 1.8f * aggroContext.TradeBias;
                if (source.IsFrozen || source.Atk <= 0) p -= 2f;
                if (source.IsDivineShield) p += 1.2f * survivalScale;
            }

            return p * survivalScale;
        }

        private static float EstimatePlayCardPriority(
            SimBoard board,
            GameAction action,
            AggroInteractionContext aggroContext,
            float lethalScale,
            float survivalScale)
        {
            var card = action.Source;
            if (card == null) return 0f;

            float p = Math.Min(7f, card.Cost * 0.66f);

            if (card.Type == Card.CType.MINION)
            {
                p += card.Atk * 0.3f + card.Health * 0.22f;
                if (card.HasBattlecry) p += 0.9f;
                if (card.HasCharge) p += 2.2f * lethalScale;
                if (card.HasRush) p += 1f * survivalScale;
            }
            else if (card.Type == Card.CType.SPELL)
            {
                p += 1f;
            }
            else if (card.Type == Card.CType.WEAPON)
            {
                p += Math.Max(0f, card.Atk * 0.75f) * lethalScale;
            }
            else if (card.Type == Card.CType.LOCATION)
            {
                p += 1.1f;
            }

            var target = action.Target;
            if (target != null)
            {
                if (board?.EnemyHero != null && target.EntityId == board.EnemyHero.EntityId)
                    p += 2.4f * lethalScale;
                else
                    p += (target.Atk * 0.25f + (target.IsTaunt ? 1.2f : 0f)) * aggroContext.ThreatBias;
            }

            return p;
        }

        private static float EstimateHeroPowerPriority(
            SimBoard board,
            GameAction action,
            AggroInteractionContext aggroContext,
            float lethalScale)
        {
            float p = 0.75f;
            var target = action.Target;
            if (target == null) return p;

            if (board?.EnemyHero != null && target.EntityId == board.EnemyHero.EntityId)
                return p + 2f * lethalScale;

            if (!target.IsFriend)
            {
                p += (target.Atk * 0.2f + target.Health * 0.1f) * aggroContext.ThreatBias;
                if (target.IsTaunt) p += 1f;
                if (target.IsDivineShield) p += 1f;
            }

            return p;
        }
    }
}
