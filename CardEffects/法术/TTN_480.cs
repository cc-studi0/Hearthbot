using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TTN_480 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "TTN_480",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("summon", v: 0, atk: 4, hp: 5, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
