// 标准法术效果 — 3费法术
using System;
using System.Collections.Generic;
using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;
using H = BotMain.AI.CardEffectsScripts.CardEffectScriptHelpers;

namespace BotMain.AI.CardEffectsScripts
{
    // 奥术智慧 抽2
    public sealed class Spell_CS2_023 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_CS2_023",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{CardEffectDB.DrawCard(b,b.FriendDeckCards);CardEffectDB.DrawCard(b,b.FriendDeckCards);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Draw); } }
    // 愤怒之锤 3伤+抽牌
    public sealed class Spell_CS2_094 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_CS2_094",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,3);CardEffectDB.DrawCard(b,b.FriendDeckCards);},BattlecryTargetType.AnyCharacter); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage|EffectKind.Draw); } }
    // 地狱烈焰 全体3伤
    public sealed class Spell_CS2_062 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_CS2_062",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.FriendMinions.Concat(b.EnemyMinions).ToArray())CardEffectDB.Dmg(b,m,3);if(b.FriendHero!=null)CardEffectDB.Dmg(b,b.FriendHero,3);if(b.EnemyHero!=null)CardEffectDB.Dmg(b,b.EnemyHero,3);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 横扫 4伤+其他1伤
    public sealed class Spell_CS2_012 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_CS2_012",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,4);foreach(var m in b.EnemyMinions.Where(m=>m!=t).ToArray())CardEffectDB.Dmg(b,m,1);if(t!=b.EnemyHero&&b.EnemyHero!=null)CardEffectDB.Dmg(b,b.EnemyHero,1);},BattlecryTargetType.EnemyOnly); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 奉献 敌人2伤
    public sealed class Spell_CS2_093 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_CS2_093",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.EnemyMinions.ToArray())CardEffectDB.Dmg(b,m,2);if(b.EnemyHero!=null)CardEffectDB.Dmg(b,b.EnemyHero,2);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 神圣新星 敌随从2伤+友方恢2
    public sealed class Spell_CS1_112 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_CS1_112",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.EnemyMinions.ToArray())CardEffectDB.Dmg(b,m,2);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 闪电风暴 敌随从3伤 过载1
    public sealed class Spell_EX1_259 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_EX1_259",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.EnemyMinions.ToArray())CardEffectDB.Dmg(b,m,3);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 妖术 变形为0/1嘲讽
    public sealed class Spell_EX1_246 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_EX1_246",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null){t.Atk=0;t.Health=1;t.MaxHealth=1;t.IsTaunt=true;t.HasDeathrattle=false;t.HasBattlecry=false;}},BattlecryTargetType.AnyMinion); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Destroy); } }
    // 致命射击 随机消灭敌随从
    public sealed class Spell_EX1_617 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_EX1_617",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{var ts2=b.EnemyMinions.Where(m=>m.Health>0).ToList();if(ts2.Count>0)ts2[H.PickIndex(ts2.Count,b,s)].Health=0;}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Destroy); } }
    // 窒息 消灭攻击力最高敌随从
    public sealed class Spell_RLK_087 : ICardEffectScript
    { public void Register(CardEffectDB db) { foreach(var n in new[]{"RLK_087","CORE_RLK_087"}){if(!Enum.TryParse(n,true,out C id))continue; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{var best=b.EnemyMinions.OrderByDescending(m=>m.Atk).FirstOrDefault();if(best!=null)best.Health=0;}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Destroy);} } }
    // 野性之怒 抉择+4攻/8护甲
    public sealed class Spell_OG_047 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_OG_047",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(b.FriendHero!=null)b.FriendHero.Armor+=8;}); } }
    // 厚重板甲 8护甲
    public sealed class Spell_SW_094 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_SW_094",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(b.FriendHero!=null)b.FriendHero.Armor+=8;}); } }
    // 野性狼魂 召2x2/3嘲讽 过载1
    public sealed class Spell_EX1_248 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_EX1_248",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int i=0;i<2&&b.FriendMinions.Count<7;i++)b.FriendMinions.Add(new SimEntity{Atk=2,Health=3,MaxHealth=3,IsTaunt=true,IsFriend=true,IsTired=true,Type=Card.CType.MINION});}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Summon); } }
    // 动物伙伴 随机召野兽
    public sealed class Spell_NEW1_031 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_NEW1_031",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{Atk=4,Health=2,MaxHealth=2,HasCharge=true,IsFriend=true,Type=Card.CType.MINION});}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Summon); } }
    // 天降蛛群 召3个1/1
    public sealed class Spell_AT_062 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_AT_062",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int i=0;i<3&&b.FriendMinions.Count<7;i++)b.FriendMinions.Add(new SimEntity{Atk=1,Health=1,MaxHealth=1,HasDeathrattle=true,IsFriend=true,IsTired=true,Type=Card.CType.MINION});}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Summon); } }
    // 视界术 抽牌-3费
    public sealed class Spell_CS2_053 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_CS2_053",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>CardEffectDB.DrawCard(b,b.FriendDeckCards)); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Draw); } }
    // 团伙劫掠 抽2海盗(连击+武器)
    public sealed class Spell_TRL_124 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_TRL_124",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int i=0;i<3;i++)CardEffectDB.DrawCard(b,b.FriendDeckCards);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Draw); } }
    // 反魔法护罩 友随从+1/+1扰魔
    public sealed class Spell_RLK_048 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("RLK_048",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.FriendMinions)H.Buff(m,1,1);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Buff); } }
    // 冰川突进 4伤+下法-2费
    public sealed class Spell_RLK_512 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("RLK_512",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,4);},BattlecryTargetType.AnyCharacter); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 凛风冲击 3伤+冻结+其他1伤
    public sealed class Spell_RLK_015 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("RLK_015",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null){CardEffectDB.Dmg(b,t,3);t.IsFrozen=true;}foreach(var m in b.EnemyMinions.Where(m=>m!=t).ToArray())CardEffectDB.Dmg(b,m,1);if(t!=b.EnemyHero&&b.EnemyHero!=null)CardEffectDB.Dmg(b,b.EnemyHero,1);},BattlecryTargetType.EnemyOnly); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 飞速离架 敌随从1伤x手牌龙数
    public sealed class Spell_TOY_714 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("TOY_714",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{int n=Math.Max(1,2);foreach(var m in b.EnemyMinions.ToArray())for(int i=0;i<n;i++)CardEffectDB.Dmg(b,m,1);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 飞翼滑翔 双方抽3 流放只己
    public sealed class Spell_VAC_928 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("VAC_928",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int i=0;i<3;i++)CardEffectDB.DrawCard(b,b.FriendDeckCards);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Draw); } }
    // 炽热火炭 敌方2伤
    public sealed class Spell_VAC_414 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("VAC_414",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.EnemyMinions.ToArray())CardEffectDB.Dmg(b,m,2);if(b.EnemyHero!=null)CardEffectDB.Dmg(b,b.EnemyHero,2);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 致命诛灭 5伤分到敌随从
    public sealed class Spell_TTN_460 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("TTN_460",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int i=0;i<5;i++){var ts2=b.EnemyMinions.Where(m=>m.Health>0).ToList();if(ts2.Count==0)break;CardEffectDB.Dmg(b,ts2[H.PickIndex(ts2.Count,b,s)],1);}}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 星体射击 3伤
    public sealed class Spell_YOG_082 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("YOG_082",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,3);},BattlecryTargetType.AnyCharacter); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 熔火符文 3伤
    public sealed class Spell_TTN_477 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("TTN_477",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,3);b.FriendCardDraw+=1;},BattlecryTargetType.AnyCharacter); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage|EffectKind.Draw); } }
    // 潮流逆转 英雄+3攻+召3/3突袭 过载1
    public sealed class Spell_TTN_722 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("TTN_722",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(b.FriendHero!=null)b.FriendHero.Atk+=3;if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{Atk=3,Health=3,MaxHealth=3,HasRush=true,IsFriend=true,Type=Card.CType.MINION});}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Summon); } }
    // 打卡 英雄+3攻+溅射
    public sealed class Spell_WORK_022 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("WORK_022",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(b.FriendHero!=null)b.FriendHero.Atk+=3;}); } }
    // 银月城传送门 +2/+2+召2费随从
    public sealed class Spell_KAR_077 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_KAR_077",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null)H.Buff(t,2,2);if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{Atk=2,Health=2,MaxHealth=2,IsFriend=true,IsTired=true,Type=Card.CType.MINION});},BattlecryTargetType.FriendlyMinion); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Buff|EffectKind.Summon); } }
    // 作战动员 召3个1/1+装1/4武
    public sealed class Spell_GVG_061 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("CORE_GVG_061",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{for(int i=0;i<3&&b.FriendMinions.Count<7;i++)b.FriendMinions.Add(new SimEntity{Atk=1,Health=1,MaxHealth=1,IsFriend=true,IsTired=true,Type=Card.CType.MINION});b.FriendWeapon=new SimEntity{Atk=1,Health=4,MaxHealth=4,Type=Card.CType.WEAPON};if(b.FriendHero!=null)b.FriendHero.Atk=1;}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Summon); } }
    // 浪潮涌起 全随从2伤(无死→再2伤)
    public sealed class Spell_VAC_953 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("VAC_953",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.FriendMinions.Concat(b.EnemyMinions).ToArray())CardEffectDB.Dmg(b,m,2);}); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 暗弦术：改 -5/-5 0攻消灭
    public sealed class Spell_ETC_305 : ICardEffectScript
    { public void Register(CardEffectDB db) { if(!Enum.TryParse("ETC_305",true,out C id))return; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{if(t!=null){t.Atk-=5;t.Health-=5;t.MaxHealth-=5;if(t.Atk<=0)t.Health=0;}},BattlecryTargetType.AnyMinion); db.RegisterEffectKind(id,EffectTrigger.Spell,EffectKind.Damage); } }
    // 其余3费批量注册
    public sealed class Spell_3Cost_Bulk : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach(var n in new[]{
                "CORE_LOOT_101","CORE_EX1_287","CORE_EX1_289","CORE_BAR_812",
                "TOY_046","ETC_528","MIS_301","MIS_027","ETC_075","ETC_318",
                "ETC_369","ETC_364","MIS_714","JAM_031","YOG_301","CATA_498",
                "MIS_302","CATA_202","VAC_949","TOY_603","TTN_865","CATA_480",
                "ETC_335","TOY_805","ETC_200","MIS_102","ETC_079","TTN_485",
                "ETC_330","CATA_215","TTN_745","CORE_TRL_339","VAC_329","VAC_528",
                "TOY_527","WORK_021","ETC_427","RLK_048","WORK_026","YOG_401",
                "VAC_533","CATA_560","VAC_457","ETC_076","CATA_525","TTN_753",
                "CATA_134","VAC_931","TTN_950","CATA_306","TOY_307","VAC_955",
                "CORE_ULD_209","VAC_523","TTN_712","ETC_113","TTN_718","TOY_809",
                "CATA_566","MIS_006","VAC_332","TOY_054","WORK_027","TOY_520",
                "VAC_957","CATA_697","CORE_BT_801","ETC_370"
            })
            { if(!Enum.TryParse(n,true,out C id))continue; db.Register(id,EffectTrigger.Spell,(b,s,t)=>{}); }
        }
    }
}
