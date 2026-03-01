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
        private List<Card.Cards> CardsToKeep = new List<Card.Cards>();

        // 核心一费随从
        private readonly HashSet<Card.Cards> CoreOneCostMinions = new HashSet<Card.Cards>
        {
            Card.Cards.WW_331, // 奇迹推销员 WW_331
            Card.Cards.WW_344, //威猛银翼巨龙 WW_344
            Card.Cards.DEEP_017, //   采矿事故 DEEP_017
            Card.Cards.VAC_955, //  戈贡佐姆 VAC_955
            Card.Cards.CORE_ULD_723, //鱼人木乃伊 CORE_ULD_723
            Card.Cards.CORE_GVG_061, //  作战动员 CORE_GVG_061
            Card.Cards.ETC_318, //  布吉舞乐 ETC_318
            Card.Cards.VAC_958, //  VAC_958	进化融合怪
						// 灌注骑
             Card.Cards.EDR_852, //苦花骑士 EDR_852
            Card.Cards.EDR_451, //金萼幼龙 EDR_451
            Card.Cards.EDR_800, //振翅守卫 EDR_800
            // Card.Cards.EDR_264, //圣光护盾 EDR_264
            Card.Cards.CORE_DMF_194, //CORE_DMF_194	赤鳞驯龙者
						Card.Cards.CORE_ICC_038, //正义保护者 CORE_ICC_038
						// 鱼人骑
						Card.Cards.EDR_999, // 啮齿绿鳍鱼人 EDR_999
						Card.Cards.TLC_426, // 潜入葛拉卡 TLC_426
						Card.Cards.TLC_427, // 抛石鱼人 TLC_427
						Card.Cards.TLC_438, // 紫色珍鳃鱼人 TLC_438
						Card.Cards.CORE_ULD_723, // 鱼人木乃伊 CORE_ULD_723
						Card.Cards.TLC_428, // 温泉踏浪鱼人 TLC_428
						Card.Cards.CORE_EX1_506, // 鱼人猎潮者 CORE_EX1_506
						Card.Cards.CORE_EX1_509, // 鱼人招潮者 CORE_EX1_509
						Card.Cards.CORE_DMF_067, // 奖品商贩 CORE_DMF_067
						Card.Cards.VAC_958, // 进化融合怪 VAC_958
						// 铺场骑
						Card.Cards.CORE_TSC_827, // 凶恶的滑矛纳迦 CORE_TSC_827
						Card.Cards.EDR_469, // 沉睡的林精 EDR_469
            Card.Cards.CORE_UNG_809, //火羽精灵 CORE_UNG_809
            Card.Cards.CORE_CS2_231, //小精灵 CORE_CS2_231
						/* 圣盾骑
						强光护卫 TIME_015
						疯狂生物 EDR_105
						永恒雏龙 TIME_045
						坦克机械师 TIME_017
						*/ 
						Card.Cards.TIME_015, // 强光护卫 TIME_015
						Card.Cards.EDR_105, // 疯狂生物 EDR
						Card.Cards.TIME_045, // 永恒雏龙 TIME_045
						Card.Cards.TIME_017, // 坦克机械师 TIME_017
        };

        // 二费以上关键牌
        private readonly HashSet<Card.Cards> KeyCards = new HashSet<Card.Cards>
        {
            Card.Cards.WORK_002,       // 忙碌机器人
            Card.Cards.EDR_105,        // 疯狂生物
            Card.Cards.CORE_GVG_061,   // 作战动员
            // Card.Cards.EDR_253, //巨熊之槌 EDR_253
            Card.Cards.CORE_REV_308,        // CORE_REV_308	迷宫向导
            Card.Cards.ULD_191,        // 欢快的同伴 Beaming Sidekick ID：ULD_191 
            Card.Cards.TLC_603,        // 栉龙 TLC_603
            Card.Cards.VAC_532,        // 椰子火炮手 VAC_532
        };

        // 根据对手留牌
        private readonly Dictionary<Card.CClass, HashSet<Card.Cards>> MatchupKeeps = 
            new Dictionary<Card.CClass, HashSet<Card.Cards>>
        {
            {
                Card.CClass.HUNTER,
                new HashSet<Card.Cards>
                {
                    Card.Cards.CORE_ICC_038,   // 正义保护者(抗快攻)
                    Card.Cards.WORK_002,       // 忙碌机器人
                }
            },
            {
                Card.CClass.WARRIOR,
                new HashSet<Card.Cards>
                {
                    Card.Cards.CORE_GVG_061,   // 作战动员(打防战)
                }
            }
        };

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            bool hasCoin = choices.Count >= 4;
            CardsToKeep.Clear();

            // 1. 核心一费随从
            foreach(var card in choices.Where(x => CoreOneCostMinions.Contains(x)))
            {
                Keep(card);
            }

            // 2. 处理关键牌
            foreach(var card in choices.Where(x => KeyCards.Contains(x)))
            {
                var cardCost = CardTemplate.LoadFromId(card).Cost;
                // 有硬币可以留3费以下,没硬币留2费以下
                if(hasCoin && cardCost <= 3 || !hasCoin && cardCost <= 2)
                {
                    Keep(card);
                }
            }

            // 3. 根据对手额外留牌
            if(MatchupKeeps.ContainsKey(opponentClass))
            {
                foreach(var card in choices.Where(x => 
                    MatchupKeeps[opponentClass].Contains(x) && 
                    !CardsToKeep.Contains(x)))
                {
                    Keep(card);
                }
            }

            // 4. 特殊处理
            // 保证至少有一费随从
            if(!CardsToKeep.Any(x => CardTemplate.LoadFromId(x).Cost == 1))
            {
                var availableOne = choices.FirstOrDefault(x => 
                    CardTemplate.LoadFromId(x).Cost == 1 && 
                    !CardsToKeep.Contains(x));
                if(availableOne != null)
                {
                    Keep(availableOne);
                }
            }

            return CardsToKeep;
        }

        private void Keep(Card.Cards id)
        {
            if (!CardsToKeep.Contains(id))
                CardsToKeep.Add(id);
        }
    }
}
