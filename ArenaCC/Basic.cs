using System.Collections.Generic;
using SmartBot.Plugins.API;
using SmartBot.Arena;

namespace Hearthbot.ArenaProfiles
{
    /// <summary>
    /// 最简竞技场选牌策略：永远选第一项。
    /// 既适用于英雄选择也适用于卡牌选择，主要作为没有任何外部决策源（盒子、HSReplay 等）
    /// 时的保底策略；选英雄时建议改用 BotService 内置的 Priority 表，会比这个智能。
    /// </summary>
    public class BasicArenaPickHandler : ArenaPickHandler
    {
        public Card.Cards HandlePickDecision(
            Card.CClass heroClass,
            List<Card.Cards> deck,
            Card.Cards choiceOne,
            Card.Cards choiceTwo,
            Card.Cards choiceThree)
        {
            return choiceOne;
        }
    }
}
