using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
                    // END_TURN 的分数就是当前棋面分数
                    completedBeams.Add(endBeam);

                    // 选项2：执行每个非 END_TURN 动作
                    foreach (var (action, profileScore) in nonEndActions)
                    {
                        totalExpansions++;
                        var clone = beam.Board.Clone();
                        bool ok = TryApplyAction(clone, action);
                        if (!ok) continue;

                        var boardScore = _eval.Evaluate(clone, param);
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

            var actionCount = bestResult.Actions.Count(a => a.Type != ActionType.EndTurn);
            OnLog?.Invoke($"[AI] beam search done: {actionCount} actions, score={bestResult.Score:0.#}, expansions={totalExpansions}, time={sw.ElapsedMilliseconds}ms, completed={completedBeams.Count}");

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
    }
}

