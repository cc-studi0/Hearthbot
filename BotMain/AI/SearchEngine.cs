using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using SmartBot.Plugins.API;
using SmartBotProfiles;

namespace BotMain.AI
{
    /// <summary>
    /// Beam 中的一条候选路径。
    /// </summary>
    internal sealed class BeamCandidate
    {
        public SimBoard Board;
        public List<GameAction> Actions;
        public float Score;
        public bool IsComplete; // 已到达 END_TURN

        public BeamCandidate Clone()
        {
            return new BeamCandidate
            {
                Board = Board.Clone(),
                Actions = new List<GameAction>(Actions),
                Score = Score,
                IsComplete = IsComplete,
            };
        }
    }

    public class SearchEngine
    {
        private readonly BoardSimulator _sim;
        private readonly BoardEvaluator _eval;
        private readonly ActionGenerator _gen;
        private readonly ProfileActionScorer _profileScorer = new ProfileActionScorer();
        private readonly TradeEvaluator _tradeEval = new TradeEvaluator();

        /// <summary>Beam 宽度（同时追踪的候选序列数）</summary>
        public int BeamWidth { get; set; } = 6;

        /// <summary>最大搜索深度（动作步数）</summary>
        public int MaxDepth { get; set; } = 25;

        /// <summary>搜索超时毫秒数</summary>
        public int TimeoutMs { get; set; } = 5000;

        public event Action<string> OnLog;

        public SearchEngine(BoardSimulator sim, BoardEvaluator eval, ActionGenerator gen)
        {
            _sim = sim;
            _eval = eval;
            _gen = gen;
        }

        public List<GameAction> FindBestSequence(SimBoard board, ProfileParameters param)
        {
            var sw = Stopwatch.StartNew();
            var hasProfileRules = HasProfileRules(param);

            // ── 初始化 Beam ──
            var initialScore = _eval.Evaluate(board, param);
            var beams = new List<BeamCandidate>
            {
                new BeamCandidate
                {
                    Board = board.Clone(),
                    Actions = new List<GameAction>(),
                    Score = initialScore,
                    IsComplete = false,
                }
            };

            var completedBeams = new List<BeamCandidate>();
            int totalExpansions = 0;

            // ── 主搜索循环 ──
            for (int depth = 0; depth < MaxDepth; depth++)
            {
                if (sw.ElapsedMilliseconds > TimeoutMs) break;

                var activeBeams = beams.Where(b => !b.IsComplete).ToList();
                if (activeBeams.Count == 0) break;

                var nextCandidates = new List<BeamCandidate>();

                foreach (var beam in activeBeams)
                {
                    if (sw.ElapsedMilliseconds > TimeoutMs) break;

                    // 生成所有可能的动作
                    var actions = _gen.Generate(beam.Board);
                    var nonEndActions = new List<(GameAction Action, ProfileActionScore ProfileScore)>();
                    int blockedCount = 0;
                    string blockedSample = null;

                    foreach (var action in actions)
                    {
                        if (action.Type == ActionType.EndTurn) continue;

                        var profileScore = _profileScorer.Evaluate(beam.Board, action, param);
                        if (profileScore.HardBlocked)
                        {
                            blockedCount++;
                            blockedSample ??= $"{action.ToActionString()} ({profileScore.Detail})";
                            continue;
                        }
                        nonEndActions.Add((action, profileScore));
                    }

                    // 首轮日志
                    if (depth == 0 && beam == activeBeams[0])
                    {
                        var totalNonEnd = actions.Count(a => a.Type != ActionType.EndTurn);
                        OnLog?.Invoke($"[AI] beam search depth0: generated={totalNonEnd}, candidates={nonEndActions.Count}, blocked={blockedCount}, mana={beam.Board.Mana}, hand={beam.Board.Hand.Count}, friendMinions={beam.Board.FriendMinions.Count}, beamWidth={BeamWidth}, hasProfile={hasProfileRules}");
                        if (hasProfileRules && blockedCount > 0)
                            OnLog?.Invoke($"[AI] profile blocked {blockedCount} action(s), sample={blockedSample}");
                    }

                    // 选项1：END_TURN（当前序列完成）
                    var endBeam = beam.Clone();
                    endBeam.Actions.Add(new GameAction { Type = ActionType.EndTurn });
                    endBeam.IsComplete = true;
                    // 重新评估当前棋面分数，确保与其他分支一致
                    endBeam.Score = _eval.Evaluate(endBeam.Board, param);
                    completedBeams.Add(endBeam);

                    // 预排序候选动作：Profile 高奖励优先
                    nonEndActions.Sort((a, b) => b.ProfileScore.Bonus.CompareTo(a.ProfileScore.Bonus));

                    // 选项2：执行每个非 END_TURN 动作
                    foreach (var (action, profileScore) in nonEndActions)
                    {
                        totalExpansions++;
                        var clone = beam.Board.Clone();
                        bool ok = TryApplyAction(clone, action);
                        if (!ok) continue;

                        var boardScore = _eval.Evaluate(clone, param);

                        // 如果模拟后敌方英雄已死，直接作为斩杀路径完成
                        if (boardScore >= 100000f)
                        {
                            var lethalCandidate = new BeamCandidate
                            {
                                Board = clone,
                                Actions = new List<GameAction>(beam.Actions) { action, new GameAction { Type = ActionType.EndTurn } },
                                Score = boardScore,
                                IsComplete = true,
                            };
                            completedBeams.Add(lethalCandidate);
                            continue;
                        }

                        var tradeBonus = _tradeEval.EvaluateAttack(beam.Board, action);
                        var finalScore = boardScore + profileScore.Bonus + tradeBonus;

                        nextCandidates.Add(new BeamCandidate
                        {
                            Board = clone,
                            Actions = new List<GameAction>(beam.Actions) { action },
                            Score = finalScore,
                            IsComplete = false,
                        });
                    }

                    // 如果没有任何可做的动作，这条 beam 只能 END_TURN
                    if (nonEndActions.Count == 0 && !beam.IsComplete)
                    {
                        // 已经加了 endBeam，不需要额外处理
                    }
                }

                if (nextCandidates.Count == 0) break;

                // ── Beam 剪枝：保留 top-N ──
                nextCandidates.Sort((a, b) => b.Score.CompareTo(a.Score));
                beams = nextCandidates.Take(BeamWidth).ToList();

                // 将低分的已完成的也加入 completed（但不占 beam 位置）
                // 已完成的在上面已经加入 completedBeams 了
            }

            // ── 所有仍在活跃的 beam 也作为 END_TURN 完成 ──
            foreach (var beam in beams.Where(b => !b.IsComplete))
            {
                beam.Actions.Add(new GameAction { Type = ActionType.EndTurn });
                beam.IsComplete = true;
                completedBeams.Add(beam);
            }

            // ── 选择最佳完成序列 ──
            BeamCandidate bestResult = null;
            foreach (var c in completedBeams)
            {
                if (bestResult == null || c.Score > bestResult.Score)
                    bestResult = c;
            }

            if (bestResult == null || bestResult.Actions.Count == 0)
            {
                OnLog?.Invoke("[AI] beam search: no result, fallback END_TURN");
                return new List<GameAction> { new GameAction { Type = ActionType.EndTurn } };
            }

            // 确保以 END_TURN 结尾
            if (bestResult.Actions.Last().Type != ActionType.EndTurn)
                bestResult.Actions.Add(new GameAction { Type = ActionType.EndTurn });

            // ── 剥离无效幸运币 ──
            // 如果幸运币是最后一个非 END_TURN 动作，说明打出后没有使用额外法力，浪費了幸运币。
            StripTrailingCoin(bestResult.Actions);

            var actionCount = bestResult.Actions.Count(a => a.Type != ActionType.EndTurn);
            OnLog?.Invoke($"[AI] beam search done: {actionCount} actions, score={bestResult.Score:0.#}, expansions={totalExpansions}, time={sw.ElapsedMilliseconds}ms, completed={completedBeams.Count}");

            // ── 详细回合决策日志 ──
            {
                var sb = new StringBuilder();
                sb.AppendLine("============================================================");
                sb.AppendLine("                    [当前回合决策]");
                sb.AppendLine("============================================================");
                sb.AppendLine($"评估了 {totalExpansions} 种打法（{completedBeams.Count} 条完整路径），最高得分：{bestResult.Score:0.#} 分");
                sb.AppendLine($"搜索耗时：{sw.ElapsedMilliseconds}ms，搜索深度：{MaxDepth}，Beam 宽度：{BeamWidth}");
                sb.AppendLine($"初始场面得分：{initialScore:0.#} 分 → 最终得分：{bestResult.Score:0.#} 分（{(bestResult.Score - initialScore >= 0 ? "+" : "")}{(bestResult.Score - initialScore):0.#}）");
                sb.AppendLine("------------------------------------------------------------");

                if (actionCount == 0)
                {
                    sb.AppendLine("准备执行的动作序列：（无动作，直接结束回合）");
                }
                else
                {
                    sb.AppendLine("准备执行的动作序列：");
                    int step = 0;
                    foreach (var action in bestResult.Actions)
                    {
                        if (action.Type == ActionType.EndTurn) continue;
                        step++;

                        string desc;
                        switch (action.Type)
                        {
                            case ActionType.PlayCard:
                                var srcCard = action.Source;
                                string cardName = srcCard?.CardId.ToString() ?? $"Entity#{action.SourceEntityId}";
                                string costStr = srcCard != null ? $"({srcCard.Cost}费)" : "";
                                string statsStr = srcCard != null && srcCard.Type == Card.CType.MINION
                                    ? $" [{srcCard.Atk}/{srcCard.Health}]" : "";

                                if (action.Target != null)
                                {
                                    string tgtDesc = DescribeEntity(action.Target);
                                    desc = $"出牌：{cardName}{costStr}{statsStr} → 目标：{tgtDesc}";
                                }
                                else
                                {
                                    desc = $"出牌：{cardName}{costStr}{statsStr}";
                                }
                                break;

                            case ActionType.Attack:
                                string atkDesc = DescribeEntity(action.Source);
                                string defDesc = DescribeEntity(action.Target);
                                desc = $"攻击：{atkDesc} → {defDesc}";
                                break;

                            case ActionType.HeroPower:
                                if (action.Target != null)
                                {
                                    string hpTgtDesc = DescribeEntity(action.Target);
                                    desc = $"英雄技能 → 目标：{hpTgtDesc}";
                                }
                                else
                                {
                                    desc = "英雄技能（无目标）";
                                }
                                break;

                            case ActionType.UseLocation:
                                string locName = action.Source?.CardId.ToString() ?? $"Entity#{action.SourceEntityId}";
                                if (action.Target != null)
                                {
                                    string locTgtDesc = DescribeEntity(action.Target);
                                    desc = $"使用地标：{locName} → 目标：{locTgtDesc}";
                                }
                                else
                                {
                                    desc = $"使用地标：{locName}";
                                }
                                break;

                            default:
                                desc = action.ToActionString();
                                break;
                        }
                        sb.AppendLine($"  {step}. {desc}");
                    }
                }

                sb.AppendLine("------------------------------------------------------------");
                sb.AppendLine("最终预测场面：");

                // 分项计算最终棋面的各维度
                var finalBoard = bestResult.Board;
                float friendBoardVal = 0, enemyBoardVal = 0;
                foreach (var m in finalBoard.FriendMinions)
                {
                    if (m.Type != Card.CType.LOCATION)
                        friendBoardVal += (m.Atk * 1.2f + m.Health);
                }
                foreach (var m in finalBoard.EnemyMinions)
                {
                    if (m.Type != Card.CType.LOCATION)
                        enemyBoardVal += (m.Atk * 1.5f + m.Health * 0.8f);
                }

                int fHp = finalBoard.FriendHero?.Health ?? 0;
                int fArmor = finalBoard.FriendHero?.Armor ?? 0;
                int eHp = finalBoard.EnemyHero?.Health ?? 0;
                int eArmor = finalBoard.EnemyHero?.Armor ?? 0;

                sb.AppendLine($"  我方场面价值：{friendBoardVal:0.#} 分（{finalBoard.FriendMinions.Count(m => m.Type != Card.CType.LOCATION)} 随从）");
                sb.AppendLine($"  敌方场面威胁：{enemyBoardVal:0.#} 分（{finalBoard.EnemyMinions.Count(m => m.Type != Card.CType.LOCATION)} 随从）");
                sb.AppendLine($"  我方英雄：{fHp} 血 + {fArmor} 甲（有效血量 {fHp + fArmor}）");
                sb.AppendLine($"  敌方英雄：{eHp} 血 + {eArmor} 甲（有效血量 {eHp + eArmor}）");
                sb.AppendLine($"  血量差：{(fHp + fArmor) - (eHp + eArmor):+0;-0;0}");
                sb.AppendLine($"  我方手牌：{finalBoard.Hand.Count} 张 | 我方剩余法力：{finalBoard.Mana}/{finalBoard.MaxMana}");

                // 我方场上随从明细
                if (finalBoard.FriendMinions.Any(m => m.Type != Card.CType.LOCATION))
                {
                    sb.AppendLine("  我方场上随从：");
                    foreach (var m in finalBoard.FriendMinions)
                    {
                        if (m.Type == Card.CType.LOCATION) continue;
                        var tags = new List<string>();
                        if (m.IsTaunt) tags.Add("嘲讽");
                        if (m.IsDivineShield) tags.Add("圣盾");
                        if (m.HasPoison) tags.Add("剧毒");
                        if (m.IsWindfury) tags.Add("风怒");
                        if (m.IsLifeSteal) tags.Add("吸血");
                        if (m.HasReborn) tags.Add("复生");
                        if (m.IsStealth) tags.Add("潜行");
                        if (m.IsFrozen) tags.Add("冰冻");
                        string tagStr = tags.Count > 0 ? $" ({string.Join(",", tags)})" : "";
                        sb.AppendLine($"    - {m.CardId} [{m.Atk}/{m.Health}]{tagStr}");
                    }
                }

                // 敌方场上随从明细
                if (finalBoard.EnemyMinions.Any(m => m.Type != Card.CType.LOCATION))
                {
                    sb.AppendLine("  敌方场上随从：");
                    foreach (var m in finalBoard.EnemyMinions)
                    {
                        if (m.Type == Card.CType.LOCATION) continue;
                        var tags = new List<string>();
                        if (m.IsTaunt) tags.Add("嘲讽");
                        if (m.IsDivineShield) tags.Add("圣盾");
                        if (m.HasPoison) tags.Add("剧毒");
                        if (m.IsWindfury) tags.Add("风怒");
                        if (m.IsStealth) tags.Add("潜行");
                        if (m.IsFrozen) tags.Add("冰冻");
                        string tagStr = tags.Count > 0 ? $" ({string.Join(",", tags)})" : "";
                        sb.AppendLine($"    - {m.CardId} [{m.Atk}/{m.Health}]{tagStr}");
                    }
                }

                sb.AppendLine("============================================================");

                OnLog?.Invoke(sb.ToString());
            }

            // Profile 规则日志
            if (hasProfileRules && actionCount > 0)
            {
                var firstAction = bestResult.Actions.First();
                var pscore = _profileScorer.Evaluate(board, firstAction, param);
                if (!string.IsNullOrWhiteSpace(pscore.Detail))
                    OnLog?.Invoke($"[AI] profile first action={firstAction.ToActionString()}, bonus={pscore.Bonus:0.##}, detail={pscore.Detail}");
            }

            return bestResult.Actions;
        }

        // ────────────────────────────────────────────────
        //  辅助方法
        // ────────────────────────────────────────────────

        private static bool HasProfileRules(ProfileParameters param)
        {
            if (param == null) return false;
            try { return param.HasAnyRulesSet(); }
            catch { return true; }
        }

        private bool TryApplyAction(SimBoard board, GameAction action)
        {
            try
            {
                switch (action.Type)
                {
                    case ActionType.Attack:
                        var atkSrc = FindEntity(board, action.Source, true);
                        var atkTgt = FindEntity(board, action.Target, false);
                        if (atkSrc == null || atkTgt == null) return false;
                        _sim.Attack(board, atkSrc, atkTgt);
                        return true;

                    case ActionType.PlayCard:
                        var sourceEntityId = action.Source?.EntityId ?? action.SourceEntityId;
                        var card = board.Hand.FirstOrDefault(c => c.EntityId == sourceEntityId);
                        if (card == null) return false;
                        var playTgt = action.Target != null ? FindEntity(board, action.Target, null) : null;
                        _sim.PlayCard(board, card, playTgt);
                        return true;

                    case ActionType.HeroPower:
                        var hpTgt = action.Target != null ? FindEntity(board, action.Target, null) : null;
                        _sim.UseHeroPower(board, hpTgt);
                        return true;

                    case ActionType.UseLocation:
                        var locEntity = board.FriendMinions.FirstOrDefault(m => m.EntityId == action.SourceEntityId);
                        if (locEntity == null) return false;
                        var locTarget = action.Target != null ? FindEntity(board, action.Target, null) : null;
                        _sim.UseLocation(board, locEntity, locTarget);
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static SimEntity FindEntity(SimBoard board, SimEntity original, bool? isFriend)
        {
            if (original == null) return null;
            int id = original.EntityId;

            if (board.FriendHero?.EntityId == id) return board.FriendHero;
            if (board.EnemyHero?.EntityId == id) return board.EnemyHero;

            if (isFriend != false)
            {
                var f = board.FriendMinions.FirstOrDefault(m => m.EntityId == id);
                if (f != null) return f;
            }
            if (isFriend != true)
            {
                var e = board.EnemyMinions.FirstOrDefault(m => m.EntityId == id);
                if (e != null) return e;
            }

            return null;
        }

        /// <summary>
        /// 生成实体的可读描述（用于决策日志）。
        /// </summary>
        private static string DescribeEntity(SimEntity entity)
        {
            if (entity == null) return "???";

            string side = entity.IsFriend ? "我方" : "敌方";

            // 英雄
            if (entity.Type == Card.CType.HERO)
            {
                int ehp = entity.Health + entity.Armor;
                return entity.Armor > 0
                    ? $"{side}英雄({entity.Health}血+{entity.Armor}甲={ehp})"
                    : $"{side}英雄({entity.Health}血)";
            }

            // 随从 / 地标 / 其他
            string cardName = entity.CardId.ToString();
            if (entity.Type == Card.CType.LOCATION)
                return $"{side}地标 {cardName}";

            var tags = new List<string>();
            if (entity.IsTaunt) tags.Add("嘲讽");
            if (entity.IsDivineShield) tags.Add("圣盾");
            if (entity.HasPoison) tags.Add("剧毒");
            if (entity.IsWindfury) tags.Add("风怒");
            string tagStr = tags.Count > 0 ? $" ({string.Join(",", tags)})" : "";
            return $"{side} {entity.Atk}/{entity.Health} 随从 {cardName}{tagStr}";
        }

        /// <summary>
        /// 如果幸运币（GAME_005）是最后一个非 END_TURN 动作，则移除它。
        /// 因为打出幸运币后没有使用额外法力就结束回合是浪费。
        /// </summary>
        private static void StripTrailingCoin(List<GameAction> actions)
        {
            // 从末尾向前找到最后一个非 END_TURN 动作
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                if (actions[i].Type == ActionType.EndTurn) continue;

                // 检查是否是幸运币
                if (actions[i].Type == ActionType.PlayCard
                    && actions[i].Source?.CardId == SmartBot.Plugins.API.Card.Cards.GAME_005)
                {
                    actions.RemoveAt(i);
                }
                break; // 只检查最后一个非 END_TURN 动作
            }
        }
    }
}

