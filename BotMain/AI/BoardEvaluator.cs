using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Plugins.API;
using SmartBotProfiles;

namespace BotMain.AI
{
    /// <summary>
    /// 综合棋盘评估器。
    /// 评估维度：场面控制、英雄存活、威胁度、节奏(法力效率)、
    /// 武器价值、法术强度、嘲讽墙、手牌资源、斩杀威胁、冰冻惩罚。
    /// 所有维度均与 ProfileParameters 联动。
    /// </summary>
    public class BoardEvaluator
    {
        private readonly CardEffectDB _db;

        public BoardEvaluator(CardEffectDB db = null)
        {
            _db = db;
        }

        // ── 权重常量（基线，会被 Profile 修饰符调整） ──

        // 英雄生命
        private const float W_FriendHeroHp      = 1.5f;   // 每点友方血量
        private const float W_FriendHeroArmor    = 1.4f;   // 每点护甲（略低于血量因为可以回复）
        private const float W_EnemyHeroHp        = 1.2f;   // 每点敌方血量（扣分）
        private const float W_EnemyHeroArmor     = 1.1f;

        // 低血量惩罚/奖励
        private const float W_FriendLowHpPenalty = -8f;    // 友方 ≤15 血时的额外惩罚系数
        private const float W_EnemyLowHpBonus    = 6f;     // 敌方 ≤15 血时的额外奖励系数

        // 场面控制
        private const float W_BoardControlBonus  = 8f;     // 有场面优势时的奖励
        private const float W_EmptyBoardPenalty  = -5f;    // 己方没随从的惩罚

        // 手牌
        private const float W_HandCard           = 2.5f;   // 每张手牌
        private const float W_HandOverflow       = -1f;    // 手牌 > 7 时递减
        private const float W_EmptyHandPenalty   = -4f;    // 0 手牌惩罚

        // 法力效率
        private const float W_WastedMana         = -0.8f;  // 每点未花费的法力

        // 武器
        private const float W_FriendWeapon       = 3f;     // 友方武器基础价值
        private const float W_EnemyWeapon        = -3f;    // 敌方武器威胁

        // 杂项
        private const float W_FriendCardDraw     = 3f;     // 每次抽牌的价值
        private const float W_SpellPower         = 2f;     // 法术强度加成

        public float Evaluate(SimBoard board, ProfileParameters param)
        {
            // ── 终局检测 ──
            if (board.EnemyHero != null && board.EnemyHero.Health + board.EnemyHero.Armor <= 0)
                return 100000f;
            if (board.FriendHero != null && board.FriendHero.Health + board.FriendHero.Armor <= 0)
                return -100000f;

            float score = 0;

            // ── 1. 场上随从评估 ──
            float friendBoardValue = 0;
            float enemyBoardValue = 0;
            int friendMinionCount = 0;
            int enemyMinionCount = 0;
            int friendTotalAtk = 0;
            int friendTauntCount = 0;
            int enemyTauntCount = 0;
            int friendSpellPower = 0;

            foreach (var m in board.FriendMinions)
            {
                if (m.Type == Card.CType.LOCATION) continue; // 地标不算随从
                float val = MinionValueFriend(m);
                float coef = GetRulesCoef(param?.OnBoardFriendlyMinionsValuesModifiers, m.CardId);
                friendBoardValue += val * coef;
                friendMinionCount++;
                friendTotalAtk += m.Atk;
                if (m.IsTaunt) friendTauntCount++;
                friendSpellPower += m.SpellPower;
            }

            foreach (var m in board.EnemyMinions)
            {
                if (m.Type == Card.CType.LOCATION) continue;
                float val = MinionValueEnemy(m);
                float coef = GetRulesCoef(param?.OnBoardBoardEnemyMinionsModifiers, m.CardId);
                enemyBoardValue += val * coef;
                enemyMinionCount++;
                if (m.IsTaunt) enemyTauntCount++;
            }

            score += friendBoardValue;
            score -= enemyBoardValue;

            // ── 2. 场面控制评判 ──
            if (friendMinionCount > 0 && enemyMinionCount == 0)
                score += W_BoardControlBonus;    // 完全控场
            else if (friendMinionCount == 0 && enemyMinionCount > 0)
                score += W_EmptyBoardPenalty;     // 没有随从很危险
            else if (friendMinionCount > enemyMinionCount)
                score += (friendMinionCount - enemyMinionCount) * 1.5f; // 数量优势

            // ── 3. 英雄血量 ──
            if (board.FriendHero != null)
            {
                float defCoef = GetModifierCoef(param?.GlobalDefenseModifier);
                int friendHp = board.FriendHero.Health;
                int friendArmor = board.FriendHero.Armor;
                int friendEhp = friendHp + friendArmor;

                score += friendHp * W_FriendHeroHp * defCoef;
                score += friendArmor * W_FriendHeroArmor * defCoef;

                // 低血量连续惩罚：越低越危急，无硬阈值跳变
                if (friendEhp < 30)
                {
                    float dangerRatio = Math.Max(0f, 1f - friendEhp / 30f); // 0~1
                    score += W_FriendLowHpPenalty * dangerRatio * dangerRatio * 15f * defCoef;
                }

                // 友方有嘲讽时低血量没那么危险
                if (friendTauntCount > 0 && friendEhp <= 15)
                    score += friendTauntCount * 2f;
            }

            if (board.EnemyHero != null)
            {
                float aggCoef = GetModifierCoef(param?.GlobalAggroModifier);
                int enemyHp = board.EnemyHero.Health;
                int enemyArmor = board.EnemyHero.Armor;
                int enemyEhp = enemyHp + enemyArmor;

                score -= enemyHp * W_EnemyHeroHp * aggCoef;
                score -= enemyArmor * W_EnemyHeroArmor * aggCoef;

                // 接近斩杀连续奖励
                if (enemyEhp < 30)
                {
                    float killRatio = Math.Max(0f, 1f - enemyEhp / 30f);
                    score += W_EnemyLowHpBonus * killRatio * killRatio * 15f * aggCoef;
                }

                // 场攻可以威胁对手
                if (friendTotalAtk > 0 && enemyEhp > 0 && enemyTauntCount == 0)
                {
                    float lethalPressure = Math.Min(1f, (float)friendTotalAtk / enemyEhp);
                    score += lethalPressure * 5f * aggCoef;
                }
            }

            // ── 4. 手牌资源 ──
            float drawCoef = GetModifierCoef(param?.GlobalDrawModifier);
            int handCount = board.Hand.Count;
            if (handCount == 0)
                score += W_EmptyHandPenalty * drawCoef;
            else if (handCount <= 7)
                score += handCount * W_HandCard * drawCoef;
            else
                score += (7 * W_HandCard + (handCount - 7) * W_HandOverflow) * drawCoef;

            // 抽牌价值
            score += board.FriendCardDraw * W_FriendCardDraw * drawCoef;

            // ── 5. 法力效率 ──
            score += board.Mana * W_WastedMana;

            // ── 6. 武器评估 ──
            if (board.FriendWeapon != null)
            {
                float weaponCoef = GetModifierCoef(param?.GlobalWeaponsAttackModifier);
                float wv = WeaponValue(board.FriendWeapon);
                score += wv * W_FriendWeapon * weaponCoef;

                // 英雄能攻击时，武器更有价值
                if (board.FriendHero != null && board.FriendHero.CanAttack)
                    score += board.FriendWeapon.Atk * 1.5f * weaponCoef;
            }
            else if (board.FriendHero != null && board.FriendHero.Atk > 0 && board.FriendHero.CanAttack)
            {
                // 无武器但英雄有攻击力（如德鲁伊变形、恶魔猎手技能等）
                float weaponCoef = GetModifierCoef(param?.GlobalWeaponsAttackModifier);
                score += board.FriendHero.Atk * 1.5f * weaponCoef;
            }
            if (board.EnemyWeapon != null)
            {
                float wv = WeaponValue(board.EnemyWeapon);
                score += wv * W_EnemyWeapon;
            }

            // ── 7. 法术强度 ──
            score += friendSpellPower * W_SpellPower;

            // ── 8. 对方场面威胁评估（下回合可能对我方造成的伤害） ──
            if (board.FriendHero != null)
            {
                float incomingDmg = EstimateIncomingDamage(board);
                int friendEhp2 = board.FriendHero.Health + board.FriendHero.Armor;

                // 如果下回合可能致命，大幅惩罚
                if (incomingDmg >= friendEhp2 && enemyMinionCount > 0)
                    score -= 30f;
                else if (incomingDmg >= friendEhp2 * 0.6f)
                    score -= incomingDmg * 0.8f;
                else
                    score -= incomingDmg * 0.3f;
            }

            // ── 8.5 敌方反打换子风险（近似 enemyTurnPen） ──
            // 评估我方高价值随从在下回合是否会被高效率吃掉，避免“当前回合看起来赚，实则送场面”。
            score -= EstimateEnemyTradePenalty(board, GetModifierCoef(param?.GlobalDefenseModifier));

            // ── 9. 嘲讽墙价值 ──
            if (friendTauntCount > 0 && board.FriendHero != null)
            {
                float defCoef = GetModifierCoef(param?.GlobalDefenseModifier);
                int friendEhp3 = board.FriendHero.Health + board.FriendHero.Armor;
                // 嘲讽墙价值 = 嘲讽总血量防御力 + 我方血越低嘲讽越值钱
                float tauntTotalHp = 0;
                foreach (var m in board.FriendMinions)
                    if (m.IsTaunt && m.Type != Card.CType.LOCATION) tauntTotalHp += m.Health;
                float tauntBonus = friendTauntCount * 1.5f + tauntTotalHp * 0.5f;
                if (friendEhp3 < 30)
                {
                    float urgency = Math.Max(0f, 1f - friendEhp3 / 30f);
                    tauntBonus *= 1f + urgency * 2f;
                }
                score += tauntBonus * defCoef;
            }

            // ── 10. 冰冻惩罚 ──
            foreach (var m in board.FriendMinions)
            {
                if (m.IsFrozen && m.Atk > 0)
                    score -= m.Atk * 1.2f; // 冻住 = 浪费了一次攻击
            }
            if (board.FriendHero != null && board.FriendHero.IsFrozen && board.FriendWeapon != null)
                score -= board.FriendWeapon.Atk * 1.5f;

            return score;
        }

        // ────────────────────────────────────────────────
        //  随从价值评估（友方 vs 敌方分开计算）
        // ────────────────────────────────────────────────

        /// <summary>友方随从价值：重视能带来持续价值的属性</summary>
        private float MinionValueFriend(SimEntity m)
        {
            if (m.Type == Card.CType.LOCATION) return 0;

            float val = 0;

            // 基础身材：攻击力权重略高（进攻价值）
            val += m.Atk * 1.2f;
            val += m.Health * 1.0f;

            // 关键字
            if (m.IsTaunt)       val += 3f;
            if (m.IsDivineShield) val += Math.Max(3f, m.Atk * 1.5f); // 圣盾 + 高攻 = 极高价值
            if (m.HasPoison)     val += 5f;                            // 剧毒 = 高交换效率
            if (m.IsLifeSteal)   val += 2f + m.Atk * 0.5f;            // 吸血与攻击力关联
            if (m.IsWindfury)    val += m.Atk * 1.5f;                  // 风怒 = 双倍攻击
            if (m.HasReborn)     val += 2f + m.Atk * 0.3f;
            if (m.IsStealth)     val += 2f;                            // 潜行 = 安全
            if (m.IsImmune)      val += 5f;
            if (m.HasDeathrattle) val += 1.5f;                         // 亡语有额外价值
            if (IsPersistentValueMinion(m)) val += 4f + m.Health * 0.5f; // 持续收益随从应尽量保命

            // 法术强度额外加分
            if (m.SpellPower > 0) val += m.SpellPower * 2f;

            // 刚下场的随从本回合不能用，价值略低
            if (m.IsTired)
                val -= 0.5f;

            return val;
        }

        /// <summary>敌方随从威胁度：重视对我方造成威胁的属性</summary>
        private float MinionValueEnemy(SimEntity m)
        {
            if (m.Type == Card.CType.LOCATION) return 0;

            float val = 0;

            // 基础身材：攻击力是主要威胁
            val += m.Atk * 1.5f; // 高攻 = 高威胁
            val += m.Health * 0.8f;

            // 关键字
            if (m.IsTaunt)        val += 3f;                            // 嘲讽阻碍我方打脸
            if (m.IsDivineShield)  val += Math.Max(3f, m.Atk * 1.2f);   // 圣盾难解
            if (m.HasPoison)      val += 6f;                            // 剧毒随从极其危险
            if (m.IsLifeSteal)    val += 1.5f + m.Atk * 0.5f;
            if (m.IsWindfury)     val += m.Atk * 1.5f;
            if (m.HasReborn)      val += 2f;
            if (m.IsStealth)      val += 3f;                            // 潜行打不到
            if (m.IsImmune)       val += 8f;
            if (m.HasDeathrattle) val += 2f;                            // 亡语通常有负面后果
            if (IsPersistentValueMinion(m)) val += 3f + m.Health * 0.3f; // 对方持续收益随从优先处理

            if (m.SpellPower > 0) val += m.SpellPower * 2f;

            // 高攻低血 = 必须立即解决
            if (m.Atk >= 4 && m.Health <= 2)
                val += 2f;

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

        // ────────────────────────────────────────────────
        //  辅助评估
        // ────────────────────────────────────────────────

        /// <summary>武器价值 = 攻击力 × 耐久</summary>
        private static float WeaponValue(SimEntity weapon)
        {
            if (weapon == null) return 0;
            return weapon.Atk * weapon.Health * 0.5f;
        }

        /// <summary>
        /// 估算对方下个回合可能造成的伤害（含场上随从 + 武器）。
        /// 这是一个悲观估计（假设对方全力打脸）。
        /// </summary>
        private static float EstimateIncomingDamage(SimBoard board)
        {
            float dmg = 0;

            foreach (var m in board.EnemyMinions)
            {
                if (m.Type == Card.CType.LOCATION) continue;
                if (m.IsFrozen) continue;
                int attacks = m.IsWindfury ? 2 : 1;
                dmg += m.Atk * attacks;
            }

            // 敌方英雄（有武器时会打）
            if (board.EnemyWeapon != null && board.EnemyHero != null && !board.EnemyHero.IsFrozen)
                dmg += board.EnemyWeapon.Atk;

            return dmg;
        }

        /// <summary>
        /// 估算敌方下回合对我方随从的“高效交换”风险。
        /// 返回值越高，代表我方当前站场越容易被反手拆掉，主评估应扣分。
        /// </summary>
        private float EstimateEnemyTradePenalty(SimBoard board, float defenseCoef)
        {
            if (board == null) return 0f;

            var enemyAttackers = board.EnemyMinions
                .Where(m => m != null && m.Type != Card.CType.LOCATION && !m.IsFrozen && m.Atk > 0 && m.Health > 0)
                .ToList();
            var friendMinions = board.FriendMinions
                .Where(m => m != null && m.Type != Card.CType.LOCATION && m.Health > 0)
                .ToList();

            if (enemyAttackers.Count == 0 || friendMinions.Count == 0)
                return 0f;

            bool hasTauntWall = friendMinions.Any(m => m.IsTaunt);
            float penalty = 0f;

            foreach (var friend in friendMinions)
            {
                // 潜行/免疫单位在常规攻击线中不易被处理
                if (friend.IsStealth || friend.IsImmune) continue;

                float friendValue = MinionValueFriend(friend);
                if (friendValue <= 0f) continue;

                float bestNetGain = 0f;
                foreach (var enemy in enemyAttackers)
                {
                    if (!CanThreatenKill(enemy, friend)) continue;

                    float enemyValue = MinionValueEnemy(enemy);
                    bool enemyDies = CanThreatenKill(friend, enemy);
                    float enemyLoss = enemyDies ? enemyValue * (enemy.IsDivineShield ? 0.45f : 0.75f) : 0f;
                    float netGain = friendValue - enemyLoss;
                    if (netGain > bestNetGain)
                        bestNetGain = netGain;
                }

                if (bestNetGain <= 0f) continue;

                // 有嘲讽时，非嘲讽通常没那么容易被直接交易
                float laneCoef = !hasTauntWall ? 0.9f : (friend.IsTaunt ? 1f : 0.35f);
                float persistentCoef = IsPersistentValueMinion(friend) ? 1.35f : 1f;
                float rebornCoef = friend.HasReborn ? 0.72f : 1f;

                penalty += bestNetGain * laneCoef * persistentCoef * rebornCoef * 0.32f;
            }

            return penalty * Math.Max(0.5f, defenseCoef);
        }

        /// <summary>
        /// 近似判断 attacker 是否能在一次攻击序列中击杀 defender。
        /// 用于风险估算，不追求逐帧精准。
        /// </summary>
        private static bool CanThreatenKill(SimEntity attacker, SimEntity defender)
        {
            if (attacker == null || defender == null) return false;
            if (attacker.Atk <= 0 || defender.Health <= 0) return false;
            if (defender.IsImmune) return false;

            if (defender.IsDivineShield)
            {
                // 风怒可视作有机会先破盾再击杀（粗略近似）
                return attacker.IsWindfury && attacker.Atk >= defender.Health;
            }

            if (attacker.HasPoison) return true;
            return attacker.Atk >= defender.Health;
        }

        // ────────────────────────────────────────────────
        //  Profile 参数桥接
        // ────────────────────────────────────────────────

        private static float GetModifierCoef(Modifier mod)
        {
            if (mod == null) return 1f;
            return mod.GetValueCoef();
        }

        private static float GetRulesCoef(RulesSet rules, Card.Cards cardId)
        {
            if (rules?.RulesCardIds == null) return 1f;
            var rule = rules.RulesCardIds[cardId];
            if (rule?.CardModifier == null) return 1f;
            return rule.CardModifier.GetValueCoef();
        }
    }
}
