using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_BT_132 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "BT_132",
            new TriggerDef(
                "Spell",
                "AnyMinion",
                new EffectDef("buff", v: 0, atk: 1, hp: 3, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
