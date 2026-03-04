using System;
using System.Linq;
using System.Reflection;
using BotMain.AI;
using SmartBot.Plugins.API;
using Xunit;

namespace BotCore.Tests
{
    public class BoardTradeInteractionTests
    {
        private static readonly MethodInfo CanThreatenKillMethod = typeof(BoardEvaluator)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "CanThreatenKill" && m.GetParameters().Length == 2);

        private static readonly MethodInfo EnemyTradePenaltyMethod = typeof(BoardEvaluator)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(m => m.Name == "EstimateEnemyTradePenalty" && m.GetParameters().Length == 4);

        [Fact]
        public void CanThreatenKill_WindfuryBreaksShieldThenKills()
        {
            var attacker = MakeMinion(3, 3, windfury: true);
            var defender = MakeMinion(3, 3, divineShield: true);

            bool canKill = InvokeCanThreatenKill(attacker, defender);
            Assert.True(canKill);
        }

        [Fact]
        public void CanThreatenKill_PoisonWithoutSecondHitCannotPassDivineShield()
        {
            var attacker = MakeMinion(1, 1, poison: true);
            var defender = MakeMinion(10, 10, divineShield: true);

            bool canKill = InvokeCanThreatenKill(attacker, defender);
            Assert.False(canKill);
        }

        [Fact]
        public void EnemyTradePenalty_DoesNotDoubleConsumeSingleAttacker()
        {
            var evaluator = new BoardEvaluator();

            var boardOneTarget = NewBaseBoard();
            boardOneTarget.EnemyMinions.Add(MakeMinion(5, 5));
            boardOneTarget.FriendMinions.Add(MakeMinion(2, 2, taunt: true));

            var boardTwoTargets = NewBaseBoard();
            boardTwoTargets.EnemyMinions.Add(MakeMinion(5, 5));
            boardTwoTargets.FriendMinions.Add(MakeMinion(2, 2, taunt: true));
            boardTwoTargets.FriendMinions.Add(MakeMinion(2, 2, taunt: true));

            float penaltyOne = InvokeEnemyTradePenalty(evaluator, boardOneTarget);
            float penaltyTwo = InvokeEnemyTradePenalty(evaluator, boardTwoTargets);

            Assert.True(penaltyOne > 0.05f, $"penaltyOne too small: {penaltyOne}");
            Assert.True(
                penaltyTwo < penaltyOne * 1.4f + 0.01f,
                $"single attacker should not be consumed twice. penaltyOne={penaltyOne}, penaltyTwo={penaltyTwo}");
        }

        [Fact]
        public void EnemyTradePenalty_TauntPhaseBlocksReachToBackline()
        {
            var evaluator = new BoardEvaluator();

            var boardWithTaunt = NewBaseBoard();
            boardWithTaunt.EnemyMinions.Add(MakeMinion(16, 16));
            boardWithTaunt.FriendMinions.Add(MakeMinion(1, 1, taunt: true));
            boardWithTaunt.FriendMinions.Add(MakeMinion(10, 10));

            var boardWithoutTaunt = NewBaseBoard();
            boardWithoutTaunt.EnemyMinions.Add(MakeMinion(16, 16));
            boardWithoutTaunt.FriendMinions.Add(MakeMinion(1, 1));
            boardWithoutTaunt.FriendMinions.Add(MakeMinion(10, 10));

            float penaltyWithTaunt = InvokeEnemyTradePenalty(evaluator, boardWithTaunt);
            float penaltyWithoutTaunt = InvokeEnemyTradePenalty(evaluator, boardWithoutTaunt);

            Assert.True(
                penaltyWithoutTaunt > penaltyWithTaunt * 2.5f,
                $"taunt should significantly reduce backline trade risk. with={penaltyWithTaunt}, without={penaltyWithoutTaunt}");
        }

        [Fact]
        public void TradeEvaluator_DeathrattleBiasIsThreatLayered()
        {
            var evaluator = new TradeEvaluator();
            var board = NewBaseBoard();

            var attacker = MakeMinion(5, 6, entityId: 1001);
            board.FriendMinions.Add(attacker);

            float lowBase = EvaluateTradeScore(
                evaluator, board, attacker, MakeMinion(2, 2, entityId: 2001, deathrattle: false));
            float lowDeathrattle = EvaluateTradeScore(
                evaluator, board, attacker, MakeMinion(2, 2, entityId: 2002, deathrattle: true));

            float highBase = EvaluateTradeScore(
                evaluator, board, attacker, MakeMinion(5, 5, entityId: 2003, poison: true, deathrattle: false));
            float highDeathrattle = EvaluateTradeScore(
                evaluator, board, attacker, MakeMinion(5, 5, entityId: 2004, poison: true, deathrattle: true));

            Assert.True(lowDeathrattle < lowBase, $"low-threat deathrattle should remain conservative. base={lowBase}, dr={lowDeathrattle}");
            Assert.True(highDeathrattle > highBase, $"high-threat deathrattle should become more encouraged. base={highBase}, dr={highDeathrattle}");
        }

        private static bool InvokeCanThreatenKill(SimEntity attacker, SimEntity defender)
        {
            return (bool)CanThreatenKillMethod.Invoke(null, new object[] { attacker, defender });
        }

        private static float InvokeEnemyTradePenalty(BoardEvaluator evaluator, SimBoard board)
        {
            var args = new object[] { board, 1f, AggroInteractionContext.FromAggroCoef(1f), null };
            return (float)EnemyTradePenaltyMethod.Invoke(evaluator, args);
        }

        private static float EvaluateTradeScore(TradeEvaluator evaluator, SimBoard board, SimEntity attacker, SimEntity target)
        {
            var action = new GameAction
            {
                Type = ActionType.Attack,
                Source = attacker,
                Target = target,
                SourceEntityId = attacker.EntityId,
                TargetEntityId = target.EntityId
            };
            return evaluator.EvaluateAttack(board, action, 1f);
        }

        private static SimBoard NewBaseBoard()
        {
            return new SimBoard
            {
                FriendHero = MakeHero(entityId: 1),
                EnemyHero = MakeHero(entityId: 2, isFriend: false)
            };
        }

        private static SimEntity MakeHero(int entityId, bool isFriend = true)
        {
            return new SimEntity
            {
                EntityId = entityId,
                Type = Card.CType.HERO,
                IsFriend = isFriend,
                Atk = 0,
                Health = 30,
                Armor = 0,
                MaxHealth = 30
            };
        }

        private static SimEntity MakeMinion(
            int atk,
            int hp,
            bool taunt = false,
            bool divineShield = false,
            bool windfury = false,
            bool poison = false,
            bool deathrattle = false,
            int entityId = 0)
        {
            return new SimEntity
            {
                EntityId = entityId == 0 ? Guid.NewGuid().GetHashCode() : entityId,
                CardId = 0,
                Type = Card.CType.MINION,
                Atk = atk,
                Health = hp,
                MaxHealth = hp,
                IsTaunt = taunt,
                IsDivineShield = divineShield,
                IsWindfury = windfury,
                HasPoison = poison,
                HasDeathrattle = deathrattle,
                IsFriend = true
            };
        }
    }
}
