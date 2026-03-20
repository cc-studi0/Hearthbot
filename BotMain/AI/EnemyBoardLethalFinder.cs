using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace BotMain.AI
{
    public sealed class EnemyBoardLethalResult
    {
        public bool ShouldConcede { get; init; }
        public string Reason { get; init; } = "blocked:unsupported-state";
        public int EstimatedFaceDamage { get; init; }
        public int SearchNodes { get; init; }
    }

    public static class EnemyBoardLethalFinder
    {
        private static readonly Lazy<BoardSimulator> Simulator = new(CreateSimulator);

        public static EnemyBoardLethalResult Evaluate(SimBoard board)
        {
            if (board?.FriendHero == null || board.EnemyHero == null)
                return new EnemyBoardLethalResult { Reason = "blocked:unsupported-state" };

            if (board.FriendSecrets != null && board.FriendSecrets.Count > 0)
            {
                return new EnemyBoardLethalResult
                {
                    Reason = "blocked:friend-secret"
                };
            }

            if (board.FriendHero.IsImmune)
            {
                return new EnemyBoardLethalResult
                {
                    Reason = "blocked:friend-immune"
                };
            }

            var prepared = PrepareForEnemyTurn(board);
            if (prepared?.FriendHero == null || prepared.EnemyHero == null)
                return new EnemyBoardLethalResult { Reason = "blocked:unsupported-state" };

            var context = new SearchContext(GetHeroEffectiveHealth(prepared.FriendHero));
            var lethal = Search(prepared, context);

            return new EnemyBoardLethalResult
            {
                ShouldConcede = lethal,
                Reason = lethal ? "positive:deterministic-lethal" : "negative:not-lethal",
                EstimatedFaceDamage = context.BestFaceDamage,
                SearchNodes = context.SearchNodes
            };
        }

        private static BoardSimulator CreateSimulator()
        {
            CardTemplate.INIT();
            return new BoardSimulator(CardEffectDB.BuildDefault());
        }

        private static SimBoard PrepareForEnemyTurn(SimBoard board)
        {
            if (board == null) return null;

            var clone = board.Clone();

            if (clone.EnemyHero != null)
            {
                clone.EnemyHero.UseBoardCanAttack = false;
                clone.EnemyHero.BoardCanAttack = false;
                clone.EnemyHero.CountAttack = 0;
                clone.EnemyHero.IsTired = false;
                clone.EnemyHero.CanAttackHeroes = true;

                if (clone.EnemyWeapon != null && clone.EnemyWeapon.Health > 0)
                {
                    clone.EnemyHero.Atk = Math.Max(clone.EnemyHero.Atk, clone.EnemyWeapon.Atk);
                    if (clone.EnemyWeapon.IsWindfury)
                        clone.EnemyHero.IsWindfury = true;
                }
            }

            if (clone.EnemyWeapon != null)
            {
                clone.EnemyWeapon.UseBoardCanAttack = false;
                clone.EnemyWeapon.BoardCanAttack = false;
            }

            foreach (var minion in clone.EnemyMinions.Where(m => m != null))
            {
                minion.UseBoardCanAttack = false;
                minion.BoardCanAttack = false;
                minion.CountAttack = 0;
                minion.IsTired = false;
            }

            return clone;
        }

        private static bool Search(SimBoard board, SearchContext context)
        {
            context.SearchNodes++;
            UpdateBestFaceDamage(board, context);
            context.BestFaceDamage = Math.Max(context.BestFaceDamage, CalculateRemainingFaceDamage(board));

            if (GetHeroEffectiveHealth(board.FriendHero) <= 0)
                return true;

            var key = BuildStateKey(board);
            if (context.Memo.TryGetValue(key, out var cached))
                return cached;

            var heroEhp = GetHeroEffectiveHealth(board.FriendHero);
            var remainingFaceDamage = CalculateRemainingFaceDamage(board);
            if (remainingFaceDamage < heroEhp)
            {
                context.Memo[key] = false;
                return false;
            }

            var tauntBarrier = CalculateTauntBarrier(board);
            var remainingTotalDamage = CalculateRemainingTotalDamage(board);
            if (remainingTotalDamage < heroEhp + tauntBarrier)
            {
                context.Memo[key] = false;
                return false;
            }

            var attackers = GetAttackers(board);
            if (attackers.Count == 0)
            {
                context.Memo[key] = false;
                return false;
            }

            var taunts = GetTauntTargets(board);
            if (taunts.Count > 0)
            {
                foreach (var attacker in attackers)
                {
                    foreach (var targetIndex in taunts)
                    {
                        var next = board.Clone();
                        var nextAttacker = ResolveAttacker(next, attacker);
                        if (nextAttacker == null || !nextAttacker.CanAttack)
                            continue;

                        if (targetIndex < 0 || targetIndex >= next.FriendMinions.Count)
                            continue;

                        var target = next.FriendMinions[targetIndex];
                        if (target == null || target.Health <= 0 || !target.IsTaunt)
                            continue;

                        Simulator.Value.Attack(next, nextAttacker, target);
                        if (Search(next, context))
                        {
                            context.Memo[key] = true;
                            return true;
                        }
                    }
                }

                context.Memo[key] = false;
                return false;
            }

            foreach (var attacker in attackers.Where(a => a.CanAttackHero))
            {
                var next = board.Clone();
                var nextAttacker = ResolveAttacker(next, attacker);
                if (nextAttacker == null || !nextAttacker.CanAttack || !nextAttacker.CanAttackHeroes)
                    continue;

                Simulator.Value.Attack(next, nextAttacker, next.FriendHero);
                if (Search(next, context))
                {
                    context.Memo[key] = true;
                    return true;
                }
            }

            context.Memo[key] = false;
            return false;
        }

        private static void UpdateBestFaceDamage(SimBoard board, SearchContext context)
        {
            var dealt = Math.Max(0, context.InitialHeroEhp - GetHeroEffectiveHealth(board.FriendHero));
            if (dealt > context.BestFaceDamage)
                context.BestFaceDamage = dealt;
        }

        private static int CalculateRemainingFaceDamage(SimBoard board)
        {
            var total = 0;
            foreach (var attacker in GetAttackers(board))
            {
                if (!attacker.CanAttackHero)
                    continue;

                var entity = ResolveAttacker(board, attacker);
                if (entity == null)
                    continue;

                total += Math.Max(0, entity.Atk) * RemainingAttacks(entity);
            }

            return total;
        }

        private static int CalculateRemainingTotalDamage(SimBoard board)
        {
            var total = 0;
            foreach (var attacker in GetAttackers(board))
            {
                var entity = ResolveAttacker(board, attacker);
                if (entity == null)
                    continue;

                total += Math.Max(0, entity.Atk) * RemainingAttacks(entity);
            }

            return total;
        }

        private static int CalculateTauntBarrier(SimBoard board)
        {
            var total = 0;
            foreach (var minion in board.FriendMinions.Where(m => m != null && m.Health > 0 && m.IsTaunt))
            {
                var barrier = Math.Max(0, minion.Health);
                if (minion.IsDivineShield) barrier += 1;
                if (minion.HasReborn) barrier += 1;
                total += barrier;
            }

            return total;
        }

        private static List<int> GetTauntTargets(SimBoard board)
        {
            return board.FriendMinions
                .Select((minion, index) => new { minion, index })
                .Where(x => x.minion != null && x.minion.Health > 0 && x.minion.IsTaunt)
                .OrderBy(x => GetTauntPriority(x.minion))
                .Select(x => x.index)
                .ToList();
        }

        private static int GetTauntPriority(SimEntity minion)
        {
            var barrier = Math.Max(0, minion?.Health ?? 0);
            if (minion?.IsDivineShield == true) barrier += 1;
            if (minion?.HasReborn == true) barrier += 1;
            return barrier;
        }

        private static List<AttackerRef> GetAttackers(SimBoard board)
        {
            var attackers = new List<AttackerRef>();

            if (board.EnemyHero != null && board.EnemyHero.CanAttack)
            {
                attackers.Add(new AttackerRef(
                    isHero: true,
                    index: -1,
                    priority: Math.Max(0, board.EnemyHero.Atk) * Math.Max(1, RemainingAttacks(board.EnemyHero)),
                    canAttackHero: board.EnemyHero.CanAttackHeroes));
            }

            for (var i = 0; i < board.EnemyMinions.Count; i++)
            {
                var minion = board.EnemyMinions[i];
                if (minion == null || minion.Type == Card.CType.LOCATION || !minion.CanAttack)
                    continue;

                attackers.Add(new AttackerRef(
                    isHero: false,
                    index: i,
                    priority: Math.Max(0, minion.Atk) * Math.Max(1, RemainingAttacks(minion)),
                    canAttackHero: minion.CanAttackHeroes));
            }

            attackers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            return attackers;
        }

        private static SimEntity ResolveAttacker(SimBoard board, AttackerRef attacker)
        {
            if (attacker.IsHero)
                return board.EnemyHero;

            if (attacker.Index < 0 || attacker.Index >= board.EnemyMinions.Count)
                return null;

            return board.EnemyMinions[attacker.Index];
        }

        private static int RemainingAttacks(SimEntity entity)
        {
            if (entity == null || entity.Atk <= 0 || entity.IsFrozen || entity.IsTired)
                return 0;

            var maxAttacks = entity.IsWindfury ? 2 : 1;
            return Math.Max(0, maxAttacks - entity.CountAttack);
        }

        private static int GetHeroEffectiveHealth(SimEntity hero)
        {
            if (hero == null) return 0;
            return Math.Max(0, hero.Health + hero.Armor);
        }

        private static string BuildStateKey(SimBoard board)
        {
            var sb = new StringBuilder(512);
            AppendEntity(sb, board.FriendHero);
            AppendEntity(sb, board.EnemyHero);
            AppendEntity(sb, board.FriendWeapon);
            AppendEntity(sb, board.EnemyWeapon);

            sb.Append("|F:");
            foreach (var minion in board.FriendMinions)
                AppendEntity(sb, minion);

            sb.Append("|E:");
            foreach (var minion in board.EnemyMinions)
                AppendEntity(sb, minion);

            return sb.ToString();
        }

        private static void AppendEntity(StringBuilder sb, SimEntity entity)
        {
            if (entity == null)
            {
                sb.Append("null;");
                return;
            }

            sb
                .Append((int)entity.CardId).Append(',')
                .Append((int)entity.Type).Append(',')
                .Append(entity.Atk).Append(',')
                .Append(entity.Health).Append(',')
                .Append(entity.MaxHealth).Append(',')
                .Append(entity.Armor).Append(',')
                .Append(entity.CountAttack).Append(',')
                .Append(entity.CanAttackHeroes ? '1' : '0').Append(',')
                .Append(entity.IsTaunt ? '1' : '0').Append(',')
                .Append(entity.IsDivineShield ? '1' : '0').Append(',')
                .Append(entity.IsWindfury ? '1' : '0').Append(',')
                .Append(entity.HasPoison ? '1' : '0').Append(',')
                .Append(entity.IsLifeSteal ? '1' : '0').Append(',')
                .Append(entity.HasReborn ? '1' : '0').Append(',')
                .Append(entity.IsFrozen ? '1' : '0').Append(',')
                .Append(entity.IsImmune ? '1' : '0').Append(',')
                .Append(entity.IsSilenced ? '1' : '0').Append(',')
                .Append(entity.IsStealth ? '1' : '0').Append(',')
                .Append(entity.HasCharge ? '1' : '0').Append(',')
                .Append(entity.HasRush ? '1' : '0').Append(',')
                .Append(entity.IsTired ? '1' : '0').Append(',')
                .Append(entity.HasDeathrattle ? '1' : '0').Append(';');
        }

        private readonly struct AttackerRef
        {
            public AttackerRef(bool isHero, int index, int priority, bool canAttackHero)
            {
                IsHero = isHero;
                Index = index;
                Priority = priority;
                CanAttackHero = canAttackHero;
            }

            public bool IsHero { get; }
            public int Index { get; }
            public int Priority { get; }
            public bool CanAttackHero { get; }
        }

        private sealed class SearchContext
        {
            public SearchContext(int initialHeroEhp)
            {
                InitialHeroEhp = initialHeroEhp;
            }

            public int InitialHeroEhp { get; }
            public int BestFaceDamage { get; set; }
            public int SearchNodes { get; set; }
            public Dictionary<string, bool> Memo { get; } = new(StringComparer.Ordinal);
        }
    }
}
