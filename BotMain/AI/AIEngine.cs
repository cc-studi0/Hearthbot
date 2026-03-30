using System;
using System.Collections.Generic;
using System.Linq;
using BotMain.AI;
using SmartBot.Database;
using SmartBot.Plugins.API;
using SmartBotProfiles;

namespace BotMain
{
    public sealed class AIDecisionPlan
    {
        public List<string> Actions { get; set; } = new List<string>();
        public bool ForceResimulation { get; set; }
        public HashSet<Card.Cards> ForcedResimulationCards { get; set; } = new HashSet<Card.Cards>();
    }

    public class AIEngine
    {
        private readonly SearchEngine _engine;
        private readonly LethalFinder _lethalFinder;
        private readonly BoardEvaluator _evaluator;
        private readonly SimpleAI _fallbackAi = new SimpleAI();
        public event Action<string> OnLog;

        public BoardEvaluator Evaluator => _evaluator;

        /// <summary>是否启用斩杀搜索（默认 true）</summary>
        public bool LethalSearchEnabled { get; set; } = true;

        /// <summary>斩杀搜索超时毫秒数（默认 2000）</summary>
        public int LethalTimeoutMs { get; set; } = 2000;

        /// <summary>搜索树策略版本开关（默认 true 使用 V2）</summary>
        public bool UseSearchTreeV2 { get; set; } = true;

        public AIEngine()
        {
            CardTemplate.INIT();
            var db = CardEffectDB.BuildDefault();
            var sim = new BoardSimulator(db);
            var aggroModel = new DefaultAggroInteractionModel();
            _evaluator = new BoardEvaluator(db, aggroModel);
            var gen = new ActionGenerator();
            gen.SetEffectDB(db);
            _engine = new SearchEngine(sim, _evaluator, gen, db, aggroModel, legacyBehaviorCompat: false);
            _engine.OnLog += msg => OnLog?.Invoke(msg);

            _lethalFinder = new LethalFinder(sim, db);
            _lethalFinder.OnLog += msg => OnLog?.Invoke(msg);
        }

        public List<string> DecideActions(
            string seed,
            Profile profile = null,
            List<Card.Cards> deckCards = null,
            Action<Board, SimBoard, ProfileParameters> parameterMutator = null)
        {
            return DecideActionPlan(seed, profile, deckCards, parameterMutator).Actions;
        }

        public AIDecisionPlan DecideActionPlan(
            string seed,
            Profile profile = null,
            List<Card.Cards> deckCards = null,
            Action<Board, SimBoard, ProfileParameters> parameterMutator = null)
        {
            var decisionPlan = new AIDecisionPlan();
            try
            {
                var compatibleSeed = SeedCompatibility.GetCompatibleSeed(seed, out var compatibilityDetail);
                if (!string.IsNullOrWhiteSpace(compatibilityDetail))
                    OnLog?.Invoke($"[AI] {compatibilityDetail}");

                var board = Board.FromSeed(compatibleSeed);
                ProfileParameters param = null;
                try
                {
                    param = profile?.GetParameters(board);
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[AI] profile {profile?.GetType().Name} GetParameters failed: {ex.Message}");
                }

                if (param != null)
                {
                    decisionPlan.ForceResimulation = param.ForceResimulation;
                    if (param.ForcedResimulationCardList != null)
                    {
                        decisionPlan.ForcedResimulationCards = new HashSet<Card.Cards>(
                            param.ForcedResimulationCardList.Where(c => c != 0));
                    }
                }

                var simBoard = SimBoard.FromBoard(board);
                if (param != null && parameterMutator != null)
                {
                    try
                    {
                        parameterMutator(board, simBoard, param);
                    }
                    catch (Exception ex)
                    {
                        OnLog?.Invoke($"[AI] parameter mutator failed: {ex.Message}");
                    }
                }

                // 注入牌库剩余卡牌列表
                if (deckCards != null && deckCards.Count > 0)
                    simBoard.FriendDeckCards = deckCards;

                // ── 斩杀搜索（优先级最高） ──
                if (LethalSearchEnabled)
                {
                    _lethalFinder.TimeoutMs = LethalTimeoutMs;
                    _lethalFinder.UseSearchTreeV2 = UseSearchTreeV2;
                    var lethalActions = _lethalFinder.FindLethal(simBoard);
                    if (lethalActions != null && lethalActions.Count > 0)
                    {
                        OnLog?.Invoke($"[AI] ★★★ LETHAL FOUND ★★★ ({lethalActions.Count} actions, {_lethalFinder.NodesExplored} nodes)");
                        var lethalResult = lethalActions.Select(a => a.ToActionString()).ToList();
                        lethalResult.Add("END_TURN");
                        decisionPlan.Actions = NormalizeActionPlan(lethalResult);
                        return decisionPlan;
                    }
                }

                // ── 常规搜索 ──
                _engine.UseSearchTreeV2 = UseSearchTreeV2;
                var actions = _engine.FindBestSequence(simBoard, param);
                var result = actions.Select(a => a.ToActionString()).ToList();

                if (result.Count == 1 && result[0] == "END_TURN")
                {
                    var unsupportedTypeCount = simBoard.Hand.Count(c =>
                        c.Type != Card.CType.MINION
                        && c.Type != Card.CType.SPELL
                        && c.Type != Card.CType.WEAPON
                        && c.Type != Card.CType.HERO
                        && c.Type != Card.CType.LOCATION);
                    OnLog?.Invoke($"[AI] only END_TURN from search; mana={simBoard.Mana}, hand={simBoard.Hand.Count}, unsupportedType={unsupportedTypeCount}, profile={profile?.GetType().Name ?? "null"}, paramLoaded={param != null}.");

                    if (param == null)
                    {
                        var fallback = _fallbackAi.DecideActions(compatibleSeed);
                        if (fallback.Count > 1)
                        {
                            OnLog?.Invoke($"[AI] fallback applied with {fallback.Count} action(s).");
                            decisionPlan.Actions = NormalizeActionPlan(fallback);
                            return decisionPlan;
                        }

                        var emergency = BuildEmergencyActions(board);
                        if (emergency.Count > 1)
                        {
                            OnLog?.Invoke($"[AI] emergency fallback applied with {emergency.Count} action(s).");
                            decisionPlan.Actions = NormalizeActionPlan(emergency);
                            return decisionPlan;
                        }
                    }
                }

                decisionPlan.Actions = NormalizeActionPlan(result);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[AI] DecideActions failed: {ex}");
                try
                {
                    var compatibleSeed = SeedCompatibility.GetCompatibleSeed(seed, out _);
                    var fallback = _fallbackAi.DecideActions(compatibleSeed);
                    OnLog?.Invoke($"[AI] exception fallback applied with {fallback.Count} action(s).");
                    decisionPlan.Actions = NormalizeActionPlan(fallback);
                }
                catch (Exception fallbackEx)
                {
                    OnLog?.Invoke($"[AI] fallback also failed: {fallbackEx}");
                    decisionPlan.Actions = new List<string> { "END_TURN" };
                }
            }
            return decisionPlan;
        }

        private List<string> BuildEmergencyActions(Board board)
        {
            var result = new List<string>();
            var myMinions = board.MinionFriend;
            var enemyMinions = board.MinionEnemy;

            // 确定攻击目标：有嘲讽打嘲讽，否则打随从或打脸
            var tauntTargets = enemyMinions.Where(m => m.IsTaunt && !m.IsStealth).ToList();
            var defaultTarget = tauntTargets.Count > 0
                ? tauntTargets.First()
                : (enemyMinions.Count > 0 ? enemyMinions.First() : board.HeroEnemy);

            // 随从攻击
            if (myMinions.Count > 0 && defaultTarget != null)
            {
                foreach (var minion in myMinions)
                {
                    if (minion.CanAttack && !minion.IsFrozen && minion.CurrentAtk > 0)
                    {
                        result.Add($"ATTACK|{minion.Id}|{defaultTarget.Id}");
                    }
                }
            }

            // 英雄攻击
            if (board.HeroFriend != null && board.HeroFriend.CanAttack
                && !board.HeroFriend.IsFrozen && board.HeroFriend.CurrentAtk > 0)
            {
                var heroTarget = defaultTarget ?? board.HeroEnemy;
                if (heroTarget != null)
                    result.Add($"ATTACK|{board.HeroFriend.Id}|{heroTarget.Id}");
            }

            result.Add("END_TURN");
            return result;
        }

        private static List<string> NormalizeActionPlan(List<string> actions)
        {
            var normalized = new List<string>();
            if (actions != null)
            {
                foreach (var action in actions)
                {
                    if (string.IsNullOrWhiteSpace(action))
                        continue;

                    var trimmed = action.Trim();
                    normalized.Add(trimmed);
                    if (trimmed.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase))
                        break;
                }
            }

            if (normalized.Count == 0)
                normalized.Add("END_TURN");
            else if (!normalized.Last().StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase))
                normalized.Add("END_TURN");

            return normalized;
        }
    }
}
