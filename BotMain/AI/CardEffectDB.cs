using System.Collections.Generic;
using SmartBot.Plugins.API;

namespace BotMain.AI
{
    public enum EffectTrigger
    {
        Battlecry,
        Deathrattle,
        Spell,
        EndOfTurn,
        Aura,
        LocationActivation,
        AfterAttackMinion,
        AfterDamaged
    }

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
            CardEffectScriptLoader.LoadAll(db);
            return db;
        }

        // 供卡牌效果脚本运行时调用的辅助方法
        internal static int SP(SimBoard b) { int sp=0; foreach(var m in b.FriendMinions) sp+=m.SpellPower; return sp; }
        internal static void Dmg(SimBoard b, SimEntity t, int d) { if(t==null||t.IsImmune||d<=0) return; if(t.IsDivineShield){t.IsDivineShield=false;return;} t.Health-=d; }
        internal static void DoSilence(SimEntity t) { if(t==null) return; t.IsTaunt=false;t.IsDivineShield=false;t.HasPoison=false;t.IsLifeSteal=false;t.IsWindfury=false;t.HasReborn=false;t.IsSilenced=true;t.SpellPower=0; }
        private static void Silence(SimEntity t) => DoSilence(t);
    }
}
