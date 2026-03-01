using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Plugins.API;
using SmartBot.Plugins.API.Actions;
using SbAction = SmartBot.Plugins.API.Actions.Action;

namespace BotMain
{
    /// <summary>
    /// 将内部动作字符串转换为 SBAPI Action 对象
    /// </summary>
    public static class ActionStringParser
    {
        public static SbAction Parse(string actionStr, Board board)
        {
            if (string.IsNullOrWhiteSpace(actionStr))
                return null;

            try
            {
                if (actionStr.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase))
                    return new EndTurnAction();

                if (actionStr.StartsWith("CONCEDE", StringComparison.OrdinalIgnoreCase))
                    return new ConcedeAction();

                var parts = actionStr.Split('|');
                if (parts.Length < 2)
                    return null;

                var command = parts[0].ToUpperInvariant();
                int.TryParse(parts[1], out var sourceId);

                var targetId = 0;
                if (parts.Length >= 3)
                    int.TryParse(parts[2], out targetId);

                var source = FindCard(board, sourceId);
                var target = targetId > 0 ? FindCard(board, targetId) : null;

                switch (command)
                {
                    case "PLAY":
                        return new PushAction(0, source, target);

                    case "ATTACK":
                        return new AttackAction(source, target);

                    case "HERO_POWER":
                        return new PushAction(0, source, target);

                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        public static List<SbAction> ParseAll(List<string> actionStrings, Board board)
        {
            var result = new List<SbAction>();
            if (actionStrings == null || board == null)
                return result;

            foreach (var str in actionStrings)
            {
                var action = Parse(str, board);
                if (action != null)
                    result.Add(action);
            }

            return result;
        }

        private static Card FindCard(Board board, int entityId)
        {
            if (board == null || entityId <= 0)
                return null;

            if (board.HeroFriend?.Id == entityId) return board.HeroFriend;
            if (board.HeroEnemy?.Id == entityId) return board.HeroEnemy;
            if (board.Ability?.Id == entityId) return board.Ability;
            if (board.WeaponFriend?.Id == entityId) return board.WeaponFriend;
            if (board.WeaponEnemy?.Id == entityId) return board.WeaponEnemy;

            var card = board.Hand?.FirstOrDefault(c => c?.Id == entityId);
            if (card != null) return card;

            card = board.MinionFriend?.FirstOrDefault(c => c?.Id == entityId);
            if (card != null) return card;

            card = board.MinionEnemy?.FirstOrDefault(c => c?.Id == entityId);
            return card;
        }
    }
}
