using System;
using System.Collections.Generic;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBotProfiles
{
    public sealed class DecisionDiscoverMemoryBuildSummary
    {
        public string generated_at_utc { get; set; }
        public int source_samples { get; set; }
        public int source_memory_hits { get; set; }
        public int source_results { get; set; }
        public int global_entries { get; set; }
        public int family_entries { get; set; }
        public int archetype_entries { get; set; }
        public int deck_entries { get; set; }
    }

    public static class DecisionDiscoverMemory
    {
        public static void CaptureTeacherSample(
            Card.Cards originCard,
            List<Card.Cards> choices,
            Card.Cards pickedCard,
            Board board,
            string profileName,
            string discoverProfileName)
        {
            // Single-file SmartBot profile compilation cannot reliably share the
            // discover memory pipeline, so this degrades to a no-op.
        }

        public static bool TryPickFromMemory(
            Card.Cards originCard,
            List<Card.Cards> choices,
            Board board,
            string profileName,
            string discoverProfileName,
            out Card.Cards pickedCard)
        {
            pickedCard = default(Card.Cards);
            return false;
        }

        public static DecisionDiscoverMemoryBuildSummary Rebuild()
        {
            return new DecisionDiscoverMemoryBuildSummary
            {
                generated_at_utc = DateTime.UtcNow.ToString("o"),
                source_samples = 0,
                source_memory_hits = 0,
                source_results = 0,
                global_entries = 0,
                family_entries = 0,
                archetype_entries = 0,
                deck_entries = 0,
            };
        }
    }
}
