using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_ETC_364 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "ETC_364",
            new TriggerDef(
                "Spell",
                "FriendlyMinion",
                new EffectDef("buff", v: 0, atk: 3, hp: 3, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
