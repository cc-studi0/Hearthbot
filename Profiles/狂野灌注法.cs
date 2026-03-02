using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;
using SmartBotAPI.Plugins.API;
using SmartBotAPI.Battlegrounds;
using SmartBot.Plugins.API.Actions;


/* Explanation on profiles :
 * 
 * 配置文件中定义的所有值都是百分比修饰符，这意味着它将影响基本配置文件的默认值。
 * 
 * 修饰符值可以在[-10000;范围内设置。 10000]（负修饰符有相反的效果）
 * 您可以为非全局修改器指定目标，这些目标特定修改器将添加到卡的全局修改器+修改器之上（无目标）
 * 
 * 应用的总修改器=全局修改器+无目标修改器+目标特定修改器
 * 
 * GlobalDrawModifier --->修改器应用于卡片绘制值
 * GlobalWeaponsAttackModifier --->修改器适用于武器攻击的价值，它越高，人工智能攻击武器的可能性就越小
 * 
 * GlobalCastSpellsModifier --->修改器适用于所有法术，无论它们是什么。修饰符越高，AI玩法术的可能性就越小
 * GlobalCastMinionsModifier --->修改器适用于所有仆从，无论它们是什么。修饰符越高，AI玩仆从的可能性就越小
 * 
 * GlobalAggroModifier --->修改器适用于敌人的健康值，越高越好，人工智能就越激进
 * GlobalDefenseModifier --->修饰符应用于友方的健康值，越高，hp保守的将是AI
 * 
 * CastSpellsModifiers --->你可以为每个法术设置个别修饰符，修饰符越高，AI玩法术的可能性越小
 * CastMinionsModifiers --->你可以为每个小兵设置单独的修饰符，修饰符越高，AI玩仆从的可能性越小
 * CastHeroPowerModifier --->修饰符应用于heropower，修饰符越高，AI玩它的可能性就越小
 * 
 * WeaponsAttackModifiers --->适用于武器攻击的修饰符，修饰符越高，AI攻击它的可能性越小
 * 
 * OnBoardFriendlyMinionsValuesModifiers --->修改器适用于船上友好的奴才。修饰语越高，AI就越保守。
 * OnBoardBoardEnemyMinionsModifiers --->修改器适用于船上的敌人。修饰符越高，AI就越会将其视为优先目标。
 *
 */

namespace SmartBotProfiles
{
    [Serializable]
    public class standardeggPaladin  : Profile
    {
#region 英雄技能
        //幸运币
        private const Card.Cards TheCoin = Card.Cards.GAME_005;
        //战士
        private const Card.Cards ArmorUp = Card.Cards.HERO_01bp;
        //萨满
        private const Card.Cards TotemicCall = Card.Cards.HERO_02bp;
        //盗贼
        private const Card.Cards DaggerMastery = Card.Cards.HERO_03bp;
        //圣骑士
        private const Card.Cards Reinforce = Card.Cards.HERO_04bp;
        //猎人
        private const Card.Cards SteadyShot = Card.Cards.HERO_05bp;
        //德鲁伊
        private const Card.Cards Shapeshift = Card.Cards.HERO_06bp;
        //术士
        private const Card.Cards LifeTap = Card.Cards.HERO_07bp;
        //法师
        private const Card.Cards Fireblast = Card.Cards.HERO_08bp;
        //牧师
        private const Card.Cards LesserHeal = Card.Cards.HERO_09bp;
        #endregion

#region 英雄能力优先级
        private readonly Dictionary<Card.Cards, int> _heroPowersPriorityTable = new Dictionary<Card.Cards, int>
        {
            {SteadyShot, 9},//猎人
            {LifeTap, 8},//术士
            {DaggerMastery, 7},//盗贼
            {Reinforce, 5},//骑士
            {Fireblast, 4},//法师
            {Shapeshift, 3},//德鲁伊
            {LesserHeal, 2},//牧师
            {ArmorUp, 1},//战士
        };
        #endregion
 private int GetTag(Card c, Card.GAME_TAG tag)
        {
            if (c.tags != null && c.tags.ContainsKey(tag))
                return c.tags[tag];
            return -1;
        }
         private int GetSireDenathriusDamageCount(Card c)
                {
                    return GetTag(c, Card.GAME_TAG.TAG_SCRIPT_DATA_NUM_2);
                }
#region 直伤卡牌 标准模式
        //直伤法术卡牌（必须是可打脸的伤害） 需要计算法强
        private static readonly Dictionary<Card.Cards, int> _spellDamagesTable = new Dictionary<Card.Cards, int>
        {
            //萨满
            {Card.Cards.CORE_EX1_238, 3},//闪电箭 Lightning Bolt     CORE_EX1_238
            {Card.Cards.DMF_701, 4},//深水炸弹 Dunk Tank     DMF_701
            {Card.Cards.DMF_701t, 4},//深水炸弹 Dunk Tank     DMF_701t
            {Card.Cards.BT_100, 3},//毒蛇神殿传送门 Serpentshrine Portal     BT_100 
            //德鲁伊

            //猎人
            {Card.Cards.BAR_801, 1},//击伤猎物 Wound Prey     BAR_801
            {Card.Cards.CORE_DS1_185, 2},//奥术射击 Arcane Shot     CORE_DS1_185
            {Card.Cards.CORE_BRM_013, 3},//快速射击 Quick Shot     CORE_BRM_013
            {Card.Cards.BT_205, 3},//废铁射击 Scrap Shot     BT_205 
            //法师
            {Card.Cards.BAR_541, 2},//符文宝珠 Runed Orb     BAR_541 
            {Card.Cards.CORE_CS2_029, 6},//火球术 Fireball     CORE_CS2_029
            {Card.Cards.BT_291, 5},//埃匹希斯冲击 Apexis Blast     BT_291 
            //骑士
            {Card.Cards.CORE_CS2_093, 2},//奉献 Consecration     CORE_CS2_093 
            //牧师
            //盗贼
            {Card.Cards.BAR_319, 2},//邪恶挥刺（等级1） Wicked Stab (Rank 1)     BAR_319
            {Card.Cards.BAR_319t, 4},//邪恶挥刺（等级2） Wicked Stab (Rank 2)     BAR_319t
            {Card.Cards.BAR_319t2, 6},//邪恶挥刺（等级3） Wicked Stab (Rank 3)     BAR_319t2 
            {Card.Cards.CORE_CS2_075, 3},//影袭 Sinister Strike     CORE_CS2_075
            {Card.Cards.TSC_086, 4},//剑鱼 TSC_086
            //术士
            {Card.Cards.CORE_CS2_062, 3},//地狱烈焰 Hellfire     CORE_CS2_062
            //战士
            {Card.Cards.DED_006, 6},//重拳先生 DED_006
            //中立
            {Card.Cards.DREAM_02, 5},//伊瑟拉苏醒 Ysera Awakens     DREAM_02
            {Card.Cards.VAC_419, 4},//针灸 VAC_419
            {Card.Cards.VAN_DS1_233, 5},//心灵震爆 VAN_DS1_233
            {Card.Cards.DS1_233, 5},//心灵震爆 DS1_233
        };
        //直伤随从卡牌（必须可以打脸）
        private static readonly Dictionary<Card.Cards, int> _MinionDamagesTable = new Dictionary<Card.Cards, int>
        {
            //盗贼
            {Card.Cards.BAR_316, 2},//油田伏击者 Oil Rig Ambusher     BAR_316 
            //萨满
            {Card.Cards.CORE_CS2_042, 4},//火元素 Fire Elemental     CORE_CS2_042 
            //德鲁伊
            //术士
            {Card.Cards.CORE_CS2_064, 1},//恐惧地狱火 Dread Infernal     CORE_CS2_064 
            //中立
            {Card.Cards.CORE_CS2_189, 1},//精灵弓箭手 Elven Archer     CORE_CS2_189
            {Card.Cards.CS3_031, 8},//生命的缚誓者阿莱克丝塔萨 Alexstrasza the Life-Binder     CS3_031 
            {Card.Cards.DMF_174t, 4},//马戏团医师 Circus Medic     DMF_174t
            {Card.Cards.DMF_066, 2},//小刀商贩 Knife Vendor     DMF_066 
            {Card.Cards.SCH_199t2, 2},//转校生 Transfer Student     SCH_199t2 
            {Card.Cards.SCH_273, 1},//莱斯·霜语 Ras Frostwhisper     SCH_273
            {Card.Cards.BT_187, 3},//凯恩·日怒 Kayn Sunfury     BT_187
            {Card.Cards.BT_717, 2},//潜地蝎 Burrowing Scorpid     BT_717 
            {Card.Cards.CORE_EX1_249, 2},//迦顿男爵 Baron Geddon     CORE_EX1_249 
            {Card.Cards.DMF_254, 30},//迦顿男爵 Baron Geddon     CORE_EX1_249 
            {Card.Cards.RLK_222t2, 14},//火焰使者阿斯塔洛 Astalor, the Flamebringer ID：RLK_222t2
            {Card.Cards.RLK_224, 2},//监督者弗里吉达拉 Overseer Frigidara ID：RLK_224
             {Card.Cards.RLK_063, 5},//冰霜巨龙之怒 Frostwyrm's Fury ID：RLK_063 
            {Card.Cards.RLK_015, 3},//凛风冲击 Howling Blast ID：RLK_015 
            {Card.Cards.RLK_516, 2},//碎骨手斧 Bone Breaker ID：RLK_516
            {Card.Cards.GVG_009, 3},//GVG_009 暗影投弹手
        };
        #endregion
#region 攻击模式和自定义 
      private string _log = "";   // 日志字符串
      private const string ProfileVersion = "2026-01-03.1";
      public ProfileParameters GetParameters(Board board)
      {
            _log = "";
            var p = new ProfileParameters(BaseProfile.Rush) { DiscoverSimulationValueThresholdPercent = -10 }; 

            try
            {
                int enemyHealth = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
                int friendHealth = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
                AddLog($"================ 狂野灌注法 决策日志 v{ProfileVersion} ================");
                AddLog($"敌方血甲: {enemyHealth} | 我方血甲: {friendHealth} | 法力:{board.ManaAvailable} | 手牌:{board.Hand.Count} | 牌库:{board.FriendDeckCount}");
                AddLog("手牌: " + string.Join(", ", board.Hand.Where(x => x != null && x.Template != null)
                    .Select(x => $"{(string.IsNullOrWhiteSpace(x.Template.NameCN) ? x.Template.Name : x.Template.NameCN)}({x.Template.Id}){x.CurrentCost}")));
            }
            catch
            {
                // ignore
            }
            //  增加思考时间
             
            int a =(board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) - BoardHelper.GetEnemyHealthAndArmor(board)>0? (board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) - BoardHelper.GetEnemyHealthAndArmor(board):0;
 					// 德：DRUID 猎：HUNTER 法：MAGE 骑：PALADIN 牧：PRIEST 贼：ROGUE 萨：SHAMAN 术：WARLOCK 战：WARRIOR 瞎：DEMONHUNTER 死：DEATHKNIGHT
          
// 主邏輯：根據職業計算動態攻擊值
switch (board.EnemyClass)
{
    case Card.CClass.PALADIN:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 145, board.EnemyClass);
        break;

    case Card.CClass.DEMONHUNTER:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 135, board.EnemyClass);
        break;

    case Card.CClass.PRIEST:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 155, board.EnemyClass);
        break;

    case Card.CClass.MAGE:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 150, board.EnemyClass);
        break;

    case Card.CClass.DEATHKNIGHT:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 140, board.EnemyClass);
        break;

    case Card.CClass.HUNTER:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 130, board.EnemyClass);
        break;

    case Card.CClass.ROGUE:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 130, board.EnemyClass);
        break;

    case Card.CClass.WARLOCK:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 160, board.EnemyClass);
        break;

    case Card.CClass.SHAMAN:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 125, board.EnemyClass);
        break;

    default:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 150, board.EnemyClass); // 預設職業
        break;
}

// 核心方法：計算動態攻擊值
int CalculateAggroModifier(double baseAggro, int baseValue, Card.CClass enemyClass)
{
    double winRateModifier = GetWinRateModifier(enemyClass);
    double usageRateModifier = GetUsageRateModifier(enemyClass);

    // 最終計算攻擊值
    int finalAggro = (int)(baseAggro * 0.625 + baseValue + winRateModifier + usageRateModifier);
    AddLog($"職業: {enemyClass}, 攻擊值: {finalAggro}, 勝率修正: {winRateModifier}, 使用率修正: {usageRateModifier}");
    return finalAggro;
}

// 方法: 計算勝率修正值
double GetWinRateModifier(Card.CClass enemyClass)
{
    double winRate = GetWinRateFromData(enemyClass);
    return (winRate - 50) * 1.5; // 每超出50%勝率增加1.5點攻擊值
}

// 方法: 計算使用率修正值
double GetUsageRateModifier(Card.CClass enemyClass)
{
    double usageRate = GetUsageRateFromData(enemyClass);
    return usageRate * 0.5; // 每1%使用率增加0.5點攻擊值
}

// 模擬職業勝率數據
double GetWinRateFromData(Card.CClass enemyClass)
{
    switch (enemyClass)
    {
        case Card.CClass.PALADIN: return 52.3;
        case Card.CClass.DEMONHUNTER: return 54.5;
        case Card.CClass.PRIEST: return 50.8;
        case Card.CClass.MAGE: return 51.2;
        case Card.CClass.DEATHKNIGHT: return 53.1;
        case Card.CClass.HUNTER: return 55.2;
        case Card.CClass.ROGUE: return 56.1;
        case Card.CClass.WARLOCK: return 49.7;
        case Card.CClass.SHAMAN: return 48.5;
        default: return 50.0; // 默認勝率
    }
}

// 模擬職業使用率數據
double GetUsageRateFromData(Card.CClass enemyClass)
{
    switch (enemyClass)
    {
        case Card.CClass.PALADIN: return 12.0;
        case Card.CClass.DEMONHUNTER: return 15.0;
        case Card.CClass.PRIEST: return 8.0;
        case Card.CClass.MAGE: return 10.0;
        case Card.CClass.DEATHKNIGHT: return 11.0;
        case Card.CClass.HUNTER: return 14.0;
        case Card.CClass.ROGUE: return 13.0;
        case Card.CClass.WARLOCK: return 7.0;
        case Card.CClass.SHAMAN: return 6.0;
        default: return 10.0; // 默認使用率
    }
}
           

       {
 
        
            int myAttack = 0;
            int enemyAttack = 0;

            if (board.MinionFriend != null)
            {
                for (int x = 0; x < board.MinionFriend.Count; x++)
                {
                    myAttack += board.MinionFriend[x].CurrentAtk;
                }
            }

            if (board.MinionEnemy != null)
            {
                for (int x = 0; x < board.MinionEnemy.Count; x++)
                {
                    enemyAttack += board.MinionEnemy[x].CurrentAtk;
                }
            }

            if (board.WeaponEnemy != null)
            {
                enemyAttack += board.WeaponEnemy.CurrentAtk;
            }

            if ((int)board.EnemyClass == 2 || (int)board.EnemyClass == 7 || (int)board.EnemyClass == 8)
            {
                enemyAttack += 1;
            }
            else if ((int)board.EnemyClass == 6)
            {
                enemyAttack += 2;
            }   
         //定义场攻  用法 myAttack <= 5 自己场攻大于小于5  enemyAttack  <= 5 对面场攻大于小于5  已计算武器伤害

            int myMinionHealth = 0;
            int enemyMinionHealth = 0;

            if (board.MinionFriend != null)
            {
                for (int x = 0; x < board.MinionFriend.Count; x++)
                {
                    myMinionHealth += board.MinionFriend[x].CurrentHealth;
                }
            }

            if (board.MinionEnemy != null)
            {
                for (int x = 0; x < board.MinionEnemy.Count; x++)
                {
                    enemyMinionHealth += board.MinionEnemy[x].CurrentHealth;
                }
            }
            // 友方随从数量
            int friendCount = board.MinionFriend.Count;
            // 手牌数量
            int HandCount = board.Hand.Count;
            // 手牌海盗数量
			int NumberHandPirates=board.Hand.Count(card => card.IsRace(Card.CRace.PIRATE))+board.Hand.Count(card => card.IsRace(Card.CRace.ALL));
            // 场上海盗数量
            int NumPirates=board.MinionFriend.Count(card => card.IsRace(Card.CRace.PIRATE))+board.MinionFriend.Count(card => card.IsRace(Card.CRace.ALL));
            // 除去空降匪徒
            int filterHaidao=NumberHandPirates-board.Hand.Count(x => x.Template.Id == Card.Cards.DRG_056);
            //奥秘数量
            int aomiCount = board.Secret.Count;
            //手上随从数量
            int minionNumber=board.Hand.Count(card => card.Type == Card.CType.MINION);
             // 船载火炮 GVG_075 
            var shipborneArtillery = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.GVG_075);
            //TOY_505	玩具船
            var wanjuchuan = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TOY_505);
            int valuableFollowers=board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Type == Card.CType.MINION)-board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.CFM_637);
            AddLog("死亡随从数"+valuableFollowers);
            // 场上可以攻击的随从数量
						int canAttackCount = board.MinionFriend.Count(x => x.CanAttack);
             // 可攻击海盗数量
            int canAttackPirates=board.MinionFriend.Count(card => card.CanAttack && (card.IsRace(Card.CRace.PIRATE)||card.IsRace(Card.CRace.ALL)));
            
          
 #endregion
 #region 不送的怪
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_938, new Modifier(150));//粗暴的猢狲 VAC_938
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_927, new Modifier(150));//狂飙邪魔 VAC_927
#endregion
#region 送的怪
        
          if(board.HasCardOnBoard(Card.Cards.TSC_962)){//修饰老巨鳍 Gigafin ID：TSC_962 
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TSC_962t, new Modifier(-100)); //修饰老巨鳍之口 Gigafin's Maw ID：TSC_962t 
          }
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.REV_373t, new Modifier(-100)); //具象暗影 Shadow Manifestation ID：REV_373t 
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.REV_955, new Modifier(-100)); //执事者斯图尔特 Stewart the Steward ID：REV_955 
#endregion
#region 硬币 GAME_005
     p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(55));//硬币 GAME_005
#endregion
#region 灰烬元素  REV_960
//克乌龟法，和自残术，或者差2斩杀有奇效
            if(board.HasCardInHand(Card.Cards.REV_960)
            &&board.HeroEnemy.CurrentHealth+board.HeroEnemy.CurrentArmor<=2
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.REV_960, new Modifier(-999)); 
            AddLog("灰烬元素 -999");
            }
            else
            {
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.REV_960, new Modifier(150)); 
            }
#endregion
#region 葛拉卡爬行蟹    UNG_807
                    if(board.HasCardInHand(Card.Cards.UNG_807)
                    && board.MinionEnemy.Count(x => x.IsRace(Card.CRace.PIRATE)) == 0)
                    {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.UNG_807, new Modifier(200));
                    AddLog("葛拉卡爬行蟹 200");
                    }
#endregion

#region LOOT_541t	国王的赎金
                    if(board.HasCardInHand(Card.Cards.LOOT_541t)
                    ){
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.LOOT_541t, new Modifier(-200));//LOOT_541t	国王的赎金
                    }
#endregion

#region 飞行员帕奇斯 VAC_933
			if(board.HasCardInHand(Card.Cards.VAC_933)){
				 p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_933, new Modifier(-350)); 
				 AddLog("飞行员帕奇斯 -350");
			}
#endregion
#region TOY_330t5 奇利亚斯豪华版3000型
   // 可攻击随从
            int canAttackMINIONs=board.MinionFriend.Count(card => card.CanAttack);
            if (board.HasCardInHand(Card.Cards.TOY_330t5) && canAttackMINIONs>=2)
            {
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(canAttackMINIONs * -30));
             AddLog("奇利亚斯豪华版3000型"+(canAttackMINIONs * -30));
            }
            else
            {
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(350));
            }
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(-50));
#endregion

#region 南海船工 CORE_CS2_146 
            if(board.HasCardInHand(Card.Cards.CORE_CS2_146)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_CS2_146, new Modifier(200)); 
            Bot.Log("南海船工 200");
            }
#endregion
#region 海盗帕奇斯 CFM_637 
            if(board.HasCardInHand(Card.Cards.CFM_637)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CFM_637, new Modifier(-20)); 
            }
            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.CFM_637, new Modifier(150)); 
#endregion
#region 空降歹徒 DRG_056
            if(board.HasCardInHand(Card.Cards.DRG_056)
            &&board.ManaAvailable>2
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(250)); 
            }
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(-100)); 
#endregion
#region 船载火炮 GVG_075 
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(3999)); 
            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(150)); 
           if(shipborneArtillery!=null
            &&NumberHandPirates>=1
            &&board.MaxMana ==1
            &&enemyAttack<3
            ){
                p.ComboModifier = new ComboSet(shipborneArtillery.Id);
                 AddLog("1费船载火炮出");
            }
            // 如果最大费用小于等于2,且敌方攻击力大于等于3,降低船载火炮优先级
            if(board.MaxMana<=2
            &&enemyAttack>=2
            &&board.HasCardInHand(Card.Cards.GVG_075)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(350));
            AddLog("船载火炮出350");
            }
            if(board.MaxMana<=2
            &&enemyAttack<2
            &&board.HasCardInHand(Card.Cards.GVG_075)
            &&NumberHandPirates>=1
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(-250));
            AddLog("船载火炮出-250");
            }
            if(board.HasCardInHand(Card.Cards.GVG_075)
            &&NumberHandPirates>=1
            &&board.MaxMana >=3
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(-250)); 
            AddLog("船载火炮出-250");
            }
            // 如果场上已经有船载火炮,手里有海盗,降低手里船载火炮优先级
            if(board.HasCardOnBoard(Card.Cards.GVG_075)
            &&NumberHandPirates>=1
            &&board.HasCardInHand(Card.Cards.GVG_075)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(150));
            AddLog("如果场上已经有船载火炮,手里有海盗,降低手里船载火炮优先级");
            }
#endregion

#region GVG_009 暗影投弹手
            if(board.HasCardInHand(Card.Cards.GVG_009)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GVG_009, new Modifier(200)); 
            AddLog("暗影投弹手 200");
            }
            // 如果场上有暗影投弹手,手上有亡者复生,提高暗影投弹手送的优先级
            if(board.HasCardOnBoard(Card.Cards.GVG_009)
            &&board.HasCardInHand(Card.Cards.SCH_514)
            ){
            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.GVG_009, new Modifier(-5));
            AddLog("如果场上有暗影投弹手,手上有亡者复生,提高暗影投弹手送的优先级");
            }
#endregion

#region SW_446 虚触侍从
            if(board.HasCardInHand(Card.Cards.SW_446)
            // 可攻击的随从数量小于等于2
            &&(canAttackCount<1||enemyAttack>myAttack
						// 我方低血量
						||board.HeroFriend.CurrentHealth<=8
						)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SW_446, new Modifier(450)); 
						AddLog("虚触侍从 450");
            }else{
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SW_446, new Modifier(-350)); 
            }
#endregion



#region NX2_050	错误产物
// 提高优先级
            if(board.HasCardInHand(Card.Cards.NX2_050)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.NX2_050, new Modifier(-99));   
            AddLog("错误产物 -99");
            }
            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.NX2_050, new Modifier(100));   
#endregion

#region TOY_518	宝藏经销商
        p.ForcedResimulationCardList.Add(Card.Cards.TOY_518);
        //   攻击优先值滞后
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(2000)); 
        p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(-50));
         if(board.HasCardInHand(Card.Cards.TOY_518)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(-150)); 
            AddLog("宝藏经销商 -150");
            }
            // 不送
            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(150));
#endregion

#region CORE_WON_065	随船外科医师
            p.ForcedResimulationCardList.Add(Card.Cards.CORE_WON_065);
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_WON_065, new Modifier(7999)); 
            if(board.HasCardInHand(Card.Cards.CORE_WON_065)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_065, new Modifier(-99)); 
            AddLog("随船外科医师 -99");
            }
			p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.CORE_WON_065, new Modifier(350)); //克托里·光刃 Kotori Lightblade  TSC_074  
#endregion

#region 心灵按摩师 VAC_512
        p.ForcedResimulationCardList.Add(Card.Cards.VAC_512);
        //   攻击优先值滞后
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_512, new Modifier(1050)); 
         if(board.HasCardInHand(Card.Cards.VAC_512)
        // 回合数大于1
            &&board.MaxMana>1
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_512, new Modifier(-60)); 
            AddLog("心灵按摩师 -60");
            }
#endregion



#region  YOD_032	狂暴邪翼蝠
p.ForcedResimulationCardList.Add(Card.Cards.YOD_032);
// 使用优先级
p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-300));
// 费用不为0,降低使用优先级

if(board.Hand.Exists(x=>x.CurrentCost>0 && x.Template.Id==Card.Cards.YOD_032)){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(200));
    AddLog("狂暴邪翼蝠 200");
}else{
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-99));
}

#endregion

#region SCH_514	亡者复生
    var resurrectionTheDead = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.SCH_514);
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.SCH_514, new Modifier(3999)); 
    if(resurrectionTheDead!=null
    &&valuableFollowers>=2
    ){
        // p.CastSpellsModifiers.AddOrUpdate(Card.Cards.SCH_514, new Modifier(-350));
				p.ComboModifier = new ComboSet(resurrectionTheDead.Id);
        AddLog("亡者复生出");
    }
#endregion

#region EX1_625t 心灵尖刺
// 如果手里有纸艺天使 TOY_381  降低心灵尖刺使用优先级
if(board.HasCardInHand(Card.Cards.TOY_381)
&&!board.HasCardOnBoard(Card.Cards.TOY_381)
&&friendCount<=6
){
    p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.EX1_625t, new Modifier(350)); 
    AddLog("有纸艺天使 心灵尖刺 350");
}else{
    p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.EX1_625t, new Modifier(55)); 
    // AddLog("心灵尖刺 55");
}
// 如果场上没有纸艺天使,降低使用顺序优先值
if(!board.HasCardOnBoard(Card.Cards.TOY_381)
){
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_625t, new Modifier(-5));
}else{
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_625t, new Modifier(999));
}
    // 心灵尖刺不对自己使用
    p.CastHeroPowerModifier.AddOrUpdate(SmartBot.Plugins.API.Card.Cards.EX1_625t, 999, board.HeroFriend.Id);
#endregion



#region 纸艺天使 TOY_381
p.ForcedResimulationCardList.Add(Card.Cards.TOY_381);
// 使用优先级
p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_381, new Modifier(999));
// 提高优先级
if(board.HasCardInHand(Card.Cards.TOY_381)
&&!board.HasCardOnBoard(Card.Cards.TOY_381)
){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_381, new Modifier(-350));
    AddLog("纸艺天使 -350");
}
if(board.HasCardInHand(Card.Cards.TOY_381)
&&board.HasCardOnBoard(Card.Cards.TOY_381)
){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_381, new Modifier(999));
    AddLog("纸艺天使 999");
}
// 不送
p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TOY_381, new Modifier(150));
#endregion

#region 一费处理
// 1 费回合
// 优先释放 1 费海盗属性卡牌，优先级：宝石经销商>心灵按摩师>随船外科医生；
//TOY_518	宝藏经销商  心灵按摩师 VAC_512 随船外科医师 CORE_WON_065
if(board.MaxMana==1
&&!board.HasCardInHand(Card.Cards.GAME_005)
&&board.Hand.Exists(x=>x.CurrentCost==1)
){
    if(board.HasCardInHand(Card.Cards.TOY_518)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(-999)); 
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_512, new Modifier(200)); 
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_065, new Modifier(200)); 
        AddLog("宝藏经销商 -999");
    }
    if(board.HasCardInHand(Card.Cards.VAC_512)
    &&!board.HasCardInHand(Card.Cards.TOY_518)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_512, new Modifier(-999)); 
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_065, new Modifier(200)); 
        AddLog("心灵按摩师 -999");
    }
    if(board.HasCardInHand(Card.Cards.CORE_WON_065)
    &&!board.HasCardInHand(Card.Cards.TOY_518)
    &&!board.HasCardInHand(Card.Cards.VAC_512)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_065, new Modifier(-999)); 
        AddLog("随船外科医师 -999");
    }
}
if(board.MaxMana==1
&&board.HasCardInHand(Card.Cards.GAME_005)
&&board.Hand.Exists(x=>x.CurrentCost==1)
){
    if(board.HasCardInHand(Card.Cards.TOY_518)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(-999)); 
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_512, new Modifier(200)); 
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_065, new Modifier(200)); 
        AddLog("宝藏经销商 -999");
    }
    if(board.HasCardInHand(Card.Cards.CORE_WON_065)
    &&!board.HasCardInHand(Card.Cards.TOY_518)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_065, new Modifier(-999)); 
        AddLog("随船外科医师 -999");
    }

    if(board.HasCardInHand(Card.Cards.VAC_512)
    &&!board.HasCardInHand(Card.Cards.TOY_518)
    &&!board.HasCardInHand(Card.Cards.CORE_WON_065)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_512, new Modifier(-999)); 
        AddLog("心灵按摩师 -999");
    }
    
}
#endregion

#region 心灵震爆 DS1_233
// 提高优先级
if(board.HasCardInHand(Card.Cards.DS1_233)
){
    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.DS1_233, new Modifier(130)); 
    AddLog("心灵震爆 130");
}
#endregion

#region 针灸 VAC_419
// 提高优先级
if(board.HasCardInHand(Card.Cards.VAC_419)
){
    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_419, new Modifier(130)); 
    AddLog("针灸 130");
}
// 一费,有针灸,有狂暴邪异蝠,提高针灸使用优先级
if(board.HasCardInHand(Card.Cards.VAC_419)
&&board.HasCardInHand(Card.Cards.YOD_032)
&&board.MaxMana ==1
){
    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_419, new Modifier(-999)); 
    AddLog("针灸 -999");
}
#endregion

#region DRG_056 空降歹徒
// 如果手上没有除空降歹徒之外的其他海盗,提高空降歹徒使用优先级
if(board.HasCardInHand(Card.Cards.DRG_056)
&&filterHaidao==0
// 亡者复生不触发
&&!(board.HasCardInHand(Card.Cards.TOY_381)&&valuableFollowers>=2)
){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(-5)); 
    AddLog("空降歹徒 -5");
}else{
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(350)); 
}
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(-50)); 
#endregion

#region REV_290 赎罪教堂
// 地标空场有费用要下，自己没怪对面7血以下可以地标给对面找伤害
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.REV_290, new Modifier(1999)); 
if(board.HasCardInHand(Card.Cards.REV_290)
// 场上随从小于等于6
&&friendCount<=6
){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.REV_290, new Modifier(-350)); 
    AddLog("赎罪教堂使用优先级提升");
}
if(board.HasCardOnBoard(Card.Cards.REV_290)
){

    // 遍历随从,降低赎罪教堂对不可攻击随从的使用优先级
    foreach (var card in board.MinionFriend)
    {
        if(!card.CanAttack
        ){
            p.LocationsModifiers.AddOrUpdate(Card.Cards.REV_290, new Modifier(999,card.Template.Id)); 
            // AddLog("降低赎罪教堂对不可攻击随从的使用优先级"+card.Template.Id);
        }
        if(card.CanAttack
        ){
            p.LocationsModifiers.AddOrUpdate(Card.Cards.REV_290, new Modifier(-150,card.Template.Id)); 
            // AddLog("降低赎罪教堂对可攻击随从的使用优先级"+card.Template.Id);
        }
    }
    // 遍历敌方随从,降低赎罪教堂对其使用优先值
    foreach (var card in board.MinionEnemy)
    {
        p.LocationsModifiers.AddOrUpdate(Card.Cards.REV_290, new Modifier(999,card.Template.Id));
        // AddLog("降低赎罪教堂对其使用优先值"+card.Template.Id);
    }
}
// 如果敌方血量小于等于7,我方场上和手上均没有随从,敌方有随从,提高赎罪教堂对敌方随从使用优先级
		if(board.HeroEnemy.CurrentHealth+board.HeroEnemy.CurrentArmor<=7
		&&board.MinionFriend.Count==0
		&&board.Hand.Count(card => card.Type == Card.CType.MINION)==0
		&&board.MinionEnemy.Count>=1
		&&board.HasCardOnBoard(Card.Cards.REV_290)
		){
				p.LocationsModifiers.AddOrUpdate(Card.Cards.REV_290, new Modifier(-350)); 
				AddLog("赎罪教堂对敌方随从使用找伤害");
		}
#endregion

#region NX2_019	精神灼烧
if(board.HasCardInHand(Card.Cards.NX2_019)){
        // 遍历敌方随从,如果有血量小于等于2的,提高精神灼烧对其使用优先级
        foreach (var card in board.MinionEnemy)
        {
            if(card.CurrentHealth<=2
            ){
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.NX2_019, new Modifier(-99,card.Template.Id));
                AddLog("提高精神灼烧对其使用优先级"+card.Template.Id);
            }else{
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.NX2_019, new Modifier(150));
            }
        }
        // 降低一点精神灼烧对帕奇斯的使用优先级
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.NX2_019, new Modifier(130,Card.Cards.CFM_637));
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.NX2_019, new Modifier(-50));
}
#endregion

#region 粗暴的猢狲 VAC_938
    if (canAttackPirates >= 1 
    && board.HasCardInHand(Card.Cards.VAC_938)
    )
    {
        // 如果场上海盗大于2，提高优先级
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_938, new Modifier(canAttackPirates*-80));
    AddLog("粗暴的猢狲" + (canAttackPirates*-80));
    }else{
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_938, new Modifier(200));
    }
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_938, new Modifier(999)); 
#endregion

#region SW_444	暮光欺诈者
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.SW_444, new Modifier(999));

if(
board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.SW_444)
){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SW_444, new Modifier(-350));
    AddLog("暮光欺诈者-350");
}
#endregion

#region TTN_924 锋鳞
    //  手上没有一费随从,提高使用优先级
    if(board.HasCardInHand(Card.Cards.TTN_924)
    &&board.Hand.Count(card => card.CurrentCost == 1) == 0
    ){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_924, new Modifier(-350));
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TTN_924, new Modifier(-5));
    AddLog("锋鳞 -350");
    }
#endregion

#region 同归于尽逻辑
//双方伤害牌 针灸 VAC_419  暗影投弹手 GVG_009 心灵震爆 DS1_233 场上没有赎罪教堂 REV_290 暗影灼烧 NX2_019 SCH_514	亡者复生
// 如果我方和敌方的血量都小于等于4,且我方场攻小于2,手上没有可直伤牌,提高针灸使用优先级
if(board.HeroFriend.CurrentHealth+board.HeroFriend.CurrentArmor==4
&&board.HeroEnemy.CurrentHealth+board.HeroEnemy.CurrentArmor==4
// 手牌中除针灸之外的手牌数为0
&&board.Hand.Count(x=>x.Template.Id!=Card.Cards.VAC_419)==0
&&myAttack<=1
){
    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_419, new Modifier(-999)); 
    AddLog("同归1");
}
if(board.HeroFriend.CurrentHealth+board.HeroFriend.CurrentArmor==3
&&board.HeroEnemy.CurrentHealth+board.HeroEnemy.CurrentArmor==3
&&myAttack==0
&&board.Hand.Count(x=>x.Template.Id!=Card.Cards.VAC_419&&x.Template.Id!=Card.Cards.GVG_009)==0
){
    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_419, new Modifier(-999)); 
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GVG_009, new Modifier(-999)); 
    AddLog("同归2");
}

#endregion

#region 亮石旋岩虫 DEEP_024
  //  锻造亮石旋岩虫 DEEP_024
	if(board.HasCardInHand(Card.Cards.DEEP_024)
	){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DEEP_024, new Modifier(350)); 
			p.ForgeModifiers.AddOrUpdate(Card.Cards.DEEP_024, new Modifier(-150));
			AddLog("亮石旋岩虫 350");
	}
#endregion

#region 投降
// 如果满足以下条件,投降:
// 1. 最大回合数大于等于10
// 2. 敌方血量大于15
// 3. 我方场攻小于5
// 4. 手牌小于3
if (board.MaxMana >= 10 &&
    board.HeroEnemy.CurrentHealth > 15 &&
    board.MinionFriend.Sum(x => x.CurrentAtk) < 5 &&
    board.Hand.Count < 3)
{
    Bot.Concede();
    AddLog("满足条件,投降");
}
#endregion


#region 卡牌施放逻辑封装与执行
// 方法封装
Card GetCardInHand(Card.Cards cardId) => board.Hand.FirstOrDefault(card => card.Template.Id == cardId);

bool IsSpellPlayable(Card card, int availableMana) => card.Type == Card.CType.SPELL && card.CurrentCost <= availableMana;

IEnumerable<Card> GetPlayableSpells(int reservedMana)
{
    return board.Hand
                .Where(card => IsSpellPlayable(card, board.ManaAvailable - reservedMana))
                .OrderBy(card => card.CurrentCost) // 按法术费用升序排列
                .ThenBy(card => card.Template.Id); // 若费用相同，按卡牌 ID 排序
}

void SetComboPriority(Card minion, IEnumerable<Card> spells)
{
    foreach (var spell in spells.OrderBy(spell => spell.CurrentCost)) // 确保按费用优先
    {
        p.ComboModifier = new ComboSet((int)minion.Id, (int)spell.Id);
        AddLog($"设置组合优先级: {minion.Template.NameCN} 与法术({spell.Template.NameCN})");
    }
}

// 通用处理逻辑
void HandleCardLogic(Card.Cards targetCard, string cardName, int number = 999, int manaThreshold = 3, int maxHandCount = 8, int maxFriendlyMinions = 6)
{
    var cardInHand = GetCardInHand(targetCard);
		if (cardInHand != null)
    {
    if (board.Hand.Count >= maxHandCount && cardInHand != null)
    {
        AddLog($"手牌过多，暂不打出{cardName}。");
        return;
    }

        if (board.MinionFriend.Count <= maxFriendlyMinions && board.MaxMana >= manaThreshold &&
            !board.MinionEnemy.Exists(x => x.Template.Id == Card.Cards.WORK_040)) 
        {
            var playableSpells = GetPlayableSpells(cardInHand.CurrentCost)
						.Where(item => {
								switch (item.Template.Id)
								{
										case Card.Cards.VAC_955t:
												return board.MinionFriend.Count <= 4;
										case Card.Cards.MIS_709:
												return item.CurrentCost == 1;
										case Card.Cards.GDB_138:
												return item.CurrentCost == 0;
										case Card.Cards.GDB_139://信仰圣契 GDB_139
												return board.MinionFriend.Count <= 4;
										case Card.Cards.GAME_005://GAME_005
												return board.MaxMana>=3;
										default:
												return true;
								}
						}).OrderBy(spell => spell.CurrentCost) // 按费用优先
                  .ToList();

            if (playableSpells.Any())
            {
                SetComboPriority(cardInHand, playableSpells);
            }
            else
            {
                p.CastMinionsModifiers.AddOrUpdate(targetCard, new Modifier(number));
                AddLog($"未找到可用法术，降低{cardName}优先级。");
            }
        }
        else
        {
            p.CastMinionsModifiers.AddOrUpdate(targetCard, new Modifier(number));
            AddLog($"条件不满足，未打出{cardName}。");
        }
		}
    var cardOnBoard = board.MinionFriend.FirstOrDefault(x => x.Template.Id == targetCard && x.HasSpellburst);
    if (cardOnBoard != null)
    {
        p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(targetCard, new Modifier(250));
        var playableSpells = GetPlayableSpells(0)
					.Where(item => {
							switch (item.Template.Id)
							{
									case Card.Cards.VAC_955t:
											return board.MinionFriend.Count <= 4;
									case Card.Cards.MIS_709:
											return item.CurrentCost == 1;
									case Card.Cards.GDB_138:
											return item.CurrentCost == 0;
									case Card.Cards.GDB_139://信仰圣契 GDB_139
										return board.MinionFriend.Count <= 4;
									case Card.Cards.GAME_005://GAME_005
											return board.MaxMana>=3;
									default:
											return true;
							}
					}).OrderBy(spell => spell.CurrentCost) // 按费用优先
                  .ToList();

        if (playableSpells.Any())
        {
            SetComboPriority(cardOnBoard, playableSpells);

            foreach (var spell in playableSpells)
            {
                p.CastSpellsModifiers.AddOrUpdate(spell.Template.Id, new Modifier(-999));
                AddLog($"提高法术({spell.Template.NameCN})的优先级。");
            }
        }
    }
}

bool ShouldExecuteOracle(Card.Cards targetCard)
{
    var cardInHand = GetCardInHand(targetCard);
    var hasPlayableSpells = false;

    // 检查是否有可用法术
    if (cardInHand != null)
    {
            var playableSpells = GetPlayableSpells(cardInHand.CurrentCost)
                .Where(item => {
                    switch (item.Template.Id)
                    {
                        case Card.Cards.VAC_955t:
                            return board.MinionFriend.Count <= 4;
                        case Card.Cards.MIS_709:
                            return item.CurrentCost == 1;
                        case Card.Cards.GDB_138:
                            return item.CurrentCost == 0;
                        case Card.Cards.GDB_139:
                            return board.MinionFriend.Count <= 4;
                        default:
                            return true;
                    }
                }).OrderBy(spell => spell.CurrentCost) // 按费用优先
                  .ToList();

        hasPlayableSpells = playableSpells.Any();
    }

    // 检查基本条件
    var meetsBasicConditions = board.MinionFriend.Count <= 6 && 
                              board.MaxMana >= 3 && 
                              !board.MinionEnemy.Exists(x => x.Template.Id == Card.Cards.WORK_040);

    return hasPlayableSpells && meetsBasicConditions;
}

void ExecuteCardLogic()
{
        HandleCardLogic(Card.Cards.GDB_310, "虚灵神谕者");
}

ExecuteCardLogic();

#endregion

#region 重新思考
var reflection = new Dictionary<Card.Cards, int>
{
     { Card.Cards.TOY_381, 500 },//TOY_381	纸艺天使
};
foreach (var card in reflection)
{
	if (board.Hand.Exists(x => x.Template.Id == card.Key))
    {
        p.ForcedResimulationCardList.Add(card.Key);
    }
}
#endregion

#region 打印场上随从id
// foreach (var item in board.MinionFriend)
// {
//     AddLog(item.Template.NameCN + ' ' + item.Template.Id);
// }
// 打印手上的卡牌id
foreach (var item in board.Hand)
{
		AddLog(item.Template.NameCN + ' ' + item.Template.Id);
}

#endregion


#region VAC_959t05 追踪护符 Amulet of Tracking 随机获取3张传说卡牌。（然后将其变形成为普通卡牌！）
// 如果手牌数小于等于3,提高使用优先级
        if(board.HasCardInHand(Card.Cards.VAC_959t05)
        &&HandCount<=6
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t05, new Modifier(-99));
        AddLog("追踪护符-99");
        }
#endregion

#region VAC_959t06 生灵护符 Amulet of Critters 随机召唤一个法力值消耗为（4）的随从并使其获得嘲讽。（但它无法攻击！）
        if(board.HasCardInHand(Card.Cards.VAC_959t06)
				// 场上随从小于等于6
				&&board.MinionFriend.Count<=6
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t06, new Modifier(-99));
        AddLog("生灵护符-99");
        }
#endregion

#region VAC_959t08 能量护符 Amulet of Energy 为你的英雄恢复12点生命值。（然后受到6点伤害！）
        if(board.HasCardInHand(Card.Cards.VAC_959t08)
                // 可恢复生命值大于6
                &&board.HeroFriend.MaxHealth-board.HeroFriend.CurrentHealth > 6
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t08, new Modifier(-99));
        AddLog("能量护符-99");
        }
#endregion

#region VAC_959t10 挺进护符 Amulet of Strides 使你手牌中的所有卡牌的法力值消耗减少（1）点。（法术牌除外！）
        if(board.HasCardInHand(Card.Cards.VAC_959t10)
        ){
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t10, new Modifier(-99));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_959t10, new Modifier(999));
        AddLog("挺进护符-99");
        }
#endregion


#region 版本输出
                AddLog("---\n版本27.7 作者by77 Q群:943879501 DC:https://discord.gg/nn329ZU6Ss\n---");
#endregion

#region 攻击优先 卡牌威胁
var cardModifiers = new Dictionary<Card.Cards, int>
{   
			{ Card.Cards.TLC_468t1,	999 }, // 细长黏团 TLC_468t1
			{ Card.Cards.DINO_410,	999 }, // 凯洛斯的蛋 DINO_410
		{ Card.Cards.DINO_410t2,	999 }, // 凯洛斯的蛋 DINO_410t2
		{ Card.Cards.DINO_410t4,	999 }, // 凯洛斯的蛋 DINO_410t4
		{ Card.Cards.DINO_410t5,	999 }, // 凯洛斯的蛋 DINO_410t5
		{ Card.Cards.DINO_410t3,	999 }, // 凯洛斯的蛋 DINO_410t3
		{ Card.Cards.SC_671t1,	200 }, // 执政官 SC_671t1
		{ Card.Cards.EDR_849,	200 }, // 梦缚迅猛龙 EDR_849  
		{ Card.Cards.EDR_892, -100 }, // 残暴的魔蝠 EDR_892
		{ Card.Cards.EDR_891, -100 }, // 贪婪的地狱猎犬 EDR_891
		{ Card.Cards.GDB_471, 200 }, // GDB_471	沃罗尼招募官
		{ Card.Cards.VAC_501, 200 }, // 极限追逐者阿兰娜 VAC_501 
		{ Card.Cards.GDB_100, 200 }, // 阿肯尼特防护水晶 GDB_100
    { Card.Cards.EDR_815, 200},//EDR_815	尸魔花
    { Card.Cards.TOY_528, 200},//伴唱机 TOY_528
    { Card.Cards.EDR_540, 200},//EDR_540	扭曲的织网蛛
    { Card.Cards.EDR_889, 200},//EDR_889	鲜花商贩 
    { Card.Cards.VAC_503, 200},//VAC_503	召唤师达克玛洛
    { Card.Cards.EDR_816, 200},//EDR_816	怪异魔蚊
    { Card.Cards.EDR_810t, 100},//EDR_810t	饱胀水蛭
    { Card.Cards.SC_765, 200},//SC_765	高阶圣堂武士
    { Card.Cards.SC_756, 200},//SC_756	航母
    { Card.Cards.SC_003, 200},//虫巢女王 SC_003
    { Card.Cards.WW_827, 200},//雏龙牧人 WW_827
    { Card.Cards.TTN_903, 200},//生命的缚誓者艾欧娜尔 TTN_903
    { Card.Cards.TTN_960, 200},//灭世泰坦萨格拉斯 TTN_960
    { Card.Cards.TTN_862, 200},//翠绿之星阿古斯 TTN_862
    { Card.Cards.TTN_429, 200},//阿曼苏尔 TTN_429
     { Card.Cards.TTN_092, 200},//复仇者阿格拉玛 TTN_092
     { Card.Cards.TTN_075, 200},//诺甘农 TTN_075  
    
     { Card.Cards.DEEP_008, 200},//针岩图腾 DEEP_008
     { Card.Cards.CORE_RLK_121, 200},//死亡侍僧 CORE_RLK_121
     { Card.Cards.TTN_737, 200},//兵主 TTN_737
     { Card.Cards.VAC_406, 900},//VAC_406	困倦的岛民
     { Card.Cards.TTN_858, 200},//TTN_858	维和者阿米图斯
     { Card.Cards.GDB_226, 200},//GDB_226	凶恶的入侵者
     { Card.Cards.TOY_330t5, 200},//TOY_330t5 奇利亚斯豪华版3000型
    { Card.Cards.TOY_330t11, 200},//TOY_330t11 奇利亚斯豪华版3000型
     { Card.Cards.CORE_EX1_012, 200},//CORE_EX1_012 血法师萨尔诺斯
     { Card.Cards.CORE_BT_187, 200},//CORE_BT_187	凯恩·日怒
     { Card.Cards.CS2_052, 200},//空气之怒图腾 CS2_052
     { Card.Cards.WORK_040, 200 },//笨拙的杂役 WORK_040
     { Card.Cards.TOY_606, 200 },//测试假人 TOY_606   
     { Card.Cards.WW_382, 200 },//步移山丘 WW_382   
     { Card.Cards.GDB_841, 200 },//GDB_841	游侠斥候  
     { Card.Cards.GDB_110, 200 },//GDB_110	邪能动力源 
     { Card.Cards.CORE_ICC_210, 200 },//CORE_ICC_210	暗影升腾者 
     { Card.Cards.JAM_024, 200 },//JAM_024	布景光耀之子 
     { Card.Cards.CORE_CS3_014, 200 },//CORE_CS3_014	赤红教士
     
     { Card.Cards.GVG_075, 200 },//GVG_075 	船载火炮
     { Card.Cards.GDB_310, 200 },//GDB_310	虚灵神谕者 
     { Card.Cards.JAM_010, 200 },//JAM_010	点唱机图腾
     { Card.Cards.TOY_351t, -200 },//TOY_351t	神秘的蛋
    { Card.Cards.TOY_351, -200 },//TOY_351	神秘的蛋
    { Card.Cards.WW_391, 200 }, // WW_391	淘金客
    { Card.Cards.TOY_515, 200 }, // 水上舞者索尼娅 TOY_515
    { Card.Cards.CORE_TOY_100, 200 }, // 侏儒飞行员诺莉亚 CORE_TOY_100
    { Card.Cards.WW_381, 200 }, // 受伤的搬运工 WW_381
    { Card.Cards.TTN_900, -200 }, // 石心之王 TTN_900
    { Card.Cards.CORE_DAL_609, 200 }, // 卡雷苟斯 CORE_DAL_609
    { Card.Cards.TOY_646, 200 }, // 捣蛋林精 TOY_646
    { Card.Cards.TOY_357, 200 }, // 抱龙王噗鲁什 TOY_357
    { Card.Cards.VAC_507, 200 }, // 阳光汲取者莱妮莎 VAC_507
    { Card.Cards.WORK_042, 500 }, // 食肉格块 WORK_042
    { Card.Cards.WW_344, 200 }, // 威猛银翼巨龙 
    { Card.Cards.TOY_812, -5},//TOY_812 皮普希·彩蹄
    { Card.Cards.VAC_532, 200 },//椰子火炮手 VAC_532
    { Card.Cards.TOY_505, 200 },//TOY_505	玩具船
    { Card.Cards.TOY_381, 200 },//TOY_381	纸艺天使
    { Card.Cards.TOY_824, 350 }, // 黑棘针线师
    { Card.Cards.VAC_927, 200 }, // 狂飙邪魔
    { Card.Cards.VAC_938, 200 }, // 粗暴的猢狲
    { Card.Cards.ETC_355, 200 }, // 剃刀沼泽摇滚明星
    { Card.Cards.WW_091, 200 },  // 腐臭淤泥波普加
    { Card.Cards.VAC_450, 200}, // 悠闲的曲奇
    { Card.Cards.TOY_028, 200 }, // 团队之灵
    { Card.Cards.VAC_436, 200 }, // 脆骨海盗
    { Card.Cards.VAC_321, 200 }, // 伊辛迪奥斯
    { Card.Cards.TTN_800, 200 }, // 雷霆之神高戈奈斯 TTN_800
    { Card.Cards.TTN_415, 200 }, // 卡兹格罗斯
    { Card.Cards.ETC_541, 200 }, // 盗版之王托尼
    { Card.Cards.CORE_LOOT_231, 200 }, // 奥术工匠
    { Card.Cards.ETC_339, 200 }, // 心动歌手
    { Card.Cards.ETC_833, 200 }, // 箭矢工匠
    { Card.Cards.MIS_026, 200 }, // 傀儡大师多里安
    { Card.Cards.CORE_WON_065, 200 }, // 随船外科医师
    { Card.Cards.WW_357, 500 }, // 老腐和老墓
    { Card.Cards.DEEP_999t2, 200 }, // 深岩之洲晶簇
    { Card.Cards.CFM_039, 200 }, // 杂耍小鬼
    { Card.Cards.WW_364t, 200 }, // 狡诈巨龙威拉罗克
    { Card.Cards.TSC_026t, 200 }, // 可拉克的壳
    { Card.Cards.WW_415, 200 }, // 许愿井
    { Card.Cards.CS3_014, 200 }, // 赤红教士
    { Card.Cards.YOG_516, 200 }, // 脱困古神尤格-萨隆
    { Card.Cards.NX2_033, 200 }, // 巨怪塔迪乌斯
    { Card.Cards.JAM_004, 200 }, // 镂骨恶犬
    { Card.Cards.TTN_330, 200 }, // Kologarn
    { Card.Cards.TTN_729, 200 }, // Melted Maker
    { Card.Cards.TTN_812, 150 }, // Victorious Vrykul
    { Card.Cards.TTN_479, 200 }, // Flame Revenant
    { Card.Cards.TTN_732, 250 }, // Invent-o-matic
    { Card.Cards.TTN_466, 250 }, // Minotauren
    { Card.Cards.TTN_801, 350 }, // Champion of Storms
    { Card.Cards.TTN_833, 200 }, // Disciple of Golganneth
    { Card.Cards.TTN_730, 300 }, // Lab Constructor
    { Card.Cards.TTN_920, 300 }, // Mimiron, the Mastermind
    { Card.Cards.TTN_856, 200 }, // Disciple of Amitus
    { Card.Cards.TTN_907, 200 }, // Astral Serpent
    { Card.Cards.TTN_071, 200 }, // Sif
    { Card.Cards.TTN_078, 200 }, // Observer of Myths
    { Card.Cards.TTN_843, 200 }, // Eredar Deceptor
};

foreach (var cardModifier in cardModifiers)
{
    if (board.MinionEnemy.Any(minion => minion.Template.Id == cardModifier.Key))
    {
        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(cardModifier.Key, new Modifier(cardModifier.Value));
    }
}
#endregion
						 
#region Bot.Log
Bot.Log(_log);         
#endregion    

            // ===== 全局硬规则：不解对面的“凯洛斯的蛋”系列（DINO_410*） =====
            // 口径：对面出现蛋阶段时，尽量不要用随从/武器/解牌去处理它（优先处理其他滚雪球点/直伤点）。
            try
            {
                if (board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.Template != null &&
                    (m.Template.Id == Card.Cards.DINO_410
                     || m.Template.Id == Card.Cards.DINO_410t2
                     || m.Template.Id == Card.Cards.DINO_410t3
                     || m.Template.Id == Card.Cards.DINO_410t4
                     || m.Template.Id == Card.Cards.DINO_410t5)))
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410, new Modifier(-9999));
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410t2, new Modifier(-9999));
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410t3, new Modifier(-9999));
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410t4, new Modifier(-9999));
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410t5, new Modifier(-9999));
                }
            }
            catch { }

//德：DRUID 猎：HUNTER 法：MAGE 骑：PALADIN 牧：PRIEST 贼：ROGUE 萨：SHAMAN 术：WARLOCK 战：WARRIOR 瞎：DEMONHUNTER
            return p;
        }}
				 // 向 _log 字符串添加日志的私有方法，包括回车和新行
        private void AddLog(string log)
        {
            _log += "\r\n" + log;
        }
        //芬利·莫格顿爵士技能选择
        public Card.Cards SirFinleyChoice(List<Card.Cards> choices)
        {
            var filteredTable = _heroPowersPriorityTable.Where(x => choices.Contains(x.Key)).ToList();
            return filteredTable.First(x => x.Value == filteredTable.Max(y => y.Value)).Key;
        }

        //卡扎库斯选择
        public Card.Cards KazakusChoice(List<Card.Cards> choices)
        {
            return choices[0];
        }

        //计算类
        public static class BoardHelper
        {
            //得到敌方的血量和护甲之和
            public static int GetEnemyHealthAndArmor(Board board)
            {
                return board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
            }

            //得到自己的法强
            public static int GetSpellPower(Board board)
            {
                //计算没有被沉默的随从的法术强度之和
                return board.MinionFriend.FindAll(x => x.IsSilenced == false).Sum(x => x.SpellPower);
            }

            //获得第二轮斩杀血线
            public static int GetSecondTurnLethalRange(Board board)
            {
                //敌方英雄的生命值和护甲之和减去可释放法术的伤害总和
                return GetEnemyHealthAndArmor(board) - GetPlayableSpellSequenceDamages(board);
            }

            //下一轮是否可以斩杀敌方英雄
            public static bool HasPotentialLethalNextTurn(Board board)
            {
                //如果敌方随从没有嘲讽并且造成伤害
                //(敌方生命值和护甲的总和 减去 下回合能生存下来的当前场上随从的总伤害 减去 下回合能攻击的可使用随从伤害总和)
                //后的血量小于总法术伤害
                if (!board.MinionEnemy.Any(x => x.IsTaunt) &&
                    (GetEnemyHealthAndArmor(board) - GetPotentialMinionDamages(board) - GetPlayableMinionSequenceDamages(GetPlayableMinionSequence(board), board))
                        <= GetTotalBlastDamagesInHand(board))
                {
                    return true;
                }
                //法术释放过敌方英雄的血量是否大于等于第二轮斩杀血线
                return GetRemainingBlastDamagesAfterSequence(board) >= GetSecondTurnLethalRange(board);
            }

            //获得下回合能生存下来的当前场上随从的总伤害
            public static int GetPotentialMinionDamages(Board board)
            {
                return GetPotentialMinionAttacker(board).Sum(x => x.CurrentAtk);
            }

            //获得下回合能生存下来的当前场上随从集合
            public static List<Card> GetPotentialMinionAttacker(Board board)
            {
                //下回合能生存下来的当前场上随从集合
                var minionscopy = board.MinionFriend.ToArray().ToList();

                //遍历 以敌方随从攻击力 降序排序 的 场上敌方随从集合
                foreach (var mi in board.MinionEnemy.OrderByDescending(x => x.CurrentAtk))
                {
                    //以友方随从攻击力 降序排序 的 场上的所有友方随从集合，如果该集合存在生命值大于与敌方随从攻击力
                    if (board.MinionFriend.OrderByDescending(x => x.CurrentAtk).Any(x => x.CurrentHealth <= mi.CurrentAtk))
                    {
                        //以友方随从攻击力 降序排序 的 场上的所有友方随从集合,找出该集合中友方随从的生命值小于等于敌方随从的攻击力的随从
                        var tar = board.MinionFriend.OrderByDescending(x => x.CurrentAtk).FirstOrDefault(x => x.CurrentHealth <= mi.CurrentAtk);
                        //将该随从移除掉
                        minionscopy.Remove(tar);
                    }
                }

                return minionscopy;
            }

            //获得下回合能生存下来的对面随从集合
            public static List<Card> GetSurvivalMinionEnemy(Board board)
            {
                //下回合能生存下来的当前对面场上随从集合
                var minionscopy = board.MinionEnemy.ToArray().ToList();

                //遍历 以友方随从攻击力 降序排序 的 场上友方可攻击随从集合
                foreach (var mi in board.MinionFriend.FindAll(x => x.CanAttack).OrderByDescending(x => x.CurrentAtk))
                {
                    //如果存在友方随从攻击力大于等于敌方随从血量
                    if (board.MinionEnemy.OrderByDescending(x => x.CurrentHealth).Any(x => x.CurrentHealth <= mi.CurrentAtk))
                    {
                        //以敌方随从血量降序排序的所有敌方随从集合，找出敌方生命值小于等于友方随从攻击力的随从
                        var tar = board.MinionEnemy.OrderByDescending(x => x.CurrentHealth).FirstOrDefault(x => x.CurrentHealth <= mi.CurrentAtk);
                        //将该随从移除掉
                        minionscopy.Remove(tar);
                    }
                }
                return minionscopy;
            }

            //获取可以使用的随从集合
            public static List<Card.Cards> GetPlayableMinionSequence(Board board)
            {
                //卡片集合
                var ret = new List<Card.Cards>();

                //当前剩余的法力水晶
                var manaAvailable = board.ManaAvailable;

                //遍历以手牌中费用降序排序的集合
                foreach (var card in board.Hand.OrderByDescending(x => x.CurrentCost))
                {
                    //如果当前卡牌不为随从，继续执行
                    if (card.Type != Card.CType.MINION) continue;

                    //当前法力值小于卡牌的费用，继续执行
                    if (manaAvailable < card.CurrentCost) continue;

                    //添加到容器里
                    ret.Add(card.Template.Id);

                    //修改当前使用随从后的法力水晶
                    manaAvailable -= card.CurrentCost;
                }

                return ret;
            }

            //获取可以使用的奥秘集合
            public static List<Card.Cards> GetPlayableSecret(Board board)
            {
                //卡片集合
                var ret = new List<Card.Cards>();

                //遍历手牌中所有奥秘集合
                foreach (var card1 in board.Hand.FindAll(card => card.Template.IsSecret))
                {
                    if (board.Secret.Count > 0)
                    {
                        //遍历头上奥秘集合
                        foreach (var card2 in board.Secret.FindAll(card => CardTemplate.LoadFromId(card).IsSecret))
                        {

                            //如果手里奥秘和头上奥秘不相等
                            if (card1.Template.Id != card2)
                            {
                                //添加到容器里
                                ret.Add(card1.Template.Id);
                            }
                        }
                    }
                    else
                    { ret.Add(card1.Template.Id); }
                }
                return ret;
            }


            //获取下回合能攻击的可使用随从伤害总和
            public static int GetPlayableMinionSequenceDamages(List<Card.Cards> minions, Board board)
            {
                //下回合能攻击的可使用随从集合攻击力相加
                return GetPlayableMinionSequenceAttacker(minions, board).Sum(x => CardTemplate.LoadFromId(x).Atk);
            }

            //获取下回合能攻击的可使用随从集合
            public static List<Card.Cards> GetPlayableMinionSequenceAttacker(List<Card.Cards> minions, Board board)
            {
                //未处理的下回合能攻击的可使用随从集合
                var minionscopy = minions.ToArray().ToList();

                //遍历 以敌方随从攻击力 降序排序 的 场上敌方随从集合
                foreach (var mi in board.MinionEnemy.OrderByDescending(x => x.CurrentAtk))
                {
                    //以友方随从攻击力 降序排序 的 场上的所有友方随���集合，如果该集合存在生命值大于与敌方随从攻击力
                    if (minions.OrderByDescending(x => CardTemplate.LoadFromId(x).Atk).Any(x => CardTemplate.LoadFromId(x).Health <= mi.CurrentAtk))
                    {
                        //以友方随从攻击力 降序排序 的 场上的所有友方随从集合,找出该集合中友方随从的生命值小于等于敌方随从的攻击力的随从
                        var tar = minions.OrderByDescending(x => CardTemplate.LoadFromId(x).Atk).FirstOrDefault(x => CardTemplate.LoadFromId(x).Health <= mi.CurrentAtk);
                        //将该随从移除掉
                        minionscopy.Remove(tar);
                    }
                }

                return minionscopy;
            }

            //获取当前回合手牌中的总法术伤害
            public static int GetTotalBlastDamagesInHand(Board board)
            {
                //从手牌中找出法术伤害表存在的法术的伤害总和(包括法强)
                return
                    board.Hand.FindAll(x => _spellDamagesTable.ContainsKey(x.Template.Id))
                        .Sum(x => _spellDamagesTable[x.Template.Id] + GetSpellPower(board));
            }

            //获取可以使用的法术集合
            public static List<Card.Cards> GetPlayableSpellSequence(Board board)
            {
                //卡片集合
                var ret = new List<Card.Cards>();

                //当前剩余的法力水晶
                var manaAvailable = board.ManaAvailable;

                if (board.Secret.Count > 0)
                {
                    //遍历以手牌中费用降序排序的集合
                    foreach (var card in board.Hand.OrderBy(x => x.CurrentCost))
                    {
                        //如果手牌中又不在法术序列的法术牌，继续执行
                        if (_spellDamagesTable.ContainsKey(card.Template.Id) == false) continue;

                        //当前法力值小于卡牌的费用，继续执行
                        if (manaAvailable < card.CurrentCost) continue;

                        //添加到容器里
                        ret.Add(card.Template.Id);

                        //修改当前使用随从后的法力水晶
                        manaAvailable -= card.CurrentCost;
                    }
                }
                else if (board.Secret.Count == 0)
                {
                    //遍历以手牌中费用降序排序的集合
                    foreach (var card in board.Hand.FindAll(x => x.Type == Card.CType.SPELL).OrderBy(x => x.CurrentCost))
                    {
                        //如果手牌中又不在法术序列的法术牌，继续执行
                        if (_spellDamagesTable.ContainsKey(card.Template.Id) == false) continue;

                        //当前法力值小于卡牌的费用，继续执行
                        if (manaAvailable < card.CurrentCost) continue;

                        //添加到容器里
                        ret.Add(card.Template.Id);

                        //修改当前使用随从后的法力水晶
                        manaAvailable -= card.CurrentCost;
                    }
                }

                return ret;
            }
            
            //获取存在于法术列表中的法术集合的伤害总和(包括法强)
            public static int GetSpellSequenceDamages(List<Card.Cards> sequence, Board board)
            {
                return
                    sequence.FindAll(x => _spellDamagesTable.ContainsKey(x))
                        .Sum(x => _spellDamagesTable[x] + GetSpellPower(board));
            }

            //得到可释放法术的伤害总和
            public static int GetPlayableSpellSequenceDamages(Board board)
            {
                return GetSpellSequenceDamages(GetPlayableSpellSequence(board), board);
            }
            
            //计算在法术释放过敌方英雄的血量
            public static int GetRemainingBlastDamagesAfterSequence(Board board)
            {
                //当前回合总法术伤害减去可释放法术的伤害总和
                return GetTotalBlastDamagesInHand(board) - GetPlayableSpellSequenceDamages(board);
            }

            public static bool IsOutCastCard(Card card, Board board)
            {
                var OutcastLeft = board.Hand.Find(x => x.CurrentCost >= 0);
                var OutcastRight = board.Hand.FindLast(x => x.CurrentCost >= 0);
                if (card.Template.Id == OutcastLeft.Template.Id
                    || card.Template.Id == OutcastRight.Template.Id)
                {
                    return true;
                    
                }
                return false;
            }
            public static bool IsGuldanOutCastCard(Card card, Board board)
            {
                if ((board.FriendGraveyard.Exists(x => CardTemplate.LoadFromId(x).Id == Card.Cards.BT_601)
                    && card.Template.Cost - card.CurrentCost == 3))
                {
                    return true;
                }
                
                return false;
            }
            public static bool  IsOutcast(Card card,Board board)
            {
                if(IsOutCastCard(card,board) || IsGuldanOutCastCard(card,board))
                {
                    return true;
                }
                return false;
            }


            //在没有法术的情况下有潜在致命的下一轮
            public static bool HasPotentialLethalNextTurnWithoutSpells(Board board)
            {
                if (!board.MinionEnemy.Any(x => x.IsTaunt) &&
                    (GetEnemyHealthAndArmor(board) -
                     GetPotentialMinionDamages(board) -
                     GetPlayableMinionSequenceDamages(GetPlayableMinionSequence(board), board) <=
                     0))
                {
                    return true;
                }
                return false;
            }
        }
    }
}