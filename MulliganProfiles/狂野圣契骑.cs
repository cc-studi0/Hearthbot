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
            Card.Cards.WW_331, // 奇迹推销员 WW_331
						Card.Cards.GDB_720, // 星光漫游者 GDB_720
						Card.Cards.GDB_461, // 星域戒卫 GDB_461
						Card.Cards.BAR_873, // 圣礼骑士 BAR_873
						Card.Cards.BT_020, // 奥尔多侍从 BT_020
						Card.Cards.BOT_909, // 水晶学 BOT_909
        };
        // 2费卡
        private readonly HashSet<Card.Cards> TwoCostCards = new HashSet<Card.Cards>
        {
            Card.Cards.ETC_418, // 乐器技师 ETC_418
						Card.Cards.GDB_728, // 星际研究员 GDB_728
						Card.Cards.GDB_861, // 遇险的航天员 GDB_861
						Card.Cards.WW_391, // 淘金客 WW_391
						Card.Cards.WW_344, // 威猛银翼巨龙 WW_344
						Card.Cards.GDB_721, // 星际旅行者 GDB_721
        };
        // 3费卡
        private readonly HashSet<Card.Cards> ThreeCostCards = new HashSet<Card.Cards>
        {
            Card.Cards.TOY_882, // 装饰美术家 TOY_882
            
        };
        // 保留的卡
        private readonly HashSet<Card.Cards> KeepableCards = new HashSet<Card.Cards>
        {
            Card.Cards.WW_331, // 奇迹推销员 WW_331
						Card.Cards.GDB_720, // 星光漫游者 GDB_720
						Card.Cards.GDB_461, // 星域戒卫 GDB_461
						Card.Cards.GDB_726, // 斩星巨刃 GDB_726
						Card.Cards.BAR_873, // 圣礼骑士 BAR_873
						Card.Cards.BT_020, // 奥尔多侍从 BT_020
						Card.Cards.BOT_909, // 水晶学 BOT_909
        };

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            bool HasCoin = choices.Count >= 4;
						 // 德：DRUID 猎：HUNTER 法：MAGE 骑：PALADIN 牧：PRIEST 贼：ROGUE 萨：SHAMAN 术：WARLOCK 战：WARRIOR 瞎：DEMONHUNTER 死：DEATHKNIGHT
            int kuaigong = ( opponentClass == Card.CClass.PRIEST ||  opponentClass == Card.CClass.DEMONHUNTER|| opponentClass == Card.CClass.WARLOCK) ? 1 : 0;
            int mansu = (opponentClass == Card.CClass.DRUID  || opponentClass == Card.CClass.SHAMAN) ? 1 : 0;
						bool hasOneCostCard = choices.Any(card => OneCostCards.Contains(card));
            bool hasTwoCostCard = choices.Any(card => TwoCostCards.Contains(card));
            bool hasThreeCostCard = choices.Any(card => ThreeCostCards.Contains(card));

            // 留一费卡
            foreach (Card.Cards card in choices.Where(card => KeepableCards.Contains(card) && !CardsToKeep.Contains(card)))
            {
                Keep(card);
            }

          	//  留2费卡
						foreach (Card.Cards card in choices.Where(card => TwoCostCards.Contains(card) && hasOneCostCard && !CardsToKeep.Contains(card)))
						{
								Keep(card);
						}

            // 如果有一费卡和二费卡，留三费卡
                foreach (Card.Cards card in choices.Where(card => ThreeCostCards.Contains(card) && !CardsToKeep.Contains(card)))
						{
								Keep(card);
						}
						// 打暗牧师留精英对决 WON_049 用于解场，打德鲁伊留十字军光环 TTN_908
						if (choices.Contains(Card.Cards.WON_049)&&opponentClass == Card.CClass.PRIEST && !CardsToKeep.Contains(Card.Cards.WON_049))
						{
								Keep(Card.Cards.WON_049);
						}
						if (choices.Contains(Card.Cards.TTN_908)&&opponentClass == Card.CClass.DRUID && !CardsToKeep.Contains(Card.Cards.TTN_908)){
								Keep(Card.Cards.TTN_908);
						}
            return CardsToKeep;
        }

        public void Keep(Card.Cards card)
        {
            CardsToKeep.Add(card);
        }
    }
}
