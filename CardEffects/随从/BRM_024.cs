using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_BRM_024 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "BRM_024",
            new TriggerDef(
                "Battlecry",
                "FriendlyMinion",
                new EffectDef("buff", v: 0, atk: 3, hp: 3, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
