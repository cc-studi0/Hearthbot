using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TIME_447 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "TIME_447",
            new TriggerDef(
                "Spell",
                "AnyCharacter",
                new EffectDef("buff", v: 0, atk: 0, hp: 2, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
