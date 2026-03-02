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
					/*
					烈焰小鬼 CORE_EX1_319
					礼盒雏龙 TOY_386
					跳虫 SC_010
					恐惧猎犬训练师 VAC_514
					暮光时空跃迁者 TIME_025
					超光子弹幕 TIME_027
					鲜花商贩 EDR_889
					*/ 

            Card.Cards.CORE_EX1_319, //烈焰小鬼 CORE_EX1_319
						Card.Cards.TOY_386, //礼盒雏龙 TOY_386
						Card.Cards.SC_010, //跳虫 SC_010
						Card.Cards.VAC_514, //恐惧猎犬训练师 VAC_514
						Card.Cards.TIME_025, //暮光时空跃迁者 TIME_025
						Card.Cards.TIME_027, //超光子弹幕 TIME_027
						Card.Cards.EDR_889, //鲜花商贩 EDR_889
				};

        // 二费以上关键牌
        private readonly HashSet<Card.Cards> KeyCards = new HashSet<Card.Cards>
        {
            Card.Cards.WORK_002,       // 忙碌机器人
            Card.Cards.EDR_105,        // 疯狂生物
            Card.Cards.CORE_GVG_061,   // 作战动员
            Card.Cards.EDR_253, //巨熊之槌 EDR_253
            Card.Cards.CORE_REV_308,        // CORE_REV_308	迷宫向导
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
