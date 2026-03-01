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
           
        };
        // 保留的卡
        private readonly HashSet<Card.Cards> KeepableCards = new HashSet<Card.Cards>
        {
            Card.Cards.CORE_ICC_038, //正义保护者 CORE_ICC_038
            Card.Cards.BOT_909, //水晶学 BOT_909
            Card.Cards.CORE_MAW_016, //法庭秩序 CORE_MAW_016
            Card.Cards.BAR_875, //逝者之剑 BAR_875
            Card.Cards.LOOT_093, //战斗号角 LOOT_093
        };

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            bool HasCoin = choices.Count >= 4;
            int isOneCostCards = choices.Count(card => OneCostCards.Contains(card));
           	int kuaigong = (opponentClass == Card.CClass.PRIEST || opponentClass == Card.CClass.DEMONHUNTER|| opponentClass == Card.CClass.ROGUE|| opponentClass == Card.CClass.SHAMAN|| opponentClass == Card.CClass.DEATHKNIGHT) ? 1 : 0;
            int mansu = (opponentClass == Card.CClass.DRUID  || opponentClass == Card.CClass.WARRIOR) ? 1 : 0;

            foreach (Card.Cards card in choices.Where(card => KeepableCards.Contains(card) && !CardsToKeep.Contains(card)))
            {
                Keep(card);
            }
						// 快攻留麦芽岩浆
						if(kuaigong>0){
							CardsToKeep.Add(Card.Cards.VAC_323);//麦芽岩浆 VAC_323
						}
						// 慢速留海拉
						if(mansu>0){
							CardsToKeep.Add(Card.Cards.TTN_850);//海拉  TTN_850
						}
            return CardsToKeep;
        }

        public void Keep(Card.Cards card)
        {
            CardsToKeep.Add(card);
        }
    }
}
