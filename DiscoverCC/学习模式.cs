using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Discover;
using SmartBot.Plugins.API;

namespace Discover
{
    /// <summary>
    /// 学习模式专用发现策略：通用评分逻辑，不针对特定卡组。
    /// 优先选费用合适、身材好、有关键词的卡。
    /// </summary>
    public class LearnDiscoverHandler : DiscoverPickHandler
    {
        public Card.Cards HandlePickDecision(Card.Cards originCard, List<Card.Cards> choices, Board board)
        {
            if (choices == null || choices.Count == 0)
                return choices[0];

            Card.Cards best = choices[0];
            double bestScore = double.MinValue;

            int mana = 10;
            try { mana = board != null ? board.ManaAvailable : 10; } catch { }

            foreach (var id in choices)
            {
                double score = ScoreCard(id, mana, board);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = id;
                }
            }

            return best;
        }

        private static double ScoreCard(Card.Cards id, int mana, Board board)
        {
            CardTemplate card;
            try { card = CardTemplate.LoadFromId(id); }
            catch { return 0; }
            if (card == null) return 0;

            double score = 0;

            // 费用适配：能立刻打出的牌更有价值
            if (card.Cost <= mana)
                score += 15;
            else if (card.Cost <= mana + 2)
                score += 5;

            // 基础身材评分
            score += card.Atk * 1.0;
            score += card.Health * 1.0;

            // 随从优先（发现随从通常更稳）
            if (card.Type == Card.CType.MINION)
                score += 3;

            // 关键词加分
            if (card.Taunt) score += 4;
            if (card.Divineshield) score += 5;
            if (card.Poison) score += 4;
            if (card.HasDeathrattle) score += 2;
            if (card.HasBattlecry) score += 2;
            if (card.Charge) score += 4;
            if (card.Windfury) score += 2;
            if (card.Stealth) score += 1;

            // 场面需求：我方随从少时，低费随从更有价值
            try
            {
                if (board != null && board.MinionFriend != null && board.MinionFriend.Count <= 2
                    && card.Type == Card.CType.MINION && card.Cost <= 4)
                    score += 5;
            }
            catch { }

            // 法术也有价值，尤其是低费法术
            if (card.Type == Card.CType.SPELL && card.Cost <= 3)
                score += 2;

            // 武器加分
            if (card.Type == Card.CType.WEAPON)
                score += 3;

            return score;
        }
    }
}
