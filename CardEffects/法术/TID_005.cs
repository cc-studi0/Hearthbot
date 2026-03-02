using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TID_005 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "TID_005",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("summon", v: 0, atk: 5, hp: 4, n: 1, dur: 0, useSP: false),
                new EffectDef("summon", v: 0, atk: 5, hp: 4, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
