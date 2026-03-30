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

            // 预计算敌方英雄技能对脸的伤害并应用
            // 在所有攻击之前使用技能是最优的（猎人直伤、法师/牧师打脸、恶魔猎手/德鲁伊增加英雄攻击力）
            ApplyEnemyHeroPowerDamage(clone);

            return clone;
        }

        /// <summary>
        /// 根据敌方职业，预先将英雄技能的伤害/增益应用到棋盘上。
        /// 只处理能直接增加斩杀能力的技能。
        /// </summary>
        private static void ApplyEnemyHeroPowerDamage(SimBoard board)
        {
            if (board.FriendHero == null || board.EnemyHero == null)
                return;

            switch (board.EnemyClass)
            {
                case Card.CClass.HUNTER:
                    // 稳固射击：对敌方英雄2点伤害
                    DealHeroPowerDamageToFace(board, 2);
                    break;

                case Card.CClass.MAGE:
                    // 火焰冲击：1点伤害（可打脸）
                    DealHeroPowerDamageToFace(board, 1 + GetEnemySpellPower(board));
                    break;

                case Card.CClass.PRIEST:
                    // 暗影形态/心灵尖刺：2点伤害（假设最坏情况）
                    // 普通牧师技能是治疗，但暗影形态很常见，保守估计按2点算
                    DealHeroPowerDamageToFace(board, 2);
                    break;

                case Card.CClass.DEMONHUNTER:
                    // 眼刺：英雄攻击力+1
                    if (board.EnemyHero != null)
                        board.EnemyHero.Atk += 1;
                    break;

                case Card.CClass.DRUID:
                    // 变形：英雄攻击力+1，+1护甲（护甲对斩杀无影响，只加攻击力）
                    if (board.EnemyHero != null)
                        board.EnemyHero.Atk += 1;
                    break;

                case Card.CClass.ROGUE:
                    // 匕首精通：装备1/2武器，只在没有武器时有意义
                    if (board.EnemyWeapon == null || board.EnemyWeapon.Health <= 0)
                    {
                        if (board.EnemyHero != null)
                            board.EnemyHero.Atk = Math.Max(board.EnemyHero.Atk, 1);
                    }
                    break;

                // WARLOCK: 生命分流（自伤），不增加斩杀
                // WARRIOR: 全副武装（+2甲），不增加斩杀
                // PALADIN: 援军（1/1），来不及攻击
                // SHAMAN: 图腾召唤，来不及攻击
            }
        }

        private static void DealHeroPowerDamageToFace(SimBoard board, int damage)
        {
            if (damage <= 0 || board.FriendHero == null)
                return;

            if (board.FriendHero.IsImmune)
                return;

            // 护甲先抵挡
            if (board.FriendHero.Armor > 0)
            {
                var absorbed = Math.Min(board.FriendHero.Armor, damage);
                board.FriendHero.Armor -= absorbed;
                damage -= absorbed;
            }

            board.FriendHero.Health -= damage;
        }

        private static int GetEnemySpellPower(SimBoard board)
        {
            var sp = 0;
            foreach (var m in board.EnemyMinions)
            {
                if (m != null && m.Health > 0)
                    sp += Math.Max(0, m.SpellPower);
            }
            return sp;
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

            // 潜行覆盖嘲讽：有潜行的嘲讽不能被选为攻击目标
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

        /// <summary>
        /// 估算嘲讽屏障值（用于剪枝，允许偏低但不能偏高）。
        /// 圣盾需要额外一次完整攻击来消耗，用敌方最大单次攻击力近似。
        /// </summary>
        private static int CalculateTauntBarrier(SimBoard board)
        {
            var maxEnemyAtk = GetMaxEnemyAttack(board);
            var total = 0;
            foreach (var minion in board.FriendMinions.Where(m =>
                m != null && m.Health > 0 && m.IsTaunt && !m.IsStealth))
            {
                var barrier = Math.Max(0, minion.Health);
                // 圣盾吸收一次完整攻击：至少需要额外消耗一个攻击者的伤害量
                if (minion.IsDivineShield)
                    barrier += Math.Max(1, maxEnemyAtk);
                // 重生：死后以1血复活，嘲讽保留，需要额外一击
                if (minion.HasReborn)
                    barrier += 1;
                total += barrier;
            }

            return total;
        }

        private static int GetMaxEnemyAttack(SimBoard board)
        {
            var max = 0;
            if (board.EnemyHero != null && board.EnemyHero.Atk > 0)
                max = board.EnemyHero.Atk;

            foreach (var m in board.EnemyMinions)
            {
                if (m != null && m.Atk > max && m.Health > 0)
                    max = m.Atk;
            }
            return max;
        }

        /// <summary>
        /// 获取有效嘲讽目标（排除潜行覆盖嘲讽的随从）。
        /// </summary>
        private static List<int> GetTauntTargets(SimBoard board)
        {
            return board.FriendMinions
                .Select((minion, index) => new { minion, index })
                .Where(x => x.minion != null && x.minion.Health > 0 && x.minion.IsTaunt && !x.minion.IsStealth)
                .OrderBy(x => GetTauntPriority(x.minion, GetMaxEnemyAttack(board)))
                .Select(x => x.index)
                .ToList();
        }

        private static int GetTauntPriority(SimEntity minion, int maxEnemyAtk)
        {
            var barrier = Math.Max(0, minion?.Health ?? 0);
            if (minion?.IsDivineShield == true) barrier += Math.Max(1, maxEnemyAtk);
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
