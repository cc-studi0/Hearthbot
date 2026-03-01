using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBot.Mulligan
{
    [Serializable]
    public class DefaultMulliganProfile : MulliganProfile
    {
        List<Card.Cards> CardsToKeep = new List<Card.Cards>();

        private readonly List<Card.Cards> WorthySpells = new List<Card.Cards> {};
        // 一费卡
        private readonly HashSet<Card.Cards> OneCostCards = new HashSet<Card.Cards>
        {
            Card.Cards.RLK_039,
        };
        // 前置牌
        private readonly HashSet<Card.Cards> frontCard = new HashSet<Card.Cards>
        {
        };
        // 大哥牌 暴食巨慢，骏马，山丘，猎潮者
        private readonly HashSet<Card.Cards> kohKae = new HashSet<Card.Cards>
        {
        };
        // 先祖召唤 GVG_029
        private readonly HashSet<Card.Cards> keyCard = new HashSet<Card.Cards>
        {
        };
        // 保留的卡
        private readonly HashSet<Card.Cards> KeepableCards = new HashSet<Card.Cards>
        {
            Card.Cards.TLC_451, // 咒怨之墓 TLC_451
            Card.Cards.KAR_089, // 玛克扎尔的小鬼 KAR_089
            Card.Cards.TLC_603, // 栉龙 TLC_603
            Card.Cards.KAR_089, // 玛克扎尔的小鬼 KAR_089
            Card.Cards.CORE_BOT_568, // 莫瑞甘的灵界 CORE_BOT_568
            Card.Cards.CORE_AT_021, // 小鬼骑士 CORE_AT_021
            Card.Cards.ULD_163, //  过期货物专卖商 ULD_163
            Card.Cards.LOOT_014, //  狗头人图书管理员 LOOT_014
            Card.Cards.JAM_028, //  鲜血树人 JAM_028
            Card.Cards.AT_021, //  小鬼骑士 AT_021
            Card.Cards.WON_099, //  小鬼骑士 WON_099
        };

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            bool HasCoin = choices.Count >= 4;
            int flag1 = choices.Count(card => OneCostCards.Contains(card));
            int isFrontCard = choices.Count(card => frontCard.Contains(card));
            int isKohKae = choices.Count(card => frontCard.Contains(card));
            int isKeyCard = choices.Count(card => frontCard.Contains(card));
            int kuaigong = (opponentClass == Card.CClass.PALADIN || opponentClass == Card.CClass.HUNTER || opponentClass == Card.CClass.PRIEST || opponentClass == Card.CClass.ROGUE || opponentClass == Card.CClass.WARRIOR || opponentClass == Card.CClass.DEMONHUNTER) ? 1 : 0;
            int mansu = (opponentClass == Card.CClass.DRUID || opponentClass == Card.CClass.MAGE || opponentClass == Card.CClass.SHAMAN) ? 1 : 0;

            foreach (Card.Cards card in choices.Where(card => KeepableCards.Contains(card) && !CardsToKeep.Contains(card)))
            {
                Keep(card);

            }

            return CardsToKeep;
        }

        public void Keep(Card.Cards card)
        {
            CardsToKeep.Add(card);
        }
    }
}
