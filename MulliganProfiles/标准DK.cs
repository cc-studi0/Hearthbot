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
						// 灌注骑
            Card.Cards.EDR_816, //怪异魔蚊 EDR_816
            Card.Cards.CORE_ULD_723, //鱼人木乃伊 CORE_ULD_723
            Card.Cards.RLK_025, //冰霜打击 RLK_025
            Card.Cards.RLK_511, //寒冬先锋 RLK_511
            Card.Cards.VAC_514, //恐惧猎犬训练师 VAC_514
            Card.Cards.CORE_RLK_083, //死亡寒冰 CORE_RLK_083
            Card.Cards.VAC_436, //脆骨海盗 VAC_436
            Card.Cards.RLK_708, //堕寒男爵 RLK_708
            Card.Cards.TOY_006, //甲虫钥匙链 TOY_006
						// 污手dk
            Card.Cards.TOY_825, //小型法术尖晶石 TOY_825
            Card.Cards.EDR_813, //病变虫群 EDR_813
            Card.Cards.RLK_712, //活力分流 RLK_712
            Card.Cards.MIS_006, //玩具盗窃恶鬼 MIS_006

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
