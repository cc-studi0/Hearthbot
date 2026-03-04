using System;
using System.Linq;
using SmartBot.Plugins.API;

namespace BotMain.AI
{
    /// <summary>
    /// 交换(Trade)质量评估器。
    /// 为攻击动作计算额外的分数奖惩，引导 AI 做出高效交换：
    ///   - 用小随从换大随从 → 奖励
    ///   - 用大随从换小随从（overkill）→ 惩罚
    ///   - 剧毒换大怪 → 大奖励
    ///   - 圣盾随从交换 → 奖励（圣盾能保命）
    ///   - 该打脸时不交换 → 惩罚
    ///   - 该交换时去打脸 → 惩罚
    /// </summary>
    public sealed class TradeEvaluator
    {
        private readonly CardEffectDB _db;

        public TradeEvaluator(CardEffectDB db = null)
        {
            _db = db;
        }

        /// <summary>
        /// 评估一次攻击动作的交换质量，返回额外分数。
        /// 正分 = 鼓励这个交换，负分 = 不鼓励。
        /// aggroCoef: 来自 profile 的激进度系数（1=中性，>1=激进，<1=保守）。
        /// 激进时打脸奖励放大、交换奖励缩小；保守时反之。
        /// </summary>
        public float EvaluateAttack(SimBoard board, GameAction action, float aggroCoef = 1f)
        {
            if (action.Type != ActionType.Attack) return 0f;

            var attacker = action.Source;
            var target = action.Target;
            if (attacker == null || target == null) return 0f;

            bool isGoingFace = (board.EnemyHero != null && target.EntityId == board.EnemyHero.EntityId);

            if (isGoingFace)
                return EvaluateFaceAttack(board, attacker, aggroCoef);
            else
                return EvaluateMinionTrade(board, attacker, target, aggroCoef);
        }

        // ────────────────────────────────────────────────
        //  打脸评估
        // ────────────────────────────────────────────────

        private float EvaluateFaceAttack(SimBoard board, SimEntity attacker, float aggroCoef)
        {
            float bonus = 0f;

            // aggroCoef > 1 → 打脸奖励放大；defCoef = 2 - aggroCoef → 解场惩罚缩小
            float defCoef = Math.Max(0.2f, 2f - aggroCoef);

            int enemyEhp = 0;
            if (board.EnemyHero != null)
                enemyEhp = board.EnemyHero.Health + board.EnemyHero.Armor;

            // 场上总攻 vs 对方血量
            int totalFaceAtk = 0;
            foreach (var m in board.FriendMinions)
                if (m.CanAttack && m.Type != Card.CType.LOCATION) totalFaceAtk += m.Atk;
            if (board.FriendHero != null && board.FriendHero.CanAttack)
                totalFaceAtk += board.FriendHero.Atk;

            // ── 如果打脸可以接近斩杀，奖励 ──
            if (enemyEhp > 0 && enemyEhp <= totalFaceAtk * 1.5f)
            {
                bonus += 3f * aggroCoef; // 激进 profile 放大接近斩杀的打脸奖励
                if (enemyEhp <= attacker.Atk)
                    bonus += 5f * aggroCoef; // 这一刀就能杀
            }

            // ── 如果对面有高威胁随从没解，打脸可能不明智 ──
            bool hasHighThreat = false;
            int totalEnemyAtk = 0;
            foreach (var m in board.EnemyMinions)
            {
                if (m.Type == Card.CType.LOCATION || m.IsStealth) continue;
                totalEnemyAtk += m.Atk;
                if (m.Atk >= 5 || m.HasPoison || (m.Atk >= 3 && m.IsWindfury))
                {
                    hasHighThreat = true;
                }
            }

            // 对方场攻接近我方生命值时，解场比打脸更重要
            int friendEhp = 30;
            if (board.FriendHero != null) friendEhp = board.FriendHero.Health + board.FriendHero.Armor;

            if (hasHighThreat && enemyEhp > 15)
            {
                // 对方血多 + 场上有大威胁 = 打脸不太好（保守 profile 惩罚更重）
                bonus -= 2f * defCoef;
            }

            // 如果对方场攻 >= 我方有效血量的 60%，打脸太危险
            if (totalEnemyAtk >= friendEhp * 0.6f && enemyEhp > 10)
            {
                bonus -= 3f * defCoef;
            }

            // ── 用剧毒随从打脸是浪费 ──
            if (attacker.HasPoison && board.EnemyMinions.Count > 0)
            {
                bonus -= 4f * defCoef; // 剧毒随从应该用来解场
            }

            // ── 低攻随从打脸价值低（除非接近斩杀） ──
            if (attacker.Atk <= 1 && enemyEhp > 10)
            {
                bonus -= 1f;
            }

            // 具有持续收益能力的随从在非斩杀局面更应优先保留解场价值
            if (IsPersistentValueMinion(attacker) && hasHighThreat && enemyEhp > 10)
            {
                bonus -= 2f * defCoef;
            }

            return bonus;
        }

        // ────────────────────────────────────────────────
        //  随从交换评估
        // ────────────────────────────────────────────────

        private float EvaluateMinionTrade(SimBoard board, SimEntity attacker, SimEntity target, float aggroCoef)
        {
            float bonus = 0f;

            // tradeCoef: 保守时(aggroCoef<1)交换奖励放大，激进时(aggroCoef>1)交换奖励缩小
            float tradeCoef = Math.Max(0.2f, 2f - aggroCoef);

            // 预判交换结果
            bool attackerDies = WillDie(attacker, target);
            bool targetDies = WillDie(target, attacker);

            float attackerValue = GetTradeValue(attacker);
            float targetValue = GetTradeValue(target);
            bool attackerIsPersistent = IsPersistentValueMinion(attacker);
            bool targetHighThreat = IsHighThreatTarget(target);
            int followupDamage = EstimateFollowupDamage(board, attacker);
            int remainingTargetHealth = EstimateRemainingHealthAfterAttack(target, attacker);
            bool setsUpKill = !targetDies && followupDamage >= remainingTargetHealth;

            // ── 1. 交换效率核心计算 ──
            if (targetDies && !attackerDies)
            {
                // 最佳交换：杀死对方、自己存活
                float efficiency = targetValue / Math.Max(1f, attackerValue);
                bonus += (6f + Math.Min(10f, efficiency * 2.2f)) * tradeCoef;

                // 剧毒换大怪特别赚
                if (attacker.HasPoison && targetValue >= 8f)
                    bonus += 5f * tradeCoef;

                // 圣盾保命交换
                if (attacker.IsDivineShield)
                    bonus += 3f * tradeCoef;

                // 持续收益随从安全解怪，额外鼓励
                if (attackerIsPersistent)
                    bonus += 1.5f * tradeCoef;
            }
            else if (targetDies && attackerDies)
            {
                // 同归于尽：看谁更值钱
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

                // 持续收益随从不应轻易平换
                if (attackerIsPersistent)
                    bonus -= 3f * tradeCoef;
            }
            else if (!targetDies && !attackerDies)
            {
                // 都没死：通常不优，只有在"为后续补刀铺路"时才可接受
                bonus -= 2.5f * tradeCoef;

                // 但如果是在磨嘲讽，还行
                if (target.IsTaunt)
                    bonus += 1.5f * tradeCoef;

                // 撞掉圣盾 = 有价值（为后续攻击铺路）
                if (target.IsDivineShield)
                    bonus += 2f * tradeCoef;

                // 多小换大：这一下虽然没解掉，但能被后续攻击补刀
                if (setsUpKill && targetHighThreat)
                    bonus += 2.5f * tradeCoef;
                else if (!setsUpKill)
                    bonus -= 1.5f * tradeCoef;

                // 持续收益随从更不应该做无效磨血
                if (attackerIsPersistent && !setsUpKill)
                    bonus -= 4f * tradeCoef;
            }
            else // !targetDies && attackerDies
            {
                // 最差：我死了对方没死
                bonus -= 8f * tradeCoef;

                // 但如果是 1/1 送掉圣盾，可以接受
                if (target.IsDivineShield && attackerValue <= 3f)
                    bonus += 2f;

                // 多小换大中的"垫刀"：若后续可补刀且目标威胁高，适度放宽
                if (setsUpKill && targetHighThreat && attackerValue <= targetValue)
                    bonus += 4f;

                // 持续收益随从不应被白白送掉
                if (attackerIsPersistent)
                    bonus -= 6f * tradeCoef;
            }

            // ── 2. 目标优先级（保守时更重视解场） ──
            // 高攻随从应该优先解
            if (target.Atk >= 5)
                bonus += 2f * tradeCoef;
            if (target.Atk >= 8)
                bonus += 2f * tradeCoef;

            // 剧毒随从必须优先解
            if (target.HasPoison)
                bonus += 3f * tradeCoef;

            // 风怒随从威胁翻倍
            if (target.IsWindfury)
                bonus += target.Atk * 0.5f * tradeCoef;

            // 法术强度随从值得解
            if (target.SpellPower > 0)
                bonus += target.SpellPower * 1.5f * tradeCoef;

            // 有亡语的目标要谨慎
            if (target.HasDeathrattle)
                bonus -= 1f;

            // ── 3. Overkill 惩罚 ──
            if (targetDies && !attacker.HasPoison)
            {
                int overkill = attacker.Atk - target.Health;
                if (target.IsDivineShield)
                    overkill = attacker.Atk; // 圣盾要打两下，第一下 overkill 无意义
                else if (overkill > 3)
                    bonus -= overkill * 0.5f; // 浪费了太多攻击力
            }

            // ── 4. 攻击者特殊考虑 ──
            // 用高攻随从换低价值目标不划算
            if (attacker.Atk >= 5 && targetValue <= 4f && attackerDies)
                bonus -= 3f;

            // 风怒随从价值高，用来换不太好（除非目标威胁大）
            if (attacker.IsWindfury && attackerDies && targetValue < attacker.Atk * 2)
                bonus -= 2f;

            if (attackerIsPersistent && attackerDies)
                bonus -= Math.Min(6f, attackerValue * 0.4f);

            return bonus;
        }

        // ────────────────────────────────────────────────
        //  辅助方法
        // ────────────────────────────────────────────────

        /// <summary>预判攻击者/防御者是否会死</summary>
        private static bool WillDie(SimEntity self, SimEntity opponent)
        {
            if (opponent.HasPoison && opponent.Atk > 0)
                return !self.IsDivineShield && !self.IsImmune;

            int incomingDmg = opponent.Atk;
            if (self.IsDivineShield)
                return false; // 圣盾挡一下
            if (self.IsImmune)
                return false;
            return incomingDmg >= self.Health;
        }

        private static bool IsHighThreatTarget(SimEntity target)
        {
            if (target == null) return false;
            if (target.HasPoison) return true;
            if (target.IsWindfury) return true;
            if (target.SpellPower > 0) return true;
            if (target.Atk >= 5) return true;
            return target.IsDivineShield && target.Atk >= 3;
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
        /// 随从的"交换价值"——用于判断交换是否划算。
        /// 与 BoardEvaluator 的 MinionValue 不同，这里更关注"如果这个随从死了，损失多大"。
        /// </summary>
        private float GetTradeValue(SimEntity m)
        {
            float val = 0;

            // 基础身材
            val += m.Atk * 1.0f;
            val += m.Health * 0.8f;

            // 关键字加成（死了就没了的价值）
            if (m.IsTaunt)        val += 2f;
            if (m.IsDivineShield)  val += Math.Max(2f, m.Atk * 1.0f);
            if (m.HasPoison)      val += 4f;
            if (m.IsLifeSteal)    val += 1f + m.Atk * 0.3f;
            if (m.IsWindfury)     val += m.Atk * 1.0f;
            if (m.HasReborn)      val += 2f;
            if (m.IsStealth)      val += 1.5f;
            if (m.IsImmune)       val += 4f;
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
    }
}
