// 标准法术效果 — 4-10费法术
using System;
using System.Collections.Generic;
using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;
using H = BotMain.AI.CardEffectsScripts.CardEffectScriptHelpers;

namespace BotMain.AI.CardEffectsScripts
{
    // ═══ 4费 ═══
    // 火球术 6伤
    public sealed class Spell_CS2_029 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_CS2_029",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,6);},BattlecryTargetType.AnyCharacter); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 暗言术：毁 消灭5+攻随从
    public sealed class Spell_EX1_197 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_EX1_197",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.FriendMinions.Concat(b.EnemyMinions).Where(m=>m.Atk>=5).ToArray())m.Health=0;}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Destroy); } }
    // 刺杀 消灭敌随从
    public sealed class Spell_CS2_076 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_CS2_076",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null)t.Health=0;},BattlecryTargetType.EnemyMinion); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Destroy); } }
    // 灵魂虹吸 消灭随从+恢3
    public sealed class Spell_EX1_309 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_EX1_309",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null)t.Health=0;if(b.FriendHero!=null){int h=Math.Min(3,b.FriendHero.MaxHealth-b.FriendHero.Health);b.FriendHero.Health+=h;}},BattlecryTargetType.AnyMinion); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Destroy|EffectKind.Heal); } }
    // 灵界打击 吸6伤
    public sealed class Spell_RLK_024 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("RLK_024",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null){CardEffectDB.Dmg(b,t,6);if(b.FriendHero!=null){int h=Math.Min(6,b.FriendHero.MaxHealth-b.FriendHero.Health);b.FriendHero.Health+=h;}}},BattlecryTargetType.AnyMinion); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 虚空碎片 吸4伤
    public sealed class Spell_SW_442 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_SW_442",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null){CardEffectDB.Dmg(b,t,4);if(b.FriendHero!=null){int h=Math.Min(4,b.FriendHero.MaxHealth-b.FriendHero.Health);b.FriendHero.Health+=h;}}},BattlecryTargetType.AnyCharacter); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 恶魔来袭 3伤+召2x1/3嘲讽
    public sealed class Spell_SW_088 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_SW_088",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,3);for(int i=0;i<2&&b.FriendMinions.Count<7;i++)b.FriendMinions.Add(new SimEntity{Atk=1,Health=3,MaxHealth=3,IsTaunt=true,IsFriend=true,IsTired=true,Type=Card.CType.MINION});},BattlecryTargetType.AnyCharacter); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage|EffectKind.Summon); } }
    // 强效治疗药水 恢12+抽
    public sealed class Spell_CFM_604 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_CFM_604",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null&&t.IsFriend){int h=Math.Min(12,t.MaxHealth-t.Health);t.Health+=h;}CardEffectDB.DrawCard(b,b.FriendDeckCards);},BattlecryTargetType.FriendlyOnly); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Heal|EffectKind.Draw); } }
    // 审判恶徒 HP=1+所有敌人1伤
    public sealed class Spell_TTN_853 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("TTN_853",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null){t.Health=1;t.MaxHealth=1;}foreach(var m in b.EnemyMinions.ToArray())CardEffectDB.Dmg(b,m,1);if(b.EnemyHero!=null)CardEffectDB.Dmg(b,b.EnemyHero,1);},BattlecryTargetType.EnemyMinion); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 冷酷严冬 敌方2伤+抽
    public sealed class Spell_RLK_709 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("RLK_709",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.EnemyMinions.ToArray())CardEffectDB.Dmg(b,m,2);if(b.EnemyHero!=null)CardEffectDB.Dmg(b,b.EnemyHero,2);CardEffectDB.DrawCard(b,b.FriendDeckCards);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage|EffectKind.Draw); } }
    // 遥控狂潮 召6x1/1
    public sealed class Spell_TOY_354 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("TOY_354",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{int cnt=0;for(int i=0;i<6&&b.FriendMinions.Count<7;i++){b.FriendMinions.Add(new SimEntity{Atk=1,Health=1,MaxHealth=1,HasRush=true,IsFriend=true,Type=Card.CType.MINION});cnt++;}int extra=6-cnt;if(extra>0)foreach(var m in b.FriendMinions.TakeLast(cnt))H.Buff(m,extra,extra);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Summon); } }
    // 霜巫十字绣 3伤
    public sealed class Spell_TOY_377 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("TOY_377",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,3);},BattlecryTargetType.AnyCharacter); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 平心静气 敌方-2攻+消灭0攻
    public sealed class Spell_TTN_483 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("TTN_483",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.EnemyMinions.ToArray()){m.Atk=Math.Max(0,m.Atk-2);if(m.Atk<=0)m.Health=0;}}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 加大音量 抽3法术
    public sealed class Spell_ETC_205 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("ETC_205",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int i=0;i<3;i++)CardEffectDB.DrawCard(b,b.FriendDeckCards);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Draw); } }
    // 假期规划 恢4+召3x1/1+抽2
    public sealed class Spell_WORK_003 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("WORK_003",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int i=0;i<3&&b.FriendMinions.Count<7;i++)b.FriendMinions.Add(new SimEntity{Atk=1,Health=1,MaxHealth=1,IsFriend=true,IsTired=true,Type=Card.CType.MINION});CardEffectDB.DrawCard(b,b.FriendDeckCards);CardEffectDB.DrawCard(b,b.FriendDeckCards);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Draw|EffectKind.Summon); } }
    // 橡树的召唤 6护甲+召牌库4-随从
    public sealed class Spell_LOOT_309 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_LOOT_309",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(b.FriendHero!=null)b.FriendHero.Armor+=6;if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{Atk=3,Health=3,MaxHealth=3,IsFriend=true,IsTired=true,Type=Card.CType.MINION});}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Armor|EffectKind.Summon); } }
    // 苏打火山 吸10伤分配
    public sealed class Spell_TOY_500 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("TOY_500",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int i=0;i<10;i++){var ts2=b.FriendMinions.Concat(b.EnemyMinions).Where(m=>m.Health>0).ToList();if(ts2.Count==0)break;CardEffectDB.Dmg(b,ts2[H.PickIndex(ts2.Count,b,s)],1);}}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 合约细则 全随从4伤
    public sealed class Spell_WORK_008 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("WORK_008",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.FriendMinions.Concat(b.EnemyMinions).ToArray())CardEffectDB.Dmg(b,m,4);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 其余4费批量
    public sealed class Spell_4Cost_Bulk : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach(var n in new[]{
                "CORE_SW_085","CATA_489","MIS_305","ETC_413","CATA_479","TTN_463",
                "RLK_118","CORE_RLK_118","ETC_316","ETC_387","TOY_640","TOY_716",
                "MIS_903","WORK_005","WORK_004","RLK_707","YOG_502","MIS_100",
                "ETC_384","TOY_800","CATA_567","MIS_709","TTN_908","VAC_445",
                "YOG_509","CATA_820","TOY_374","VAC_526","VAC_324","WORK_028",
                "YOG_513","VAC_952","WORK_001","CATA_569","TOY_915","CATA_160",
                "ETC_336","VAC_464","VAC_959","ETC_080","TTN_714","TTN_717",
                "TTN_076","VAC_432","VAC_437","CATA_470","TTN_751","CATA_780",
                "VAC_420","CATA_186","TOY_513","VAC_935","JAM_034","VAC_333",
                "RLK_062","CORE_RLK_062","ETC_108","TOY_391","TOY_388"
            })
            { if(!Enum.TryParse(n,true,out C id))continue; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{}); }
        }
    }

    // ═══ 5费 ═══
    // 绝命乱斗 随机保留1
    public sealed class Spell_EX1_407 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_EX1_407",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{var all=b.FriendMinions.Concat(b.EnemyMinions).ToList();if(all.Count<=1)return;var keep=all[H.PickIndex(all.Count,b,s)];foreach(var m in all.Where(m=>m!=keep))m.Health=0;}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Destroy); } }
    // 划水好友 抉择6/6嘲讽或6x1/1突袭
    public sealed class Spell_TSC_650 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_TSC_650",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{Atk=6,Health=6,MaxHealth=6,IsTaunt=true,IsFriend=true,IsTired=true,Type=Card.CType.MINION});}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Summon); } }
    // 剑龙骑术 +2/+6嘲讽
    public sealed class Spell_UNG_952 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_UNG_952",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null){t.Atk+=2;t.Health+=6;t.MaxHealth+=6;t.IsTaunt=true;}},BattlecryTargetType.FriendlyMinion); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Buff); } }
    // 雷电崩鸣 敌方3伤
    public sealed class Spell_TTN_831 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("TTN_831",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.EnemyMinions.ToArray())CardEffectDB.Dmg(b,m,3);if(b.EnemyHero!=null)CardEffectDB.Dmg(b,b.EnemyHero,3);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 邪爆 1伤循环直到全灭
    public sealed class Spell_RLK_035 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_RLK_035",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int r=0;r<20;r++){var alive=b.FriendMinions.Concat(b.EnemyMinions).Where(m=>m.Health>0).ToList();if(alive.Count==0)break;foreach(var m in alive)CardEffectDB.Dmg(b,m,1);if(alive.All(m=>m.Health<=0))break;}}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 冰霜摆件 召2x2/4嘲讽(+4护甲亡语)
    public sealed class Spell_VAC_305 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("VAC_305",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int i=0;i<2&&b.FriendMinions.Count<7;i++)b.FriendMinions.Add(new SimEntity{Atk=2,Health=4,MaxHealth=4,IsTaunt=true,HasDeathrattle=true,IsFriend=true,IsTired=true,Type=Card.CType.MINION});}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Summon); } }
    // 林中奇遇 召2x2/5嘲讽
    public sealed class Spell_TOY_804 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("TOY_804",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int i=0;i<2&&b.FriendMinions.Count<7;i++)b.FriendMinions.Add(new SimEntity{Atk=2,Health=5,MaxHealth=5,IsTaunt=true,IsFriend=true,IsTired=true,Type=Card.CType.MINION});}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Summon); } }
    // 加工失误 抽3
    public sealed class Spell_TOY_371 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("TOY_371",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int i=0;i<3;i++)CardEffectDB.DrawCard(b,b.FriendDeckCards);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Draw); } }
    // 其余5费批量
    public sealed class Spell_5Cost_Bulk : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach(var n in new[]{
                "ETC_356","ETC_365","ETC_417","ETC_838","CATA_533","JAM_018",
                "MIS_701","CATA_610","CATA_308","TTN_085","TTN_841","VAC_416",
                "CATA_471","TTN_855t","ETC_362","RLK_060","CATA_978","JAM_002",
                "RLK_730","ETC_506","ETC_085","ETC_428","TOY_383","VAC_443",
                "TOY_652","TOY_341","MIS_914","VAC_519","CATA_722","VAC_423",
                "TOY_521","TOY_385","VAC_529","TOY_877"
            })
            { if(!Enum.TryParse(n,true,out C id))continue; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{}); }
        }
    }

    // ═══ 6费 ═══
    // 暴风雪 敌随从2伤+冻结
    public sealed class Spell_CS2_028 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_CS2_028",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.EnemyMinions.ToArray()){CardEffectDB.Dmg(b,m,2);m.IsFrozen=true;}}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 圣光炸弹 等攻伤害
    public sealed class Spell_GVG_008 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_GVG_008",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.FriendMinions.Concat(b.EnemyMinions).ToArray())CardEffectDB.Dmg(b,m,m.Atk);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 紧壳商品 召2x2/7嘲讽
    public sealed class Spell_SW_429 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_SW_429",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int i=0;i<2&&b.FriendMinions.Count<7;i++)b.FriendMinions.Add(new SimEntity{Atk=2,Health=7,MaxHealth=7,IsTaunt=true,IsFriend=true,IsTired=true,Type=Card.CType.MINION});}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Summon); } }
    // 其余6费
    public sealed class Spell_6Cost_Bulk : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach(var n in new[]{
                "VAC_431","VAC_915","VAC_417","VAC_926","CATA_491","TOY_506",
                "TOY_602","ETC_082","VAC_410","CATA_156","CATA_581","ETC_314",
                "CATA_497","WORK_063","TTN_487","VAC_945","CATA_213","VAC_506",
                "TOY_373","ETC_386","CATA_552"
            })
            { if(!Enum.TryParse(n,true,out C id))continue; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{}); }
        }
    }

    // ═══ 7费 ═══
    // 烈焰风暴 敌随从5伤
    public sealed class Spell_CS2_032 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_CS2_032",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.EnemyMinions.ToArray())CardEffectDB.Dmg(b,m,5);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 火焰之地传送门 6伤+召6费随从
    public sealed class Spell_KAR_076 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_KAR_076",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,6);if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{Atk=5,Health=5,MaxHealth=5,IsFriend=true,IsTired=true,Type=Card.CType.MINION});},BattlecryTargetType.AnyCharacter); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage|EffectKind.Summon); } }
    // 永存石中 召4/8+2/4+1/2嘲讽
    public sealed class Spell_TSC_076 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_TSC_076",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var st in new[]{(4,8),(2,4),(1,2)}){if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{Atk=st.Item1,Health=st.Item2,MaxHealth=st.Item2,IsTaunt=true,IsFriend=true,IsTired=true,Type=Card.CType.MINION});}}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Summon); } }
    // 冰霜巨龙之怒 5伤+冻全敌+召5/5
    public sealed class Spell_RLK_063 : ICardEffectScript
    { public void Register(CardEffectDB db) { foreach(var n in new[]{"RLK_063","CORE_RLK_063"}){if(!Enum.TryParse(n,true,out C id))continue; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,5);foreach(var m in b.EnemyMinions)m.IsFrozen=true;if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{Atk=5,Health=5,MaxHealth=5,IsFriend=true,IsTired=true,Type=Card.CType.MINION});},BattlecryTargetType.AnyCharacter); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage|EffectKind.Summon);} } }
    // 其余7费
    public sealed class Spell_7Cost_Bulk : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach(var n in new[]{
                "WORK_012","TTN_470","ETC_373","ETC_370","TOY_372","TOY_877",
                "TTN_480","TOY_879","TOY_808","CATA_553","VAC_415","ETC_541",
                "VAC_702","ETC_409","CORE_GIL_598","VAC_301","VAC_321","CATA_591"
            })
            { if(!Enum.TryParse(n,true,out C id))continue; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{}); }
        }
    }

    // ═══ 8+费 ═══
    // 海啸 召3x3/6冻结+攻击
    public sealed class Spell_VAC_509 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("VAC_509",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int i=0;i<3&&b.FriendMinions.Count<7;i++)b.FriendMinions.Add(new SimEntity{Atk=3,Health=6,MaxHealth=6,IsFriend=true,Type=Card.CType.MINION});}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Summon); } }
    // 其余8+费
    public sealed class Spell_8Plus_Bulk : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach(var n in new[]{
                "VAC_948","VAC_700","ETC_208","TOY_529","CATA_465","VAC_907",
                "TOY_519","TTN_954","TOY_884","CATA_568","RLK_122","VAC_558",
                "ETC_210","TOY_883","TOY_378","CATA_452","TOY_960","TTN_441"
            })
            { if(!Enum.TryParse(n,true,out C id))continue; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{}); }
        }
    }
}
