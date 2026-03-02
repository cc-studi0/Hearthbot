using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_DMF_067 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "CORE_DMF_067",
            new TriggerDef(
                "Battlecry",
                "None",
                new EffectDef("draw", v: 0, atk: 0, hp: 0, n: 1, dur: 0, useSP: false)
            ),
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("draw", v: 0, atk: 0, hp: 0, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
