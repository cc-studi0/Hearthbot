using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DEEP_017 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "DEEP_017",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("summon", v: 0, atk: 1, hp: 1, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
