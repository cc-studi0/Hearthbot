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
            Card.Cards.CORE_UNG_205, //冰川裂片 CORE_UNG_205
            Card.Cards.CORE_UNG_809, //火羽精灵 CORE_UNG_809
            Card.Cards.TLC_249, //炽烈烬火 TLC_249
            Card.Cards.CORE_UNG_018, // 烈焰喷涌 CORE_UNG_018
            Card.Cards.CORE_DRG_107, //紫罗兰魔翼鸦 CORE_DRG_107
            Card.Cards.FIR_929, //活体烈焰 FIR_929
            Card.Cards.GDB_302, //吸积炽焰 GDB_302
            Card.Cards.TLC_226, //咒术图书管理员 TLC_226

        };

        // 二费以上关键牌
        private readonly HashSet<Card.Cards> KeyCards = new HashSet<Card.Cards>
        {

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
						// 如果有硬币和苦花骑士 EDR_852时,留龙鳞军备 EDR_251
						if (hasCoin && choices.Contains(Card.Cards.EDR_852) && choices.Contains(Card.Cards.EDR_251))
						{
								Keep(Card.Cards.EDR_251); //龙鳞军备 EDR_251
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
