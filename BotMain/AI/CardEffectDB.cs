using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI
{
    public enum EffectTrigger { Battlecry, Deathrattle, Spell, EndOfTurn, Aura, LocationActivation }

    public class CardEffectDB
    {
        private readonly Dictionary<(Card.Cards, EffectTrigger), Action<SimBoard, SimEntity, SimEntity>> _db = new();
        private readonly Dictionary<(Card.Cards, EffectTrigger), BattlecryTargetType> _targetTypes = new();

        public void Register(Card.Cards id, EffectTrigger t, Action<SimBoard, SimEntity, SimEntity> fn)
            => _db[(id, t)] = fn;

        public void RegisterTargetType(Card.Cards id, EffectTrigger t, BattlecryTargetType targetType)
            => _targetTypes[(id, t)] = targetType;

        public void Register(Card.Cards id, EffectTrigger t, Action<SimBoard, SimEntity, SimEntity> fn, BattlecryTargetType targetType)
        {
            _db[(id, t)] = fn;
            if (targetType != BattlecryTargetType.None)
                _targetTypes[(id, t)] = targetType;
        }

        public bool TryGet(Card.Cards id, EffectTrigger t, out Action<SimBoard, SimEntity, SimEntity> fn)
            => _db.TryGetValue((id, t), out fn);

        public bool TryGetTargetType(Card.Cards id, EffectTrigger t, out BattlecryTargetType targetType)
            => _targetTypes.TryGetValue((id, t), out targetType);

        public bool Has(Card.Cards id, EffectTrigger t) => _db.ContainsKey((id, t));

        public int Count => _db.Count;

        public static CardEffectDB BuildDefault()
        {
            var db = new CardEffectDB();
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1. 从 effects_data.json 加载模板化效果（AOE/伤害/治疗等批量卡牌）
            var jsonPath = System.IO.Path.Combine(baseDir, "effects_data.json");
            if (!System.IO.File.Exists(jsonPath))
                jsonPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "..", "effects_data.json");
            CardEffectLoader.LoadFromJson(db, jsonPath);

            // 2. 从 CardEffects/ 目录加载单卡效果文件（每张卡一个 JSON）
            var cardEffectsDir = System.IO.Path.Combine(baseDir, "CardEffects");
            if (!System.IO.Directory.Exists(cardEffectsDir))
                cardEffectsDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "..", "CardEffects");
            CardEffectFileLoader.LoadFromDirectory(db, cardEffectsDir);

            // 3. 注册 C# 硬编码的复杂效果（会覆盖前两步的同卡牌注册）
            RegisterOverrides(db);
            return db;
        }

        // 供 CardEffectLoader 调用的辅助方法
        internal static int SP(SimBoard b) { int sp=0; foreach(var m in b.FriendMinions) sp+=m.SpellPower; return sp; }
        internal static void Dmg(SimBoard b, SimEntity t, int d) { if(t==null||t.IsImmune||d<=0) return; if(t.IsDivineShield){t.IsDivineShield=false;return;} t.Health-=d; }
        internal static void DoSilence(SimEntity t) { if(t==null) return; t.IsTaunt=false;t.IsDivineShield=false;t.HasPoison=false;t.IsLifeSteal=false;t.IsWindfury=false;t.HasReborn=false;t.IsSilenced=true;t.SpellPower=0; }
        private static void Silence(SimEntity t) => DoSilence(t);

        /// <summary>
        /// 所有卡牌效果已迁移到 CardEffects/*.json 文件
        /// 仅保留此方法用于无法用 JSON 表达的极少数复杂效果
        /// </summary>
        private static void RegisterOverrides(CardEffectDB db)
        {
            // 所有效果已迁移到 CardEffects/ 目录下的独立 JSON 文件
        }
    }
}
