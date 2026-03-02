using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_SW_432 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "SW_432",
            new TriggerDef(
                "Spell",
                "AnyMinion",
                new EffectDef("buff", v: 0, atk: 4, hp: 2, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
