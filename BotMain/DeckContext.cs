using System;
using System.Collections.Generic;
using ApiCard = SmartBot.Plugins.API.Card;

namespace BotMain
{
    internal sealed class DeckContext
    {
        public string DeckName { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public IReadOnlyList<ApiCard.Cards> FullDeckCards { get; set; } = Array.Empty<ApiCard.Cards>();
        public IReadOnlyList<ApiCard.Cards> RemainingDeckCards { get; set; } = Array.Empty<ApiCard.Cards>();
    }
}
