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
        private readonly IAggroInteractionModel _aggroModel;
        public bool EnableTradePenaltyDebug { get; set; }
        public Action<string> OnDebugLog { get; set; }

        public BoardEvaluator(CardEffectDB db = null, IAggroInteractionModel aggroModel = null)
        {
            _db = db;
            _aggroModel = aggroModel ?? new DefaultAggroInteractionModel();
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
        private const float W_WastedMana         = -2.0f;  // 每点未花费的法力（高惩罚鼓励花费法力）

        // 节奏奖励（鼓励出牌，弥补未注册效果的估值缺失）
        private const float W_TempoCardPlayed    = 1.5f;   // 每张本回合打出的牌的基础奖励
        private const float W_TempoCostRatio      = 0.6f;   // 出牌费用越高额外奖励越大

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
            var aggroCtx = _aggroModel.Build(board, param);
            var aggroCoef = aggroCtx.AggroCoef;
            var friendBoardScale = Clamp(0.85f + 0.22f * aggroCtx.SurvivalBias, 0.7f, 1.4f);
            var enemyBoardScale = Clamp(0.8f + 0.35f * aggroCtx.ThreatBias, 0.65f, 1.6f);

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
                friendBoardValue += val * coef * friendBoardScale;
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
                enemyBoardValue += val * coef * enemyBoardScale;
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
                int enemyHp = board.EnemyHero.Health;
                int enemyArmor = board.EnemyHero.Armor;
                int enemyEhp = enemyHp + enemyArmor;

                score -= enemyHp * W_EnemyHeroHp * aggroCoef * aggroCtx.FaceBias;
                score -= enemyArmor * W_EnemyHeroArmor * aggroCoef * aggroCtx.FaceBias;

                // 接近斩杀连续奖励
                if (enemyEhp < 30)
                {
                    float killRatio = Math.Max(0f, 1f - enemyEhp / 30f);
                    score += W_EnemyLowHpBonus * killRatio * killRatio * 15f * aggroCoef * aggroCtx.FaceBias;
                }

                // 场攻可以威胁对手
                if (friendTotalAtk > 0 && enemyEhp > 0 && enemyTauntCount == 0)
                {
                    float lethalPressure = Math.Min(1f, (float)friendTotalAtk / enemyEhp);
                    score += lethalPressure * 5f * aggroCoef * aggroCtx.FaceBias;
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

            // ── 5.5 节奏奖励（鼓励出牌，即使效果未注册也给予正向反馈） ──
            if (board.CardsPlayedThisTurn > 0)
            {
                // 基础奖励：每出一张牌 +W_TempoCardPlayed
                score += board.CardsPlayedThisTurn * W_TempoCardPlayed;
                // 费用加成：花费的法力越多（MaxMana - Mana），额外奖励越大
                int manaSpent = Math.Max(0, board.MaxMana - board.Mana);
                score += manaSpent * W_TempoCostRatio;
            }

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
                    score -= 30f * aggroCtx.SurvivalBias;
                else if (incomingDmg >= friendEhp2 * 0.6f)
                    score -= incomingDmg * 0.8f * aggroCtx.SurvivalBias;
                else
                    score -= incomingDmg * 0.3f * Math.Max(0.7f, aggroCtx.SurvivalBias * 0.7f);
            }

            // ── 8.5 敌方反打换子风险（近似 enemyTurnPen） ──
            // 评估我方高价值随从在下回合是否会被高效率吃掉，避免“当前回合看起来赚，实则送场面”。
            var enemyTradePenalty = EstimateEnemyTradePenalty(board, GetModifierCoef(param?.GlobalDefenseModifier), aggroCtx, out var enemyTradeDebug);
            score -= enemyTradePenalty;
            if (EnableTradePenaltyDebug && !string.IsNullOrWhiteSpace(enemyTradeDebug))
                OnDebugLog?.Invoke(enemyTradeDebug);

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
                score += tauntBonus * defCoef * aggroCtx.SurvivalBias;
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

        private sealed class EnemyAttackSlot
        {
            public SimEntity Attacker;
            public int RemainingStrikes;
            public bool IsHeroAttacker;
        }

        private readonly struct TradeAttempt
        {
            public TradeAttempt(int slotIndex, SimEntity target, SimEntity attackerAfter, float netGain)
            {
                SlotIndex = slotIndex;
                Target = target;
                AttackerAfter = attackerAfter;
                NetGain = netGain;
            }

            public int SlotIndex { get; }
            public SimEntity Target { get; }
            public SimEntity AttackerAfter { get; }
            public float NetGain { get; }
        }

        /// <summary>
        /// 估算敌方下回合对我方随从的“高效交换”风险。
        /// 返回值越高，代表我方当前站场越容易被反手拆掉，主评估应扣分。
        /// </summary>
        private float EstimateEnemyTradePenalty(SimBoard board, float defenseCoef, AggroInteractionContext aggroCtx, out string debugInfo)
        {
            debugInfo = null;
            if (board == null) return 0f;

            var friendPool = board.FriendMinions
                .Where(m => m != null && m.Type != Card.CType.LOCATION && m.Health > 0)
                .Select(m => m.Clone())
                .ToList();
            var attackSlots = BuildEnemyAttackSlots(board);

            if (friendPool.Count == 0 || attackSlots.Count == 0)
                return 0f;

            var debugEvents = EnableTradePenaltyDebug ? new List<string>() : null;
            float penalty = 0f;

            // 阶段A：先处理嘲讽线（有嘲讽时只能打嘲讽）
            penalty += SimulateEnemyTradeLane(friendPool, attackSlots, tauntPhase: true, debugEvents);
            // 阶段B：嘲讽清空后再处理其他目标
            penalty += SimulateEnemyTradeLane(friendPool, attackSlots, tauntPhase: false, debugEvents);

            var survivalScale = aggroCtx != null
                ? Clamp(0.7f + 0.4f * aggroCtx.SurvivalBias, 0.65f, 1.7f)
                : 1f;
            var finalPenalty = penalty * Math.Max(0.5f, defenseCoef) * survivalScale;

            if (debugEvents != null && debugEvents.Count > 0)
            {
                debugInfo = $"[AI][trade-risk] raw={penalty:0.##}, final={finalPenalty:0.##}, defenseCoef={Math.Max(0.5f, defenseCoef):0.##}, survivalScale={survivalScale:0.##}, picks={string.Join(" | ", debugEvents.Take(4))}";
            }

            return finalPenalty;
        }

        private static List<EnemyAttackSlot> BuildEnemyAttackSlots(SimBoard board)
        {
            var slots = new List<EnemyAttackSlot>();
            if (board == null) return slots;

            foreach (var enemy in board.EnemyMinions)
            {
                if (enemy == null || enemy.Type == Card.CType.LOCATION) continue;
                if (enemy.IsFrozen || enemy.Atk <= 0 || enemy.Health <= 0) continue;

                slots.Add(new EnemyAttackSlot
                {
                    Attacker = enemy.Clone(),
                    RemainingStrikes = enemy.IsWindfury ? 2 : 1,
                    IsHeroAttacker = false
                });
            }

            if (board.EnemyHero != null
                && board.EnemyWeapon != null
                && board.EnemyWeapon.Health > 0
                && !board.EnemyHero.IsFrozen)
            {
                var hero = board.EnemyHero.Clone();
                hero.Atk = Math.Max(0, board.EnemyWeapon.Atk);
                if (hero.Atk > 0)
                {
                    slots.Add(new EnemyAttackSlot
                    {
                        Attacker = hero,
                        RemainingStrikes = (hero.IsWindfury || board.EnemyWeapon.IsWindfury) ? 2 : 1,
                        IsHeroAttacker = true
                    });
                }
            }

            return slots;
        }

        private float SimulateEnemyTradeLane(List<SimEntity> friendPool, List<EnemyAttackSlot> slots, bool tauntPhase, List<string> debugEvents)
        {
            float lanePenalty = 0f;

            while (true)
            {
                List<SimEntity> candidateTargets;
                if (tauntPhase)
                {
                    candidateTargets = friendPool.Where(m => IsTradableTarget(m) && m.IsTaunt).ToList();
                }
                else
                {
                    if (friendPool.Any(m => IsTradableTarget(m) && m.IsTaunt))
                        break;
                    candidateTargets = friendPool.Where(IsTradableTarget).ToList();
                }

                if (candidateTargets.Count == 0)
                    break;

                TradeAttempt? bestAttempt = null;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (!IsSlotReady(slots[i])) continue;

                    foreach (var target in candidateTargets)
                    {
                        if (!TryBuildTradeAttempt(slots[i], i, target, out var attempt))
                            continue;

                        if (!bestAttempt.HasValue || attempt.NetGain > bestAttempt.Value.NetGain + 0.001f)
                            bestAttempt = attempt;
                    }
                }

                if (!bestAttempt.HasValue)
                    break;

                var chosen = bestAttempt.Value;
                var chosenTarget = chosen.Target;

                float laneCoef = chosenTarget.IsTaunt ? 1f : 0.88f;
                float persistentCoef = IsPersistentValueMinion(chosenTarget) ? 1.35f : 1f;
                float rebornCoef = chosenTarget.HasReborn ? 0.72f : 1f;
                float applied = chosen.NetGain * laneCoef * persistentCoef * rebornCoef * 0.34f;
                lanePenalty += applied;

                var slot = slots[chosen.SlotIndex];
                slot.Attacker = chosen.AttackerAfter;
                slot.RemainingStrikes--;
                if (slot.Attacker == null || slot.Attacker.Health <= 0)
                    slot.RemainingStrikes = 0;

                friendPool.Remove(chosenTarget);

                if (debugEvents != null)
                    debugEvents.Add($"{slot.Attacker?.CardId}->{chosenTarget.CardId}, net={chosen.NetGain:0.##}, applied={applied:0.##}, tauntPhase={(tauntPhase ? "Y" : "N")}");
            }

            return lanePenalty;
        }

        private bool TryBuildTradeAttempt(EnemyAttackSlot slot, int slotIndex, SimEntity target, out TradeAttempt attempt)
        {
            attempt = default;
            if (!IsSlotReady(slot) || !IsTradableTarget(target))
                return false;

            var attackerAfter = slot.Attacker.Clone();
            var targetAfter = target.Clone();
            ResolveSingleAttack(attackerAfter, targetAfter);

            if (targetAfter.Health > 0)
                return false;

            float friendBeforeValue = MinionValueFriend(target);
            float enemyBeforeValue = GetEnemyAttackerTradeValue(slot.Attacker, slot.IsHeroAttacker);
            float enemyAfterValue = attackerAfter.Health > 0
                ? GetEnemyAttackerTradeValue(attackerAfter, slot.IsHeroAttacker)
                : 0f;
            float enemyLoss = Math.Max(0f, enemyBeforeValue - enemyAfterValue);
            float netGain = friendBeforeValue - enemyLoss;
            if (netGain <= 0f)
                return false;

            attempt = new TradeAttempt(slotIndex, target, attackerAfter, netGain);
            return true;
        }

        private static bool IsSlotReady(EnemyAttackSlot slot)
        {
            return slot != null
                && slot.RemainingStrikes > 0
                && slot.Attacker != null
                && slot.Attacker.Health > 0
                && !slot.Attacker.IsFrozen
                && slot.Attacker.Atk > 0;
        }

        private static bool IsTradableTarget(SimEntity m)
        {
            return m != null
                && m.Type != Card.CType.LOCATION
                && m.Health > 0
                && !m.IsStealth
                && !m.IsImmune;
        }

        private static void ResolveSingleAttack(SimEntity attacker, SimEntity defender)
        {
            if (attacker == null || defender == null) return;
            if (attacker.Health <= 0 || defender.Health <= 0) return;

            int attackerAtk = Math.Max(0, attacker.Atk);
            int defenderAtk = Math.Max(0, defender.Atk);
            bool attackerPoison = attacker.HasPoison;
            bool defenderPoison = defender.HasPoison;

            DealCombatDamage(defender, attackerAtk, attackerPoison);
            if (defenderAtk > 0)
                DealCombatDamage(attacker, defenderAtk, defenderPoison);
        }

        private static void DealCombatDamage(SimEntity target, int damage, bool poisonous)
        {
            if (target == null || target.Health <= 0 || damage <= 0) return;

            if (target.IsDivineShield)
            {
                target.IsDivineShield = false;
                return;
            }

            target.Health -= damage;
            if (poisonous && target.Type != Card.CType.HERO && target.Health > 0)
                target.Health = 0;
        }

        private float GetEnemyAttackerTradeValue(SimEntity attacker, bool isHeroAttacker)
        {
            if (attacker == null) return 0f;
            if (!isHeroAttacker) return MinionValueEnemy(attacker);

            // 英雄交换价值只弱关联生命值，避免把英雄血量当作“随从损失”过度放大。
            float hpComponent = Math.Max(0, Math.Min(30, attacker.Health + attacker.Armor)) * 0.08f;
            float value = attacker.Atk * 1.8f + hpComponent;
            if (attacker.IsWindfury) value += attacker.Atk * 0.4f;
            if (attacker.IsImmune) value += 4f;
            return value;
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

            int remainingHealth = defender.Health;
            bool hasDivineShield = defender.IsDivineShield;
            int hits = attacker.IsWindfury ? 2 : 1;

            for (int i = 0; i < hits; i++)
            {
                if (remainingHealth <= 0) return true;
                if (hasDivineShield)
                {
                    hasDivineShield = false;
                    continue;
                }

                if (attacker.HasPoison && defender.Type != Card.CType.HERO)
                    return true;

                remainingHealth -= attacker.Atk;
            }

            return remainingHealth <= 0;
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

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
