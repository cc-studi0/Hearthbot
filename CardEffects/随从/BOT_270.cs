using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_BOT_270 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "BOT_270",
            new TriggerDef(
                "Battlecry",
                "None",
                new EffectDef("summon", v: 0, atk: 1, hp: 2, n: 1, dur: 0, useSP: false),
                new EffectDef("summon", v: 0, atk: 1, hp: 2, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
