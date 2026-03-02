using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_RLK_592 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "RLK_592",
            new TriggerDef(
                "Battlecry",
                "FriendlyMinion",
                new EffectDef("buff", v: 0, atk: 5, hp: 5, n: 1, dur: 0, useSP: false)
            ),
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("buff", v: 0, atk: 5, hp: 5, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
