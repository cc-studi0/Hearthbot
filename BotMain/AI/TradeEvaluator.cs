using System;
using System.Linq;
using SmartBot.Plugins.API;
using SmartBotProfiles;

namespace BotMain.AI
{
    /// <summary>
    /// 交换(Trade)质量评估器。
    /// 使用 Profile 的 GlobalAggroModifier 连续驱动打脸/解场偏好，避免固定阈值硬编码。
    /// </summary>
    public sealed class TradeEvaluator
    {
        private readonly CardEffectDB _db;
        private readonly IAggroInteractionModel _aggroModel;

        public TradeEvaluator(CardEffectDB db = null, IAggroInteractionModel aggroModel = null)
        {
            _db = db;
            _aggroModel = aggroModel ?? new DefaultAggroInteractionModel();
        }

        /// <summary>
        /// 评估一次攻击动作。正分=鼓励，负分=不鼓励。
        /// </summary>
        public float EvaluateAttack(SimBoard board, GameAction action, ProfileParameters param)
        {
            var context = _aggroModel.Build(board, param);
            return EvaluateAttack(board, action, context);
        }

        /// <summary>
        /// 兼容旧调用：仅传 aggroCoef。
        /// </summary>
        public float EvaluateAttack(SimBoard board, GameAction action, float aggroCoef = 1f)
        {
            var context = AggroInteractionContext.FromAggroCoef(aggroCoef);
            return EvaluateAttack(board, action, context);
        }

        private float EvaluateAttack(SimBoard board, GameAction action, AggroInteractionContext context)
        {
            if (action == null || action.Type != ActionType.Attack) return 0f;

            var attacker = action.Source;
            var target = action.Target;
            if (attacker == null || target == null) return 0f;

            bool isGoingFace = board?.EnemyHero != null && target.EntityId == board.EnemyHero.EntityId;
            return isGoingFace
                ? EvaluateFaceAttack(board, attacker, context)
                : EvaluateMinionTrade(board, attacker, target, context);
        }

        // ────────────────────────────────────────────────
        //  打脸评估
        // ────────────────────────────────────────────────

        private float EvaluateFaceAttack(SimBoard board, SimEntity attacker, AggroInteractionContext context)
        {
            float bonus = 0f;

            int enemyEhp = board?.EnemyHero != null
                ? board.EnemyHero.Health + board.EnemyHero.Armor
                : 0;

            int totalFaceAtk = 0;
            if (board != null)
            {
                foreach (var m in board.FriendMinions)
                    if (m.CanAttack && m.Type != Card.CType.LOCATION) totalFaceAtk += m.Atk;
                if (board.FriendHero != null && board.FriendHero.CanAttack)
                    totalFaceAtk += board.FriendHero.Atk;
            }

            // 接近斩杀窗口：激进系数越高，越鼓励打脸。
            if (enemyEhp > 0 && enemyEhp <= totalFaceAtk * context.LethalWindowMultiplier)
            {
                bonus += 3f * context.FaceBias;
                if (enemyEhp <= attacker.Atk)
                    bonus += 5f * context.FaceBias;
            }

            bool hasHighThreat = false;
            int totalEnemyAtk = 0;
            if (board != null)
            {
                foreach (var m in board.EnemyMinions)
                {
                    if (m.Type == Card.CType.LOCATION || m.IsStealth) continue;
                    totalEnemyAtk += m.Atk;
                    if (m.Atk >= context.HighThreatAttackThreshold
                        || m.HasPoison
                        || (m.IsWindfury && m.Atk >= context.HighThreatAttackThreshold - 2f))
                    {
                        hasHighThreat = true;
                    }
                }
            }

            int friendEhp = 30;
            if (board?.FriendHero != null)
                friendEhp = board.FriendHero.Health + board.FriendHero.Armor;

            if (hasHighThreat && enemyEhp > context.SafeFaceEnemyEhpThreshold)
                bonus -= 2f * context.SurvivalBias;

            if (friendEhp > 0
                && totalEnemyAtk >= friendEhp * context.DangerPressureThreshold
                && enemyEhp > Math.Max(6f, context.SafeFaceEnemyEhpThreshold - 2f))
            {
                bonus -= 3f * context.SurvivalBias;
            }

            if (attacker.HasPoison && board != null && board.EnemyMinions.Count > 0)
                bonus -= 4f * context.ThreatBias;

            if (attacker.Atk <= 1 && enemyEhp > Math.Max(8f, context.SafeFaceEnemyEhpThreshold))
                bonus -= 1f * context.TradeBias;

            if (IsPersistentValueMinion(attacker) && hasHighThreat && enemyEhp > context.SafeFaceEnemyEhpThreshold - 1f)
                bonus -= 2f * context.PersistentMinionConservation;

            return bonus;
        }

        // ────────────────────────────────────────────────
        //  随从交换评估
        // ────────────────────────────────────────────────

        private float EvaluateMinionTrade(SimBoard board, SimEntity attacker, SimEntity target, AggroInteractionContext context)
        {
            float bonus = 0f;
            float tradeCoef = context.TradeBias;

            bool attackerDies = WillDie(attacker, target);
            bool targetDies = WillDie(target, attacker);

            float attackerValue = GetTradeValue(attacker);
            float targetValue = GetTradeValue(target);
            bool attackerIsPersistent = IsPersistentValueMinion(attacker);
            bool targetHighThreat = IsHighThreatTarget(target, context);
            int followupDamage = EstimateFollowupDamage(board, attacker);
            int remainingTargetHealth = EstimateRemainingHealthAfterAttack(target, attacker);
            bool setsUpKill = !targetDies && followupDamage >= remainingTargetHealth;

            if (targetDies && !attackerDies)
            {
                float efficiency = targetValue / Math.Max(1f, attackerValue);
                bonus += (6f + Math.Min(10f, efficiency * 2.2f)) * tradeCoef;

                if (attacker.HasPoison && targetValue >= 8f)
                    bonus += 5f * tradeCoef;

                if (attacker.IsDivineShield)
                    bonus += 3f * tradeCoef;

                if (attackerIsPersistent)
                    bonus += 1.5f * tradeCoef;
            }
            else if (targetDies && attackerDies)
            {
                float tradeRatio = targetValue / Math.Max(1f, attackerValue);

                if (tradeRatio >= 2f)
                    bonus += 5f * tradeCoef;
                else if (tradeRatio >= 1.2f)
                    bonus += 2f * tradeCoef;
                else if (tradeRatio >= 0.8f)
                    bonus += 0f;
                else if (tradeRatio >= 0.5f)
                    bonus -= 2f * tradeCoef;
                else
                    bonus -= 4f * tradeCoef;

                if (attackerIsPersistent)
                    bonus -= 3f * tradeCoef;
            }
            else if (!targetDies && !attackerDies)
            {
                bonus -= 2.5f * tradeCoef;

                if (target.IsTaunt)
                    bonus += 1.5f * tradeCoef;

                if (target.IsDivineShield)
                    bonus += 2f * tradeCoef;

                if (setsUpKill && targetHighThreat)
                    bonus += 2.5f * tradeCoef;
                else if (!setsUpKill)
                    bonus -= 1.5f * tradeCoef;

                if (attackerIsPersistent && !setsUpKill)
                    bonus -= 4f * tradeCoef;
            }
            else
            {
                bonus -= 8f * tradeCoef;

                if (target.IsDivineShield && attackerValue <= 3f)
                    bonus += 2f;

                if (setsUpKill && targetHighThreat && attackerValue <= targetValue)
                    bonus += 4f;

                if (attackerIsPersistent)
                    bonus -= 6f * tradeCoef;
            }

            if (target.Atk >= context.HighThreatAttackThreshold)
                bonus += 2f * context.ThreatBias;
            if (target.Atk >= context.HighThreatAttackThreshold + 2f)
                bonus += 2f * context.ThreatBias;

            if (target.HasPoison)
                bonus += 3f * context.ThreatBias;

            if (target.IsWindfury)
                bonus += target.Atk * 0.5f * context.ThreatBias;

            if (target.SpellPower > 0)
                bonus += target.SpellPower * 1.5f * context.ThreatBias;

            bonus += EvaluateDeathrattleTradeBias(target, context);

            if (targetDies && !attacker.HasPoison)
            {
                var overkill = attacker.Atk - target.Health;
                if (target.IsDivineShield)
                    overkill = attacker.Atk;

                if (overkill > context.OverkillTolerance)
                {
                    var waste = overkill - context.OverkillTolerance + 1f;
                    bonus -= waste * 0.5f * context.OverkillPenaltyScale;
                }
            }

            if (attacker.Atk >= context.HighThreatAttackThreshold && targetValue <= 4f && attackerDies)
                bonus -= 3f * context.OverkillPenaltyScale;

            if (attacker.IsWindfury && attackerDies && targetValue < attacker.Atk * 2)
                bonus -= 2f * context.PersistentMinionConservation;

            if (attackerIsPersistent && attackerDies)
                bonus -= Math.Min(6f, attackerValue * 0.4f) * context.PersistentMinionConservation;

            return bonus;
        }

        // ────────────────────────────────────────────────
        //  辅助方法
        // ────────────────────────────────────────────────

        private static bool WillDie(SimEntity self, SimEntity opponent)
        {
            if (opponent.HasPoison && opponent.Atk > 0)
                return !self.IsDivineShield && !self.IsImmune;

            var incomingDmg = opponent.Atk;
            if (self.IsDivineShield) return false;
            if (self.IsImmune) return false;
            return incomingDmg >= self.Health;
        }

        private static bool IsHighThreatTarget(SimEntity target, AggroInteractionContext context)
        {
            if (target == null) return false;
            if (target.HasPoison) return true;
            if (target.IsWindfury) return true;
            if (target.SpellPower > 0) return true;
            if (target.Atk >= context.HighThreatAttackThreshold) return true;
            return target.IsDivineShield && target.Atk >= Math.Max(3f, context.HighThreatAttackThreshold - 1f);
        }

        private static int EstimateFollowupDamage(SimBoard board, SimEntity attacker)
        {
            if (board == null) return 0;

            int dmg = 0;
            foreach (var m in board.FriendMinions)
            {
                if (m == null || m.EntityId == attacker?.EntityId) continue;
                if (!m.CanAttack || m.Type == Card.CType.LOCATION) continue;
                dmg += m.IsWindfury ? m.Atk * 2 : m.Atk;
            }

            if (board.FriendHero != null
                && board.FriendHero.EntityId != attacker?.EntityId
                && board.FriendHero.CanAttack
                && !board.FriendHero.IsFrozen)
            {
                dmg += board.FriendHero.Atk;
            }

            return Math.Max(0, dmg);
        }

        private static int EstimateRemainingHealthAfterAttack(SimEntity target, SimEntity attacker)
        {
            if (target == null) return int.MaxValue;
            if (target.IsImmune) return int.MaxValue;
            if (target.IsDivineShield) return Math.Max(1, target.Health);

            int dmg = Math.Max(0, attacker?.Atk ?? 0);
            int remain = target.Health - dmg;
            return Math.Max(1, remain);
        }

        /// <summary>
        /// 随从的交换价值：更偏向“死亡损失”。
        /// </summary>
        private float GetTradeValue(SimEntity m)
        {
            float val = 0;

            val += m.Atk * 1f;
            val += m.Health * 0.8f;

            if (m.IsTaunt) val += 2f;
            if (m.IsDivineShield) val += Math.Max(2f, m.Atk * 1f);
            if (m.HasPoison) val += 4f;
            if (m.IsLifeSteal) val += 1f + m.Atk * 0.3f;
            if (m.IsWindfury) val += m.Atk * 1f;
            if (m.HasReborn) val += 2f;
            if (m.IsStealth) val += 1.5f;
            if (m.IsImmune) val += 4f;
            if (m.HasDeathrattle) val += 1f;
            if (m.SpellPower > 0) val += m.SpellPower * 1.5f;
            if (IsPersistentValueMinion(m)) val += 4f + m.Health * 0.4f;

            return val;
        }

        private bool IsPersistentValueMinion(SimEntity m)
        {
            if (m == null || m.Type != Card.CType.MINION) return false;
            if (m.SpellPower > 0) return true;
            if (_db == null) return false;

            return _db.Has(m.CardId, EffectTrigger.EndOfTurn)
                || _db.Has(m.CardId, EffectTrigger.Aura);
        }

        /// <summary>
        /// 亡语目标的分层偏置：
        /// - 低威胁亡语：维持轻微惩罚，避免无收益硬解；
        /// - 高威胁亡语（剧毒/高攻/风怒/法强/持续收益）：提升交换意愿。
        /// </summary>
        private float EvaluateDeathrattleTradeBias(SimEntity target, AggroInteractionContext context)
        {
            if (target == null || !target.HasDeathrattle)
                return 0f;

            float baseBias = -0.7f;
            float dangerScore = 0f;

            if (target.Atk >= context.HighThreatAttackThreshold)
                dangerScore += 1.6f;
            if (target.HasPoison)
                dangerScore += 2.5f;
            if (target.IsWindfury)
                dangerScore += 1.4f;
            if (target.SpellPower > 0)
                dangerScore += 1.2f + target.SpellPower * 0.8f;
            if (target.IsLifeSteal)
                dangerScore += 0.8f + target.Atk * 0.15f;
            if (target.IsTaunt)
                dangerScore += 0.9f;
            if (target.IsDivineShield)
                dangerScore += 0.6f;
            if (IsPersistentValueMinion(target))
                dangerScore += 1.4f;

            return baseBias + dangerScore * context.ThreatBias * 0.55f;
        }
    }
}
