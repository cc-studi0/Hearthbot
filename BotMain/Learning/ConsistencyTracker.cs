using System;
using System.Collections.Generic;

namespace BotMain.Learning
{
    public enum ConsistencyDimension
    {
        Action,
        Mulligan,
        Choice
    }

    public sealed class ConsistencyTracker
    {
        private readonly int _windowSize;
        private readonly object _sync = new object();
        private readonly Dictionary<ConsistencyDimension, Queue<bool>> _windows = new();
        private readonly Dictionary<ConsistencyDimension, int> _windowMatchCount = new();
        private readonly Dictionary<ConsistencyDimension, long> _totalCounts = new();

        public ConsistencyTracker(int windowSize = 200)
        {
            _windowSize = Math.Max(1, windowSize);
            foreach (ConsistencyDimension dim in Enum.GetValues(typeof(ConsistencyDimension)))
            {
                _windows[dim] = new Queue<bool>();
                _windowMatchCount[dim] = 0;
                _totalCounts[dim] = 0;
            }
        }

        public void Record(ConsistencyDimension dimension, bool isMatch)
        {
            lock (_sync)
            {
                var queue = _windows[dimension];
                queue.Enqueue(isMatch);
                if (isMatch) _windowMatchCount[dimension]++;
                _totalCounts[dimension]++;

                while (queue.Count > _windowSize)
                {
                    var evicted = queue.Dequeue();
                    if (evicted) _windowMatchCount[dimension]--;
                }
            }
        }

        public double GetRate(ConsistencyDimension dimension)
        {
            lock (_sync)
            {
                var queue = _windows[dimension];
                if (queue.Count == 0) return 0.0;
                return Math.Round(_windowMatchCount[dimension] * 100.0 / queue.Count, 2);
            }
        }

        public int GetWindowCount(ConsistencyDimension dimension)
        {
            lock (_sync) { return _windows[dimension].Count; }
        }

        public long GetTotalCount(ConsistencyDimension dimension)
        {
            lock (_sync) { return _totalCounts[dimension]; }
        }

        public ConsistencySnapshot GetSnapshot()
        {
            lock (_sync)
            {
                return new ConsistencySnapshot
                {
                    ActionRate = GetRate_NoLock(ConsistencyDimension.Action),
                    MulliganRate = GetRate_NoLock(ConsistencyDimension.Mulligan),
                    ChoiceRate = GetRate_NoLock(ConsistencyDimension.Choice),
                    ActionCount = _windows[ConsistencyDimension.Action].Count,
                    MulliganCount = _windows[ConsistencyDimension.Mulligan].Count,
                    ChoiceCount = _windows[ConsistencyDimension.Choice].Count,
                    TotalActions = _totalCounts[ConsistencyDimension.Action],
                    TotalMulligans = _totalCounts[ConsistencyDimension.Mulligan],
                    TotalChoices = _totalCounts[ConsistencyDimension.Choice],
                };
            }
        }

        private double GetRate_NoLock(ConsistencyDimension dimension)
        {
            var queue = _windows[dimension];
            if (queue.Count == 0) return 0.0;
            return Math.Round(_windowMatchCount[dimension] * 100.0 / queue.Count, 2);
        }
    }

    public sealed class ConsistencySnapshot
    {
        public double ActionRate { get; set; }
        public double MulliganRate { get; set; }
        public double ChoiceRate { get; set; }
        public int ActionCount { get; set; }
        public int MulliganCount { get; set; }
        public int ChoiceCount { get; set; }
        public long TotalActions { get; set; }
        public long TotalMulligans { get; set; }
        public long TotalChoices { get; set; }
    }
}
