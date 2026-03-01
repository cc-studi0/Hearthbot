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
            Card.Cards.BAR_546, //野火 BAR_546
            Card.Cards.EDR_804, //巫卜 EDR_804
            Card.Cards.EDR_871, //灵体采集者 EDR_871
            Card.Cards.EDR_852, //苦花骑士 EDR_852
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
