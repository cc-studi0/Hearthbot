using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_REV_351 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "REV_351",
            new TriggerDef(
                "Battlecry",
                "FriendlyMinion",
                new EffectDef("buff", v: 0, atk: 2, hp: 0, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
