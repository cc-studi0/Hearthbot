using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBotProfiles
{
    /// <summary>
    /// 学习模式专用 Profile：为 SearchEngine 提供默认参数，
    /// 使本地 AI 能正常搜索出牌/攻击/技能等动作，避免因 param==null
    /// 退化到 SimpleAI（只会无脑英雄技能）。
    ///
    /// 不含任何卡组特化逻辑，仅提供通用基线参数，
    /// 学习权重由 LearnedStrategyCoordinator 通过 parameterMutator 注入。
    /// </summary>
    [Serializable]
    public class LearnProfile : Profile
    {
        private const Card.Cards TheCoin = Card.Cards.GAME_005;

        public ProfileParameters GetParameters(Board board)
        {
            var p = new ProfileParameters(BaseProfile.Rush)
            {
                DiscoverSimulationValueThresholdPercent = -10
            };

            // 将手牌（除幸运币）加入重新思考队列，每打出一张牌后重算，减少顺序错误
            if (board?.Hand != null && board.Hand.Count > 0)
            {
                var unique = new HashSet<Card.Cards>();
                foreach (var c in board.Hand)
                {
                    if (c?.Template == null) continue;
                    var id = c.Template.Id;
                    if (id == TheCoin) continue;
                    if (!unique.Add(id)) continue;
                    p.ForcedResimulationCardList.Add(id);
                }

                if (unique.Count > 0)
                    p.ForceResimulation = true;
            }

            return p;
        }

        public Card.Cards SirFinleyChoice(List<Card.Cards> choices)
        {
            return choices[0];
        }

        public Card.Cards KazakusChoice(List<Card.Cards> choices)
        {
            return choices[0];
        }
    }
}
