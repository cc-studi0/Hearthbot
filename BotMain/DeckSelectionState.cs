using System;
using System.Collections.Generic;
using System.Linq;

namespace BotMain
{
    internal static class DeckSelectionState
    {
        internal static IReadOnlyList<string> Normalize(IEnumerable<string> selectedDecks, string legacyDeckName = null)
        {
            var normalized = (selectedDecks ?? Enumerable.Empty<string>())
                .Where(deck => !string.IsNullOrWhiteSpace(deck))
                .Select(deck => deck.Trim())
                .Where(deck => !string.Equals(deck, "(auto)", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalized.Count > 0)
                return normalized;

            if (!string.IsNullOrWhiteSpace(legacyDeckName)
                && !string.Equals(legacyDeckName.Trim(), "(auto)", StringComparison.OrdinalIgnoreCase))
                return new[] { legacyDeckName.Trim() };

            return Array.Empty<string>();
        }

        internal static string BuildSummary(IReadOnlyList<string> selectedDecks)
        {
            var count = selectedDecks?.Count ?? 0;
            if (count <= 0)
                return "(auto)";

            if (count == 1)
                return selectedDecks[0];

            return $"随机({count})";
        }

        internal static string ChooseActiveDeck(IReadOnlyList<string> selectedDecks, Random random)
        {
            var normalized = Normalize(selectedDecks);
            if (normalized.Count <= 0)
                return "(auto)";

            if (normalized.Count == 1)
                return normalized[0];

            var resolvedRandom = random ?? new Random();
            return normalized[resolvedRandom.Next(normalized.Count)];
        }
    }
}
