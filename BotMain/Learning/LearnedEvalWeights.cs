using System;
using System.Collections.Generic;

namespace BotMain.Learning
{
    public enum EvalBucketPhase { Early, Mid, Late }
    public enum EvalBucketPosture { Ahead, Even, Behind }

    public readonly struct EvalBucketKey : IEquatable<EvalBucketKey>
    {
        public EvalBucketPhase Phase { get; }
        public EvalBucketPosture HpPosture { get; }
        public EvalBucketPosture BoardPosture { get; }

        public EvalBucketKey(EvalBucketPhase phase, EvalBucketPosture hpPosture, EvalBucketPosture boardPosture)
        {
            Phase = phase;
            HpPosture = hpPosture;
            BoardPosture = boardPosture;
        }

        public string ToKey() => $"{Phase}|{HpPosture}|{BoardPosture}";

        public static EvalBucketKey FromBoardState(int turn, int friendHp, int enemyHp, int friendMinions, int enemyMinions)
        {
            var phase = turn <= 4 ? EvalBucketPhase.Early : turn <= 7 ? EvalBucketPhase.Mid : EvalBucketPhase.Late;
            var hpPosture = (friendHp - enemyHp) > 10 ? EvalBucketPosture.Ahead
                : (enemyHp - friendHp) > 10 ? EvalBucketPosture.Behind
                : EvalBucketPosture.Even;
            var boardPosture = friendMinions > enemyMinions + 1 ? EvalBucketPosture.Ahead
                : enemyMinions > friendMinions + 1 ? EvalBucketPosture.Behind
                : EvalBucketPosture.Even;
            return new EvalBucketKey(phase, hpPosture, boardPosture);
        }

        public bool Equals(EvalBucketKey other) => Phase == other.Phase && HpPosture == other.HpPosture && BoardPosture == other.BoardPosture;
        public override bool Equals(object obj) => obj is EvalBucketKey k && Equals(k);
        public override int GetHashCode() => HashCode.Combine(Phase, HpPosture, BoardPosture);
    }

    public sealed class EvalWeightSet
    {
        public float FaceBiasScale { get; set; } = 1.0f;
        public float BoardControlScale { get; set; } = 1.0f;
        public float TempoPenaltyScale { get; set; } = 1.0f;
        public float HandValueScale { get; set; } = 1.0f;
        public float HeroPowerBonusScale { get; set; } = 1.0f;
        public int SampleCount { get; set; }
    }

    public sealed class LearnedEvalWeights
    {
        private const float Alpha = 0.02f;
        private readonly object _sync = new object();
        private readonly Dictionary<string, EvalWeightSet> _buckets = new(StringComparer.Ordinal);

        public void Update(EvalBucketKey bucket, ActionPatternSignals signals)
        {
            lock (_sync)
            {
                var key = bucket.ToKey();
                if (!_buckets.TryGetValue(key, out var weights))
                {
                    weights = new EvalWeightSet();
                    _buckets[key] = weights;
                }

                weights.FaceBiasScale = Ema(weights.FaceBiasScale, 1.0f + (float)(signals.FaceDamageRatio - 0.5) * 0.6f);

                var tradeRatio = signals.AttackCount > 0 ? (float)signals.TradeAttackCount / signals.AttackCount : 0.5f;
                weights.BoardControlScale = Ema(weights.BoardControlScale, 1.0f + (tradeRatio - 0.5f) * 0.6f);

                weights.TempoPenaltyScale = Ema(weights.TempoPenaltyScale, 1.0f + (float)(signals.ManaEfficiency - 0.5) * 0.6f);

                weights.HandValueScale = Ema(weights.HandValueScale, 1.0f + (float)(0.5 - signals.PlayRatio) * 0.4f);

                weights.HeroPowerBonusScale = Ema(weights.HeroPowerBonusScale, signals.UsedHeroPower ? 1.15f : 0.95f);

                weights.SampleCount++;
            }
        }

        public bool TryGet(EvalBucketKey bucket, out EvalWeightSet weights)
        {
            lock (_sync)
            {
                return _buckets.TryGetValue(bucket.ToKey(), out weights);
            }
        }

        public Dictionary<string, EvalWeightSet> GetAll()
        {
            lock (_sync)
            {
                return new Dictionary<string, EvalWeightSet>(_buckets);
            }
        }

        public void Load(Dictionary<string, EvalWeightSet> data)
        {
            lock (_sync)
            {
                _buckets.Clear();
                foreach (var kv in data)
                    _buckets[kv.Key] = kv.Value;
            }
        }

        private static float Ema(float current, float observed)
        {
            return Alpha * observed + (1f - Alpha) * current;
        }
    }
}
