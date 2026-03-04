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
        public float CumulativeAdjustment;
        public ulong Fingerprint;
        public bool IsComplete; // 已到达 END_TURN

        public BeamCandidate Clone()
        {
            return new BeamCandidate
            {
                Board = Board.Clone(),
                Actions = new List<GameAction>(Actions),
                Score = Score,
                CumulativeAdjustment = CumulativeAdjustment,
                Fingerprint = Fingerprint,
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
        private readonly TradeEvaluator _tradeEval;
        private readonly IAggroInteractionModel _aggroModel;
        private readonly List<IHeuristicRule> _heuristicRules;

        /// <summary>Beam 宽度（同时追踪的候选序列数）</summary>
        public int BeamWidth { get; set; } = 6;

        /// <summary>启用动态束宽（回合初更宽、资源见底时更窄）</summary>
        public bool UseDynamicBeamWidth { get; set; } = true;

        /// <summary>启用进入模拟前的启发式动作剪枝</summary>
        public bool EnableHeuristicPruning { get; set; } = true;

        /// <summary>启用同构状态哈希（忽略手牌/随从列表顺序）</summary>
        public bool UseOrderInvariantFingerprint { get; set; } = true;

        /// <summary>最大搜索深度（动作步数）</summary>
        public int MaxDepth { get; set; } = 25;

        /// <summary>搜索超时毫秒数</summary>
        public int TimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 转置表剪枝阈值：若同一棋面历史最佳分数高于当前分数 + 阈值，则跳过该分支。
        /// </summary>
        public float TranspositionMinDelta { get; set; } = 0.05f;

        /// <summary>启用旧版卡牌特例启发式（默认 false）</summary>
        public bool LegacyBehaviorCompat { get; }

        public event Action<string> OnLog;

        public SearchEngine(
            BoardSimulator sim,
            BoardEvaluator eval,
            ActionGenerator gen,
            CardEffectDB effectDb = null,
            IAggroInteractionModel aggroModel = null,
            IEnumerable<IHeuristicRule> heuristicRules = null,
            bool legacyBehaviorCompat = false)
        {
            _sim = sim;
            _eval = eval;
            _gen = gen;
            _aggroModel = aggroModel ?? new DefaultAggroInteractionModel();
            _tradeEval = new TradeEvaluator(effectDb, _aggroModel);
            LegacyBehaviorCompat = legacyBehaviorCompat;
            _heuristicRules = heuristicRules?.Where(r => r != null).ToList()
                ?? HeuristicRuleFactory.CreateDefault(effectDb, legacyBehaviorCompat);
        }

        public List<GameAction> FindBestSequence(SimBoard board, ProfileParameters param)
        {
            var sw = Stopwatch.StartNew();
            var hasProfileRules = HasProfileRules(param);

            // ── 初始化 Beam ──
            var initialScore = _eval.Evaluate(board, param);
            var rootBoard = board.Clone();
            var rootFingerprint = ComputeBoardFingerprint(rootBoard, UseOrderInvariantFingerprint);
            var beams = new List<BeamCandidate>
            {
                new BeamCandidate
                {
                    Board = rootBoard,
                    Actions = new List<GameAction>(),
                    Score = initialScore,
                    CumulativeAdjustment = 0f,
                    Fingerprint = rootFingerprint,
                    IsComplete = false,
                }
            };

            var completedBeams = new List<BeamCandidate>();
            int totalExpansions = 0;
            int duplicateActionCount = 0;
            int profileBlockedCount = 0;
            int heuristicPrunedCount = 0;
            int transpositionPrunedCount = 0;
            int mergedStateCount = 0;
            int transpositionCacheHitCount = 0;
            int dynamicBeamMinUsed = int.MaxValue;
            int dynamicBeamMaxUsed = 0;
            int lastBeamWidthUsed = Math.Max(1, BeamWidth);

            var bestScoreByFingerprint = new Dictionary<ulong, float>
            {
                [rootFingerprint] = initialScore
            };
            var boardScoreCacheByFingerprint = new Dictionary<ulong, float>
            {
                [rootFingerprint] = initialScore
            };

            // ── 主搜索循环 ──
            for (int depth = 0; depth < MaxDepth; depth++)
            {
                if (sw.ElapsedMilliseconds > TimeoutMs) break;

                var activeBeams = beams.Where(b => !b.IsComplete).ToList();
                if (activeBeams.Count == 0) break;

                var nextBestByFingerprint = new Dictionary<ulong, BeamCandidate>();
                int depthCandidateCount = 0;
                int depthExpandedBeamCount = 0;

                foreach (var beam in activeBeams)
                {
                    if (sw.ElapsedMilliseconds > TimeoutMs) break;
                    var aggroContext = _aggroModel.Build(beam.Board, param);

                    // 生成所有可能的动作
                    var actions = _gen.Generate(beam.Board);
                    var nonEndActions = new List<(GameAction Action, ProfileActionScore ProfileScore, float Priority)>();
                    int blockedCount = 0;
                    int heuristicBlockedInBeam = 0;
                    int duplicatedActionsInBeam = 0;
                    string blockedSample = null;
                    string heuristicBlockedSample = null;
                    var actionKeySet = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var action in actions)
                    {
                        if (action.Type == ActionType.EndTurn) continue;

                        var actionKey = action.ToActionString();
                        if (!actionKeySet.Add(actionKey))
                        {
                            duplicatedActionsInBeam++;
                            duplicateActionCount++;
                            continue;
                        }

                        if (EnableHeuristicPruning && TryHeuristicPruneAction(beam.Board, action, out var heuristicReason))
                        {
                            heuristicBlockedInBeam++;
                            heuristicPrunedCount++;
                            heuristicBlockedSample ??= $"{action.ToActionString()} ({heuristicReason})";
                            continue;
                        }

                        var profileScore = _profileScorer.Evaluate(beam.Board, action, param);
                        if (profileScore.HardBlocked)
                        {
                            blockedCount++;
                            profileBlockedCount++;
                            blockedSample ??= $"{action.ToActionString()} ({profileScore.Detail})";
                            continue;
                        }

                        var priority = EstimateActionPriority(beam.Board, action, profileScore, aggroContext);
                        nonEndActions.Add((action, profileScore, priority));
                    }

                    depthCandidateCount += nonEndActions.Count;
                    depthExpandedBeamCount++;

                    // 首轮日志
                    if (depth == 0 && beam == activeBeams[0])
                    {
                        var attackerCount = beam.Board.FriendMinions.Count(m => m != null && m.CanAttack);
                        if (beam.Board.FriendHero != null && beam.Board.FriendHero.CanAttack)
                            attackerCount++;
                        var totalNonEnd = actions.Count(a => a.Type != ActionType.EndTurn);
                        OnLog?.Invoke($"[AI] beam search depth0: generated={totalNonEnd}, deduped={actionKeySet.Count}, candidates={nonEndActions.Count}, profileBlocked={blockedCount}, heuristicPruned={heuristicBlockedInBeam}, duplicated={duplicatedActionsInBeam}, mana={beam.Board.Mana}, hand={beam.Board.Hand.Count}, friendMinions={beam.Board.FriendMinions.Count}, attackers={attackerCount}, baseBeamWidth={BeamWidth}, dynamicBeam={UseDynamicBeamWidth}, hasProfile={hasProfileRules}");
                        if (hasProfileRules && blockedCount > 0)
                            OnLog?.Invoke($"[AI] profile blocked {blockedCount} action(s), sample={blockedSample}");
                        if (heuristicBlockedInBeam > 0)
                            OnLog?.Invoke($"[AI] heuristic pruned {heuristicBlockedInBeam} action(s), sample={heuristicBlockedSample}");
                    }

                    // 选项1：END_TURN（当前序列完成）
                    var endBeam = beam.Clone();
                    endBeam.Actions.Add(new GameAction { Type = ActionType.EndTurn });
                    endBeam.IsComplete = true;
                    // 重新评估当前棋面分数，并保留路径累计修正（Profile/换子奖励）
                    endBeam.Score = GetOrEvaluateBoardScore(endBeam.Board, endBeam.Fingerprint, param, boardScoreCacheByFingerprint, ref transpositionCacheHitCount) + endBeam.CumulativeAdjustment;
                    UpdateBestStateScore(bestScoreByFingerprint, endBeam.Fingerprint, endBeam.Score);
                    completedBeams.Add(endBeam);

                    // 预排序候选动作：优先考虑策略相关高价值动作，超时时更容易拿到好解。
                    nonEndActions.Sort((a, b) =>
                    {
                        var cmp = b.Priority.CompareTo(a.Priority);
                        if (cmp != 0) return cmp;
                        return b.ProfileScore.Bonus.CompareTo(a.ProfileScore.Bonus);
                    });

                    // 选项2：执行每个非 END_TURN 动作
                    foreach (var (action, profileScore, _) in nonEndActions)
                    {
                        totalExpansions++;
                        var clone = beam.Board.Clone();
                        bool ok = TryApplyAction(clone, action);
                        if (!ok) continue;

                        var fingerprint = ComputeBoardFingerprint(clone, UseOrderInvariantFingerprint);
                        var boardScore = GetOrEvaluateBoardScore(clone, fingerprint, param, boardScoreCacheByFingerprint, ref transpositionCacheHitCount);

                        // 如果模拟后敌方英雄已死，直接作为斩杀路径完成
                        if (boardScore >= 100000f)
                        {
                            if (IsDominatedState(bestScoreByFingerprint, fingerprint, boardScore, TranspositionMinDelta))
                            {
                                transpositionPrunedCount++;
                                continue;
                            }
                            UpdateBestStateScore(bestScoreByFingerprint, fingerprint, boardScore);
                            var lethalCandidate = new BeamCandidate
                            {
                                Board = clone,
                                Actions = new List<GameAction>(beam.Actions) { action, new GameAction { Type = ActionType.EndTurn } },
                                Score = boardScore,
                                Fingerprint = fingerprint,
                                IsComplete = true,
                            };
                            completedBeams.Add(lethalCandidate);
                            continue;
                        }

                        var tradeBonus = _tradeEval.EvaluateAttack(beam.Board, action, param);
                        var cumulativeAdjustment = beam.CumulativeAdjustment + profileScore.Bonus + tradeBonus;
                        var finalScore = boardScore + cumulativeAdjustment;

                        if (IsDominatedState(bestScoreByFingerprint, fingerprint, finalScore, TranspositionMinDelta))
                        {
                            transpositionPrunedCount++;
                            continue;
                        }
                        UpdateBestStateScore(bestScoreByFingerprint, fingerprint, finalScore);

                        var candidate = new BeamCandidate
                        {
                            Board = clone,
                            Actions = new List<GameAction>(beam.Actions) { action },
                            Score = finalScore,
                            CumulativeAdjustment = cumulativeAdjustment,
                            Fingerprint = fingerprint,
                            IsComplete = false,
                        };

                        AddOrReplaceCandidate(nextBestByFingerprint, candidate, ref mergedStateCount);
                    }
                }

                if (nextBestByFingerprint.Count == 0) break;

                var avgCandidatesPerBeam = depthExpandedBeamCount > 0
                    ? (float)depthCandidateCount / depthExpandedBeamCount
                    : 0f;
                var currentBeamWidth = UseDynamicBeamWidth
                    ? ComputeDynamicBeamWidth(depth, activeBeams, avgCandidatesPerBeam)
                    : Math.Max(1, BeamWidth);

                lastBeamWidthUsed = currentBeamWidth;
                dynamicBeamMinUsed = Math.Min(dynamicBeamMinUsed, currentBeamWidth);
                dynamicBeamMaxUsed = Math.Max(dynamicBeamMaxUsed, currentBeamWidth);

                OnLog?.Invoke($"[AI] beam depth={depth}: active={activeBeams.Count}, avgCandidates={avgCandidatesPerBeam:0.##}, kept={Math.Min(currentBeamWidth, nextBestByFingerprint.Count)}/{nextBestByFingerprint.Count}, beamWidth={currentBeamWidth}");

                // ── Beam 剪枝：保留 top-N ──
                beams = nextBestByFingerprint.Values
                    .OrderByDescending(c => c.Score)
                    .Take(currentBeamWidth)
                    .ToList();
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
                if (bestResult == null
                    || c.Score > bestResult.Score + 0.001f
                    || (Math.Abs(c.Score - bestResult.Score) <= 0.001f && c.Actions.Count < bestResult.Actions.Count))
                {
                    bestResult = c;
                }
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
            // 若移除幸运币后其余动作仍可完整执行，说明幸运币没有产生实际节奏收益，应直接移除。
            if (hasProfileRules)
                StripTrailingCoin(bestResult.Actions, board);
            else
                StripWastefulCoin(bestResult.Actions, board);

            var actionCount = bestResult.Actions.Count(a => a.Type != ActionType.EndTurn);
            OnLog?.Invoke($"[AI] beam search done: {actionCount} actions, score={bestResult.Score:0.#}, expansions={totalExpansions}, time={sw.ElapsedMilliseconds}ms, completed={completedBeams.Count}, transpositionPruned={transpositionPrunedCount}, transpositionCacheHit={transpositionCacheHitCount}, mergedState={mergedStateCount}, duplicateActions={duplicateActionCount}, profileBlocked={profileBlockedCount}, heuristicPruned={heuristicPrunedCount}");

            // ── 详细回合决策日志 ──
            {
                var sb = new StringBuilder();
                sb.AppendLine("============================================================");
                sb.AppendLine("                    [当前回合决策]");
                sb.AppendLine("============================================================");
                sb.AppendLine($"评估了 {totalExpansions} 种打法（{completedBeams.Count} 条完整路径），最高得分：{bestResult.Score:0.#} 分");

                var dynamicMinForLog = dynamicBeamMinUsed == int.MaxValue ? lastBeamWidthUsed : dynamicBeamMinUsed;
                var dynamicMaxForLog = dynamicBeamMaxUsed <= 0 ? lastBeamWidthUsed : dynamicBeamMaxUsed;
                if (UseDynamicBeamWidth)
                    sb.AppendLine($"搜索耗时：{sw.ElapsedMilliseconds}ms，搜索深度：{MaxDepth}，Beam 宽度：基础 {BeamWidth}，动态 {dynamicMinForLog}-{dynamicMaxForLog}");
                else
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

                            case ActionType.TradeCard:
                                var tradeCardName = action.Source?.CardId.ToString() ?? $"Entity#{action.SourceEntityId}";
                                desc = $"交易：{tradeCardName}（消耗 1 法力）";
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

        private static bool IsDominatedState(
            Dictionary<ulong, float> bestScoreByFingerprint,
            ulong fingerprint,
            float score,
            float minDelta)
        {
            if (!bestScoreByFingerprint.TryGetValue(fingerprint, out var bestScore))
                return false;

            var delta = Math.Max(0f, minDelta);
            return bestScore >= score + delta;
        }

        private static void UpdateBestStateScore(
            Dictionary<ulong, float> bestScoreByFingerprint,
            ulong fingerprint,
            float score)
        {
            if (!bestScoreByFingerprint.TryGetValue(fingerprint, out var existing) || score > existing)
                bestScoreByFingerprint[fingerprint] = score;
        }

        private static void AddOrReplaceCandidate(
            Dictionary<ulong, BeamCandidate> nextBestByFingerprint,
            BeamCandidate candidate,
            ref int mergedStateCount)
        {
            if (candidate == null) return;

            if (!nextBestByFingerprint.TryGetValue(candidate.Fingerprint, out var existing))
            {
                nextBestByFingerprint[candidate.Fingerprint] = candidate;
                return;
            }

            mergedStateCount++;
            if (candidate.Score > existing.Score + 0.001f
                || (Math.Abs(candidate.Score - existing.Score) <= 0.001f
                    && candidate.Actions.Count < existing.Actions.Count))
            {
                nextBestByFingerprint[candidate.Fingerprint] = candidate;
            }
        }

        private float GetOrEvaluateBoardScore(
            SimBoard board,
            ulong fingerprint,
            ProfileParameters param,
            Dictionary<ulong, float> boardScoreCacheByFingerprint,
            ref int transpositionCacheHitCount)
        {
            if (boardScoreCacheByFingerprint.TryGetValue(fingerprint, out var cached))
            {
                transpositionCacheHitCount++;
                return cached;
            }

            var score = _eval.Evaluate(board, param);
            boardScoreCacheByFingerprint[fingerprint] = score;
            return score;
        }

        private int ComputeDynamicBeamWidth(int depth, IReadOnlyList<BeamCandidate> activeBeams, float avgCandidatesPerBeam)
        {
            int baseWidth = Math.Max(1, BeamWidth);
            if (activeBeams == null || activeBeams.Count == 0) return baseWidth;

            float avgMana = activeBeams.Average(b => (float)Math.Max(0, b.Board?.Mana ?? 0));
            float avgMaxMana = activeBeams.Average(b => (float)Math.Max(1, b.Board?.MaxMana ?? 1));
            float avgHand = activeBeams.Average(b => (float)Math.Max(0, b.Board?.Hand?.Count ?? 0));

            float manaRatio = Clamp01(avgMana / avgMaxMana);
            float handRatio = Math.Min(1.5f, avgHand / 7f);
            float depthRatio = MaxDepth <= 1 ? 1f : (float)depth / (MaxDepth - 1);
            float branchRatio = avgCandidatesPerBeam <= 0f
                ? 0.5f
                : Math.Min(2.2f, avgCandidatesPerBeam / Math.Max(2f, baseWidth));

            // 开局资源与分支数高时放宽，越往后越收敛。
            float expansion = 0.7f + manaRatio * 0.9f + handRatio * 0.35f;
            float contraction = 1f - depthRatio * 0.5f;
            float width = baseWidth * expansion * contraction * (0.75f + branchRatio * 0.45f);

            if (avgMana <= 1f || avgCandidatesPerBeam <= 2f) width *= 0.55f;
            if (avgMana <= 0.1f) width *= 0.7f;

            int minWidth = Math.Max(2, (int)Math.Ceiling(baseWidth * 0.45f));
            int maxWidth = Math.Max(baseWidth + 1, (int)Math.Ceiling(baseWidth * 2.2f));
            return ClampInt((int)Math.Round(width), minWidth, maxWidth);
        }

        private bool TryHeuristicPruneAction(SimBoard board, GameAction action, out string reason)
        {
            reason = null;
            if (board == null || action == null || _heuristicRules == null || _heuristicRules.Count == 0)
                return false;

            foreach (var rule in _heuristicRules)
            {
                if (rule == null) continue;
                if (rule.TryPrune(board, action, out reason))
                    return true;
            }

            reason = null;
            return false;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static float EstimateActionPriority(
            SimBoard board,
            GameAction action,
            ProfileActionScore profileScore,
            AggroInteractionContext aggroContext)
        {
            if (action == null) return 0f;
            if (aggroContext == null) aggroContext = AggroInteractionContext.FromAggroCoef(1f);

            float p = profileScore?.Bonus ?? 0f;

            switch (action.Type)
            {
                case ActionType.Attack:
                    {
                        var source = action.Source;
                        var target = action.Target;
                        if (target == null) return p;

                        var isFace = board?.EnemyHero != null && target.EntityId == board.EnemyHero.EntityId;
                        if (isFace)
                        {
                            p += Math.Max(0f, (source?.Atk ?? 0) * 0.35f) * aggroContext.FaceBias;
                            int enemyEhp = (board?.EnemyHero?.Health ?? 0) + (board?.EnemyHero?.Armor ?? 0);
                            if (enemyEhp > 0 && enemyEhp <= aggroContext.SafeFaceEnemyEhpThreshold)
                                p += 2f * aggroContext.FaceBias;
                        }
                        else
                        {
                            p += (target.Atk * 0.55f + target.Health * 0.15f) * aggroContext.ThreatBias;
                            if (target.HasPoison) p += 2.5f * aggroContext.ThreatBias;
                            if (target.IsWindfury) p += 1.5f * aggroContext.ThreatBias;
                            if (target.SpellPower > 0) p += target.SpellPower * 1.2f * aggroContext.ThreatBias;
                            if (target.IsTaunt) p += 1.2f * aggroContext.TradeBias;
                        }

                        if (source != null)
                        {
                            if (source.HasPoison && !isFace) p += 1.5f * aggroContext.TradeBias;
                            if (source.IsFrozen || source.Atk <= 0) p -= 2f;
                        }
                        break;
                    }

                case ActionType.PlayCard:
                    {
                        var card = action.Source;
                        if (card == null) return p;

                        p += Math.Min(6f, card.Cost * 0.6f);
                        if (card.Type == Card.CType.MINION)
                            p += card.Atk * 0.25f + card.Health * 0.2f + (card.HasBattlecry ? 0.8f : 0f);
                        else if (card.Type == Card.CType.SPELL)
                            p += 0.8f;
                        else if (card.Type == Card.CType.WEAPON)
                            p += Math.Max(0f, card.Atk * 0.6f);
                        else if (card.Type == Card.CType.LOCATION)
                            p += 1f;
                        break;
                    }

                case ActionType.HeroPower:
                    p += 0.6f;
                    break;

                case ActionType.TradeCard:
                    p += board != null && board.Hand.Count >= 8 ? 1.5f : 0.25f;
                    break;

                case ActionType.UseLocation:
                    p += 1.1f;
                    if (action.Target != null)
                        p += action.Target.Atk * 0.2f;
                    break;
            }

            return p;
        }

        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private static ulong ComputeBoardFingerprint(SimBoard board, bool ignoreEntityListOrder)
        {
            ulong h = FnvOffsetBasis;
            if (board == null) return h;

            HashInt(ref h, board.Mana);
            HashInt(ref h, board.MaxMana);
            HashBool(ref h, board.HeroPowerUsed);
            HashInt(ref h, board.CardsPlayedThisTurn);
            HashInt(ref h, board.FriendCardDraw);
            HashInt(ref h, (int)board.FriendClass);
            HashInt(ref h, (int)board.EnemyClass);

            HashEntity(ref h, board.FriendHero);
            HashEntity(ref h, board.EnemyHero);
            HashEntity(ref h, board.FriendWeapon);
            HashEntity(ref h, board.EnemyWeapon);
            HashEntity(ref h, board.HeroPower);

            HashEntityList(ref h, board.FriendMinions, ignoreEntityListOrder);
            HashEntityList(ref h, board.EnemyMinions, ignoreEntityListOrder);
            HashEntityList(ref h, board.Hand, ignoreEntityListOrder);

            HashInt(ref h, board.FriendDeckCards?.Count ?? 0);
            if (board.FriendDeckCards != null)
            {
                foreach (var cardId in board.FriendDeckCards)
                    HashInt(ref h, (int)cardId);
            }

            return h;
        }

        private static void HashEntityList(ref ulong h, List<SimEntity> list, bool ignoreOrder)
        {
            HashInt(ref h, list?.Count ?? 0);
            if (list == null) return;

            if (!ignoreOrder)
            {
                foreach (var e in list)
                    HashEntity(ref h, e);
                return;
            }

            var entityHashes = new List<ulong>(list.Count);
            foreach (var e in list)
            {
                ulong eh = FnvOffsetBasis;
                HashEntity(ref eh, e);
                entityHashes.Add(eh);
            }

            entityHashes.Sort();
            foreach (var eh in entityHashes)
                HashULong(ref h, eh);
        }

        private static void HashEntity(ref ulong h, SimEntity e)
        {
            if (e == null)
            {
                HashInt(ref h, -1);
                return;
            }

            HashInt(ref h, (int)e.CardId);
            HashInt(ref h, e.EntityId);
            HashInt(ref h, e.Atk);
            HashInt(ref h, e.Health);
            HashInt(ref h, e.MaxHealth);
            HashInt(ref h, e.Armor);
            HashInt(ref h, e.Cost);
            HashInt(ref h, e.SpellPower);
            HashInt(ref h, e.CountAttack);
            HashInt(ref h, (int)e.Type);

            HashBool(ref h, e.IsFriend);
            HashBool(ref h, e.IsTaunt);
            HashBool(ref h, e.IsDivineShield);
            HashBool(ref h, e.IsWindfury);
            HashBool(ref h, e.HasPoison);
            HashBool(ref h, e.IsLifeSteal);
            HashBool(ref h, e.HasReborn);
            HashBool(ref h, e.IsFrozen);
            HashBool(ref h, e.IsImmune);
            HashBool(ref h, e.IsSilenced);
            HashBool(ref h, e.IsStealth);
            HashBool(ref h, e.HasCharge);
            HashBool(ref h, e.HasRush);
            HashBool(ref h, e.IsTired);
            HashBool(ref h, e.IsTradeable);
            HashBool(ref h, e.HasBattlecry);
            HashBool(ref h, e.HasDeathrattle);
            HashBool(ref h, e.EnrageBonusActive);
            HashBool(ref h, e.UseBoardCanAttack);
            HashBool(ref h, e.BoardCanAttack);
        }

        private static void HashInt(ref ulong h, int value)
        {
            unchecked
            {
                h ^= (uint)value;
                h *= FnvPrime;
            }
        }

        private static void HashULong(ref ulong h, ulong value)
        {
            unchecked
            {
                HashInt(ref h, (int)(value & 0xFFFFFFFFUL));
                HashInt(ref h, (int)(value >> 32));
            }
        }

        private static void HashBool(ref ulong h, bool value) => HashInt(ref h, value ? 1 : 0);

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

                    case ActionType.TradeCard:
                        var tradeSourceId = action.Source?.EntityId ?? action.SourceEntityId;
                        var tradeCard = board.Hand.FirstOrDefault(c => c.EntityId == tradeSourceId);
                        if (tradeCard == null) return false;
                        _sim.TradeCard(board, tradeCard);
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
        /// 在 profile 生效时仅做最保守的幸运币剥离：
        /// 只移除“最后一个非 END_TURN 动作”为幸运币且移除后仍可执行的情况。
        /// </summary>
        private void StripTrailingCoin(List<GameAction> actions, SimBoard initialBoard)
        {
            if (actions == null || actions.Count == 0 || initialBoard == null) return;

            int lastNonEnd = -1;
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                var a = actions[i];
                if (a == null || a.Type == ActionType.EndTurn) continue;
                lastNonEnd = i;
                break;
            }
            if (lastNonEnd < 0) return;

            var lastAction = actions[lastNonEnd];
            if (lastAction.Type != ActionType.PlayCard) return;
            if (lastAction.Source?.CardId != SmartBot.Plugins.API.Card.Cards.GAME_005) return;

            var planWithoutTrailingCoin = new List<GameAction>();
            for (int i = 0; i < actions.Count; i++)
            {
                if (i == lastNonEnd) continue;
                var a = actions[i];
                if (a == null || a.Type == ActionType.EndTurn) continue;
                planWithoutTrailingCoin.Add(a);
            }

            if (CanExecutePlan(initialBoard, planWithoutTrailingCoin))
            {
                actions.RemoveAt(lastNonEnd);
                OnLog?.Invoke("[AI] strip trailing coin: removed GAME_005 (trailing/no extra tempo)");
            }
        }

        /// <summary>
        /// 移除浪费的幸运币：如果删掉某个幸运币动作后，其余动作序列仍可完整执行，则该幸运币无效。
        /// </summary>
        private void StripWastefulCoin(List<GameAction> actions, SimBoard initialBoard)
        {
            if (actions == null || actions.Count == 0 || initialBoard == null) return;

            for (int i = actions.Count - 1; i >= 0; i--)
            {
                var action = actions[i];
                if (action == null) continue;
                if (action.Type != ActionType.PlayCard) continue;
                if (action.Source?.CardId != SmartBot.Plugins.API.Card.Cards.GAME_005) continue;

                var planWithoutCoin = new List<GameAction>();
                for (int j = 0; j < actions.Count; j++)
                {
                    if (j == i) continue;
                    var a = actions[j];
                    if (a == null || a.Type == ActionType.EndTurn) continue;
                    planWithoutCoin.Add(a);
                }

                if (CanExecutePlan(initialBoard, planWithoutCoin))
                {
                    actions.RemoveAt(i);
                    OnLog?.Invoke("[AI] strip wasteful coin: removed GAME_005 (plan executable without coin)");
                }
            }
        }

        private bool CanExecutePlan(SimBoard initialBoard, List<GameAction> plan)
        {
            if (initialBoard == null) return false;
            if (plan == null || plan.Count == 0) return true;

            var clone = initialBoard.Clone();
            foreach (var action in plan)
            {
                if (action == null) continue;
                if (!TryApplyAction(clone, action))
                    return false;
            }

            return true;
        }
    }
}
