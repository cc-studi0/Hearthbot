using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_GIL_130 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "GIL_130",
            new TriggerDef(
                "Battlecry",
                "FriendlyMinion",
                new EffectDef("buff", v: 0, atk: 2, hp: 2, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
