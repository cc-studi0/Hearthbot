using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SmartBot.Plugins.API;

namespace BotMain.AI
{
    /// <summary>
    /// 专注搜索致命伤害(斩杀)的独立模块。
    /// 穷举所有可能的动作组合，判断是否存在一个动作序列可以将对方英雄血量降到 0 或以下。
    /// 搜索策略：
    ///   1. 先快速估算最大伤害上界（剪枝）
    ///   2. 深度优先搜索，优先尝试高伤害动作
    ///   3. 严格时间限制（默认 2 秒）
    /// </summary>
    public sealed class LethalFinder
    {
        private readonly BoardSimulator _sim;
        private readonly CardEffectDB _db;

        /// <summary>搜索超时 (毫秒)</summary>
        public int TimeoutMs { get; set; } = 2000;

        /// <summary>搜索过程中探索的节点数</summary>
        public long NodesExplored { get; private set; }

        public event Action<string> OnLog;

        public LethalFinder(BoardSimulator sim, CardEffectDB db)
        {
            _sim = sim;
            _db = db;
        }

        /// <summary>
        /// 在给定棋盘上搜索斩杀路线。
        /// 返回 null 表示未找到；否则返回能杀死对方英雄的动作序列。
        /// </summary>
        public List<GameAction> FindLethal(SimBoard board)
        {
            if (board.EnemyHero == null) return null;

            var sw = Stopwatch.StartNew();
            NodesExplored = 0;

            // ── 快速上界剪枝 ──
            int enemyEhp = GetEffectiveHp(board.EnemyHero, board);
            int maxDmgEstimate = EstimateMaxDamage(board);
            if (maxDmgEstimate < enemyEhp)
            {
                OnLog?.Invoke($"[Lethal] quick reject: enemyEhp={enemyEhp}, maxDmgEstimate={maxDmgEstimate}");
                return null;
            }

            OnLog?.Invoke($"[Lethal] searching... enemyEhp={enemyEhp}, maxEstimate={maxDmgEstimate}");

            // ── DFS 搜索 ──
            var result = new List<GameAction>();
            bool found = DFS(board.Clone(), result, sw, new HashSet<long>());

            if (found)
            {
                OnLog?.Invoke($"[Lethal] *** FOUND *** in {sw.ElapsedMilliseconds}ms, nodes={NodesExplored}, actions={result.Count}");
                return result;
            }

            OnLog?.Invoke($"[Lethal] not found in {sw.ElapsedMilliseconds}ms, nodes={NodesExplored}");
            return null;
        }

        // ────────────────────────────────────────────────
        //  核心 DFS
        // ────────────────────────────────────────────────

        private bool DFS(SimBoard board, List<GameAction> path, Stopwatch sw, HashSet<long> visited)
        {
            NodesExplored++;

            // 超时保护
            if (sw.ElapsedMilliseconds > TimeoutMs) return false;

            // 检查是否已斩杀
            if (IsEnemyDead(board)) return true;

            // 节点上限保护
            if (NodesExplored > 500_000) return false;

            // 路径深度限制（一般回合内不会超过 20 个动作）
            if (path.Count >= 20) return false;

            // 生成候选动作（不含 END_TURN）
            var candidates = GenerateLethalCandidates(board);
            if (candidates.Count == 0) return false;

            // 状态哈希去重
            long hash = BoardHash(board);
            if (!visited.Add(hash)) return false;

            // 尝试每个候选动作
            foreach (var action in candidates)
            {
                var clone = board.Clone();
                if (!TryApply(clone, action)) continue;

                path.Add(action);

                if (DFS(clone, path, sw, visited))
                    return true;

                path.RemoveAt(path.Count - 1);
            }

            visited.Remove(hash);
            return false;
        }

        // ────────────────────────────────────────────────
        //  候选动作生成（斩杀专用，并排序）
        // ────────────────────────────────────────────────

        private List<GameAction> GenerateLethalCandidates(SimBoard board)
        {
            var actions = new List<(GameAction action, int priority)>();
            var enemyHero = board.EnemyHero;
            if (enemyHero == null) return new List<GameAction>();

            // 是否有嘲讽
            bool hasTaunt = board.EnemyMinions.Any(m => m.IsTaunt && !m.IsStealth && m.IsAlive);
            var tauntMinions = hasTaunt
                ? board.EnemyMinions.Where(m => m.IsTaunt && !m.IsStealth && m.IsAlive).ToList()
                : new List<SimEntity>();

            // ─── 1. 场上随从 + 英雄直接攻击 ───
            var attackers = new List<SimEntity>();
            foreach (var m in board.FriendMinions)
                if (m.CanAttack && m.Type != Card.CType.LOCATION) attackers.Add(m);
            if (board.FriendHero != null && board.FriendHero.CanAttack)
                attackers.Add(board.FriendHero);

            foreach (var atk in attackers)
            {
                // 如果有嘲讽，必须先解嘲讽
                if (hasTaunt)
                {
                    foreach (var taunt in tauntMinions)
                    {
                        // Rush 随从首回合可以打随从
                        actions.Add((new GameAction
                        {
                            Type = ActionType.Attack,
                            Source = atk,
                            Target = taunt,
                            SourceEntityId = atk.EntityId,
                            TargetEntityId = taunt.EntityId
                        }, PriorityAttackTaunt(atk, taunt)));
                    }
                }
                else
                {
                    // 无嘲讽：打脸！
                    if (!enemyHero.IsImmune)
                    {
                        // Rush 随从首回合不能打脸
                        if (atk.HasRush && atk.CountAttack == 0 && !atk.IsTired)
                            continue;

                        actions.Add((new GameAction
                        {
                            Type = ActionType.Attack,
                            Source = atk,
                            Target = enemyHero,
                            SourceEntityId = atk.EntityId,
                            TargetEntityId = enemyHero.EntityId
                        }, 1000 + atk.Atk * 10)); // 优先高攻打脸
                    }
                }
            }

            // ─── 2. 手牌出牌：只考虑有助于斩杀的卡牌 ───
            foreach (var card in board.Hand)
            {
                if (card.Cost > board.Mana) continue;

                if (card.Type == Card.CType.SPELL)
                {
                    // 伤害法术打脸
                    if (!enemyHero.IsImmune)
                    {
                        actions.Add((new GameAction
                        {
                            Type = ActionType.PlayCard,
                            Source = card,
                            Target = enemyHero,
                            SourceEntityId = card.EntityId,
                            TargetEntityId = enemyHero.EntityId
                        }, 900 + EstimateSpellDamage(card, board)));
                    }

                    // 法术杀嘲讽
                    if (hasTaunt)
                    {
                        foreach (var taunt in tauntMinions)
                        {
                            actions.Add((new GameAction
                            {
                                Type = ActionType.PlayCard,
                                Source = card,
                                Target = taunt,
                                SourceEntityId = card.EntityId,
                                TargetEntityId = taunt.EntityId
                            }, 500 + EstimateSpellDamage(card, board)));
                        }
                    }

                    // 无目标法术（AOE 等）
                    actions.Add((new GameAction
                    {
                        Type = ActionType.PlayCard,
                        Source = card,
                        Target = null,
                        SourceEntityId = card.EntityId,
                        TargetEntityId = 0
                    }, 400));
                }
                else if (card.Type == Card.CType.MINION)
                {
                    if (board.FriendMinions.Count >= 7) continue;

                    // 冲锋/突袭随从：优先考虑
                    if (card.HasCharge || card.HasRush)
                    {
                        // 无目标出牌
                        actions.Add((new GameAction
                        {
                            Type = ActionType.PlayCard,
                            Source = card,
                            Target = null,
                            SourceEntityId = card.EntityId,
                            TargetEntityId = 0
                        }, card.HasCharge ? 850 + card.Atk * 10 : 700 + card.Atk * 5));
                    }

                    // 战吼伤害随从（打脸或解嘲讽）
                    bool hasBattlecryDmg = _db.Has(card.CardId, EffectTrigger.Battlecry);
                    if (hasBattlecryDmg)
                    {
                        if (!enemyHero.IsImmune)
                        {
                            actions.Add((new GameAction
                            {
                                Type = ActionType.PlayCard,
                                Source = card,
                                Target = enemyHero,
                                SourceEntityId = card.EntityId,
                                TargetEntityId = enemyHero.EntityId
                            }, 800));
                        }
                        if (hasTaunt)
                        {
                            foreach (var taunt in tauntMinions)
                            {
                                actions.Add((new GameAction
                                {
                                    Type = ActionType.PlayCard,
                                    Source = card,
                                    Target = taunt,
                                    SourceEntityId = card.EntityId,
                                    TargetEntityId = taunt.EntityId
                                }, 600));
                            }
                        }
                        // 无目标战吼
                        actions.Add((new GameAction
                        {
                            Type = ActionType.PlayCard,
                            Source = card,
                            Target = null,
                            SourceEntityId = card.EntityId,
                            TargetEntityId = 0
                        }, 500));
                    }
                }
                else if (card.Type == Card.CType.WEAPON)
                {
                    // 装武器 = 英雄获得攻击力，本回合准备好就能打
                    actions.Add((new GameAction
                    {
                        Type = ActionType.PlayCard,
                        Source = card,
                        Target = null,
                        SourceEntityId = card.EntityId,
                        TargetEntityId = 0
                    }, 750 + card.Atk * 10));
                }
            }

            // ─── 3. 英雄技能 ───
            if (!board.HeroPowerUsed && board.HeroPower != null && board.HeroPower.Cost <= board.Mana)
            {
                int hpPriority = GetHeroPowerLethalPriority(board);
                if (hpPriority > 0)
                {
                    // 有指向的英雄技能（法师、牧师伤害型）
                    bool isDirectedDamageHP = board.FriendClass == Card.CClass.MAGE
                        || (board.FriendClass == Card.CClass.PRIEST && IsPriestDamageHeroPower(board));
                    if (isDirectedDamageHP && !enemyHero.IsImmune)
                    {
                        actions.Add((new GameAction
                        {
                            Type = ActionType.HeroPower,
                            Source = board.HeroPower,
                            SourceEntityId = board.HeroPower.EntityId,
                            Target = enemyHero,
                            TargetEntityId = enemyHero.EntityId
                        }, hpPriority));
                    }
                    else if (!isDirectedDamageHP)
                    {
                        // 无指向英雄技能（猎人稳固射击、盗贼匹首等）
                        actions.Add((new GameAction
                        {
                            Type = ActionType.HeroPower,
                            Source = board.HeroPower,
                            SourceEntityId = board.HeroPower.EntityId,
                            TargetEntityId = 0
                        }, hpPriority));
                    }

                    // 指向性技能打嘲讽（法师 + 牧师伤害型）
                    if ((board.FriendClass == Card.CClass.MAGE
                        || (board.FriendClass == Card.CClass.PRIEST && IsPriestDamageHeroPower(board)))
                        && hasTaunt)
                    {
                        foreach (var taunt in tauntMinions)
                        {
                            actions.Add((new GameAction
                            {
                                Type = ActionType.HeroPower,
                                Source = board.HeroPower,
                                SourceEntityId = board.HeroPower.EntityId,
                                Target = taunt,
                                TargetEntityId = taunt.EntityId
                            }, 300));
                        }
                    }
                }
            }

            // 按优先级降序排列
            actions.Sort((a, b) => b.priority.CompareTo(a.priority));
            return actions.Select(a => a.action).ToList();
        }

        // ────────────────────────────────────────────────
        //  辅助方法
        // ────────────────────────────────────────────────

        private bool TryApply(SimBoard board, GameAction action)
        {
            try
            {
                switch (action.Type)
                {
                    case ActionType.Attack:
                    {
                        var src = FindEntity(board, action.SourceEntityId, true);
                        var tgt = FindEntity(board, action.TargetEntityId, false);
                        if (src == null || tgt == null || !src.CanAttack) return false;
                        _sim.Attack(board, src, tgt);
                        return true;
                    }
                    case ActionType.PlayCard:
                    {
                        var card = board.Hand.FirstOrDefault(c => c.EntityId == action.SourceEntityId);
                        if (card == null || card.Cost > board.Mana) return false;
                        var tgt = action.TargetEntityId > 0
                            ? FindEntity(board, action.TargetEntityId, null)
                            : null;
                        _sim.PlayCard(board, card, tgt);
                        return true;
                    }
                    case ActionType.HeroPower:
                    {
                        if (board.HeroPowerUsed || board.HeroPower == null) return false;
                        if (board.HeroPower.Cost > board.Mana) return false;
                        var tgt = action.TargetEntityId > 0
                            ? FindEntity(board, action.TargetEntityId, null)
                            : null;
                        _sim.UseHeroPower(board, tgt);
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private SimEntity FindEntity(SimBoard board, int entityId, bool? isFriend)
        {
            if (board.FriendHero?.EntityId == entityId) return board.FriendHero;
            if (board.EnemyHero?.EntityId == entityId) return board.EnemyHero;

            if (isFriend != false)
            {
                var f = board.FriendMinions.FirstOrDefault(m => m.EntityId == entityId);
                if (f != null) return f;
            }
            if (isFriend != true)
            {
                var e = board.EnemyMinions.FirstOrDefault(m => m.EntityId == entityId);
                if (e != null) return e;
            }
            return null;
        }

        private static bool IsEnemyDead(SimBoard board)
        {
            return board.EnemyHero != null && board.EnemyHero.Health + board.EnemyHero.Armor <= 0;
        }

        /// <summary>
        /// 快速估算最大可能伤害（上界），包含：
        /// - 场上可攻击随从的攻击力（含风怒）
        /// - 英雄攻击力
        /// - 手牌冲锋随从的攻击力
        /// - 手牌法术的估算伤害
        /// - 英雄技能伤害
        /// 不考虑嘲讽（上界计算），不考虑法力限制以外的约束
        /// </summary>
        private int EstimateMaxDamage(SimBoard board)
        {
            int dmg = 0;
            int manaPool = board.Mana;

            // 场上可攻击的随从
            foreach (var m in board.FriendMinions)
            {
                if (!m.CanAttack || m.Type == Card.CType.LOCATION) continue;
                int attacks = m.IsWindfury ? (2 - m.CountAttack) : (1 - m.CountAttack);
                if (attacks > 0) dmg += m.Atk * attacks;
            }

            // 英雄攻击
            if (board.FriendHero != null && board.FriendHero.CanAttack)
            {
                int heroAtk = board.FriendHero.Atk;
                if (board.FriendWeapon != null) heroAtk = Math.Max(heroAtk, board.FriendWeapon.Atk);
                dmg += heroAtk;
            }

            // 法术强度加成
            int sp = 0;
            foreach (var m in board.FriendMinions)
                sp += m.SpellPower;

            // 手牌贡献
            var handByMana = board.Hand.OrderBy(c => c.Cost).ToList();
            int remainingMana = manaPool;
            foreach (var card in handByMana)
            {
                if (card.Cost > remainingMana) continue;

                if (card.Type == Card.CType.SPELL)
                {
                    int spellDmg = EstimateSpellDamage(card, board);
                    dmg += spellDmg;
                    remainingMana -= card.Cost;
                }
                else if (card.Type == Card.CType.MINION)
                {
                    if (card.HasCharge)
                    {
                        dmg += card.Atk * (card.IsWindfury ? 2 : 1);
                        remainingMana -= card.Cost;
                    }
                    else if (_db.Has(card.CardId, EffectTrigger.Battlecry))
                    {
                        // 战吼伤害估算
                        dmg += Math.Max(1, card.Cost / 2);
                        remainingMana -= card.Cost;
                    }
                }
                else if (card.Type == Card.CType.WEAPON)
                {
                    // 装武器后英雄可以打
                    if (board.FriendHero != null)
                    {
                        dmg += card.Atk;
                        remainingMana -= card.Cost;
                    }
                }
            }

            // 英雄技能
            if (!board.HeroPowerUsed && board.HeroPower != null && board.HeroPower.Cost <= remainingMana)
            {
                dmg += GetHeroPowerDamage(board);
            }

            return dmg;
        }

        /// <summary>估算法术伤害</summary>
        private int EstimateSpellDamage(SimEntity card, SimBoard board)
        {
            int sp = 0;
            foreach (var m in board.FriendMinions)
                sp += m.SpellPower;

            // 如果已注册效果，尝试模拟
            if (_db.Has(card.CardId, EffectTrigger.Spell))
            {
                // 通过模拟测量实际伤害
                var testBoard = board.Clone();
                int hpBefore = 30; // 用满血英雄测量
                testBoard.EnemyHero = new SimEntity
                {
                    EntityId = -999,
                    Health = hpBefore,
                    MaxHealth = 30,
                    Armor = 0,
                    Atk = 0,
                    IsFriend = false
                };
                var cardClone = card.Clone();
                testBoard.Hand.Add(cardClone);
                testBoard.Mana = Math.Max(testBoard.Mana, card.Cost + 1);
                try
                {
                    _sim.PlayCard(testBoard, cardClone, testBoard.EnemyHero);
                    int dealt = hpBefore - (testBoard.EnemyHero.Health + testBoard.EnemyHero.Armor);
                    if (dealt > 0) return dealt;
                }
                catch { }
            }

            // 未注册的法术兜底：max(1, cost-1)
            return Math.Max(1, card.Cost - 1) + sp;
        }

        /// <summary>获取有效生命值（血量+护甲，考虑圣盾）</summary>
        private static int GetEffectiveHp(SimEntity hero, SimBoard board)
        {
            if (hero == null) return 9999;
            int ehp = hero.Health + hero.Armor;
            if (hero.IsImmune) ehp = 9999;
            return ehp;
        }

        /// <summary>英雄技能的斩杀参与度（接受 SimBoard 以判断牧师技能类型）</summary>
        private int GetHeroPowerLethalPriority(SimBoard board)
        {
            switch (board.FriendClass)
            {
                case Card.CClass.HUNTER: return 600;  // 稳固射击 2 点打脸
                case Card.CClass.MAGE:   return 550;  // 火焰冲击 1 点指向
                case Card.CClass.PRIEST:
                    // 伤害型英雄技能（心灵尖刺）：2点指向伤害
                    if (IsPriestDamageHeroPower(board)) return 570;
                    return 0;
                case Card.CClass.DRUID:  return 500;  // +1攻+1甲，英雄可以打
                case Card.CClass.ROGUE:  return 450;  // 装1/2匹首
                case Card.CClass.DEMONHUNTER: return 500; // +1攻
                default: return 0;
            }
        }

        /// <summary>英雄技能的直接伤害</summary>
        private int GetHeroPowerDamage(SimBoard board)
        {
            switch (board.FriendClass)
            {
                case Card.CClass.HUNTER: return 2;
                case Card.CClass.MAGE:   return 1;
                case Card.CClass.PRIEST:
                    if (IsPriestDamageHeroPower(board)) return 2;
                    return 0;
                case Card.CClass.DRUID:  return 1;
                case Card.CClass.ROGUE:  return 1;
                case Card.CClass.DEMONHUNTER: return 1;
                default: return 0;
            }
        }

        /// <summary>判断牧师当前英雄技能是否为伤害型（心灵尖刺系列）</summary>
        private static bool IsPriestDamageHeroPower(SimBoard board)
        {
            if (board.HeroPower == null) return false;
            var name = board.HeroPower.CardId.ToString();
            return name == "EX1_625"   // Mind Spike
                || name == "EX1_625t"  // Mind Shatter
                || name.Contains("MindSpike")
                || name.Contains("SCH_270")
                || name.Contains("YOP_028")
                ;
        }

        /// <summary>攻击嘲讽的优先级：能一刀杀的最高</summary>
        private static int PriorityAttackTaunt(SimEntity atk, SimEntity taunt)
        {
            // 能一刀杀嘲讽、且攻击者不会死，最高优先
            bool canKill = atk.Atk >= taunt.Health ||
                           (atk.Atk >= taunt.Health - (taunt.IsDivineShield ? 0 : 0) && !taunt.IsDivineShield) ||
                           atk.HasPoison;
            bool survives = taunt.Atk < atk.Health || atk.IsDivineShield || atk.IsImmune;

            if (canKill && survives) return 700;
            if (canKill) return 650;
            return 300 + atk.Atk;
        }

        /// <summary>简单的棋盘状态哈希，用于去重</summary>
        private static long BoardHash(SimBoard board)
        {
            unchecked
            {
                long h = 17;
                h = h * 31 + board.Mana;
                h = h * 31 + (board.HeroPowerUsed ? 1 : 0);

                if (board.EnemyHero != null)
                {
                    h = h * 31 + board.EnemyHero.Health;
                    h = h * 31 + board.EnemyHero.Armor;
                }

                if (board.FriendHero != null)
                {
                    h = h * 31 + board.FriendHero.Atk;
                    h = h * 31 + (board.FriendHero.CanAttack ? 1 : 0);
                }

                foreach (var m in board.FriendMinions)
                {
                    h = h * 31 + m.EntityId;
                    h = h * 31 + (m.CanAttack ? 1 : 0);
                    h = h * 31 + m.Atk;
                    h = h * 31 + m.Health;
                }

                foreach (var m in board.EnemyMinions)
                {
                    h = h * 31 + m.EntityId;
                    h = h * 31 + (m.IsTaunt ? 1 : 0);
                    h = h * 31 + m.Health;
                    h = h * 31 + (m.IsDivineShield ? 1 : 0);
                }

                h = h * 31 + board.Hand.Count;
                foreach (var c in board.Hand)
                {
                    h = h * 31 + c.EntityId;
                }

                return h;
            }
        }
    }
}
