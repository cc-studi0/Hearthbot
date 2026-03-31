using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace BotMain.Learning
{
    internal sealed class LearnedStrategyCoordinator : IDisposable
    {
        private readonly object _queueSync = new object();
        private readonly Queue<Action> _queue = new Queue<Action>();
        private readonly AutoResetEvent _queueSignal = new AutoResetEvent(false);
        private readonly Thread _worker;
        private readonly ILearnedStrategyStore _store;
        private readonly ILearnedStrategyTrainer _trainer;
        private readonly ILearnedStrategyRuntime _runtime;
        private readonly ConsistencyTracker _consistencyTracker;
        private readonly SqliteConsistencyStore _consistencyStore;
        private readonly LearnedEvalWeights _evalWeights = new LearnedEvalWeights();
        private LinearScoringModel _scoringModel;
        private int _periodicSaveCounter;
        private volatile bool _disposed;

        public LearnedStrategyCoordinator(
            ILearnedStrategyStore store = null,
            ILearnedStrategyTrainer trainer = null,
            ILearnedStrategyRuntime runtime = null)
        {
            _store = store ?? new SqliteLearnedStrategyStore();
            _trainer = trainer ?? new LearnedStrategyTrainer();
            _runtime = runtime ?? new LearnedStrategyRuntime();
            SafeReloadRuntime("startup");

            _consistencyTracker = new ConsistencyTracker(windowSize: 200);
            try { _consistencyStore = new SqliteConsistencyStore(); }
            catch { _consistencyStore = null; }
            LoadConsistencyHistory();
            LoadEvalWeights();
            LoadScoringModel();

            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "HsBoxTeacherLearning"
            };
            _worker.Start();
        }

        public Action<string> OnLog { get; set; }

        public ILearnedStrategyRuntime Runtime => _runtime;

        public ConsistencyTracker Consistency => _consistencyTracker;

        public SqliteConsistencyStore ConsistencyStore => _consistencyStore;

        public LearnedEvalWeights EvalWeights => _evalWeights;

        public LinearScoringModel ScoringModel => _scoringModel;

        public void EnqueueActionSample(ActionLearningSample sample)
        {
            if (sample == null)
                return;

            // 一致率跟踪（主线程，不入队）
            var isMatch = !string.IsNullOrEmpty(sample.TeacherAction)
                && !string.IsNullOrEmpty(sample.LocalAction)
                && string.Equals(
                    NormalizeActionForComparison(sample.TeacherAction),
                    NormalizeActionForComparison(sample.LocalAction),
                    StringComparison.OrdinalIgnoreCase);
            _consistencyTracker.Record(ConsistencyDimension.Action, isMatch);
            try { _consistencyStore?.RecordConsistency("Action", isMatch); } catch { }

            var rate = _consistencyTracker.GetRate(ConsistencyDimension.Action);
            var turn = sample.PlanningBoard?.TurnCount ?? 0;
            var teacherDisplay = AnnotateAction(sample.TeacherAction, sample.PlanningBoard);
            var localDisplay = AnnotateAction(sample.LocalAction, sample.PlanningBoard);
            Log($"[Learning] T{turn} 动作{(isMatch ? "一致 ✓" : "不一致 ✗")} (盒子:{Truncate(teacherDisplay, 60)} 本地:{Truncate(localDisplay, 60)}) 滑动一致率:{rate:0.0}%");

            Enqueue(() =>
            {
                if (!_trainer.TryBuildActionTraining(sample, out var record, out var detail))
                {
                    Log($"[Learning] action skipped: {detail}");
                    return;
                }

                if (!_store.TryStoreActionTraining(record, out var storeDetail))
                {
                    Log($"[Learning] action deduped/skipped: {storeDetail}");
                    return;
                }

                SafeReloadRuntime("action");
                Log($"[Learning] action stored: {detail}; {storeDetail}");

                // P2: 评估信号提取
                try
                {
                    if (sample.PlanningBoard != null && !string.IsNullOrEmpty(sample.TeacherAction))
                    {
                        var board = sample.PlanningBoard;
                        var enemyHeroId = board.HeroEnemy?.Id ?? 0;
                        var signals = ActionPatternClassifier.Classify(
                            new[] { sample.TeacherAction },
                            enemyHeroId,
                            board.ManaAvailable,
                            board.MaxMana,
                            board.Hand?.Count ?? 0);
                        var bucketKey = EvalBucketKey.FromBoardState(
                            board.TurnCount,
                            (board.HeroFriend?.CurrentHealth ?? 30) + (board.HeroFriend?.CurrentArmor ?? 0),
                            (board.HeroEnemy?.CurrentHealth ?? 30) + (board.HeroEnemy?.CurrentArmor ?? 0),
                            board.MinionFriend?.Count ?? 0,
                            board.MinionEnemy?.Count ?? 0);
                        _evalWeights.Update(bucketKey, signals);
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Learning] eval signal error: {ex.Message}");
                }

                // P3: 特征模型在线训练
                try
                {
                    if (_scoringModel != null && sample.PlanningBoard != null
                        && !string.IsNullOrEmpty(sample.TeacherAction)
                        && !string.IsNullOrEmpty(sample.LocalAction)
                        && !string.Equals(sample.TeacherAction, sample.LocalAction, StringComparison.OrdinalIgnoreCase))
                    {
                        var boardFeatures = FeatureVectorExtractor.ExtractBoardFeatures(sample.PlanningBoard);
                        var enemyHeroId = sample.PlanningBoard.HeroEnemy?.Id ?? 0;
                        var teacherActionFeatures = FeatureVectorExtractor.ExtractActionFeatures(sample.TeacherAction, sample.PlanningBoard, enemyHeroId);
                        var localActionFeatures = FeatureVectorExtractor.ExtractActionFeatures(sample.LocalAction, sample.PlanningBoard, enemyHeroId);
                        var teacherCombined = FeatureVectorExtractor.Combine(boardFeatures, teacherActionFeatures);
                        var localCombined = FeatureVectorExtractor.Combine(boardFeatures, localActionFeatures);
                        _scoringModel.UpdatePairwise(teacherCombined, localCombined);
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Learning] scoring model update error: {ex.Message}");
                }
            });
        }

        public void EnqueueMulliganSample(MulliganLearningSample sample)
        {
            if (sample == null)
                return;

            // 留牌一致率
            var isMatch = AreEntityIdSetsEqual(sample.TeacherReplaceEntityIds, sample.LocalReplaceEntityIds);
            _consistencyTracker.Record(ConsistencyDimension.Mulligan, isMatch);
            try { _consistencyStore?.RecordConsistency("Mulligan", isMatch); } catch { }

            var rate = _consistencyTracker.GetRate(ConsistencyDimension.Mulligan);
            Log($"[Learning] 留牌{(isMatch ? "一致 ✓" : "不一致 ✗")} 滑动一致率:{rate:0.0}%");

            Enqueue(() =>
            {
                if (!_trainer.TryBuildMulliganTraining(sample, out var record, out var detail))
                {
                    Log($"[Learning] mulligan skipped: {detail}");
                    return;
                }

                if (!_store.TryStoreMulliganTraining(record, out var storeDetail))
                {
                    Log($"[Learning] mulligan deduped/skipped: {storeDetail}");
                    return;
                }

                SafeReloadRuntime("mulligan");
                Log($"[Learning] mulligan stored: {detail}; {storeDetail}");
            });
        }

        public void EnqueueChoiceSample(ChoiceLearningSample sample)
        {
            if (sample == null)
                return;

            // 选择一致率
            var isMatch = AreEntityIdSetsEqual(sample.TeacherSelectedEntityIds, sample.LocalSelectedEntityIds);
            _consistencyTracker.Record(ConsistencyDimension.Choice, isMatch);
            try { _consistencyStore?.RecordConsistency("Choice", isMatch); } catch { }

            var rate = _consistencyTracker.GetRate(ConsistencyDimension.Choice);
            Log($"[Learning] 选择{(isMatch ? "一致 ✓" : "不一致 ✗")} 滑动一致率:{rate:0.0}%");

            Enqueue(() =>
            {
                if (!_trainer.TryBuildChoiceTraining(sample, out var record, out var detail))
                {
                    Log($"[Learning] choice skipped: {detail}");
                    return;
                }

                if (!_store.TryStoreChoiceTraining(record, out var storeDetail))
                {
                    Log($"[Learning] choice deduped/skipped: {storeDetail}");
                    return;
                }

                SafeReloadRuntime("choice");
                Log($"[Learning] choice stored: {detail}; {storeDetail}");
            });
        }

        public void ApplyMatchOutcome(string matchId, LearnedMatchOutcome outcome)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                return;

            Enqueue(() =>
            {
                if (!_store.TryApplyMatchOutcome(matchId, outcome, out var detail))
                {
                    Log($"[Learning] outcome skipped: {detail}");
                    return;
                }

                SafeReloadRuntime("outcome");
                Log($"[Learning] outcome applied: {detail}");
            });
        }

        public void Dispose()
        {
            _disposed = true;
            _queueSignal.Set();
            try { _worker.Join(1000); } catch { }
            _queueSignal.Dispose();
            _consistencyStore?.Dispose();
        }

        private void Enqueue(Action work)
        {
            if (work == null || _disposed)
                return;

            lock (_queueSync)
            {
                if (_queue.Count >= 2048)
                {
                    Log("[Learning] queue saturated, dropping new work item.");
                    return;
                }

                _queue.Enqueue(work);
            }

            _queueSignal.Set();
        }

        private void WorkerLoop()
        {
            while (!_disposed)
            {
                Action work = null;
                lock (_queueSync)
                {
                    if (_queue.Count > 0)
                        work = _queue.Dequeue();
                }

                if (work == null)
                {
                    _queueSignal.WaitOne(500);
                    continue;
                }

                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    Log($"[Learning] worker error: {ex.Message}");
                }

                _periodicSaveCounter++;
                if (_periodicSaveCounter >= 50)
                {
                    _periodicSaveCounter = 0;
                    try { _store.SaveEvalWeights(_evalWeights.GetAll()); }
                    catch (Exception ex) { Log($"[Learning] eval weights save error: {ex.Message}"); }
                    try { if (_scoringModel != null) _store.SaveScoringModel("action_v1", _scoringModel.Serialize()); }
                    catch (Exception ex) { Log($"[Learning] scoring model save error: {ex.Message}"); }
                }
            }
        }

        private void SafeReloadRuntime(string reason)
        {
            try
            {
                _runtime.Reload(_store.LoadSnapshot());
            }
            catch (Exception ex)
            {
                Log($"[Learning] reload failed ({reason}): {ex.Message}");
            }
        }

        private void LoadScoringModel()
        {
            try
            {
                var serialized = _store.LoadScoringModel("action_v1");
                _scoringModel = !string.IsNullOrEmpty(serialized)
                    ? LinearScoringModel.Deserialize(serialized)
                    : new LinearScoringModel(FeatureVectorExtractor.TotalFeatureCount);
                Log($"[Learning] 评分模型已加载 ({_scoringModel.FeatureCount} 维)");
            }
            catch (Exception ex)
            {
                _scoringModel = new LinearScoringModel(FeatureVectorExtractor.TotalFeatureCount);
                Log($"[Learning] 评分模型加载失败，使用初始模型: {ex.Message}");
            }
        }

        private void LoadEvalWeights()
        {
            try
            {
                var data = _store.LoadEvalWeights();
                if (data.Count > 0)
                {
                    _evalWeights.Load(data);
                    Log($"[Learning] 评估权重已加载: {data.Count} 个桶");
                }
            }
            catch (Exception ex)
            {
                Log($"[Learning] 评估权重加载失败: {ex.Message}");
            }
        }

        private void LoadConsistencyHistory()
        {
            try
            {
                foreach (ConsistencyDimension dim in Enum.GetValues(typeof(ConsistencyDimension)))
                {
                    var records = _consistencyStore?.LoadRecentRecords(dim.ToString(), 200);
                    if (records == null) continue;
                    foreach (var r in records)
                        _consistencyTracker.Record(dim, r);
                }
                var snapshot = _consistencyTracker.GetSnapshot();
                Log($"[Learning] 一致率历史已加载: 动作={snapshot.ActionRate:0.0}%({snapshot.ActionCount}), 留牌={snapshot.MulliganRate:0.0}%({snapshot.MulliganCount}), 选择={snapshot.ChoiceRate:0.0}%({snapshot.ChoiceCount})");
            }
            catch (Exception ex)
            {
                Log($"[Learning] 一致率历史加载失败: {ex.Message}");
            }
        }

        private static string NormalizeActionForComparison(string action)
        {
            if (string.IsNullOrWhiteSpace(action)) return string.Empty;
            var parts = action.Split('|');
            if (parts.Length >= 3) return $"{parts[0]}|{parts[1]}|{parts[2]}";
            if (parts.Length >= 2) return $"{parts[0]}|{parts[1]}";
            return parts[0];
        }

        private static bool AreEntityIdSetsEqual(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            var setA = new HashSet<int>(a);
            return b.All(id => setA.Contains(id));
        }

        private static string AnnotateAction(string action, Board board)
        {
            if (string.IsNullOrWhiteSpace(action) || board == null)
                return action ?? "";

            var parts = action.Split('|');
            for (var i = 1; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out var entityId) || entityId <= 0)
                    continue;
                var name = ResolveEntityName(board, entityId);
                if (!string.IsNullOrEmpty(name))
                    parts[i] = name;
            }
            return string.Join("|", parts);
        }

        private static string ResolveEntityName(Board board, int entityId)
        {
            var card = FindEntityOnBoard(board, entityId);
            if (card?.Template == null)
                return null;

            try
            {
                var fullTemplate = CardTemplate.LoadFromId(card.Template.Id);
                if (fullTemplate != null)
                {
                    if (!string.IsNullOrWhiteSpace(fullTemplate.NameCN))
                        return fullTemplate.NameCN;
                    if (!string.IsNullOrWhiteSpace(fullTemplate.Name))
                        return fullTemplate.Name;
                }
            }
            catch { }

            return card.Template.Id.ToString();
        }

        private static Card FindEntityOnBoard(Board board, int entityId)
        {
            if (board.HeroFriend != null && board.HeroFriend.Id == entityId) return board.HeroFriend;
            if (board.HeroEnemy != null && board.HeroEnemy.Id == entityId) return board.HeroEnemy;
            if (board.Ability != null && board.Ability.Id == entityId) return board.Ability;
            if (board.WeaponFriend != null && board.WeaponFriend.Id == entityId) return board.WeaponFriend;
            if (board.WeaponEnemy != null && board.WeaponEnemy.Id == entityId) return board.WeaponEnemy;
            if (board.Hand != null)
                foreach (var c in board.Hand) { if (c != null && c.Id == entityId) return c; }
            if (board.MinionFriend != null)
                foreach (var c in board.MinionFriend) { if (c != null && c.Id == entityId) return c; }
            if (board.MinionEnemy != null)
                foreach (var c in board.MinionEnemy) { if (c != null && c.Id == entityId) return c; }
            return null;
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s ?? "";
            return s.Substring(0, maxLen) + "...";
        }

        private void Log(string message)
        {
            OnLog?.Invoke(message);
        }
    }
}
