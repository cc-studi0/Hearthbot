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
        // 一费海盗
        private readonly HashSet<Card.Cards> oneFeePirate = new HashSet<Card.Cards>
        {
            Card.Cards.MIS_710, // 滑矛布袋手偶 MIS_710
            Card.Cards.WW_407, // 焦渴的亡命徒 WW_407
            Card.Cards.CORE_BT_351, // 战斗邪犬 CORE_BT_351
            Card.Cards.EDR_469, // 沉睡的林精 EDR_469
            Card.Cards.TIME_444, // 迷时战刃 TIME_444
        };
        // 伞降咒符
        private readonly HashSet<Card.Cards> parachuteInFuzhou = new HashSet<Card.Cards>
        {
            Card.Cards.VAC_925, // 伞降咒符 VAC_925
        };
        // 滑矛布袋手偶
        private readonly HashSet<Card.Cards> slipperySpearBagPuppet = new HashSet<Card.Cards>
        {
            Card.Cards.MIS_710, //滑矛布袋手偶 MIS_710
        };
        // 保留的卡
        private readonly HashSet<Card.Cards> KeepableCards = new HashSet<Card.Cards>
        {
            Card.Cards.MIS_710, // 滑矛布袋手偶 MIS_710
            Card.Cards.TOY_028, // 团队之灵 TOY_028
            // Card.Cards.TOY_330t12, //奇利亚斯豪华版3000型 TOY_330t12
            Card.Cards.DEEP_014, // 疾速矿锄 DEEP_014
            Card.Cards.CORE_BT_351, // 战斗邪犬 CORE_BT_351
            Card.Cards.VAC_512, // 心灵按摩师 VAC_512
            Card.Cards.GDB_473, // 猎头 GDB_473
            Card.Cards.CORE_BAR_330, // 獠牙锥刃 CORE_BAR_330
            Card.Cards.VAC_933, // 飞行员帕奇斯 VAC_933
            Card.Cards.EDR_840, // 恐怖收割 EDR_840
            Card.Cards.GDB_471, // 沃罗尼招募官 GDB_471
            Card.Cards.TOY_517, // 泼漆彩鳍鱼人 TOY_517
            // Card.Cards.GDB_117, // 蒂尔德拉，反抗军头目 GDB_117
            // Card.Cards.TOY_642, //  球霸野猪人 TOY_642
            Card.Cards.EDR_820, //  飞龙之眠 EDR_820
            Card.Cards.CORE_YOP_001, // 伊利达雷研习 CORE_YOP_001
            Card.Cards.FIR_929, // 活体烈焰 FIR_929
            Card.Cards.TLC_833, // 昆虫利爪 TLC_833
            Card.Cards.TLC_427, // 抛石鱼人 TLC_427
            Card.Cards.TLC_254, // 讲故事的始祖龟 TLC_254
            Card.Cards.TLC_840, // 格里什掘洞虫 TLC_840
            Card.Cards.EDR_469, // 沉睡的林精 EDR_469
            Card.Cards.TIME_444, // 迷时战刃 TIME_444
        };

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            bool HasCoin = choices.Count >= 4;
            int isOneFeePirate = choices.Count(card => oneFeePirate.Contains(card));
            int isParachuteInFuzhou = choices.Count(card => parachuteInFuzhou.Contains(card));
            int isSlipperySpearBagPuppet = choices.Count(card => slipperySpearBagPuppet.Contains(card));
            int isKeepableCards = choices.Count(card => KeepableCards.Contains(card));
            int isSouthseaCaptain = choices.Count(card => Card.Cards.CORE_NEW1_027 == card);
            int isHozenRoughhouser = choices.Count(card => Card.Cards.VAC_938 == card);
// 德：DRUID 猎：HUNTER 法：MAGE 骑：PALADIN 牧：PRIEST 贼：ROGUE 萨：SHAMAN 术：WARLOCK 战：WARRIOR 瞎：DEMONHUNTER DEATHKNIGHT)
						int kuaigong = (opponentClass == Card.CClass.DEMONHUNTER|| opponentClass == Card.CClass.SHAMAN|| opponentClass == Card.CClass.MAGE) ? 1 : 0;
						int mansu = (opponentClass == Card.CClass.WARLOCK ||opponentClass == Card.CClass.DRUID || opponentClass == Card.CClass.WARRIOR|| opponentClass == Card.CClass.DEATHKNIGHT) ? 1 : 0;

            foreach (Card.Cards card in choices.Where(card => KeepableCards.Contains(card) && !CardsToKeep.Contains(card)))
            {
                Keep(card);
            }

						// if (kuaigong>0)
						// {
						// 		Keep(Card.Cards.VAC_414);//炽热火炭 VAC_414
						// }
						// 如果是后手，可以留下2费焦渴的亡命徒、3费虚灵神谕者。
						if (HasCoin){
								Keep(Card.Cards.WW_407);//焦渴的亡命徒 WW_407
								Keep(Card.Cards.TOY_330t12);//奇利亚斯豪华版3000型 TOY_330t12
						}
						// 突破邪火 JAM_017 如果有一费随从或者焦渴的亡命徒，可以留下。
						if (isOneFeePirate>0){
								Keep(Card.Cards.JAM_017);//突破邪火 JAM_017
						}


            return CardsToKeep;
        }

        public void Keep(Card.Cards card)
        {
            CardsToKeep.Add(card);
        }
    }
}
