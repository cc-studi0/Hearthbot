using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_DMF_521 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "CORE_DMF_521",
            new TriggerDef(
                "Battlecry",
                "None",
                new EffectDef("equip", v: 0, atk: 3, hp: 0, n: 1, dur: 2, useSP: false)
            )
            );
        }
    }
}
