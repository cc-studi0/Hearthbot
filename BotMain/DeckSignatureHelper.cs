using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ApiCard = SmartBot.Plugins.API.Card;

namespace BotMain
{
    internal static class DeckSignatureHelper
    {
        public static string Compute(IEnumerable<ApiCard.Cards> cards)
        {
            if (cards == null)
                return string.Empty;

            var normalized = cards
                .Where(card => card != 0)
                .Select(card => card.ToString())
                .OrderBy(cardId => cardId, StringComparer.Ordinal)
                .ToArray();
            if (normalized.Length == 0)
                return string.Empty;

            var payload = string.Join("||", normalized);
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hashBytes);
        }
    }
}
