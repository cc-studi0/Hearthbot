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
        /// <summary>
        /// 评估一次攻击动作的交换质量，返回额外分数。
        /// 正分 = 鼓励这个交换，负分 = 不鼓励。
        /// </summary>
        public float EvaluateAttack(SimBoard board, GameAction action)
        {
            if (action.Type != ActionType.Attack) return 0f;

            var attacker = action.Source;
            var target = action.Target;
            if (attacker == null || target == null) return 0f;

            bool isGoingFace = (board.EnemyHero != null && target.EntityId == board.EnemyHero.EntityId);

            if (isGoingFace)
                return EvaluateFaceAttack(board, attacker);
            else
                return EvaluateMinionTrade(board, attacker, target);
        }

        // ────────────────────────────────────────────────
        //  打脸评估
        // ────────────────────────────────────────────────

        private float EvaluateFaceAttack(SimBoard board, SimEntity attacker)
        {
            float bonus = 0f;

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
                bonus += 3f; // 接近斩杀，打脸很好
                if (enemyEhp <= attacker.Atk)
                    bonus += 5f; // 这一刀就能杀
            }

            // ── 如果对面有高威胁随从没解，打脸可能不明智 ──
            bool hasHighThreat = false;
            foreach (var m in board.EnemyMinions)
            {
                if (m.Type == Card.CType.LOCATION || m.IsStealth) continue;
                if (m.Atk >= 5 || m.HasPoison || (m.Atk >= 3 && m.IsWindfury))
                {
                    hasHighThreat = true;
                    break;
                }
            }

            if (hasHighThreat && enemyEhp > 15)
            {
                // 对方血多 + 场上有大威胁 = 打脸不太好
                bonus -= 2f;
            }

            // ── 用剧毒随从打脸是浪费 ──
            if (attacker.HasPoison && board.EnemyMinions.Count > 0)
            {
                bonus -= 4f; // 剧毒随从应该用来解场
            }

            // ── 低攻随从打脸价值低（除非接近斩杀） ──
            if (attacker.Atk <= 1 && enemyEhp > 10)
            {
                bonus -= 1f;
            }

            return bonus;
        }

        // ────────────────────────────────────────────────
        //  随从交换评估
        // ────────────────────────────────────────────────

        private float EvaluateMinionTrade(SimBoard board, SimEntity attacker, SimEntity target)
        {
            float bonus = 0f;

            // 预判交换结果
            bool attackerDies = WillDie(attacker, target);
            bool targetDies = WillDie(target, attacker);

            float attackerValue = GetTradeValue(attacker);
            float targetValue = GetTradeValue(target);

            // ── 1. 交换效率核心计算 ──
            if (targetDies && !attackerDies)
            {
                // 最佳交换：杀死对方、自己存活
                float efficiency = targetValue / Math.Max(1f, attackerValue);
                bonus += 4f + Math.Min(8f, efficiency * 2f);

                // 剧毒换大怪特别赚
                if (attacker.HasPoison && targetValue >= 8f)
                    bonus += 5f;

                // 圣盾保命交换
                if (attacker.IsDivineShield)
                    bonus += 3f;
            }
            else if (targetDies && attackerDies)
            {
                // 同归于尽：看谁更值钱
                float tradeRatio = targetValue / Math.Max(1f, attackerValue);

                if (tradeRatio >= 2f)
                    bonus += 5f;  // 用低值换高值 = 好交换
                else if (tradeRatio >= 1.2f)
                    bonus += 2f;  // 略赚
                else if (tradeRatio >= 0.8f)
                    bonus += 0f;  // 平换
                else if (tradeRatio >= 0.5f)
                    bonus -= 2f;  // 亏了
                else
                    bonus -= 4f;  // 大亏
            }
            else if (!targetDies && !attackerDies)
            {
                // 都没死：一般不太好，除非…
                // 有特殊情况（比如把嘲讽血磨低方便后续解）
                float dmgRatio = (float)attacker.Atk / Math.Max(1, target.Health);
                bonus -= 1f;

                // 但如果是在磨嘲讽，还行
                if (target.IsTaunt)
                    bonus += 2f;
            }
            else // !targetDies && attackerDies
            {
                // 最差：我死了对方没死
                bonus -= 5f;

                // 但如果是 1/1 送掉圣盾，可以接受
                if (target.IsDivineShield && attackerValue <= 3f)
                    bonus += 3f;
            }

            // ── 2. 目标优先级 ──
            // 高攻随从应该优先解
            if (target.Atk >= 5)
                bonus += 2f;
            if (target.Atk >= 8)
                bonus += 2f;

            // 剧毒随从必须优先解
            if (target.HasPoison)
                bonus += 3f;

            // 风怒随从威胁翻倍
            if (target.IsWindfury)
                bonus += target.Atk * 0.5f;

            // 法术强度随从值得解
            if (target.SpellPower > 0)
                bonus += target.SpellPower * 1.5f;

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

        /// <summary>
        /// 随从的"交换价值"——用于判断交换是否划算。
        /// 与 BoardEvaluator 的 MinionValue 不同，这里更关注"如果这个随从死了，损失多大"。
        /// </summary>
        private static float GetTradeValue(SimEntity m)
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

            return val;
        }
    }
}
