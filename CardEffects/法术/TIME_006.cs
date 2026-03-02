using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TIME_006 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "TIME_006",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("summon", v: 0, atk: 0, hp: 4, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
