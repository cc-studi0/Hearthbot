using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_REV_936 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "REV_936",
            new TriggerDef(
                "Battlecry",
                "None",
                new EffectDef("summon", v: 0, atk: 3, hp: 5, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
