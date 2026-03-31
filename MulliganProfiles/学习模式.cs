using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBot.Mulligan
{
    /// <summary>
    /// 学习模式专用留牌策略：通用费用曲线留牌，不针对特定卡组。
    /// 留低费（1-3费随从/武器），扔高费，有币时放宽到4费。
    /// </summary>
    [Serializable]
    public class LearnMulliganProfile : MulliganProfile
    {
        private List<Card.Cards> _keep = new List<Card.Cards>();

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass,
            Card.CClass ownClass)
        {
            _keep = new List<Card.Cards>();
            var hasCoin = choices.Count >= 4;

            int kept1 = 0, kept2 = 0, kept3 = 0, kept4 = 0;

            foreach (var id in choices)
            {
                var card = CardTemplate.LoadFromId(id);
                if (card == null) continue;
                var cost = card.Cost;

                // 1费：留1张，随从/武器/有身材的
                if (cost == 1 && kept1 < 1 && IsPlayable(card))
                {
                    Keep(id);
                    kept1++;
                    continue;
                }

                // 2费：留1张
                if (cost == 2 && kept2 < 1 && IsPlayable(card))
                {
                    Keep(id);
                    kept2++;
                    continue;
                }

                // 3费：有2费时才留，最多1张
                if (cost == 3 && kept3 < 1 && kept2 > 0 && IsPlayable(card))
                {
                    Keep(id);
                    kept3++;
                    continue;
                }

                // 4费：有币+有2费时才留，最多1张
                if (cost == 4 && kept4 < 1 && hasCoin && kept2 > 0 && card.Type == Card.CType.MINION)
                {
                    Keep(id);
                    kept4++;
                    continue;
                }
            }

            return _keep;
        }

        private void Keep(Card.Cards id)
        {
            _keep.Add(id);
        }

        private static bool IsPlayable(CardTemplate card)
        {
            return card.Type == Card.CType.MINION
                || card.Type == Card.CType.WEAPON
                || card.Type == Card.CType.LOCATION;
        }
    }
}
