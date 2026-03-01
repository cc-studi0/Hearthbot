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
					private int quantityOverflowLava(Card c)
				{
						return GetTag(c, Card.GAME_TAG.TAG_SCRIPT_DATA_NUM_1);
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
            {Card.Cards.VAC_414, 3},//VAC_414	炽热火炭
            {Card.Cards.BT_429p, 5},//BT_429p 恶魔冲击 
            {Card.Cards.BT_429p2, 5},//BT_429p2 恶魔冲击 
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
            {Card.Cards.CORE_BT_187, 3},//凯恩·日怒 Kayn Sunfury     BT_187
            {Card.Cards.BT_717, 2},//潜地蝎 Burrowing Scorpid     BT_717 
            {Card.Cards.CORE_EX1_249, 2},//迦顿男爵 Baron Geddon     CORE_EX1_249 
            {Card.Cards.DMF_254, 30},//迦顿男爵 Baron Geddon     CORE_EX1_249 
            {Card.Cards.RLK_222t2, 14},//火焰使者阿斯塔洛 Astalor, the Flamebringer ID：RLK_222t2
            {Card.Cards.RLK_224, 2},//监督者弗里吉达拉 Overseer Frigidara ID：RLK_224
             {Card.Cards.RLK_063, 5},//冰霜巨龙之怒 Frostwyrm's Fury ID：RLK_063 
            {Card.Cards.RLK_015, 3},//凛风冲击 Howling Blast ID：RLK_015 
            {Card.Cards.RLK_516, 2},//碎骨手斧 Bone Breaker ID：RLK_516
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
                AddLog($"================ 标准术士 决策日志 v{ProfileVersion} ================");
                AddLog($"敌方血甲: {enemyHealth} | 我方血甲: {friendHealth} | 法力:{board.ManaAvailable} | 手牌:{board.Hand.Count} | 牌库:{board.FriendDeckCount}");
                AddLog("手牌: " + string.Join(", ", board.Hand.Where(x => x != null && x.Template != null)
                    .Select(x => $"{(string.IsNullOrWhiteSpace(x.Template.NameCN) ? x.Template.Name : x.Template.NameCN)}({x.Template.Id}){x.CurrentCost}")));
            }
            catch
            {
                // ignore
            }
             
            //  增加思考时间
            int a =(board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) - BoardHelper.GetEnemyHealthAndArmor(board);
            //攻击模式切换
 // 德：DRUID 猎：HUNTER 法：MAGE 骑：PALADIN 牧：PRIEST 贼：ROGUE 萨：SHAMAN 术：WARLOCK 战：WARRIOR 瞎：DEMONHUNTER 死：DEATHKNIGHT
            if(board.HeroEnemy.CurrentHealth+board.HeroEnemy.CurrentArmor<=10
						// 敌方没有吸血随从
						&&board.MinionEnemy.Count(x=>x.IsLifeSteal==true)==0
						 ){
                p.GlobalAggroModifier = 500;
                AddLog("走脸");
							}else if(board.EnemyClass == Card.CClass.PALADIN
            || board.EnemyClass == Card.CClass.DEMONHUNTER
            || board.EnemyClass == Card.CClass.PRIEST
            || board.EnemyClass == Card.CClass.DEATHKNIGHT
            || board.EnemyClass == Card.CClass.HUNTER
            || board.EnemyClass == Card.CClass.MAGE
            || board.EnemyClass == Card.CClass.ROGUE
            || board.EnemyClass == Card.CClass.WARLOCK
            || board.EnemyClass == Card.CClass.SHAMAN)
            {
               p.GlobalAggroModifier = (int)(a * 0.625 + 86);
                AddLog("攻击值"+(a * 0.625 + 86));
            }else
            {
                         p.GlobalAggroModifier = (int)(a * 0.625 + 120);
                AddLog("攻击值"+(a * 0.625 + 120));
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
            int minionNumber=board.Hand.Count(card => card.Type == Card.CType.MINION);
        		// 定义敌方血量小于三血的随从
						int enemyMinionLessThreeHealth = board.MinionEnemy.Count(x => x.CurrentHealth <= 3);
						// 定义手上一费卡牌的数量
						int oneCostCardCount = board.Hand.Count(x => x.CurrentCost == 1);
								// 定义手上所有随从数量
						int allMinionCount = CountSpecificRacesInHand(board);
						// 尸体数量
            int numberBodies=board.CorpsesCount;
						 int safeHealth = 1;
            switch (board.EnemyClass)
            {
                case Card.CClass.HUNTER:
                    safeHealth = 7; // 猎人
                    break;
                case Card.CClass.ROGUE:
                    safeHealth = 2; // 盗贼
                    break;
                case Card.CClass.PRIEST:
                    safeHealth = 1; // 牧师
                    break;
                case Card.CClass.WARLOCK:
                    safeHealth = 7; // 术士
                    break;
                case Card.CClass.WARRIOR:
                    safeHealth = 4; // 战士
                    break;
                case Card.CClass.DEMONHUNTER:
                    safeHealth = 8; // 恶魔猎手
                    break;
                case Card.CClass.DEATHKNIGHT:
                    safeHealth = 6; // 死亡骑士
                    break;
                case Card.CClass.PALADIN:
                    safeHealth = 7; // 圣骑士
                    break;
                case Card.CClass.SHAMAN:
                    safeHealth = 7; // 萨满
                    break;
                case Card.CClass.MAGE:
                case Card.CClass.DRUID:
                    safeHealth = 5; // 法师或德鲁伊
                    break;
            }

            // 手里没有地狱火,场面随从为0,安全血量为1
            if (!board.Hand.Exists(x => x.Template.Id == Card.Cards.CORE_CS2_062) && board.MinionFriend.Count == 0)
            {
                safeHealth = 1;
            }
            // 手里有地狱火,安全血量为1
            if (board.Hand.Exists(x => x.Template.Id == Card.Cards.CORE_CS2_062))
            {
                safeHealth = 1;
            }
            // 手里没有地狱火,友方随从攻击力小于敌方随从攻击力,安全血量为1
            if (!board.Hand.Exists(x => x.Template.Id == Card.Cards.CORE_CS2_062) && myAttack < enemyAttack)
            {
                safeHealth = 1;
            }
            AddLog("当前血量"+board.HeroFriend.CurrentHealth);
            AddLog("安全血量"+safeHealth);
						 // 一费手中自残随从的数量
            int oneFeeSelfHarmMinionCount = board.Hand.Count(card => card.Template.Id == Card.Cards.CORE_EX1_319)+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_940);
            // 一费自残法术数量
            int oneFeeSelfHarmSpellCount = board.Hand.Count(card => card.Template.Id == Card.Cards.MIS_707);
            // 我方疲劳数
            int myFatigue=board.FriendFatigue;
 #endregion

  #region 自残卡牌
        //    烈焰小鬼 CORE_EX1_319
        var flameImp = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.CORE_EX1_319);
        //    派对邪犬 VAC_940
        var partyEvilDog = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.VAC_940);
        //    批量生产 MIS_707
        var massProduction = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.MIS_707);
        //    鲜血树人 JAM_028
        var bloodTreeMan = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.JAM_028);
        //    源质晶簇 DEEP_030
        var sourceCrystalCluster = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.DEEP_030);
        //    凶魔城堡 TOY_526
        var demonCastle = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TOY_526);
        // 地狱火
        var Hellfire = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.MIS_703);
#endregion

 #region 不送的怪
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.WW_331, new Modifier(150)); //奇迹推销员 WW_331
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(150)); //鱼人木乃伊 CORE_ULD_723 
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_927, new Modifier(150)); //VAC_927	狂飙邪魔
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_938, new Modifier(150)); //粗暴的猢狲 VAC_938
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(150)); //TOY_518	宝藏经销商
#endregion

#region 送的怪
          if(board.HasCardOnBoard(Card.Cards.TSC_962)){//修饰老巨鳍 Gigafin ID：TSC_962 
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TSC_962t, new Modifier(-100)); //修饰老巨鳍之口 Gigafin's Maw ID：TSC_962t 
          }
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.REV_373t, new Modifier(-100)); //具象暗影 Shadow Manifestation ID：REV_373t 
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.REV_955, new Modifier(-100)); //执事者斯图尔特 Stewart the Steward ID：REV_955 
#endregion

#region 生命分流 Life Tap   HERO_07bp 技能
        // p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_07bp, new Modifier(130));
// 如果血量大于4,手上有批量生产 MIS_707,降低生命分流优先级
        if(board.HeroFriend.CurrentHealth>=safeHealth+3
        &&board.HasCardInHand(Card.Cards.MIS_707)
        ){
        p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_07bp, new Modifier(999));
        // AddLog("生命分流1 999");
        }
        if(board.HeroFriend.CurrentHealth>=safeHealth+2
        &&board.HasCardInHand(Card.Cards.DEEP_030)
        ){
        p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_07bp, new Modifier(999));
        // AddLog("生命分流2 999");
        }
        // 如果手牌数小于3,且没有过牌,提高生命分流优先级
        if(board.Hand.Count<=5
        && board.HeroFriend.CurrentHealth>2
        &&!board.HasCardInHand(Card.Cards.MIS_707)
        &&!board.HasCardInHand(Card.Cards.DEEP_030)
        ){  
        p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_07bp, new Modifier(-10));
        AddLog("生命分流 -10");
        }
#endregion

// 狡诈的郊狼 TIME_047 郊狼的费用低于3费提高使用优先级
#region  狡诈的郊狼 TIME_047
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-999));
        var CunningJackal = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TIME_047);
        if(CunningJackal!=null
        &&board.Hand.Exists(x=>x.CurrentCost==0 && x.Template.Id==Card.Cards.TIME_047
        )){
             p.ComboModifier = new ComboSet(CunningJackal.Id);
            AddLog("低费郊狼出");
        }
        if(board.Hand.Exists(x=>(x.CurrentCost<=3) && x.Template.Id == Card.Cards.TIME_047)
        // 且手里没有1费随从
        &&!board.Hand.Exists(x=>x.CurrentCost==1)
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-150));
        AddLog("低于3费郊狼出");
        }
#endregion


// 灾毁迅疾幼龙 TIME_029 随从 标记态才使用
#region 灾毁迅疾幼龙 TIME_029
				if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TIME_029)
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_029, new Modifier(-150));
						AddLog("灾毁迅疾幼龙 TIME_029 -150");
				}else{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_029, new Modifier(350));
				}
#endregion

// 破命之龙 TIME_028 随从 标记态才使用
#region 破命之龙 TIME_028
				if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TIME_028)
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_028, new Modifier(-150));
						AddLog("破命之龙 TIME_028 -150");
				}else{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_028, new Modifier(350));
				}
#endregion

// 召唤师达克玛洛 VAC_503 4费随从
#region 召唤师达克玛洛 VAC_503
				if(board.HasCardInHand(Card.Cards.VAC_503)
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_503, new Modifier(-150));
						AddLog("召唤师达克玛洛 VAC_503 -150");
				}
#endregion

#region 甜筒殡淇淋 VAC_427
    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.VAC_427)
    ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_427, new Modifier(-5*numberBodies));
        AddLog($"甜筒殡淇淋{(-5*numberBodies)}");
						// 遍历友方随从降低对其使用优先级
				foreach (var item in board.MinionFriend)
				{
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_427, new Modifier(350,item.Template.Id));
						AddLog("降低对其使用甜筒殡淇淋"+(item.Template.Id));
				}
    }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_427, new Modifier(350));
    }

#endregion

// 鲜花商贩 EDR_889 随从 友方随从有龙 提高使用优先级
// #region 鲜花商贩 EDR_889
// 				if(board.HasCardInHand(Card.Cards.EDR_889)
// 					&&board.MinionFriend.Count(card => card.IsRace == Card.CRace.DRAGON) >= 1  
// 				){
// 					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_889, new Modifier(-150));
// 					AddLog("鲜花商贩 EDR_889 -150");
// 				}else{
// 					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_889, new Modifier(350));
// 				}
// #endregion

// 超光子弹幕 TIME_027 法术
#region 超光子弹幕 TIME_027
				if(board.HasCardInHand(Card.Cards.TIME_027)
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-99));
					AddLog("超光子弹幕 TIME_027 -99");
				}
#endregion

// 暮光时空跃迁者 TIME_025 随从
#region 暮光时空跃迁者 TIME_025
				if(board.HasCardInHand(Card.Cards.TIME_025)
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_025, new Modifier(-150));
					AddLog("暮光时空跃迁者 -150");
				}
#endregion

#region SC_010	跳虫
// 如果场上随从小于等于3
        if(board.HasCardInHand(Card.Cards.SC_010)
				// 场上随从数量小于等于6
				&&board.MinionFriend.Count<=5
        ){
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_010, new Modifier(350));
        	AddLog("跳虫 130");
        }else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_010, new Modifier(350));
				}
#endregion

// 续连熵能 TIME_026 友方随从大于等于2 提高使用优先级 数量越多 优先级越高
#region 续连熵能 TIME_026
				if(board.HasCardInHand(Card.Cards.TIME_026)
				&&board.MinionFriend.Count>=2
				){
						int countMinion=board.MinionFriend.Count;
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_026, new Modifier(-20*countMinion));
						AddLog("续连熵能 -"+20*countMinion);
				}
#endregion

// 礼盒雏龙 TOY_386 不是标记状态不用
#region 礼盒雏龙 TOY_386
				if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TOY_386)
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_386, new Modifier(-150));
						AddLog("礼盒雏龙 TOY_386");
				}else{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_386, new Modifier(350));
				}
#endregion


#region 派对策划者沃娜 VAC_945
// 如果处于标记状态,且当前随从数小于6,无脑出
        var Werner = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.VAC_945);
        if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.VAC_945)
        &&board.MinionFriend.Count < 6
        &&Werner!=null
        &&board.MaxMana>=6
        ){
            p.ComboModifier = new ComboSet(Werner.Id);
            AddLog("派对策划者沃娜出");
        }else{
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_945, new Modifier(999));
        }
#endregion

#region 咒怨之墓 TLC_451
				if(board.HasCardInHand(Card.Cards.TLC_451)
				// 费用大于等于2
				&&board.ManaAvailable >= 2
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-150));
					AddLog("咒怨之墓 TLC_451");
				}
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(3999));
#endregion

#region 烈焰小鬼 CORE_EX1_319
        // 一费有派对邪犬,降低烈焰小鬼优先级
        if(board.HasCardInHand(Card.Cards.CORE_EX1_319)
        &&board.HasCardInHand(Card.Cards.VAC_940)
        &&board.MaxMana==1
        ){
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_319, new Modifier(999)); 
          AddLog("烈焰小鬼999");
        }
        if(board.HasCardInHand(Card.Cards.CORE_EX1_319)
        &&board.HasCardInHand(Card.Cards.VAC_940)
        // 且随从数小于4
        &&board.MinionFriend.Count <=4
        &&board.MaxMana>1
        ){
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_319, new Modifier(150)); 
          AddLog("烈焰小鬼150");
        }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_319, new Modifier(999));
#endregion


#region 硬币 GAME_005
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(-55));//硬币 GAME_005
#endregion

#region 打印场上随从id
foreach (var item in board.MinionFriend)
{
    AddLog(item.Template.NameCN + ' ' + item.Template.Id);
}
// 打印手上的卡牌id
foreach (var item in board.Hand)
{
		AddLog(item.Template.NameCN + ' ' + item.Template.Id);
}

#endregion

#region 临时牌逻辑
// 判断是否为临时牌
foreach (var c in board.Hand.ToArray())
{
    var ench = c.Enchantments.FirstOrDefault(x => x.EnchantCard != null );
    if (ench != null)
    {
			p.CastMinionsModifiers.AddOrUpdate(c.Template.Id, new Modifier(-150));
			p.CastSpellsModifiers.AddOrUpdate(c.Template.Id, new Modifier(-150));
      AddLog($"提高临时牌使用优先级{c.Template.NameCN}-150");
    }
}
if (board != null && board.Hand != null)
{
    var markedIds = new HashSet<Card.Cards>();
    foreach (var c in board.Hand.ToArray())
    {
        if (c != null && c.Template != null && c.HasTag(SmartBot.Plugins.API.Card.GAME_TAG.LUNAHIGHLIGHTHINT))
        {
            markedIds.Add(c.Template.Id);
        }
    }

    foreach (var c in board.Hand.ToArray())
    {
        if (c != null && c.Template != null)
        {
            if (markedIds.Contains(c.Template.Id) && c.HasTag(SmartBot.Plugins.API.Card.GAME_TAG.LUNAHIGHLIGHTHINT))
            {
                p.CastMinionsModifiers.AddOrUpdate(c.Template.Id, -550);
                AddLog($"为临时卡 ,提高使用优先级{c.Template.NameCN}-550");
            }
            else if (markedIds.Contains(c.Template.Id) && !c.HasTag(SmartBot.Plugins.API.Card.GAME_TAG.LUNAHIGHLIGHTHINT) && c.Type == Card.CType.SPELL)
            {
                p.CastSpellsModifiers.AddOrUpdate(c.Template.Id, 150);
                AddLog($"卡片匹配但不是临时卡,降低使用优先级{c.Template.NameCN}150");
            }
        }
    }
}
#endregion

#region 重新思考
// 定义需要排除的卡牌集合

var excludedCards = new HashSet<string>
{
    Card.Cards.GAME_005.ToString(),
};
// 遍历并处理卡牌的方法
void EvaluateCards(IEnumerable<Card> cards)
{
    foreach (var card in cards)
    {
         // 如果卡牌不在排除列表中，则加入重新思考的队列
        if (!excludedCards.Contains(card.Template.Id.ToString()))
        {
            p.ForcedResimulationCardList.Add(card.Template.Id);
        }
    }
}
// 遍历手牌
EvaluateCards(board.Hand);

#endregion


#region 版本输出
               AddLog("---\n版本27.7 作者by77 Q群:943879501 DC:https://discord.gg/bc2kDM5E\n---");
#endregion

#region 攻击优先 卡牌威胁
var cardModifiers = new Dictionary<Card.Cards, int>
{
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
    { Card.Cards.VAC_406, 900 }, // 困倦的岛民 VAC_406
    { Card.Cards.TOY_646, 200 }, // 捣蛋林精 TOY_646
    { Card.Cards.TOY_357, 200 }, // 抱龙王噗鲁什 TOY_357
    { Card.Cards.VAC_507, 200 }, // 阳光汲取者莱妮莎 VAC_507
    { Card.Cards.WORK_042, 500 }, // 食肉格块 WORK_042
    { Card.Cards.WW_344, 200 }, // 威猛银翼巨龙 
    { Card.Cards.TOY_812, -500 },//TOY_812 皮普希·彩蹄
    { Card.Cards.VAC_532, 200 },//椰子火炮手 VAC_532
    { Card.Cards.RLK_511, 200 },//寒冬先锋	RLK_511
   
    { Card.Cards.TOY_505, 500 },//TOY_505	玩具船
    { Card.Cards.VAC_926t, 100 },//VAC_926t	坠落的伊利达雷
    { Card.Cards.TOY_381, 200 },//TOY_381	纸艺天使
    { Card.Cards.TSC_908, 55 }, // TSC_908	海中向导芬利爵士
    { Card.Cards.WORK_013, 55 }, // 湍流元素特布勒斯 WORK_013
    { Card.Cards.WW_901, 100 }, // WW_901	贪婪的伴侣
    { Card.Cards.TOY_824, 200 }, // 黑棘针线师
    { Card.Cards.VAC_927, 200 }, // 狂飙邪魔
    { Card.Cards.VAC_938, 200 }, // 粗暴的猢狲
    { Card.Cards.ETC_355, 200 }, // 剃刀沼泽摇滚明星
    { Card.Cards.WW_091, 500 },  // 腐臭淤泥波普加
    { Card.Cards.VAC_450, 9999 }, // 悠闲的曲奇
    { Card.Cards.TOY_028, 200 }, // 团队之灵
    { Card.Cards.VAC_436, 500 }, // 脆骨海盗
    { Card.Cards.VAC_321, 200 }, // 伊辛迪奥斯
    { Card.Cards.TTN_800, 200 }, // 雷霆之神高戈奈斯
    { Card.Cards.TTN_415, 200 }, // 卡兹格罗斯
    { Card.Cards.ETC_541, 200 }, // 盗版之王托尼
    { Card.Cards.CORE_LOOT_231, 200 }, // 奥术工匠
    { Card.Cards.ETC_339, 200 }, // 心动歌手
    { Card.Cards.ETC_833, 200 }, // 箭矢工匠
    { Card.Cards.MIS_026, 500 }, // 傀儡大师多里安
    { Card.Cards.CORE_WON_065, 200 }, // 随船外科医师
    { Card.Cards.WW_357, 500 }, // 老腐和老墓
    { Card.Cards.DEEP_999t2, 200 }, // 深岩之洲晶簇
    { Card.Cards.CFM_039, 200 }, // 杂耍小鬼
    { Card.Cards.WW_364t, 200 }, // 狡诈巨龙威拉罗克
    { Card.Cards.TSC_026t, 200 }, // 可拉克的壳
   
    { Card.Cards.WW_415, 200 }, // 许愿井
    { Card.Cards.CS3_014, 200 }, // 赤红教士
    { Card.Cards.YOG_516, 500 }, // 脱困古神尤格-萨隆
    { Card.Cards.NX2_033, 200 }, // 巨怪塔迪乌斯
    { Card.Cards.JAM_004, 500 }, // 镂骨恶犬
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
    { Card.Cards.TTN_856, 500 }, // Disciple of Amitus
    { Card.Cards.TTN_907, 200 }, // Astral Serpent
    { Card.Cards.TTN_071, 200 }, // Sif
    { Card.Cards.TTN_078, 200 }, // Observer of Myths
    { Card.Cards.TTN_843, 200 }, // Eredar Deceptor
   { Card.Cards.TTN_960, 200 }, // Sargeras, the Destroyer TITAN
    { Card.Cards.TTN_721, 200 }, // V-07-TR-0N Prime TITAN
    { Card.Cards.TTN_429, 500 }, // Aman'Thul TITAN
    { Card.Cards.TTN_858, 200 }, // Amitus, the Peacekeeper TITAN
    { Card.Cards.TTN_075, 200 }, // Norgannon TITAN
    { Card.Cards.TTN_092, 200 }, // Aggramar, the Avenger TITAN
    { Card.Cards.TTN_903, 200 }, // Eonar, the Life Binder TITAN
    { Card.Cards.TTN_862, 200 }, // Argus, the Emerald Star TITAN
    { Card.Cards.TTN_737, 200 }, // The Primus TITAN
    { Card.Cards.NX2_006, 200 }, // 旗标骷髅
    { Card.Cards.ETC_105, 200 }, // 立体声图腾
    { Card.Cards.ETC_522, 200 }, // 尖叫女妖
    { Card.Cards.RLK_121, 200 }, // 死亡侍僧
    { Card.Cards.RLK_539, 200 }, // 达尔坎·德拉希尔
    { Card.Cards.RLK_061, 200 }, // 战场通灵师
    { Card.Cards.RLK_824, 200 }, // 肢体商贩
    { Card.Cards.CORE_EX1_012, 200 }, // 血法师萨尔诺斯
    { Card.Cards.TSC_074, 200 }, // 克托里·光刃
    { Card.Cards.RLK_607, 200 }, // 搅局破法者
    { Card.Cards.RLK_924, 200 }, // 血骑士领袖莉亚德琳
    { Card.Cards.CORE_NEW1_020, 200 }, // 狂野炎术师
    { Card.Cards.RLK_083, 200 }, // 死亡寒冰
    { Card.Cards.RLK_572, 550 }, // 药剂大师普崔塞德
    { Card.Cards.RLK_218, 200 }, // 银月城奥术师
    { Card.Cards.REV_935, 200 }, // 派对图腾
    { Card.Cards.RLK_912, 200 }, // 天灾巨魔
    { Card.Cards.DMF_709, 200 }, // 巨型图腾埃索尔
    { Card.Cards.RLK_970, 200 }, // 陆行鸟牧人
    { Card.Cards.MAW_009t, 200 }, // 影犬
    { Card.Cards.TSC_922, 200 }, // 驻锚图腾
    { Card.Cards.AV_137, 200 }, // 深铁穴居人
    { Card.Cards.REV_515, 200 }, // 豪宅管家俄里翁
    { Card.Cards.TSC_959, 200 }, // 扎库尔
    { Card.Cards.TSC_218, 200 }, // 赛丝诺女士
    { Card.Cards.TSC_962, 200 }, // 老巨鳍
    { Card.Cards.REV_016, 200 }, // 邪恶的厨师
    { Card.Cards.REV_828t, 200 }, // 绑架犯的袋子
    { Card.Cards.KAR_006, 200 }, // 神秘女猎手
    { Card.Cards.REV_332, 200 }, // 心能提取者
    { Card.Cards.REV_011, 200 }, // 嫉妒收割者
    { Card.Cards.LOOT_412, 200 }, // 狗头人幻术师
    { Card.Cards.TSC_950, 200 }, // 海卓拉顿
    { Card.Cards.SW_062, 200 }, // 闪金镇豺狼人
    { Card.Cards.REV_513, 200 }, // 健谈的调酒师
    { Card.Cards.ONY_007, 200 }, // 监护者哈尔琳
    { Card.Cards.CS3_032, 200 }, // 龙巢之母奥妮克希亚
    { Card.Cards.SW_431, 200 }, // 花园猎豹
    { Card.Cards.AV_340, 200 }, // 亮铜之翼
    { Card.Cards.SW_458t, 200 }, // 塔维什的山羊
    { Card.Cards.WC_006, 200 }, // 安娜科德拉
    { Card.Cards.ONY_004, 200 }, // 团本首领奥妮克希亚
    { Card.Cards.TSC_032, 200 }, // 剑圣奥卡尼
    { Card.Cards.SW_319, 200 }, // 农夫
    { Card.Cards.TSC_002, 200 }, // 刺豚拳手
    { Card.Cards.CORE_LOE_077, 200 }, // 布莱恩·铜须
    { Card.Cards.TSC_620, 200 }, // 恶鞭海妖
    { Card.Cards.TSC_073, 200 }, // 拉伊·纳兹亚
    { Card.Cards.DED_006, 200 }, // 重拳先生
    { Card.Cards.CORE_AT_029, 200 }, // 锈水海盗
    { Card.Cards.BAR_074, 500 }, // 前沿哨所
    { Card.Cards.AV_118, 200 }, // 历战先锋
    { Card.Cards.GVG_040, 200 }, // 沙鳞灵魂行者
    { Card.Cards.BT_304, 200 }, // 改进型恐惧魔王
    { Card.Cards.SW_068, 200 }, // 莫尔葛熔魔
    { Card.Cards.DED_519, 200 }, // 迪菲亚炮手
    { Card.Cards.CFM_807, 200 }, // 大富翁比尔杜
    { Card.Cards.TSC_054, 200 }, // 机械鲨鱼
    { Card.Cards.GIL_646, 200 }, // 发条机器人
    { Card.Cards.SW_115, 200 }, // 伯尔纳·锤喙
    { Card.Cards.DMF_237, 200 }, // 狂欢报幕员
    { Card.Cards.DMF_217, 200 }, // 越线的游客
    { Card.Cards.DMF_120, 200 }, // 纳兹曼尼织血者
    { Card.Cards.DMF_707, 200 }, // 鱼人魔术师
    { Card.Cards.DMF_082, 200 }, // 暗月雕像
    { Card.Cards.DMF_708, 200 }, // 伊纳拉·碎雷
    { Card.Cards.DMF_102, 200 }, // 游戏管理员
    { Card.Cards.DMF_222, 200 }, // 获救的流民
    { Card.Cards.ULD_003, 200 }, // 了不起的杰弗里斯
    { Card.Cards.GVG_104, 200 }, // 大胖
    { Card.Cards.UNG_900, 250 }, // 灵魂歌者安布拉
    { Card.Cards.ULD_240, 250 }, // 对空奥术法师
    { Card.Cards.FP1_022, 50 }, // 空灵
    { Card.Cards.FP1_004, 50 }, // 疯狂的科学家
    { Card.Cards.BRM_002, 500 }, // 火妖
    { Card.Cards.CFM_020, 0 }, // 缚链者拉兹
    { Card.Cards.EX1_608, 250 }, // 巫师学徒
    { Card.Cards.BOT_447, -10 }, // 晶化师
    { Card.Cards.SCH_600t3, 250 }, // 加攻击的恶魔伙伴
    { Card.Cards.DRG_320, 0 }, // 新伊瑟拉
    { Card.Cards.CS2_237, 300 }, // 饥饿的秃鹫
    { Card.Cards.YOP_031, 250 }, // 螃蟹骑士
    { Card.Cards.BAR_537, 200 }, // 钢鬃卫兵
    { Card.Cards.BAR_035, 200 }, // 科卡尔驯犬者
    { Card.Cards.BAR_871, 250 }, // 士兵车队
    { Card.Cards.BAR_312, 200 }, // 占卜者车队
    { Card.Cards.BAR_043, 250 }, // 鱼人宝宝车队
    { Card.Cards.BAR_720, 230 }, // 古夫·符文图腾
    { Card.Cards.BAR_038, 200 }, // 塔维什·雷矛
    { Card.Cards.BAR_545, 200 }, // 奥术发光体
    { Card.Cards.BAR_888, 200 }, // 霜舌半人马
    { Card.Cards.BAR_317, 200 }, // 原野联络人
    { Card.Cards.BAR_918, 250 }, // 塔姆辛·罗姆
    { Card.Cards.BAR_076, 200 }, // 莫尔杉哨所
    { Card.Cards.BAR_890, 200 }, // 十字路口大嘴巴
    { Card.Cards.BAR_082, 200 }, // 贫瘠之地诱捕者
    { Card.Cards.BAR_540, 200 }, // 腐烂的普雷莫尔
    { Card.Cards.BAR_878, 200 }, // 战地医师老兵
    { Card.Cards.BAR_048, 200 }, // 布鲁坎
    { Card.Cards.BAR_075, 200 }, // 十字路口哨所
    { Card.Cards.BAR_744, 200 }, // 灵魂医者
    { Card.Cards.FP1_028, 200 }, // 送葬者
    { Card.Cards.CS3_019, 200 }, // 考瓦斯·血棘
    { Card.Cards.CORE_FP1_031, 200 }, // 瑞文戴尔男爵
    { Card.Cards.SCH_317, 200 }, // 团伙核心
    { Card.Cards.BAR_847, 200 }, // 洛卡拉
    { Card.Cards.CS3_025, 200 }, // 伦萨克大王
    { Card.Cards.YOP_021, 200 }, // 被禁锢的凤凰
    { Card.Cards.CS3_033, 200 }, // 沉睡者伊瑟拉
    { Card.Cards.CS3_034, 200 }, // 织法者玛里苟斯
    { Card.Cards.CORE_EX1_110, 0 }, // 凯恩·血蹄
    { Card.Cards.BAR_072, 0 }, // 火刃侍僧
    { Card.Cards.SCH_351, 200 } // 詹迪斯·巴罗夫
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
	public int CountSpecificRacesInHand(Board board)
				{
						if (board?.MinionFriend == null) return 0; // 检查手牌是否为 null

						// 定义所有可能的种族
						Card.CRace[] races = new Card.CRace[]
						{
								Card.CRace.BLOODELF,
								Card.CRace.DRAENEI,
								Card.CRace.DWARF,
								Card.CRace.GNOME,
								Card.CRace.GOBLIN,
								Card.CRace.HUMAN,
								Card.CRace.NIGHTELF,
								Card.CRace.ORC,
								Card.CRace.TAUREN,
								Card.CRace.TROLL,
								Card.CRace.UNDEAD,
								Card.CRace.WORGEN,
								Card.CRace.GOBLIN2,
								Card.CRace.MURLOC,
								Card.CRace.DEMON,
								Card.CRace.SCOURGE,
								Card.CRace.MECHANICAL,
								Card.CRace.ELEMENTAL,
								Card.CRace.OGRE,
								Card.CRace.PET,
								Card.CRace.TOTEM,
								Card.CRace.NERUBIAN,
								Card.CRace.PIRATE,
								Card.CRace.DRAGON,
								Card.CRace.BLANK,
								Card.CRace.ALL,
								Card.CRace.EGG,
								Card.CRace.QUILBOAR,
								Card.CRace.CENTAUR,
								Card.CRace.FURBOLG,
								Card.CRace.HIGHELF,
								Card.CRace.TREANT,
								Card.CRace.OWLKIN,
								Card.CRace.HALFORC,
								Card.CRace.LOCK,
								Card.CRace.NAGA,
								Card.CRace.OLDGOD,
								Card.CRace.PANDAREN,
								Card.CRace.GRONN,
								Card.CRace.CELESTIAL,
								Card.CRace.GNOLL,
								Card.CRace.GOLEM,
								Card.CRace.HARPY,
								Card.CRace.VULPERA
						};

						HashSet<Card.CRace> uniqueRaces = new HashSet<Card.CRace>();

						foreach (Card card in board.MinionFriend)
						{
								if (card?.Type != Card.CType.MINION) continue; // 忽略空卡或非随从卡

								foreach (Card.CRace race in races)
								{
										if (card.IsRace(race))
										{
												uniqueRaces.Add(race);
												break; // 确保每张卡只添加一个种族
										}
								}
						}

						return uniqueRaces.Count;
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