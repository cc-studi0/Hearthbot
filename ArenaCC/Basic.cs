using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SmartBot.Plugins.API;
using SmartBot.Arena;
using SmartBotUI;
using SmartBotUI.Settings;

namespace SmartBotUI.Arena
{
	public class BasicArenaPickHandler : ArenaPickHandler
    {
        public Card.Cards HandlePickDecision(Card.CClass heroClass, List<Card.Cards> deck, Card.Cards choiceOne, Card.Cards choiceTwo,
            Card.Cards choiceThree)
        {
            return choiceOne;
        }
    }
}