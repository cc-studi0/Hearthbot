using System.Collections.Generic;
using System.Linq;

namespace HearthstonePayload
{
    public static class SeedBuilder
    {
        public const string SeedNotReadyPrefix = "SEED_NOT_READY:";

        public static string Build(GameStateData d)
        {
            var parts = new string[67];
            for (int i = 0; i < parts.Length; i++)
                parts[i] = "";

            int pid = d.FriendlyPlayerId > 0 ? d.FriendlyPlayerId : 1;

            parts[0] = d.MaxMana.ToString();
            parts[1] = d.ManaAvailable.ToString();
            parts[2] = pid.ToString();
            parts[3] = d.FriendFatigue.ToString();
            parts[4] = d.EnemyFatigue.ToString();
            parts[5] = d.FriendDeckCount.ToString();
            parts[6] = d.EnemyDeckCount.ToString();
            parts[7] = d.EnemySecretCount.ToString();
            parts[8] = B(d.EnemySecretCount > 0);
            parts[9] = d.TurnCount.ToString();
            parts[10] = B(d.IsCombo);
            parts[11] = d.EnemyHandCount.ToString();

            parts[12] = d.WeaponEnemy != null ? SerializeEntity(d.WeaponEnemy) : "0";
            parts[13] = d.WeaponFriend != null ? SerializeEntity(d.WeaponFriend) : "0";
            parts[14] = SerializeEntity(d.HeroFriend);
            parts[15] = SerializeEntity(d.HeroEnemy);
            parts[16] = SerializeEntity(d.AbilityFriend);
            parts[17] = SerializeEntity(d.AbilityEnemy);

            parts[18] = SerializeEntityList(d.MinionFriend?.OrderBy(e => e.ZonePosition).ToList());
            parts[19] = SerializeEntityList(d.MinionEnemy?.OrderBy(e => e.ZonePosition).ToList());
            parts[20] = SerializeEntityList(d.Hand?.OrderBy(e => e.ZonePosition).ToList());
            parts[21] = d.SecretsFriend.Count > 0 ? string.Join("|", d.SecretsFriend) : "0";
            parts[22] = d.GraveyardFriend.Count > 0 ? string.Join("|", d.GraveyardFriend) : "0";
            parts[23] = "0"; // FriendGraveyardTurn: 本回合死亡的随从卡牌ID列表，暂不追踪
            parts[24] = d.GraveyardEnemy.Count > 0 ? string.Join("|", d.GraveyardEnemy) : "0";
            parts[25] = "0"; // EnemyGraveyardTurn: 本回合死亡的随从卡牌ID列表，暂不追踪
            parts[26] = "False=False=False"; // TrapMgr
            parts[27] = d.Overload.ToString();
            parts[28] = d.LockedMana.ToString();
            parts[29] = d.HeroPowerCountThisTurn.ToString();
            parts[30] = B(d.LockAndLoad);
            // Planning seed 只承载 Board.FromSeed 可解析的棋盘信息。
            // 牌库剩余卡牌明细统一通过 GET_DECK_STATE 单独获取，不再写入 seed。
            parts[31] = string.Empty;
            parts[32] = d.BaseMinionDiedThisTurnEnemy.ToString();
            parts[33] = d.BaseMinionDiedThisTurnFriend.ToString();
            parts[34] = d.LockAndLoad ? "1" : "0";
            parts[35] = d.CthunAttack.ToString();
            parts[36] = d.CthunHealth.ToString();
            parts[37] = B(d.CthunTaunt);
            parts[38] = B(d.SpellsCostHealth);
            parts[39] = (d.EnemyMaxMana > 0 ? d.EnemyMaxMana : d.MaxMana).ToString();
            parts[40] = B(d.EmbraceTheShadow);
            parts[41] = d.JadeGolem.ToString();
            parts[42] = d.JadeGolemEnemy.ToString();
            parts[43] = d.QuestFriendlyProgress.ToString();
            parts[44] = d.QuestFriendlyId;
            parts[45] = d.QuestFriendlyTotal.ToString();
            parts[46] = d.QuestFriendlyReward.ToString();
            parts[47] = d.QuestEnemyProgress.ToString();
            parts[48] = d.QuestEnemyId;
            parts[49] = d.QuestEnemyTotal.ToString();
            parts[50] = d.QuestEnemyReward.ToString();
            parts[51] = B(d.ElemBuffEnabled);
            parts[52] = d.CardsPlayedThisTurn.ToString();
            parts[53] = B(d.Stampede);
            parts[54] = d.ElemPlayedLastTurn.ToString();

            // 扩展字段（对齐 Board.FromSeed 期望的位置）
            parts[55] = "0";                                    // IdolCount
            parts[56] = d.HeroPowerDamagesThisGame.ToString();  // HeroPowerDamagesThisGame
            parts[57] = "";                                     // 未使用
            parts[58] = "0";                                    // StartHandSize
            parts[59] = d.HealAmountThisGame.ToString();        // HealAmountThisGame
            parts[60] = "";                                     // TagFriend（空 tag map）
            parts[61] = "";                                     // TagEnemy（空 tag map）
            parts[62] = "0";                                    // FriendlySideQuests
            parts[63] = "0";                                    // EnemySideQuests
            parts[64] = "";                                     // 未使用
            parts[65] = "0";                                    // PlayedCards
            parts[66] = "0";                                    // PlayedGeneratedCards

            return string.Join("~", parts);
        }

        public static bool TryBuild(GameStateData d, out string seed, out string detail)
        {
            seed = string.Empty;
            if (!TryValidateState(d, out detail))
                return false;

            seed = Build(d);
            if (string.IsNullOrWhiteSpace(seed))
            {
                detail = "seed_empty";
                return false;
            }

            detail = "ok";
            return true;
        }

        public static bool TryValidateState(GameStateData d, out string detail)
        {
            if (d == null)
            {
                detail = "state_null";
                return false;
            }

            var missing = new List<string>();
            if (d.FriendlyPlayerId <= 0)
                missing.Add("friendly_player");
            if (d.TurnCount <= 0)
                missing.Add("turn_count");
            if (!HasCoreEntity(d.HeroFriend))
                missing.Add("hero_friend");
            if (!HasCoreEntity(d.HeroEnemy))
                missing.Add("hero_enemy");
            if (!HasCoreEntity(d.AbilityFriend))
                missing.Add("ability_friend");
            if (!HasCoreEntity(d.AbilityEnemy))
                missing.Add("ability_enemy");

            if (missing.Count > 0)
            {
                detail = "missing=" + string.Join(",", missing);
                return false;
            }

            detail = "ok";
            return true;
        }

        private static string SerializeEntity(EntityData e)
        {
            if (e == null) return "";
            var p = new string[39];

            p[0] = e.CardId ?? "";
            p[1] = e.ZonePosition.ToString();
            p[2] = e.Armor.ToString();
            p[3] = e.Atk.ToString();
            p[4] = e.Cost.ToString();
            p[5] = e.Damage.ToString();
            p[6] = e.Durability.ToString();
            p[7] = e.EntityId.ToString();
            p[8] = e.Health.ToString();
            p[9] = e.AttackCount.ToString();
            p[10] = e.NumTurnsInPlay.ToString(); // CountTurnsInPlay
            p[11] = e.TempAtk.ToString();
            p[12] = e.SpellPower.ToString();
            p[13] = B(e.Charge);
            p[14] = B(e.DivineShield);
            p[15] = B(e.Taunt);
            p[16] = B(e.WindfuryValue > 0);
            p[17] = B(e.Exhausted);
            p[18] = B(e.IsEnraged);
            p[19] = B(e.Exhausted);
            p[20] = B(e.Freeze);
            p[21] = B(e.Frozen);
            p[22] = B(e.Immune);
            p[23] = B(e.Poisonous);
            p[24] = B(e.Silenced);
            p[25] = B(e.Stealth);
            p[26] = e.NumTurnsInPlay.ToString(); // NumTurnsInPlay
            p[27] = B(e.IsInspire);
            p[28] = B(e.IsTargetable);
            p[29] = B(e.IsGenerated);
            p[30] = e.CountPlayed.ToString();
            p[31] = B(e.Lifesteal);
            p[32] = B(e.Rush);
            p[33] = B(e.IsPowered);
            p[34] = B(!e.CanAttackHeroes);
            p[35] = B(e.HasEcho);
            p[36] = B(e.IsCombo);
            p[37] = B(e.Reborn);
            p[38] = SerializeTags(e.Tags);

            return string.Join("*", p);
        }

        private static string SerializeEntityList(List<EntityData> list)
        {
            if (list == null || list.Count == 0) return "0";
            return string.Join("|", list.Select(SerializeEntity));
        }

        private static string SerializeTags(Dictionary<int, int> tags)
        {
            if (tags == null || tags.Count == 0) return "";
            return string.Join("&", tags.Select(kv => kv.Key + "=" + kv.Value));
        }

        private static string B(bool v)
        {
            return v ? "True" : "False";
        }

        private static bool HasCoreEntity(EntityData entity)
        {
            return entity != null
                && entity.EntityId > 0
                && !string.IsNullOrWhiteSpace(entity.CardId);
        }
    }
}
