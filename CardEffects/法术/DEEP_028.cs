using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DEEP_028 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "DEEP_028",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("summon", v: 0, atk: 3, hp: 7, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
