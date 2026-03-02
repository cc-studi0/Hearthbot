using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_RLK_051 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "RLK_051",
            new TriggerDef(
                "Spell",
                "FriendlyMinion",
                new EffectDef("buff", v: 0, atk: 0, hp: 5, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
