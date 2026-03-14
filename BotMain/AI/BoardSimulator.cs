using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Plugins.API;

namespace BotMain.AI
{
    public class BoardSimulator
    {
        private readonly CardEffectDB _db;

        public BoardSimulator(CardEffectDB db) => _db = db;

        private static void DisableBoardCanAttackHints(SimBoard board)
        {
            if (board == null) return;

            if (board.FriendHero != null) board.FriendHero.UseBoardCanAttack = false;
            if (board.EnemyHero != null) board.EnemyHero.UseBoardCanAttack = false;

            foreach (var m in board.FriendMinions)
                m.UseBoardCanAttack = false;
            foreach (var m in board.EnemyMinions)
                m.UseBoardCanAttack = false;
        }

        public void Attack(SimBoard board, SimEntity attacker, SimEntity target)
        {
            DisableBoardCanAttackHints(board);
            var targetHealthBefore = target?.Health ?? 0;
            var targetArmorBefore = target?.Armor ?? 0;
            var targetHadDivineShield = target?.IsDivineShield ?? false;

            var attackerIsFriendHero = attacker == board.FriendHero;
            var attackerIsEnemyHero = attacker == board.EnemyHero;
            var attackerWeapon = attackerIsFriendHero ? board.FriendWeapon : attackerIsEnemyHero ? board.EnemyWeapon : null;
            var applyImmuneWhileAttacking = attackerWeapon != null
                && _db.HasHeroImmuneWhileAttackingWeapon(attackerWeapon.CardId);
            var originalImmune = attacker.IsImmune;
            if (applyImmuneWhileAttacking)
                attacker.IsImmune = true;

            attacker.CountAttack++;
            if (attacker.Type == Card.CType.HERO)
            {
                if (!attacker.IsWindfury || attacker.CountAttack >= 2)
                    attacker.IsTired = true;
            }

            var attackerAtk = attacker.Atk;
            if (attacker == board.FriendHero && board.FriendWeapon != null && board.FriendWeapon.Health > 0)
                attackerAtk = Math.Max(attackerAtk, board.FriendWeapon.Atk);
            else if (attacker == board.EnemyHero && board.EnemyWeapon != null && board.EnemyWeapon.Health > 0)
                attackerAtk = Math.Max(attackerAtk, board.EnemyWeapon.Atk);

            // 双方互相造成伤害
            DealDamage(board, target, attackerAtk, attacker.HasPoison);
            if (target.Atk > 0)
                DealDamage(board, attacker, target.Atk, target.HasPoison);

            if (applyImmuneWhileAttacking)
                attacker.IsImmune = originalImmune;

            // 攻击敌方随从后触发（如“攻击一个随从后...”）
            if (attacker != null
                && target != null
                && target.Type == Card.CType.MINION
                && _db.TryGet(attacker.CardId, EffectTrigger.AfterAttackMinion, out var afterAttackFn))
            {
                afterAttackFn(board, attacker, target);
            }

            if (attackerIsFriendHero || attackerIsEnemyHero)
            {
                var heroCtx = new CardEffectDB.HeroAttackContext
                {
                    AttackDamage = Math.Max(0, attackerAtk),
                    TargetWasMinion = target != null && target.Type == Card.CType.MINION,
                    HonorableKill = target != null
                        && target.Type == Card.CType.MINION
                        && !targetHadDivineShield
                        && targetArmorBefore <= 0
                        && targetHealthBefore > 0
                        && target.Health <= 0
                        && attackerAtk == targetHealthBefore
                };

                _db.TriggerAfterHeroAttackWeapon(board, attacker, target, heroCtx);
                if (attackerIsFriendHero)
                    BreakHeroWeaponIfBroken(board, isFriendHero: true);
                else if (attackerIsEnemyHero)
                    BreakHeroWeaponIfBroken(board, isFriendHero: false);
            }

            // 吸血
            if (attacker.IsLifeSteal && board.FriendHero != null)
                board.FriendHero.Health = Math.Min(board.FriendHero.MaxHealth, board.FriendHero.Health + attackerAtk);

            // 武器耐久
            if (attacker == board.FriendHero && board.FriendWeapon != null)
            {
                board.FriendWeapon.Health--;
                BreakHeroWeaponIfBroken(board, isFriendHero: true);
            }
            else if (attacker == board.EnemyHero && board.EnemyWeapon != null)
            {
                board.EnemyWeapon.Health--;
                BreakHeroWeaponIfBroken(board, isFriendHero: false);
            }

            ProcessDeaths(board);
            ApplyAuras(board);
        }

        public void PlayCard(SimBoard board, SimEntity card, SimEntity target)
        {
            DisableBoardCanAttackHints(board);
            ApplyAuras(board);
            board.Mana -= card.Cost;
            board.Hand.Remove(card);
            board.CardsPlayedThisTurn++;

            if (card.Type == Card.CType.MINION)
            {
                card.IsTired = !(card.HasCharge || card.HasRush);
                card.CountAttack = 0;
                card.UseBoardCanAttack = false;
                card.BoardCanAttack = false;
                board.FriendMinions.Add(card);

                // 只执行已注册的卡牌效果脚本
                if (_db.TryGet(card.CardId, EffectTrigger.Battlecry, out var fn))
                    fn(board, card, target);
            }
            else if (card.Type == Card.CType.SPELL)
            {
                if (_db.TryGet(card.CardId, EffectTrigger.Spell, out var fn))
                    fn(board, card, target);
                _db.TriggerAfterFriendlySpellCastWeapon(board, card);
                BreakHeroWeaponIfBroken(board, isFriendHero: true);
            }
            else if (card.Type == Card.CType.WEAPON)
            {
                board.FriendWeapon = card;
                if (board.FriendHero != null)
                    board.FriendHero.Atk = card.Atk;
                if (_db.TryGet(card.CardId, EffectTrigger.Battlecry, out var weaponFn))
                    weaponFn(board, card, target);
            }
            else if (card.Type == Card.CType.HERO)
            {
                // 英雄牌：加护甲 + 触发战吼
                if (board.FriendHero != null)
                    board.FriendHero.Armor += card.Armor;
                if (_db.TryGet(card.CardId, EffectTrigger.Battlecry, out var heroFn))
                    heroFn(board, card, target);
            }
            else if (card.Type == Card.CType.LOCATION)
            {
                // 地标牌：放置到场上，不可攻击
                card.IsTired = true;
                card.CountAttack = 0;
                card.UseBoardCanAttack = false;
                card.BoardCanAttack = false;
                board.FriendMinions.Add(card);
                if (_db.TryGet(card.CardId, EffectTrigger.Spell, out var locFn))
                    locFn(board, card, target);
            }

            ProcessDeaths(board);
            ApplyAuras(board);
        }

        public void TradeCard(SimBoard board, SimEntity card)
        {
            DisableBoardCanAttackHints(board);
            if (board == null || card == null) return;
            if (!card.IsTradeable) return;
            if (board.Mana < 1) return;

            board.Mana -= 1;
            board.Hand.Remove(card);

            // 可交易：原牌洗回牌库
            board.FriendDeckCards.Add(card.CardId);

            // 抽 1 张：若有可见牌库，则按当前牌库顺序抽取一张用于模拟
            if (board.Hand.Count < 10)
            {
                if (board.FriendDeckCards.Count > 0)
                {
                    var drawId = board.FriendDeckCards[0];
                    board.FriendDeckCards.RemoveAt(0);
                    var drawn = CreateDeckDrawEntity(drawId);
                    if (drawn != null)
                        board.Hand.Add(drawn);
                    else
                        board.FriendCardDraw += 1;
                }
                else
                {
                    board.FriendCardDraw += 1;
                }
            }

            _db.TriggerAfterTradeCard(board, card);

            ApplyAuras(board);
        }

        public void UseHeroPower(SimBoard board, SimEntity target)
        {
            DisableBoardCanAttackHints(board);
            board.Mana -= board.HeroPower.Cost;
            board.HeroPowerUsed = true;
            ApplyHeroPower(board, target);
            _db.TriggerAfterFriendlyHeroPowerUsedWeapon(board);
            BreakHeroWeaponIfBroken(board, isFriendHero: true);
            ProcessDeaths(board);
            ApplyAuras(board);
        }

        public void UseLocation(SimBoard board, SimEntity location, SimEntity target)
        {
            DisableBoardCanAttackHints(board);
            location.IsTired = true;
            // 触发地标激活效果（复用 LocationActivation 层，如果没有就尝试 Spell 层）
            if (_db.TryGet(location.CardId, EffectTrigger.LocationActivation, out var fn))
                fn(board, location, target);
            else if (_db.TryGet(location.CardId, EffectTrigger.Spell, out var spFn))
                spFn(board, location, target);
            ProcessDeaths(board);
            ApplyAuras(board);
        }

        public void EndTurn(SimBoard board)
        {
            DisableBoardCanAttackHints(board);
            // 触发回合结束效果
            foreach (var m in board.FriendMinions.ToArray())
                if (_db.TryGet(m.CardId, EffectTrigger.EndOfTurn, out var fn))
                    fn(board, m, null);
            foreach (var m in board.EnemyMinions.ToArray())
                if (_db.TryGet(m.CardId, EffectTrigger.EndOfTurn, out var fn))
                    fn(board, m, null);

            ProcessDeaths(board);
            ApplyAuras(board);
        }

        private void ApplyHeroPower(SimBoard board, SimEntity target)
        {
            switch (board.FriendClass)
            {
                case Card.CClass.MAGE: // 火焰冲击：1点伤害
                    if (target != null) DealDamage(board, target, 1, false);
                    break;
                case Card.CClass.PRIEST:
                    // 判断是治疗技能还是伤害技能
                    if (IsPriestDamageHeroPower(board))
                    {
                        // 心灵尖刺/Mind Spike 等：造成2点伤害
                        if (target != null) DealDamage(board, target, 2, false);
                    }
                    else
                    {
                        // 次级治疗术/Lesser Heal：恢复2点生命
                        if (target != null) target.Health = Math.Min(target.MaxHealth, target.Health + 2);
                    }
                    break;
                case Card.CClass.HUNTER: // 稳固射击：对敌方英雄2点
                    if (board.EnemyHero != null) DealDamage(board, board.EnemyHero, 2, false);
                    break;
                case Card.CClass.WARLOCK: // 生命分流：抽牌扣2血
                    if (board.FriendHero != null) board.FriendHero.Health -= 2;
                    break;
                case Card.CClass.WARRIOR: // 全副武装：+2护甲
                    if (board.FriendHero != null) board.FriendHero.Armor += 2;
                    break;
                case Card.CClass.PALADIN: // 援军：召唤1/1
                    board.FriendMinions.Add(new SimEntity { CardId = Card.Cards.CS2_101t, Atk = 1, Health = 1, MaxHealth = 1, IsFriend = true, IsTired = true });
                    break;
                case Card.CClass.SHAMAN: // 图腾召唤：随机图腾
                    board.FriendMinions.Add(new SimEntity { CardId = Card.Cards.CS2_050, Atk = 0, Health = 2, MaxHealth = 2, IsFriend = true, IsTired = true });
                    break;
                case Card.CClass.ROGUE: // 匕首精通：装备1/2武器
                    board.FriendWeapon = new SimEntity { Atk = 1, Health = 2, MaxHealth = 2, IsFriend = true };
                    if (board.FriendHero != null) board.FriendHero.Atk = 1;
                    break;
                case Card.CClass.DRUID: // 变形：+1攻+1甲
                    if (board.FriendHero != null) { board.FriendHero.Atk += 1; board.FriendHero.Armor += 1; }
                    break;
                case Card.CClass.DEMONHUNTER: // 眼刺：英雄攻击+1
                    if (board.FriendHero != null) board.FriendHero.Atk += 1;
                    break;
            }
        }

        private static SimEntity CreateDeckDrawEntity(Card.Cards cardId)
        {
            if (cardId == 0) return null;
            return CardEffectsScripts.CardEffectScriptHelpers.CreateCardInHand(cardId, true);
        }

        /// <summary>
        /// 判断牧师当前英雄技能是否为伤害型（心灵尖刺系列）
        /// 伤害型：Mind Spike、Mind Shatter 等
        /// 治疗型：CS1_112 次级治疗术（默认）
        /// </summary>
        private static bool IsPriestDamageHeroPower(SimBoard board)
        {
            if (board.HeroPower == null) return false;
            // 心灵尖刺系列：EX1_625(Mind Spike), EX1_625t(Mind Shatter)
            // 其他多版本用名字匹配傍错
            var name = board.HeroPower.CardId.ToString();
            return name == "EX1_625"   // 心灵尖刺
                || name == "EX1_625t"  // 心灵破碎
                || name.Contains("MindSpike")
                || name.Contains("SCH_270")
                || name.Contains("YOP_028")
                ;
        }

        private void DealDamage(SimBoard board, SimEntity target, int dmg, bool hasPoison)
        {
            if (target == null || target.IsImmune || dmg <= 0) return;

            // 英雄受伤替代（如：埃辛诺斯壁垒）
            if (target == board.FriendHero && board.FriendWeapon != null && board.FriendWeapon.Health > 0)
            {
                dmg = _db.ApplyHeroDamageReplacement(board.FriendWeapon.CardId, board, target, dmg);
                BreakHeroWeaponIfBroken(board, isFriendHero: true);
                if (dmg <= 0) return;
            }
            else if (target == board.EnemyHero && board.EnemyWeapon != null && board.EnemyWeapon.Health > 0)
            {
                dmg = _db.ApplyHeroDamageReplacement(board.EnemyWeapon.CardId, board, target, dmg);
                BreakHeroWeaponIfBroken(board, isFriendHero: false);
                if (dmg <= 0) return;
            }

            if (target.IsDivineShield)
            {
                target.IsDivineShield = false;
                if (target.IsFriend && target.Type == Card.CType.MINION)
                {
                    _db.TriggerAfterFriendlyDivineShieldLostWeapon(board, target);
                    BreakHeroWeaponIfBroken(board, isFriendHero: true);
                }
                return;
            }
            if (target.Armor > 0)
            {
                int absorbed = Math.Min(target.Armor, dmg);
                target.Armor -= absorbed;
                dmg -= absorbed;
            }
            target.Health -= dmg;
            if (hasPoison && target.Health > 0 && target != board.FriendHero && target != board.EnemyHero)
                target.Health = 0;

            // 受伤后触发（仅存活目标）
            if (target.Health > 0 && _db.TryGet(target.CardId, EffectTrigger.AfterDamaged, out var damagedFn))
                damagedFn(board, target, null);
        }

        private void BreakHeroWeaponIfBroken(SimBoard board, bool isFriendHero)
        {
            var weapon = isFriendHero ? board.FriendWeapon : board.EnemyWeapon;
            if (weapon == null || weapon.Health > 0)
                return;

            if (isFriendHero)
                board.FriendWeapon = null;
            else
                board.EnemyWeapon = null;

            var hero = isFriendHero ? board.FriendHero : board.EnemyHero;
            if (hero != null) hero.Atk = 0;

            if (_db.TryGet(weapon.CardId, EffectTrigger.Deathrattle, out var drFn))
                drFn(board, weapon, null);
        }

        private void ProcessDeaths(SimBoard board)
        {
            // 友方死亡
            var deadFriend = board.FriendMinions.Where(m => !m.IsAlive).ToList();
            foreach (var d in deadFriend)
            {
                _db.TriggerAfterFriendlyMinionDiedWeapon(board, d);
                BreakHeroWeaponIfBroken(board, isFriendHero: true);
                board.FriendMinions.Remove(d);
                if (_db.TryGet(d.CardId, EffectTrigger.Deathrattle, out var fn))
                    fn(board, d, null);
                if (d.HasReborn)
                    board.FriendMinions.Add(new SimEntity
                    {
                        CardId = d.CardId, Atk = d.Atk, Health = 1, MaxHealth = d.MaxHealth,
                        IsFriend = true, IsTired = true,
                        IsTaunt = d.IsTaunt, HasPoison = d.HasPoison, IsWindfury = d.IsWindfury,
                        IsDivineShield = false, HasReborn = false, // 复生只触发一次
                        IsLifeSteal = d.IsLifeSteal, HasDeathrattle = d.HasDeathrattle,
                        Type = d.Type,
                    });
            }
            // 敌方死亡
            var deadEnemy = board.EnemyMinions.Where(m => !m.IsAlive).ToList();
            foreach (var d in deadEnemy)
            {
                board.EnemyMinions.Remove(d);
                if (_db.TryGet(d.CardId, EffectTrigger.Deathrattle, out var fn))
                    fn(board, d, null);
                if (d.HasReborn)
                    board.EnemyMinions.Add(new SimEntity
                    {
                        CardId = d.CardId, Atk = d.Atk, Health = 1, MaxHealth = d.MaxHealth,
                        IsFriend = false, IsTired = true,
                        IsTaunt = d.IsTaunt, HasPoison = d.HasPoison, IsWindfury = d.IsWindfury,
                        IsDivineShield = false, HasReborn = false,
                        IsLifeSteal = d.IsLifeSteal, HasDeathrattle = d.HasDeathrattle,
                        Type = d.Type,
                    });
            }
        }

        private void ApplyAuras(SimBoard board)
        {
            foreach (var c in board.Hand.ToArray())
                if (_db.TryGet(c.CardId, EffectTrigger.Aura, out var fn))
                    fn(board, c, null);

            foreach (var m in board.FriendMinions.ToArray())
                if (_db.TryGet(m.CardId, EffectTrigger.Aura, out var fn))
                    fn(board, m, null);

            foreach (var m in board.EnemyMinions.ToArray())
                if (_db.TryGet(m.CardId, EffectTrigger.Aura, out var fn))
                    fn(board, m, null);

            _db.ApplyWeaponAuras(board);
        }

    }
}
