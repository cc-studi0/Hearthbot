using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_ETC_365 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "ETC_365",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("summon", v: 0, atk: 3, hp: 4, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
