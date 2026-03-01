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
            Card.Cards.VAC_933, //  VAC_933	飞行员帕奇斯
            Card.Cards.TOY_518, // 宝藏经销商 TOY_518
            Card.Cards.NX2_050, // NX2_050	错误产物
             Card.Cards.CORE_CS2_146, //  南海船工 CORE_CS2_146
            Card.Cards.CFM_325, // 蹩脚海盗 CFM_325
        };
        // 保留两张的牌
        private readonly HashSet<Card.Cards> TwoCostCards = new HashSet<Card.Cards>
        {
            Card.Cards.DRG_056, //  空降歹徒 DRG_056
        };
          // 一费海盗
        private readonly HashSet<Card.Cards> oneFeePirate = new HashSet<Card.Cards>
        {
            Card.Cards.VAC_933, //  VAC_933	飞行员帕奇斯
            Card.Cards.TOY_518, // 宝藏经销商 TOY_518
            Card.Cards.NX2_050, // NX2_050	错误产物
            Card.Cards.CFM_325, // 蹩脚海盗 CFM_325
        };
        // 伞降咒符
        private readonly HashSet<Card.Cards> parachuteInFuzhou = new HashSet<Card.Cards>
        {
            Card.Cards.VAC_925, // 伞降咒符 VAC_925
        };
        // 保留的卡
        private readonly HashSet<Card.Cards> KeepableCards = new HashSet<Card.Cards>
        {
            Card.Cards.VAC_933, //  VAC_933	飞行员帕奇斯
            Card.Cards.TOY_518, // 宝藏经销商 TOY_518
            Card.Cards.VAC_925, // 伞降咒符 VAC_925
            Card.Cards.DRG_056, //  空降歹徒 DRG_056
            Card.Cards.NX2_050, // NX2_050	错误产物
            Card.Cards.CFM_325, // 蹩脚海盗 CFM_325
            Card.Cards.GDB_333, // 太空海盗 GDB_333
        };

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            bool HasCoin = choices.Count >= 4;
            int flag1 = choices.Count(card => OneCostCards.Contains(card));
            int kuaigong = (opponentClass == Card.CClass.PALADIN || opponentClass == Card.CClass.HUNTER || opponentClass == Card.CClass.PRIEST || opponentClass == Card.CClass.ROGUE || opponentClass == Card.CClass.WARRIOR || opponentClass == Card.CClass.DEMONHUNTER) ? 1 : 0;
            int mansu = (opponentClass == Card.CClass.DRUID || opponentClass == Card.CClass.MAGE || opponentClass == Card.CClass.SHAMAN) ? 1 : 0;
            int isOneFeePirate = choices.Count(card => oneFeePirate.Contains(card));
            int isParachuteInFuzhou = choices.Count(card => parachuteInFuzhou.Contains(card));
            int DEMONHUNTER = opponentClass == Card.CClass.DEMONHUNTER ? 1 : 0;
            int MAGE = opponentClass == Card.CClass.MAGE ? 1 : 0;
            int DRUID = opponentClass == Card.CClass.DRUID ? 1 : 0;
            int PRIEST = opponentClass == Card.CClass.PRIEST ? 1 : 0;
            

            bool hasOneCostCard = choices.Any(card => OneCostCards.Contains(card));

            foreach (Card.Cards card in choices.Where(card => KeepableCards.Contains(card)))
            {
                if (isOneFeePirate > 0)
                {
                   CardsToKeep.Add(Card.Cards.DRG_056);
                   CardsToKeep.Add(Card.Cards.DRG_056);
                }
                if (PRIEST == 1 || DEMONHUNTER == 1)
                {
                    CardsToKeep.Add(Card.Cards.UNG_807); // 葛拉卡爬行蟹
                }
                // 如果是法师,则留法力燃烧
                if (opponentClass == Card.CClass.MAGE)
                {
                   CardsToKeep.Add(Card.Cards.BT_753);//BT_753	法力燃烧
                }
                // 如果有硬币,留船载火炮
                if (HasCoin)
                {
                    CardsToKeep.Add(Card.Cards.GVG_075); //  船载火炮 GVG_075 
                }
                 if(!CardsToKeep.Contains(card)){
                Keep(card);
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