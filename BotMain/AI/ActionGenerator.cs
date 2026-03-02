using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Plugins.API;

namespace BotMain.AI
{
    public enum ActionType { PlayCard, TradeCard, Attack, HeroPower, EndTurn, UseLocation }

    /// <summary>战吼目标类型</summary>
    public enum BattlecryTargetType
    {
        None,              // 无目标或自动生效
        AnyCharacter,      // 任意角色（随从+英雄）
        AnyMinion,         // 任意随从
        EnemyOnly,         // 仅敌方（随从+英雄）
        EnemyMinion,       // 仅敌方随从
        FriendlyOnly,      // 仅友方（随从+英雄）
        FriendlyMinion,    // 仅友方随从
    }

    public class GameAction
    {
        public ActionType Type;
        public SimEntity Source;
        public SimEntity Target;
        public int SourceEntityId;
        public int TargetEntityId;

        public string ToActionString()
        {
            switch (Type)
            {
                case ActionType.PlayCard:
                    return $"PLAY|{SourceEntityId}|{TargetEntityId}|0";
                case ActionType.TradeCard:
                    return $"TRADE|{SourceEntityId}";
                case ActionType.Attack:
                    return $"ATTACK|{SourceEntityId}|{TargetEntityId}";
                case ActionType.HeroPower:
                    return TargetEntityId > 0
                        ? $"HERO_POWER|{SourceEntityId}|{TargetEntityId}"
                        : $"HERO_POWER|{SourceEntityId}|0";
                case ActionType.UseLocation:
                    return TargetEntityId > 0
                        ? $"USE_LOCATION|{SourceEntityId}|{TargetEntityId}"
                        : $"USE_LOCATION|{SourceEntityId}";
                case ActionType.EndTurn:
                    return "END_TURN";
                default:
                    return "END_TURN";
            }
        }
    }

    public class ActionGenerator
    {
        private CardEffectDB _effectDb;

        /// <summary>
        /// 注入 CardEffectDB，用于判断随从是否有需要目标的战吼
        /// </summary>
        public void SetEffectDB(CardEffectDB db) => _effectDb = db;

        public List<GameAction> Generate(SimBoard board)
        {
            var actions = new List<GameAction>();

            GenerateAttacks(board, actions);
            GenerateTrades(board, actions);
            GeneratePlayCards(board, actions);
            GenerateHeroPower(board, actions);
            GenerateLocationActivations(board, actions);

            // 始终可以结束回合
            actions.Add(new GameAction { Type = ActionType.EndTurn });
            return actions;
        }

        private void GenerateAttacks(SimBoard board, List<GameAction> actions)
        {
            var attackers = board.FriendMinions.Where(m => m.CanAttack).ToList();
            if (board.FriendHero != null && board.FriendHero.CanAttack)
                attackers.Add(board.FriendHero);

            var hasTaunt = board.EnemyMinions.Any(m => m.IsTaunt && !m.IsStealth);
            var targets = hasTaunt
                ? board.EnemyMinions.Where(m => m.IsTaunt && !m.IsStealth).ToList()
                : board.EnemyMinions.Where(m => !m.IsStealth).ToList();

            // 无嘲讽时可以打脸
            if (!hasTaunt && board.EnemyHero != null && !board.EnemyHero.IsImmune)
                targets.Add(board.EnemyHero);

            foreach (var atk in attackers)
            {
                // Rush随从首回合不能打脸
                var validTargets = targets;
                if (atk.HasRush && atk.CountAttack == 0 && atk.IsTired == false)
                    validTargets = validTargets.Where(t => t != board.EnemyHero).ToList();

                foreach (var tgt in validTargets)
                    actions.Add(new GameAction
                    {
                        Type = ActionType.Attack,
                        Source = atk,
                        Target = tgt,
                        SourceEntityId = atk.EntityId,
                        TargetEntityId = tgt.EntityId
                    });
            }
        }

        private void GeneratePlayCards(SimBoard board, List<GameAction> actions)
        {
            foreach (var card in board.Hand)
            {
                if (card.Cost > board.Mana) continue;

                if (card.Type == Card.CType.MINION)
                {
                    if (board.FriendMinions.Count >= 7) continue;
                    GenerateMinionPlay(board, card, actions);
                }
                else if (card.Type == Card.CType.SPELL)
                {
                    GenerateSpellPlay(board, card, actions);
                }
                else if (card.Type == Card.CType.WEAPON)
                {
                    actions.Add(MakePlayAction(card, null));
                }
                else if (card.Type == Card.CType.HERO)
                {
                    actions.Add(MakePlayAction(card, null));
                }
                else if (card.Type == Card.CType.LOCATION)
                {
                    if (board.FriendMinions.Count >= 7) continue;
                    actions.Add(MakePlayAction(card, null));
                }
                else
                {
                    // 未知类型：无目标 + 打脸
                    actions.Add(MakePlayAction(card, null));
                    if (board.EnemyHero != null && !board.EnemyHero.IsImmune)
                        actions.Add(MakePlayAction(card, board.EnemyHero));
                }
            }
        }

        private void GenerateTrades(SimBoard board, List<GameAction> actions)
        {
            if (board.Mana < 1) return;

            foreach (var card in board.Hand)
            {
                if (!card.IsTradeable) continue;
                actions.Add(MakeTradeAction(card));
            }
        }

        /// <summary>
        /// 生成随从出牌动作。
        /// 如果随从有需要目标的战吼，为每个合法目标生成一个动作。
        /// 否则只生成无目标动作。
        /// </summary>
        private void GenerateMinionPlay(SimBoard board, SimEntity card, List<GameAction> actions)
        {
            var targetType = GetBattlecryTargetType(card.CardId);

            if (targetType == BattlecryTargetType.None)
            {
                // 无目标战吼（自动生效）或无战吼
                actions.Add(MakePlayAction(card, null));
                return;
            }

            // 有目标战吼：为每个合法目标生成一个动作
            var targets = GetBattlecryTargets(board, targetType, card);

            if (targets.Count == 0)
            {
                // 没有合法目标，仍然可以打出（战吼不触发或无效）
                actions.Add(MakePlayAction(card, null));
                return;
            }

            foreach (var tgt in targets)
            {
                actions.Add(MakePlayAction(card, tgt));
            }
        }

        /// <summary>
        /// 生成法术出牌动作。
        /// 如果法术在 CardEffectDB 有注册目标类型，使用智能过滤；
        /// 否则兜底生成所有目标。
        /// </summary>
        private void GenerateSpellPlay(SimBoard board, SimEntity card, List<GameAction> actions)
        {
            var targetType = GetSpellTargetType(card.CardId);

            if (targetType == BattlecryTargetType.None)
            {
                // AOE、抽牌、护甲等无需目标的法术
                // 但如果没有注册效果，仍然生成所有目标以兜底
                if (_effectDb != null && _effectDb.Has(card.CardId, EffectTrigger.Spell))
                {
                    actions.Add(MakePlayAction(card, null));
                    return;
                }

                // 未注册法术：生成所有目标（兜底兼容）
                var allTargets = GetSpellTargets(board);
                if (allTargets.Count == 0)
                {
                    actions.Add(MakePlayAction(card, null));
                }
                else
                {
                    foreach (var tgt in allTargets)
                        actions.Add(MakePlayAction(card, tgt));
                }
                return;
            }

            // 有注册目标类型：智能过滤
            var targets = GetFilteredSpellTargets(board, targetType);
            if (targets.Count == 0)
            {
                actions.Add(MakePlayAction(card, null));
            }
            else
            {
                foreach (var tgt in targets)
                    actions.Add(MakePlayAction(card, tgt));
            }
        }

        /// <summary>
        /// 查询法术的目标类型
        /// </summary>
        private BattlecryTargetType GetSpellTargetType(Card.Cards cardId)
        {
            if (_effectDb == null) return BattlecryTargetType.None;
            if (_effectDb.TryGetTargetType(cardId, EffectTrigger.Spell, out var tt))
                return tt;
            return BattlecryTargetType.None;
        }

        /// <summary>
        /// 根据目标类型获取法术的合法目标
        /// </summary>
        private List<SimEntity> GetFilteredSpellTargets(SimBoard board, BattlecryTargetType type)
        {
            var targets = new List<SimEntity>();

            switch (type)
            {
                case BattlecryTargetType.AnyCharacter:
                    targets.AddRange(board.FriendMinions);
                    targets.AddRange(board.EnemyMinions.Where(m => !m.IsStealth));
                    if (board.FriendHero != null) targets.Add(board.FriendHero);
                    if (board.EnemyHero != null && !board.EnemyHero.IsImmune) targets.Add(board.EnemyHero);
                    break;

                case BattlecryTargetType.AnyMinion:
                    targets.AddRange(board.FriendMinions);
                    targets.AddRange(board.EnemyMinions.Where(m => !m.IsStealth));
                    break;

                case BattlecryTargetType.EnemyOnly:
                    targets.AddRange(board.EnemyMinions.Where(m => !m.IsStealth));
                    if (board.EnemyHero != null && !board.EnemyHero.IsImmune) targets.Add(board.EnemyHero);
                    break;

                case BattlecryTargetType.EnemyMinion:
                    targets.AddRange(board.EnemyMinions.Where(m => !m.IsStealth));
                    break;

                case BattlecryTargetType.FriendlyOnly:
                    targets.AddRange(board.FriendMinions);
                    if (board.FriendHero != null) targets.Add(board.FriendHero);
                    break;

                case BattlecryTargetType.FriendlyMinion:
                    targets.AddRange(board.FriendMinions);
                    break;
            }

            return targets;
        }

        /// <summary>
        /// 根据效果类型判断战吼需要什么类型的目标
        /// </summary>
        private BattlecryTargetType GetBattlecryTargetType(Card.Cards cardId)
        {
            if (_effectDb == null) return BattlecryTargetType.None;
            if (!_effectDb.Has(cardId, EffectTrigger.Battlecry)) return BattlecryTargetType.None;

            // 查询效果的目标分类（通过 CardEffectDB 的元数据）
            if (_effectDb.TryGetTargetType(cardId, EffectTrigger.Battlecry, out var tt))
                return tt;

            // 兜底：如果有战吼但没有目标类型元数据，当做无目标（自动生效）
            return BattlecryTargetType.None;
        }

        /// <summary>
        /// 根据目标类型获取合法目标列表
        /// </summary>
        private List<SimEntity> GetBattlecryTargets(SimBoard board, BattlecryTargetType type, SimEntity self)
        {
            var targets = new List<SimEntity>();

            switch (type)
            {
                case BattlecryTargetType.AnyCharacter:
                    targets.AddRange(board.FriendMinions.Where(m => m.EntityId != self.EntityId));
                    targets.AddRange(board.EnemyMinions.Where(m => !m.IsStealth));
                    if (board.FriendHero != null) targets.Add(board.FriendHero);
                    if (board.EnemyHero != null && !board.EnemyHero.IsImmune) targets.Add(board.EnemyHero);
                    break;

                case BattlecryTargetType.AnyMinion:
                    targets.AddRange(board.FriendMinions.Where(m => m.EntityId != self.EntityId));
                    targets.AddRange(board.EnemyMinions.Where(m => !m.IsStealth));
                    break;

                case BattlecryTargetType.EnemyOnly:
                    targets.AddRange(board.EnemyMinions.Where(m => !m.IsStealth));
                    if (board.EnemyHero != null && !board.EnemyHero.IsImmune) targets.Add(board.EnemyHero);
                    break;

                case BattlecryTargetType.EnemyMinion:
                    targets.AddRange(board.EnemyMinions.Where(m => !m.IsStealth));
                    break;

                case BattlecryTargetType.FriendlyOnly:
                    targets.AddRange(board.FriendMinions.Where(m => m.EntityId != self.EntityId));
                    if (board.FriendHero != null) targets.Add(board.FriendHero);
                    break;

                case BattlecryTargetType.FriendlyMinion:
                    targets.AddRange(board.FriendMinions.Where(m => m.EntityId != self.EntityId));
                    break;
            }

            return targets;
        }

        private void GenerateHeroPower(SimBoard board, List<GameAction> actions)
        {
            if (board.HeroPowerUsed || board.HeroPower == null) return;
            if (board.HeroPower.Cost > board.Mana) return;

            var hpId = board.HeroPower.EntityId;
            var hpSource = board.HeroPower;

            switch (board.FriendClass)
            {
                case Card.CClass.MAGE:
                    // 火焰冲击：只打敌方
                    foreach (var tgt in GetEnemyTargets(board))
                    {
                        actions.Add(new GameAction
                        {
                            Type = ActionType.HeroPower,
                            Source = hpSource,
                            SourceEntityId = hpId,
                            Target = tgt,
                            TargetEntityId = tgt.EntityId
                        });
                    }
                    break;
                case Card.CClass.PRIEST:
                    // 根据英雄技能类型决定目标
                    var priestTargets = IsPriestDamageHeroPower(board)
                        ? GetEnemyTargets(board)      // 心灵尖刺：只打敌方
                        : GetAllTargets(board);        // 次级治疗术：可以治任意目标
                    foreach (var tgt in priestTargets)
                    {
                        actions.Add(new GameAction
                        {
                            Type = ActionType.HeroPower,
                            Source = hpSource,
                            SourceEntityId = hpId,
                            Target = tgt,
                            TargetEntityId = tgt.EntityId
                        });
                    }
                    break;
                default:
                    actions.Add(new GameAction
                    {
                        Type = ActionType.HeroPower,
                        Source = hpSource,
                        SourceEntityId = hpId,
                        TargetEntityId = 0
                    });
                    break;
            }
        }

        private static GameAction MakePlayAction(SimEntity card, SimEntity target)
        {
            return new GameAction
            {
                Type = ActionType.PlayCard,
                Source = card,
                Target = target,
                SourceEntityId = card.EntityId,
                TargetEntityId = target?.EntityId ?? 0
            };
        }

        private static GameAction MakeTradeAction(SimEntity card)
        {
            return new GameAction
            {
                Type = ActionType.TradeCard,
                Source = card,
                SourceEntityId = card.EntityId
            };
        }

        private List<SimEntity> GetSpellTargets(SimBoard board)
        {
            var targets = new List<SimEntity>();
            targets.AddRange(board.FriendMinions);
            targets.AddRange(board.EnemyMinions);
            if (board.FriendHero != null) targets.Add(board.FriendHero);
            if (board.EnemyHero != null) targets.Add(board.EnemyHero);
            return targets;
        }

        private List<SimEntity> GetAllTargets(SimBoard board)
        {
            return GetSpellTargets(board);
        }

        private List<SimEntity> GetEnemyTargets(SimBoard board)
        {
            var targets = new List<SimEntity>();
            targets.AddRange(board.EnemyMinions.Where(m => !m.IsStealth));
            if (board.EnemyHero != null && !board.EnemyHero.IsImmune)
                targets.Add(board.EnemyHero);
            return targets;
        }

        /// <summary>
        /// 判断牧师英雄技能是否为伤害型（心灵尖刺系列），而非治疗型（次级治疗术）
        /// </summary>
        private static bool IsPriestDamageHeroPower(SimBoard board)
        {
            if (board.HeroPower == null) return false;
            var name = board.HeroPower.CardId.ToString();
            return name == "EX1_625"   // Mind Spike（心灵尖刺）
                || name == "EX1_625t"  // Mind Shatter（心灵破碎）
                || name.Contains("MindSpike")
                || name.Contains("SCH_270")
                || name.Contains("YOP_028")
                ;
        }


        private void GenerateLocationActivations(SimBoard board, List<GameAction> actions)
        {
            foreach (var loc in board.FriendMinions)
            {
                if (loc.Type != Card.CType.LOCATION) continue;
                if (loc.IsTired) continue;

                // 查询地标激活效果的目标类型
                BattlecryTargetType targetType = BattlecryTargetType.None;
                if (_effectDb != null)
                {
                    // 先查 LocationActivation 层，再查 Spell 层
                    if (!_effectDb.TryGetTargetType(loc.CardId, EffectTrigger.LocationActivation, out targetType))
                        _effectDb.TryGetTargetType(loc.CardId, EffectTrigger.Spell, out targetType);
                }

                if (targetType == BattlecryTargetType.None)
                {
                    // 无目标地标（AOE 或纯增益）
                    actions.Add(new GameAction
                    {
                        Type = ActionType.UseLocation,
                        Source = loc,
                        SourceEntityId = loc.EntityId
                    });
                }
                else
                {
                    // 有目标地标：为每个合法目标生成一个动作
                    var targets = GetFilteredSpellTargets(board, targetType);
                    if (targets.Count == 0)
                    {
                        actions.Add(new GameAction
                        {
                            Type = ActionType.UseLocation,
                            Source = loc,
                            SourceEntityId = loc.EntityId
                        });
                    }
                    else
                    {
                        foreach (var tgt in targets)
                            actions.Add(new GameAction
                            {
                                Type = ActionType.UseLocation,
                                Source = loc,
                                SourceEntityId = loc.EntityId,
                                Target = tgt,
                                TargetEntityId = tgt.EntityId
                            });
                    }
                }
            }
        }
    }
}

