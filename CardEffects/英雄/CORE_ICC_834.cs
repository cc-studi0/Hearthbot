using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_ICC_834 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "CORE_ICC_834",
            new TriggerDef(
                "Battlecry",
                "None",
                new EffectDef("equip", v: 0, atk: 4, hp: 0, n: 1, dur: 3, useSP: false)
            )
            );
        }
    }
}
