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

        public void Attack(SimBoard board, SimEntity attacker, SimEntity target)
        {
            attacker.CountAttack++;

            // 双方互相造成伤害
            DealDamage(board, target, attacker.Atk, attacker.HasPoison);
            if (target.Atk > 0)
                DealDamage(board, attacker, target.Atk, target.HasPoison);

            // 吸血
            if (attacker.IsLifeSteal && board.FriendHero != null)
                board.FriendHero.Health = Math.Min(board.FriendHero.MaxHealth, board.FriendHero.Health + attacker.Atk);

            // 武器耐久
            if (attacker == board.FriendHero && board.FriendWeapon != null)
            {
                board.FriendWeapon.Health--;
                if (board.FriendWeapon.Health <= 0)
                {
                    var deadWeapon = board.FriendWeapon;
                    board.FriendWeapon = null;
                    // 武器被打掉后清除英雄的武器攻击力
                    if (board.FriendHero != null) board.FriendHero.Atk = 0;
                    if (_db.TryGet(deadWeapon.CardId, EffectTrigger.Deathrattle, out var drFn))
                        drFn(board, deadWeapon, null);
                }
            }

            ProcessDeaths(board);
        }

        public void PlayCard(SimBoard board, SimEntity card, SimEntity target)
        {
            board.Mana -= card.Cost;
            board.Hand.Remove(card);
            board.CardsPlayedThisTurn++;

            if (card.Type == Card.CType.MINION)
            {
                card.IsTired = !(card.HasCharge || card.HasRush);
                card.CountAttack = 0;
                board.FriendMinions.Add(card);

                // 触发战吼，未注册则按费用兜底
                if (_db.TryGet(card.CardId, EffectTrigger.Battlecry, out var fn))
                    fn(board, card, target);
                else if (card.HasBattlecry)
                    FallbackBattlecry(board, card, target);
            }
            else if (card.Type == Card.CType.SPELL)
            {
                if (_db.TryGet(card.CardId, EffectTrigger.Spell, out var fn))
                    fn(board, card, target);
                else
                    FallbackSpell(board, card, target);
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
                board.FriendMinions.Add(card);
                if (_db.TryGet(card.CardId, EffectTrigger.Spell, out var locFn))
                    locFn(board, card, target);
            }

            ProcessDeaths(board);
        }

        public void UseHeroPower(SimBoard board, SimEntity target)
        {
            board.Mana -= board.HeroPower.Cost;
            board.HeroPowerUsed = true;
            ApplyHeroPower(board, target);
            ProcessDeaths(board);
        }

        public void UseLocation(SimBoard board, SimEntity location, SimEntity target)
        {
            location.IsTired = true;
            // 触发地标激活效果（复用 LocationActivation 层，如果没有就尝试 Spell 层）
            if (_db.TryGet(location.CardId, EffectTrigger.LocationActivation, out var fn))
                fn(board, location, target);
            else if (_db.TryGet(location.CardId, EffectTrigger.Spell, out var spFn))
                spFn(board, location, target);
        }

        public void EndTurn(SimBoard board)
        {
            // 触发回合结束效果
            foreach (var m in board.FriendMinions.ToArray())
                if (_db.TryGet(m.CardId, EffectTrigger.EndOfTurn, out var fn))
                    fn(board, m, null);
            foreach (var m in board.EnemyMinions.ToArray())
                if (_db.TryGet(m.CardId, EffectTrigger.EndOfTurn, out var fn))
                    fn(board, m, null);

            ProcessDeaths(board);
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
            return name == "EX1_625"   // Mind Spike
                || name == "EX1_625t"  // Mind Shatter
                || name.Contains("MindSpike")
                || name.Contains("SCH_270")
                || name.Contains("YOP_028")
                ;
        }

        private void DealDamage(SimBoard board, SimEntity target, int dmg, bool hasPoison)
        {
            if (target == null || target.IsImmune || dmg <= 0) return;
            if (target.IsDivineShield) { target.IsDivineShield = false; return; }
            if (target.Armor > 0)
            {
                int absorbed = Math.Min(target.Armor, dmg);
                target.Armor -= absorbed;
                dmg -= absorbed;
            }
            target.Health -= dmg;
            if (hasPoison && target.Health > 0 && target != board.FriendHero && target != board.EnemyHero)
                target.Health = 0;
        }

        private void ProcessDeaths(SimBoard board)
        {
            // 友方死亡
            var deadFriend = board.FriendMinions.Where(m => !m.IsAlive).ToList();
            foreach (var d in deadFriend)
            {
                board.FriendMinions.Remove(d);
                if (_db.TryGet(d.CardId, EffectTrigger.Deathrattle, out var fn))
                    fn(board, d, null);
                else if (d.HasDeathrattle)
                    FallbackDeathrattle(board, d);
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
                else if (d.HasDeathrattle)
                    FallbackDeathrattle(board, d);
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

        // 未注册法术的兜底：对目标造成 max(1, cost-1) 伤害
        private void FallbackSpell(SimBoard board, SimEntity card, SimEntity target)
        {
            int dmg = Math.Max(1, card.Cost - 1);
            if (target != null)
                DealDamage(board, target, dmg, false);
            else if (board.EnemyHero != null)
                DealDamage(board, board.EnemyHero, dmg, false);
        }

        // 未注册战吼的兜底：对目标造成 max(1, cost/2) 伤害
        private void FallbackBattlecry(SimBoard board, SimEntity card, SimEntity target)
        {
            int dmg = Math.Max(1, card.Cost / 2);
            if (target != null)
                DealDamage(board, target, dmg, false);
        }

        // 未注册亡语的兜底：召唤 cost/3 / cost/3 随从
        private void FallbackDeathrattle(SimBoard board, SimEntity dead)
        {
            int stat = Math.Max(1, dead.Cost / 3);
            var list = dead.IsFriend ? board.FriendMinions : board.EnemyMinions;
            if (list.Count < 7)
                list.Add(new SimEntity { Atk = stat, Health = stat, MaxHealth = stat, IsFriend = dead.IsFriend, IsTired = true });
        }
    }
}
