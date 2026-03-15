// ═══════════════════════════════════════════════════════════════
//  标准法术效果 — 2费法术
// ═══════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;
using H = BotMain.AI.CardEffectsScripts.CardEffectScriptHelpers;
using R = BotMain.AI.CardEffectScriptRuntime;

namespace BotMain.AI.CardEffectsScripts
{
    // ═══ 2费 直伤法术 ═══
    // 寒冰箭 3伤+冻结
    public sealed class Spell_CORE_CS2_024 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_CS2_024",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null){CardEffectDB.Dmg(b,t,3);t.IsFrozen=true;}}, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 刺骨 2伤 连击4伤
    public sealed class Spell_CORE_EX1_124 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_124",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,4);}, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 愤怒 抉择3伤/1伤+抽牌 → 近似3伤
    public sealed class Spell_CORE_EX1_154 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_154",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,3);}, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 怒袭 3伤+3护甲
    public sealed class Spell_CORE_AT_064 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_AT_064",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,3);if(b.FriendHero!=null)b.FriendHero.Armor+=3;}, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 快速射击 3伤
    public sealed class Spell_CORE_BRM_013 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BRM_013",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,3);}, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 净化吐息 对随从5伤
    public sealed class Spell_CATA_303 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_303",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,5);}, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 吸取灵魂 吸血3伤
    public sealed class Spell_CORE_ICC_055 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_ICC_055",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null){CardEffectDB.Dmg(b,t,3);if(b.FriendHero!=null){int h=Math.Min(3,b.FriendHero.MaxHealth-b.FriendHero.Health);b.FriendHero.Health+=h;}}}, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 冰霜打击 对随从3伤
    public sealed class Spell_RLK_025 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_025",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,3);}, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 凋零打击 3伤 死→召2/2突袭
    public sealed class Spell_RLK_018 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_018",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>
            {
                if(t!=null){CardEffectDB.Dmg(b,t,3);if(t.Health<=0 && b.FriendMinions.Count<7)
                    b.FriendMinions.Add(new SimEntity{Atk=2,Health=2,MaxHealth=2,HasRush=true,IsFriend=true,Type=Card.CType.MINION});}
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage|EffectKind.Summon);
        }
    }
    // 触须缠握 3伤
    public sealed class Spell_YOG_526 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_526",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,3);}, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 献祭光环 对所有随从1伤两次
    public sealed class Spell_CORE_BT_514 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BT_514",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>
            {
                for(int wave=0;wave<2;wave++){foreach(var m in b.FriendMinions.Concat(b.EnemyMinions).ToArray())CardEffectDB.Dmg(b,m,1);}
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 布洛克斯加的奋战 对所有随从1伤 死后抽牌
    public sealed class Spell_CATA_526 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_526",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>
            {
                int died=0;foreach(var m in b.FriendMinions.Concat(b.EnemyMinions).ToArray()){CardEffectDB.Dmg(b,m,1);if(m.Health<=0)died++;}
                for(int i=0;i<died;i++)CardEffectDB.DrawCard(b,b.FriendDeckCards);
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage|EffectKind.Draw);
        }
    }
    // 灼热裂隙 对所有随从1伤+英雄+3攻
    public sealed class Spell_CATA_582 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_582",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>
            {
                foreach(var m in b.FriendMinions.Concat(b.EnemyMinions).ToArray())CardEffectDB.Dmg(b,m,1);
                if(b.FriendHero!=null)b.FriendHero.Atk+=3;
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 玩具故障 3伤分配到敌随从
    public sealed class Spell_MIS_107 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("MIS_107",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>
            {
                for(int i=0;i<3;i++){var ts2=b.EnemyMinions.Where(m=>m.Health>0).ToList();if(ts2.Count==0)break;CardEffectDB.Dmg(b,ts2[H.PickIndex(ts2.Count,b,s)],1);}
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 麦芽岩浆 对所有敌人1伤
    public sealed class Spell_VAC_323 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_323",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>
            {
                foreach(var m in b.EnemyMinions.ToArray())CardEffectDB.Dmg(b,m,1);
                if(b.EnemyHero!=null)CardEffectDB.Dmg(b,b.EnemyHero,1);
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 暮光洪流 友方恢复6/敌方3伤
    public sealed class Spell_YOG_508 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_508",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null&&!t.IsFriend)CardEffectDB.Dmg(b,t,3);else if(t!=null){int h=Math.Min(6,t.MaxHealth-t.Health);t.Health+=h;}}, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage|EffectKind.Heal);
        }
    }
    // 把经理叫来 2伤
    public sealed class Spell_VAC_460 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_460",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null)CardEffectDB.Dmg(b,t,2);}, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 渐强声浪 受疲劳伤+同伤全敌
    public sealed class Spell_ETC_069 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_069",true,out C id)) return; db.Register(id, EffectTrigger.Spell, (b,s,t)=>{}); } }

    // ═══ 2费 抽牌/护甲/BUFF ═══
    // 盾牌格挡 5护甲+抽牌
    public sealed class Spell_CORE_EX1_606 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_606",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(b.FriendHero!=null)b.FriendHero.Armor+=5;CardEffectDB.DrawCard(b,b.FriendDeckCards);});
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw);
        }
    }
    // 刀扇 对敌随从1伤+抽牌
    public sealed class Spell_CORE_EX1_129 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_129",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{foreach(var m in b.EnemyMinions.ToArray())CardEffectDB.Dmg(b,m,1);CardEffectDB.DrawCard(b,b.FriendDeckCards);});
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage|EffectKind.Draw);
        }
    }
    // 混乱打击 英雄+2攻+抽牌
    public sealed class Spell_CORE_BT_035 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BT_035",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(b.FriendHero!=null)b.FriendHero.Atk+=2;CardEffectDB.DrawCard(b,b.FriendDeckCards);});
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw);
        }
    }
    // 安全护目镜 6护甲
    public sealed class Spell_TOY_907 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_907",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(b.FriendHero!=null)b.FriendHero.Armor+=6;});
        }
    }
    // 圣光闪现 恢复4+抽牌
    public sealed class Spell_CORE_TRL_307 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_TRL_307",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(b.FriendHero!=null){int h=Math.Min(4,b.FriendHero.MaxHealth-b.FriendHero.Health);b.FriendHero.Health+=h;}CardEffectDB.DrawCard(b,b.FriendDeckCards);});
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Heal|EffectKind.Draw);
        }
    }
    // 阿达尔之手 +2/+1+抽牌
    public sealed class Spell_CORE_BT_292 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BT_292",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null){t.Atk+=2;t.Health+=1;t.MaxHealth+=1;}CardEffectDB.DrawCard(b,b.FriendDeckCards);}, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Buff|EffectKind.Draw);
        }
    }
    // 野性印记 +2/+3嘲讽
    public sealed class Spell_CORE_CS2_009 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_CS2_009",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null){t.Atk+=2;t.Health+=3;t.MaxHealth+=3;t.IsTaunt=true;}}, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Buff);
        }
    }
    // 幽灵视觉 抽牌 流放再抽
    public sealed class Spell_CORE_BT_491 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BT_491",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{CardEffectDB.DrawCard(b,b.FriendDeckCards);CardEffectDB.DrawCard(b,b.FriendDeckCards);});
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw);
        }
    }
    // 质量保证 抽2嘲讽
    public sealed class Spell_TOY_605 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_605",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{CardEffectDB.DrawCard(b,b.FriendDeckCards);CardEffectDB.DrawCard(b,b.FriendDeckCards);});
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw);
        }
    }
    // 掌声雷动 抽1+每不同类型再抽
    public sealed class Spell_ETC_372 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_372",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{for(int i=0;i<2;i++)CardEffectDB.DrawCard(b,b.FriendDeckCards);});
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw);
        }
    }
    // 吃掉小鬼 消灭友随从+抽3
    public sealed class Spell_VAC_939 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_939",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null&&t.IsFriend)t.Health=0;for(int i=0;i<3;i++)CardEffectDB.DrawCard(b,b.FriendDeckCards);}, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw);
        }
    }
    // 对照鳞摹 抽2龙
    public sealed class Spell_TOY_387 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_387",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{CardEffectDB.DrawCard(b,b.FriendDeckCards);CardEffectDB.DrawCard(b,b.FriendDeckCards);});
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw);
        }
    }
    // 拼布好朋友 获取3伙伴
    public sealed class Spell_TOY_353 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_353",true,out C id)) return; db.Register(id, EffectTrigger.Spell, (b,s,t)=> b.FriendCardDraw += 3); db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw); } }
    // 生而平等 所有随从HP=1
    public sealed class Spell_CORE_EX1_619 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_619",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{foreach(var m in b.FriendMinions.Concat(b.EnemyMinions).ToArray()){m.Health=1;m.MaxHealth=1;}});
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
    // 保安!! 召2个1/1突袭 流放再+1
    public sealed class Spell_ETC_411 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_411",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>
            {
                for(int i=0;i<3&&b.FriendMinions.Count<7;i++)
                    b.FriendMinions.Add(new SimEntity{Atk=1,Health=1,MaxHealth=1,HasRush=true,IsFriend=true,Type=Card.CType.MINION});
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Summon);
        }
    }
    // 野性之力 抉择全随从+1/+1或召3/2
    public sealed class Spell_CORE_EX1_160 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_160",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{foreach(var m in b.FriendMinions)H.Buff(m,1,1);});
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Buff);
        }
    }
    // 活力分流 手牌随从+1/+1 消耗2残骸再+1/+1
    public sealed class Spell_RLK_712 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach(var n in new[]{"RLK_712","CORE_RLK_712"})
            {
                if(!Enum.TryParse(n,true,out C id))continue;
                db.Register(id, EffectTrigger.Spell, (b,s,t)=>{H.BuffAllMinionsInHand(b,1,1);if(b.FriendExcavateCount>=2){b.FriendExcavateCount-=2;H.BuffAllMinionsInHand(b,1,1);}});
                db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Buff);
            }
        }
    }
    // 净化之力 沉默友随从+1/+2
    public sealed class Spell_TOY_384 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_384",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{foreach(var m in b.FriendMinions)H.Buff(m,1,2);});
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Buff);
        }
    }
    // 森林赠礼 友随从+1/+1 每控制一随从
    public sealed class Spell_CATA_138 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_138",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null&&t.IsFriend)H.Buff(t,b.FriendMinions.Count,b.FriendMinions.Count);}, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Buff);
        }
    }
    // 即兴演奏 友随从+3/+3 其他1伤 过载1
    public sealed class Spell_JAM_013 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("JAM_013",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>
            {
                if(t!=null&&t.IsFriend)H.Buff(t,3,3);
                foreach(var m in b.FriendMinions.Concat(b.EnemyMinions).Where(m=>m!=t).ToArray())CardEffectDB.Dmg(b,m,1);
            }, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Buff|EffectKind.Damage);
        }
    }
    // 兽性癫狂 全体+1攻
    public sealed class Spell_YOG_505 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_505",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{foreach(var m in b.FriendMinions)m.Atk+=1;H.BuffAllMinionsInHand(b,1,0);});
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Buff);
        }
    }
    // 悦耳轻音乐 英雄+2攻+4护甲
    public sealed class Spell_ETC_379 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_379",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(b.FriendHero!=null){b.FriendHero.Atk+=2;b.FriendHero.Armor+=4;}});
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Buff);
        }
    }
    // 咒怨纪念品 +3/+3（有负面效果但模拟器近似不算）
    public sealed class Spell_VAC_944 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_944",true,out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b,s,t)=>{if(t!=null)H.Buff(t,3,3);}, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Buff);
        }
    }

    // ═══ 2费 奥秘 ═══
    public sealed class Spell_2Cost_Secrets : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach(var n in new[]{"CORE_EX1_610","CORE_EX1_611","CORE_AV_226","CORE_GIL_577",
                "TTN_302","TTN_504","JAM_003","MIS_105","TTN_851"})
            { if(!Enum.TryParse(n,true,out C id))continue; db.Register(id, EffectTrigger.Spell, (b,s,t)=>{}); }
        }
    }

    // ═══ 2费 发现/获取类 draw+1 ═══
    public sealed class Spell_2Cost_Discover : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach(var n in new[]{"CS3_028","TTN_735","CORE_UNG_941","VAC_308","ETC_532",
                "ETC_206","VAC_513","TOY_514","TOY_643","TOY_037","YOG_507","TTN_845",
                "VAC_408","CORE_SCH_158","WORK_070","ETC_083","TOY_645","CATA_785",
                "TOY_851","TOY_826","TOY_822","ETC_338","TTN_728","CATA_561","CATA_530",
                "TTN_955","TTN_430","ETC_717","CORE_EDR_002","VAC_508","CATA_203",
                "WORK_024","VAC_925","CATA_496","WORK_007","CATA_499","YOG_522",
                "YOG_526","MIS_902","JAM_025","JAM_008","CATA_557","CORE_RLK_051",
                "RLK_057","ETC_320","TTN_854","TOY_886","TTN_803","TTN_744","ETC_427",
                "WORK_014","WORK_030","RLK_056","JAM_006","VAC_428","CATA_135",
                "TTN_079","VAC_427","TTN_830","CORE_BT_801","ETC_069"})
            { if(!Enum.TryParse(n,true,out C id))continue; db.Register(id, EffectTrigger.Spell, (b,s,t)=>{}); }
        }
    }
}
