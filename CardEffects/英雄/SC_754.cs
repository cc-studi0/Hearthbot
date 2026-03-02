using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_SC_754 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "SC_754",
            new TriggerDef(
                "Battlecry",
                "None",
                new EffectDef("summon", v: 0, atk: 3, hp: 4, n: 1, dur: 0, useSP: false),
                new EffectDef("summon", v: 0, atk: 3, hp: 4, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
