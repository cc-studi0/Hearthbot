using System;
using Xunit;
using BotMain.Learning;

namespace BotCore.Tests.Learning
{
    public class ConsistencyTrackerTests
    {
        [Fact]
        public void RecordMatch_ReturnsCorrectRate()
        {
            var tracker = new ConsistencyTracker(windowSize: 5);
            tracker.Record(ConsistencyDimension.Action, true);
            tracker.Record(ConsistencyDimension.Action, true);
            tracker.Record(ConsistencyDimension.Action, false);

            var rate = tracker.GetRate(ConsistencyDimension.Action);
            Assert.True(Math.Abs(rate - 66.67) < 0.1);
        }

        [Fact]
        public void SlidingWindow_EvictsOldEntries()
        {
            var tracker = new ConsistencyTracker(windowSize: 3);
            tracker.Record(ConsistencyDimension.Action, false);
            tracker.Record(ConsistencyDimension.Action, true);
            tracker.Record(ConsistencyDimension.Action, true);
            tracker.Record(ConsistencyDimension.Action, true);

            Assert.Equal(100.0, tracker.GetRate(ConsistencyDimension.Action));
        }

        [Fact]
        public void EmptyTracker_ReturnsZero()
        {
            var tracker = new ConsistencyTracker(windowSize: 200);
            Assert.Equal(0.0, tracker.GetRate(ConsistencyDimension.Action));
        }

        [Fact]
        public void MultipleDimensions_TrackIndependently()
        {
            var tracker = new ConsistencyTracker(windowSize: 100);
            tracker.Record(ConsistencyDimension.Action, true);
            tracker.Record(ConsistencyDimension.Mulligan, false);

            Assert.Equal(100.0, tracker.GetRate(ConsistencyDimension.Action));
            Assert.Equal(0.0, tracker.GetRate(ConsistencyDimension.Mulligan));
        }

        [Fact]
        public void TotalCount_TracksAllRecords()
        {
            var tracker = new ConsistencyTracker(windowSize: 100);
            tracker.Record(ConsistencyDimension.Action, true);
            tracker.Record(ConsistencyDimension.Action, false);
            tracker.Record(ConsistencyDimension.Action, true);

            Assert.Equal(3, tracker.GetTotalCount(ConsistencyDimension.Action));
        }

        [Fact]
        public void GetSnapshot_ReturnsAllDimensions()
        {
            var tracker = new ConsistencyTracker(windowSize: 100);
            tracker.Record(ConsistencyDimension.Action, true);
            tracker.Record(ConsistencyDimension.Mulligan, false);
            tracker.Record(ConsistencyDimension.Choice, true);

            var snap = tracker.GetSnapshot();
            Assert.Equal(100.0, snap.ActionRate);
            Assert.Equal(0.0, snap.MulliganRate);
            Assert.Equal(100.0, snap.ChoiceRate);
        }
    }
}
