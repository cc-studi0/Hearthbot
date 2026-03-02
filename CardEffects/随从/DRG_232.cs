using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DRG_232 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "DRG_232",
            new TriggerDef(
                "Battlecry",
                "None",
                new EffectDef("equip", v: 0, atk: 4, hp: 0, n: 1, dur: 2, useSP: false)
            )
            );
        }
    }
}
