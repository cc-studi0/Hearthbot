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
            Card.Cards.TOY_507, // 童话林地 TOY_507
            Card.Cards.DAL_052, // 泥沼变形怪 DAL_052
        };
        // 大哥牌 暴食巨慢，骏马，山丘，猎潮者
        private readonly HashSet<Card.Cards> kohKae = new HashSet<Card.Cards>
        {
            Card.Cards.TSC_639, // 暴食巨鳗格拉格 TSC_639
            Card.Cards.WW_440, // 奔雷骏马 WW_440
            Card.Cards.TID_712, // 猎潮者耐普图隆 TID_712
            Card.Cards.WW_382, // 步移山丘 WW_382
        };
        // 先祖召唤 GVG_029
        private readonly HashSet<Card.Cards> keyCard = new HashSet<Card.Cards>
        {
            Card.Cards.TSC_639, // 先祖召唤 GVG_029
        };
        // 保留的卡
        private readonly HashSet<Card.Cards> KeepableCards = new HashSet<Card.Cards>
        {
            Card.Cards.TOY_507, // 童话林地 TOY_507
            Card.Cards.DAL_052, // 泥沼变形怪 DAL_052
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
                // 如果手上有【童话林地】或者【泥沼变形怪】，还可留下【拍卖行木槌】。
                if (isFrontCard > 0)
                {
                    CardsToKeep.Add(Card.Cards.SW_025);//拍卖行木槌 SW_025
                }
                // 或者起手有大哥（暴食巨慢，骏马，山丘，猎潮者），可留下【先祖召唤】
                if(isKohKae > 0)
                {
                    CardsToKeep.Add(Card.Cards.GVG_029);//先祖召唤 GVG_029
                }
                // 如果有先祖召唤,留大哥
                if(isKeyCard > 0)
                {
                    CardsToKeep.Add(Card.Cards.TSC_639);//暴食巨鳗格拉格 TSC_639
                    CardsToKeep.Add(Card.Cards.WW_440);//奔雷骏马 WW_440
                    CardsToKeep.Add(Card.Cards.WW_382);//步移山丘 WW_382
                    CardsToKeep.Add(Card.Cards.TID_712);//猎潮者耐普图隆 TID_712
								}
								// 如果对面是快攻,留退化
								if (kuaigong > 0)
								{
										CardsToKeep.Add(Card.Cards.JAM_013);//即兴演奏 JAM_013
										CardsToKeep.Add(Card.Cards.CFM_696);// 衰变 CFM_696
										CardsToKeep.Add(Card.Cards.TOY_508);// 立体书  TOY_508
										CardsToKeep.Add(Card.Cards.TSC_631);// 鱼群聚集 TSC_631
								}

            }

            return CardsToKeep;
        }

        public void Keep(Card.Cards card)
        {
            CardsToKeep.Add(card);
        }
    }
}
