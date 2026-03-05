using System;

namespace BotMain.AI
{
    public sealed class SearchDiagnostics
    {
        public int DepthReached { get; set; }
        public int BeamWidthMin { get; set; }
        public int BeamWidthMax { get; set; }
        public int CandidatesEvaluated { get; set; }
        public int ExpandedNodes { get; set; }
        public int ProfileBlocked { get; set; }
        public int HeuristicPruned { get; set; }
        public int TranspositionPruned { get; set; }
        public int FutilityPruned { get; set; }
        public int TacticalKept { get; set; }
        public int PvKeepCount { get; set; }
        public int MergedStateCount { get; set; }
        public int DuplicateActions { get; set; }
        public int CacheHit { get; set; }
        public long ElapsedMs { get; set; }

        public SearchDiagnostics Clone()
        {
            return (SearchDiagnostics)MemberwiseClone();
        }

        public override string ToString()
        {
            return $"depth={DepthReached}, eval={CandidatesEvaluated}, expand={ExpandedNodes}, " +
                   $"profileBlocked={ProfileBlocked}, heuristicPruned={HeuristicPruned}, " +
                   $"transpositionPruned={TranspositionPruned}, futilityPruned={FutilityPruned}, " +
                   $"tacticalKept={TacticalKept}, pvKeep={PvKeepCount}, merged={MergedStateCount}, " +
                   $"dup={DuplicateActions}, cacheHit={CacheHit}, beam={BeamWidthMin}-{BeamWidthMax}, t={ElapsedMs}ms";
        }
    }
}
