using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_ULD_715 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "ULD_715",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("equip", v: 0, atk: 2, hp: 0, n: 1, dur: 2, useSP: false)
            )
            );
        }
    }
}
