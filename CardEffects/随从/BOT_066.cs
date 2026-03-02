using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_BOT_066 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "BOT_066",
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("summon", v: 0, atk: 7, hp: 7, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
