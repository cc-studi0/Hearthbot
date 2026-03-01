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
    public class STDFloodPaladin  : Profile
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
#endregion

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
                AddLog($"================ 标准猎人 决策日志 v{ProfileVersion} ================");
                AddLog($"敌方血甲: {enemyHealth} | 我方血甲: {friendHealth} | 法力:{board.ManaAvailable} | 手牌:{board.Hand.Count} | 牌库:{board.FriendDeckCount}");
                AddLog("手牌: " + string.Join(", ", board.Hand.Where(x => x != null && x.Template != null)
                    .Select(x => $"{(string.IsNullOrWhiteSpace(x.Template.NameCN) ? x.Template.Name : x.Template.NameCN)}({x.Template.Id}){x.CurrentCost}")));
            }
            catch
            {
                // ignore
            }
            //  增加思考时间
            // p.ForceResimulation = true; 
            int a =(board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) - BoardHelper.GetEnemyHealthAndArmor(board);
            //攻击模式切换
            // 德：DRUID 猎：HUNTER 法：MAGE 骑：PALADIN 牧：PRIEST 贼：ROGUE 萨：SHAMAN 术：WARLOCK 战：WARRIOR 瞎：DEMONHUNTER 死：DEATHKNIGHT
           
// 主邏輯：根據職業計算動態攻擊值
switch (board.EnemyClass)
{
    case Card.CClass.PALADIN:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 64, board.EnemyClass);
        break;

    case Card.CClass.DEMONHUNTER:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 55, board.EnemyClass);
        break;

    case Card.CClass.PRIEST:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 65, board.EnemyClass);
        break;

    case Card.CClass.MAGE:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 60, board.EnemyClass);
        break;

    case Card.CClass.DEATHKNIGHT:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 50, board.EnemyClass);
        break;

    case Card.CClass.HUNTER:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 40, board.EnemyClass);
        break;

    case Card.CClass.ROGUE:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 40, board.EnemyClass);
        break;

    case Card.CClass.WARLOCK:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 80, board.EnemyClass);
        break;

    case Card.CClass.SHAMAN:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 35, board.EnemyClass);
        break;

    default:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 60, board.EnemyClass); // 預設職業
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
						// 定义敌方手牌的数量
						int enemyHandCount = board.EnemyCardCount;
            // 友方随从数量
            int friendCount = board.MinionFriend.Count;
            // 手牌数量
            int HandCount = board.Hand.Count;
             // 通电机器人 BOT_907
            int aomiCount = board.Secret.Count;
            int minionNumber=board.Hand.Count(card => card.Type == Card.CType.MINION);
            var TheVirtuesofthePainter = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TOY_810);
             // 定义坟场中一费随从的数量 TUTR_HERO_11bpt
            int oneCostMinionCount = board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id==Card.Cards.WW_331||CardTemplate.LoadFromId(card).Id==Card.Cards.CORE_ICC_038||CardTemplate.LoadFromId(card).Id==Card.Cards.CORE_ULD_723)
            // 加手上的这些牌
            +board.Hand.Count(card => card.Template.Id==Card.Cards.WW_331||card.Template.Id==Card.Cards.CORE_ICC_038||card.Template.Id==Card.Cards.CORE_ULD_723)
            // 加场上的这些牌
            +board.MinionFriend.Count(card => card.Template.Id==Card.Cards.WW_331||card.Template.Id==Card.Cards.CORE_ICC_038||card.Template.Id==Card.Cards.CORE_ULD_723);
            ;
            // AddLog("一费随从数量"+oneCostMinionCount);
            // 场上实际能存活随从数 -脆弱的食尸鬼的数量 TUTR_HERO_11bpt/HERO_11bpt
            int liveMinionCount = board.MinionFriend.Count-board.MinionFriend.Count(x => x.Template.Id == Card.Cards.HERO_11bpt);
            int canAttackMinion=board.MinionFriend.Count(card => card.CanAttack);
            // 定义敌方三血及以下随从的数量
            int enemyThreeHealthMinionCount = board.MinionEnemy.Count(card => card.CurrentHealth<=3);
							// 定义场上所有随从数量
						int allMinionCount = CountSpecificRacesInHand(board);
						// 定义场上野兽的数量
						int beastCount = board.MinionFriend.Count(card => card.IsRace(Card.CRace.PET));
						AddLog($"场上野兽数量: {beastCount}");
						// 低费法术可以对目标使用的
						int lowCostSpells = board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916t2)  // 圣光护盾
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916)  // 神圣佳酿 VAC_916
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916t3) // 神圣佳酿 VAC_916
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_922) // 救生光环 VAC_922 
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_251); // 龙鳞军备 EDR_251; // 神圣佳酿 VAC_916 神圣
						// AddLog($"手上低费法术数量: {lowCostSpells}");
						// 判断手上是否有大于0费的海上船歌 VAC_558和大于0费的灯火机器人 MIS_918 MIS_918t
						int seaShantyCount = board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_558 && card.CurrentCost > 0)+
						board.Hand.Count(card => card.Template.Id == Card.Cards.MIS_918t && card.CurrentCost > 0)+
						board.Hand.Count(card => card.Template.Id == Card.Cards.MIS_918 && card.CurrentCost > 0);
						// AddLog($"手上大于0费的海上船歌和灯火机器人数量: {seaShantyCount}");
						// 判断手里是否有灌注牌，苦花骑士 EDR_852  金萼幼龙 EDR_451  振翅守卫 EDR_800 圣光护盾 EDR_264
						int infusedCardsCount = board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_852) // 苦花骑士 EDR_852
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_451) // 金萼幼龙 EDR_451
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_800) // 振翅守卫 EDR_800
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_264); // 圣光护盾 EDR_264
						  // 可攻击随从
            int canAttackMINIONs=board.MinionFriend.Count(card => card.CanAttack);
						// 判断手上是否有buff牌 观赏鸟类 VAC_408 预留泊位 WORK_021 腱力金杯 VAC_338 VAC_338t VAC_338t2
						int buffCardsCount = board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_408) // 观赏鸟类 VAC_408
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.WORK_021) // 预留泊位 WORK_021
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_338) // 腱力金杯 VAC_338
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_338t) // 腱力金杯 VAC_338t
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_338t2); // 腱力金杯 VAC_338t2
						// AddLog($"手上增益牌数量: {buffCardsCount}");
						// 手上是否有可优先被buff的随从  笨拙的搬运工 VAC_521 神秘符文熊 EDR_481
						int buffableMinionsCount = board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_521) // 笨拙的搬运工 VAC_521
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_481); // 神秘符文熊 EDR_481
						// AddLog($"手上可优先被buff的随从数量: {buffableMinionsCount}");
						// 定义敌方2血及以下随从
						int enemyMinionsWith2HealthOrLess = board.MinionEnemy.Count(card => card.CurrentHealth <= 2);
						// AddLog($"敌方2血及以下随从数量: {enemyMinionsWith2HealthOrLess}");
 #endregion
 #region 不送的怪
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.WW_331, new Modifier(150)); //奇迹推销员 WW_331
        //   p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(150)); //鱼人木乃伊 CORE_ULD_723 
#endregion

#region 送的怪
        
          if(board.HasCardOnBoard(Card.Cards.TSC_962)){//修饰老巨鳍 Gigafin ID：TSC_962 
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TSC_962t, new Modifier(-100)); //修饰老巨鳍之口 Gigafin's Maw ID：TSC_962t 
          }
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.REV_373t, new Modifier(-100)); //具象暗影 Shadow Manifestation ID：REV_373t 
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.REV_955, new Modifier(-100)); //执事者斯图尔特 Stewart the Steward ID：REV_955 
#endregion


#region 硬币 GAME_005
		// 一费时,手上没有两费随从,但是有三费随从,则降低,其他时候随意
		// if(board.MaxMana == 1
		// && board.HasCardInHand(Card.Cards.GAME_005) //硬币
		// &&!board.Hand.Exists(x=>x.CurrentCost==2&&x.Type == Card.CType.MINION)
		// &&board.Hand.Exists(x=>x.CurrentCost==3&&(x.Type == Card.CType.MINION||x.Template.Id == Card.Cards.CORE_GVG_061))
		// ){
		// 	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(55));
		// }else{
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(-55));
		// }

#endregion

#region Card.Cards.HERO_05bp 英雄技能
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.HERO_05bp, new Modifier(-500));
				// 
        p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_05bp, new Modifier(100));
				// 打印出英雄技能board.EnemyAbility.Template.Id
				AddLog($"英雄技能: {board.Ability.Template.Id} ");
#endregion
// 远古迅猛龙 TLC_245 默认选择第二项
#region 远古迅猛龙 TLC_245
			if (board.HasCardInHand(Card.Cards.TLC_245))
			{
					p.ChoicesModifiers.AddOrUpdate(Card.Cards.TLC_245, new Modifier( 100, 2 ));
					AddLog($"远古迅猛龙 TLC_245 默认选择第二项 优先级提升: {-100}");
			}
#endregion

// 手牌数大于等于3,不使用拾箭龙鹰 TIME_601 需要减去他自身
#region 拾箭龙鹰 TIME_601
			if (board.HasCardInHand(Card.Cards.TIME_601)
			&& board.Hand.Count >= 3
			)
			{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_601, new Modifier(350));
					AddLog($"拾箭龙鹰 TIME_601 优先级降低: {350}");
			}else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_601, new Modifier(-150));
			}
#endregion

#region 宠物鹦鹉 VAC_961
      if (board.HasCardInHand(Card.Cards.VAC_961)
			// 随从数量小于等于6
			&& board.MinionFriend.Count <= 6
			// 手牌数量小于等于8
			&& board.Hand.Count <= 8
			)
      {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_961, new Modifier(-150));
          AddLog($"宠物鹦鹉 VAC_961 优先级提升: {-150}");
      }else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_961, new Modifier(350));
			}
#endregion

#region 潮池学徒 VAC_304
      if (board.HasCardInHand(Card.Cards.VAC_304)
			&& board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.VAC_304)
			)
      {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_304, new Modifier(-150));
          AddLog($"潮池学徒 VAC_304 优先级提升: {-150}");
      }else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_304, new Modifier(350));
			}
#endregion

#region 丑恶的残躯 EDR_810
      if (board.HasCardInHand(Card.Cards.EDR_810)
			&&board.ManaAvailable >= 6
			)
      {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_810, new Modifier(-150));
          AddLog($"丑恶的残躯 EDR_810 优先级提升: {-150}");
      }else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_810, new Modifier(350));
			}
#endregion

#region 甲虫钥匙链 TOY_006
      if (board.HasCardInHand(Card.Cards.TOY_006))
      {
          p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_006, new Modifier(-150));
          p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_006, new Modifier(999));
          AddLog($"甲虫钥匙链 TOY_006 优先级提升: {-150}");
      }
#endregion

#region 劳作老马 WORK_018
      if (board.HasCardInHand(Card.Cards.WORK_018))
      {
          p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WORK_018, new Modifier(-150));
          p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WORK_018, new Modifier(999));
          AddLog($"劳作老马 WORK_018 优先级提升: {-150}");
      }
#endregion

#region 卡纳莎的故事 TLC_826
      if (board.HasCardInHand(Card.Cards.TLC_826))
      {
          p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_826, new Modifier(-150));
          AddLog($"卡纳莎的故事 优先级提升: {-150}");
      }
#endregion

#region 阿迦玛甘 EDR_489
      if (board.HasCardInHand(Card.Cards.EDR_489))
      {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_489, new Modifier(-150));
          AddLog($"阿迦玛甘 优先级提升: {-150}");
      }
#endregion

#region 乌索克 EDR_819
      if (board.HasCardInHand(Card.Cards.EDR_819))
      {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_819, new Modifier(-150));
          AddLog($"乌索克 优先级提升: {-150}");
      }
#endregion

#region 恐惧畏缩 TLC_823
// 遍历手牌,拿到野兽牌的费用,如果当前野兽牌的费用-2小于等于可用水晶,提高使用恐惧畏缩优先级
foreach (var card in board.Hand)
			{
					if (card.IsRace(Card.CRace.PET) && card.CurrentCost - 2 <= board.ManaAvailable)
					{
							 //  敌方有随从,提高恐惧畏缩 TLC_823使用优先级
						if (board.MinionEnemy.Count > 0
						&& board.HasCardInHand(Card.Cards.TLC_823)
						)
						{
								p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_823, new Modifier(-20));
								AddLog($"恐惧畏缩 TLC_823 优先级提升: {-20}");
						}
							break; // 找到一张符合条件的野兽牌后退出循环
					}
			}

     
#endregion

#region 天降蛛群 CORE_AT_062
        // 如果场上随从数量小于等于4
				if (board.MinionFriend.Count <= 4
				&& board.HasCardInHand(Card.Cards.CORE_AT_062) //天降蛛群 CORE_AT_062
				)
				{
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_AT_062, new Modifier(-150));
						AddLog($"天降蛛群 CORE_AT_062 优先级提升: {-150}");
				}
#endregion


#region 抛接嬉戏 TOY_352
				// 如果手上有抛接嬉戏 TOY_352
				if (board.HasCardInHand(Card.Cards.TOY_352)
				&& board.Hand.Count <= 8
				)
				{
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_352, new Modifier(-350));
						AddLog($"抛接嬉戏 TOY_352 优先级提升: {-350}");
				}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_352, new Modifier(3999));
#endregion

#region 漆彩帆布龙 TOY_350
        // 如果手上有漆彩帆布龙 TOY_350
				if (board.HasCardInHand(Card.Cards.TOY_350)
				// 且场上野兽随从数量大于等于2
				&& beastCount >= 2
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_350, new Modifier(-20*beastCount));
						AddLog($"漆彩帆布龙 TOY_350 优先级提升: {-20*beastCount}");
				}else{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_350, new Modifier(350));
				}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_350, new Modifier(-5));
#endregion

#region 梦缚迅猛龙 EDR_849
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_849, new Modifier(4999)); 
			//  大于等于两费,提高使用优先级
			 if ((board.MaxMana >= 2||board.HasCardInHand(Card.Cards.GAME_005))
			//  手里有硬币
				&& board.HasCardInHand(Card.Cards.EDR_849) //梦缚迅猛龙 EDR_849
				&&!board.HasCardOnBoard(Card.Cards.EDR_849)
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_849, new Modifier(-350)); 
						AddLog("梦缚迅猛龙 EDR_849 -350");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_849, new Modifier(350));
				}
			if(board.HasCardOnBoard(Card.Cards.EDR_849)){
				p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.EDR_849, new Modifier(350)); 
				AddLog("梦缚迅猛龙不送");
				}
				// 手里有迅猛龙,不用凶恶的滑矛纳迦 CORE_TSC_827
				if (board.HasCardInHand(Card.Cards.EDR_849) //梦缚迅猛龙 EDR_849
				&& board.HasCardInHand(Card.Cards.CORE_TSC_827) //凶恶的滑矛纳迦 CORE_TSC_827
				&&(board.MaxMana >= 2||(board.HasCardInHand(Card.Cards.GAME_005)&&board.MaxMana == 1))
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_TSC_827, new Modifier(350)); 
						AddLog("凶恶的滑矛纳迦不使用");
				}
#endregion

#region 丛林乐园 TOY_359
				// 如果手上有丛林乐园 TOY_359
				if (board.HasCardInHand(Card.Cards.TOY_359)
				&& board.MinionFriend.Count <= 6
				)
				{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_359, new Modifier(-20*beastCount));
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_359, new Modifier(999));
					AddLog($"丛林乐园 TOY_359 优先级提升: {-20*beastCount}");
				}
				// 如果场上有丛林乐园 TOY_359
				if (board.HasCardOnBoard(Card.Cards.TOY_359)
				&& beastCount < 2
				)
				{
					p.LocationsModifiers.AddOrUpdate(Card.Cards.TOY_359, new Modifier(350));
					AddLog($"丛林乐园 不用");
				}
#endregion

#region 爆裂射击 FIR_909
       if(board.HasCardInHand(Card.Cards.FIR_909) 
			 && enemyMinionsWith2HealthOrLess > 2)
       {
               p.CastSpellsModifiers.AddOrUpdate(Card.Cards.FIR_909, new Modifier(-99));
               AddLog($"爆裂射击 FIR_909 优先级提升: {-99}");
       }else if(
						board.HasCardInHand(Card.Cards.FIR_909)
						// 敌方英雄血量小于等于12
						&& board.HeroEnemy.CurrentHealth <= 12	
			 ){
							 p.CastSpellsModifiers.AddOrUpdate(Card.Cards.FIR_909, new Modifier(-150));
							 AddLog($"爆裂射击 FIR_909 优先级提升: {-150}");
			 }
       else
       {
               p.CastSpellsModifiers.AddOrUpdate(Card.Cards.FIR_909, new Modifier(350));
       }
#endregion

#region 追踪术 CORE_DS1_184
// 手里有buff牌,手里没有随从牌
				if (board.HasCardInHand(Card.Cards.CORE_DS1_184) && buffCardsCount > 0 && allMinionCount == 0)
				{
						// 提高使用优先级
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_DS1_184, new Modifier(-350));
						AddLog($"追踪术 CORE_DS1_184 优先级提升: {-350}");
				}
				else
				{
						// 降低使用优先级
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_DS1_184, new Modifier(350));
						AddLog($"追踪术 CORE_DS1_184 优先级降低: {350}");
				}
       
#endregion

#region 腱力金杯 VAC_338 VAC_338t VAC_338t2
      //  如果手上有腱力金杯 VAC_338 VAC_338t VAC_338t2,手里有笨拙的搬运工 VAC_521或者神秘符文熊 EDR_481
			if (board.HasCardInHand(Card.Cards.VAC_338) || board.HasCardInHand(Card.Cards.VAC_338t) || board.HasCardInHand(Card.Cards.VAC_338t2))
			{
					// 如果手上有笨拙的搬运工 VAC_521
					if (board.HasCardInHand(Card.Cards.VAC_521))
					{
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_338, new Modifier(-99));
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_338t, new Modifier(-99));
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_338t2, new Modifier(-99));
							AddLog("腱力金杯 VAC_338 VAC_338t VAC_338t2 优先级提升: -99");
					}
					// 如果手上有神秘符文熊 EDR_481
					if (board.HasCardInHand(Card.Cards.EDR_481))
					{
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_338, new Modifier(-99));
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_338t, new Modifier(-99));
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_338t2, new Modifier(-99));
							AddLog("腱力金杯 VAC_338 VAC_338t VAC_338t2 优先级提升: -99");
					}
			}
#endregion

#region 遥控器 TOY_358
			if(board.WeaponFriend == null
			&& board.HasCardInHand(Card.Cards.TOY_358)
			){
				// 提刀
				p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.TOY_358, new Modifier(-5));
				// 打印出提刀信息
				AddLog($"遥控器: {Card.Cards.TOY_358}");
			}
			// 攻击优先级提高
			p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TOY_358, new Modifier(3999));
			p.AttackOrderModifiers.AddOrUpdate(Card.Cards.EDR_416, new Modifier(3999));
			// p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.TOY_358, new Modifier(3999));
			// p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.EDR_416, new Modifier(3999));//牧人之杖 EDR_416
				if(board.WeaponFriend != null
			&& board.WeaponFriend.Template.Id == Card.Cards.TOY_358
			//场上随从小于等于6
			&& board.MinionFriend.Count == 7
			){
				// 提刀
				p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.TOY_358, new Modifier(350));
				// 打印出提刀信息
				AddLog($"遥控器不A");
			}
#endregion

#region 牧人之杖 EDR_416
// 如果手上有牧人之杖 EDR_416
			if (board.HasCardInHand(Card.Cards.EDR_416)
			)
			{
			p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.EDR_416, new Modifier(-10));
			AddLog($"牧人之杖 EDR_416 优先级提升: {-10}");
			}
			if(board.WeaponFriend != null
			&& board.WeaponFriend.Template.Id == Card.Cards.EDR_416
			//场上随从小于等于6
			&& board.MinionFriend.Count <= 6
			){
				// 提刀
				p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.EDR_416, new Modifier(-150));
				// 打印出提刀信息
				AddLog($"牧人之杖A");
			}else{
				p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.EDR_416, new Modifier(350));
			}
#endregion

#region 遥控狂潮 TOY_354
/*

场上已有随从数量	剩余空位	实际召唤猎犬数量	每只猎犬属性	总攻击力
0	7	6	1/1	6
1	6	6	1/1	6
2	5	5	2/2	10
3	4	4	3/3	12
4	3	3	4/4	12
5	2	2	5/5	10
6	1	1	6/6	6
7	0	0	无	0


*/ 
    // 计算场上猎犬的数量
    int houndCount = board.MinionFriend.Count(minion => minion.Template.Id == Card.Cards.TOY_358t);
    // 计算场上剩余的空位
    int availableSlots =  board.MinionFriend.Count;

    // 如果手上有遥控狂潮 TOY_354
    if (board.HasCardInHand(Card.Cards.TOY_354))
    {
        // 如果场上有足够的空位放下召唤的猎犬
        if (availableSlots <= 6)
        {
            // 提高使用优先级
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_354, new Modifier(-20*availableSlots));
            AddLog($"遥控狂潮优先级提升: {(-20*availableSlots)}，当前猎犬数量: {houndCount}，空位: {availableSlots}");
        }
        else
        {
            // 如果没有足够的空位，降低使用优先级
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_354, new Modifier(999));
            AddLog($"遥控狂潮优先级降低: {999}，当前猎犬数量: {houndCount}，空位: {availableSlots}");
        }
    }
            // p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_354, new Modifier(3999));
#endregion

#region 拼布好朋友 TOY_353
      // 手牌数小于等于8
				if (HandCount <= 8
				)
				{
						// 如果手上有拼布好朋友 TOY_353,则使用优先级+100
						if (board.HasCardInHand(Card.Cards.TOY_353))
						{
								p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_353, new Modifier(-999));
								p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_353, new Modifier(3999));
								AddLog($"拼布好朋友 TLC_353 -999");
						}
				}
#endregion


#region 城市首脑埃舒 TLC_110
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_110, new Modifier(3999));
				if(board.HasCardInHand(Card.Cards.TLC_110)
				// 场上有野兽
				&&beastCount>0
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_110, new Modifier(-50*beastCount));
						AddLog($"城市首脑埃舒 TLC_110 {-50*beastCount}");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_110, new Modifier(-150));
				}
#endregion

#region 顶级恐龙学 TLC_828
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_828, new Modifier(999));
				if(board.HasCardInHand(Card.Cards.TLC_828)
				// 场上有野兽
				&&beastCount>0
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_828, new Modifier(-50*beastCount));
						AddLog($"顶级恐龙学 TLC_828 {-50*beastCount}");
				}else{
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_828, new Modifier(-150));
				}
#endregion

#region 观赏鸟类 VAC_408
// 提高VAC_408使用优先级
if(board.HasCardInHand(Card.Cards.VAC_408)
){
p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_408, new Modifier(-350));
AddLog("观赏鸟类-350");
}
#endregion

#region 预留泊位 WORK_021
// 标记时使用,否则降低使用优先级
if(
board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.WORK_021)
){
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WORK_021, new Modifier(-350));
		AddLog("预留泊位 WORK_021 -350");
}else if(buffableMinionsCount> 0)
{
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WORK_021, new Modifier(-350));
		AddLog("预留泊位 WORK_021 优先级提升: -350");
}else{
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WORK_021, new Modifier(350));
}
#endregion

#region 神秘符文熊 EDR_481
// 标记时使用,否则降低使用优先级
if(
board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.EDR_481)
// 场上友方随从小于等于5
&& board.MinionFriend.Count <= 5
&& buffCardsCount==0
){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_481, new Modifier(-150));
		AddLog("神秘符文熊 EDR_481 标记时使用");
}else{
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_481, new Modifier(350));
}
#endregion

#region 笨拙的搬运工 VAC_521
// 标记时使用,否则降低使用优先级
if(
board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.VAC_521)
// 场上友方随从小于等于5
&& board.MinionFriend.Count <= 5
&&buffCardsCount==0
){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_521, new Modifier(-150));
		AddLog("笨拙的搬运工 VAC_521 标记时使用");
}else{
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_521, new Modifier(350));
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


// 逻辑后续 - 重新思考
// if (p.ForcedResimulationCardList.Any())
// {
    // 如果支持日志系统，使用框架的日志记录方法
    // AddLog("需要重新评估策略的卡牌数: " + p.ForcedResimulationCardList.Count);

    // 如果没有日志系统，可以直接处理策略
    // 替代具体的重新思考逻辑
// }
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


#region 减费出高费的逻辑
// 如果手上存在0费的牌,降低一费随从的使用优先级 啮齿绿鳍鱼人 EDR_999  进化融合怪 VAC_958 鱼人木乃伊 CORE_ULD_723 鱼人招潮者 CORE_EX1_509
if(
	board.Hand.Any(card => card.CurrentCost == 0 && card.IsRace(Card.CRace.MURLOC))
){
	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_999, new Modifier(999));
	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_958, new Modifier(999));
	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(999));
	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_509, new Modifier(999));
	AddLog("手上有0费的鱼人牌，降低一费随从的使用优先级");
}else{
	 if(board.HasCardInHand(Card.Cards.EDR_999)
    ){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_999, new Modifier(-150));
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_999, new Modifier(1999));
			AddLog("啮齿绿鳍鱼人 -150");
    }
		if(board.HasCardInHand(Card.Cards.VAC_958)
					){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_958, new Modifier(-150));
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_958, new Modifier(999));
					AddLog("进化融合怪-150");
					}
					  if(board.HasCardInHand(Card.Cards.CORE_ULD_723)
                    // 当有其他一费随从时
          &&board.Hand.Count(card => card.CurrentCost==1&&card.Template.Id!=Card.Cards.CORE_ULD_723)>=1
					// 小于3费时
					&&board.MaxMana<=3
					){
        	        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(130)); 
					AddLog("鱼人木乃伊130");
					}else{
					        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(-20)); 
					}
        	        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(999));       
									 if(board.HasCardInHand(Card.Cards.CORE_EX1_509)
		// 友方随从小于等于5
		&&board.MinionFriend.Count <= 5
    ){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_509, new Modifier(4999));
    }
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
				public class SilverHandProtector
				{
						private readonly Board _board;
						private readonly List<Card.Cards> _silverHandCards;

						public SilverHandProtector(Board board)
					
						{
								_board = board;
								_silverHandCards = new List<Card.Cards>
								{
										Card.Cards.CS2_101t,
										Card.Cards.CS2_101t2,
										Card.Cards.CS2_101t3,
										Card.Cards.CS2_101t4,
										Card.Cards.CS2_101t5,
										Card.Cards.CS2_101t6,
										Card.Cards.CS2_101t7,
										Card.Cards.CS2_101t8
								};
						}

						public void ProtectMinions(ProfileParameters p)
						{
								foreach (var card in _silverHandCards)
								{
										if (_board.HasCardOnBoard(card))
										{
												p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(card, new Modifier(250));
										}
								}
						}

						public bool ShouldProtect()
						{
								// 如果我方随从数量大于敌方随从数量，且手上有十字军光环或者光速抢购
								return (_board.HasCardInHand(Card.Cards.TTN_908) || _board.HasCardInHand(Card.Cards.TOY_716)) &&
											_silverHandCards.Any(card => _board.HasCardOnBoard(card));
						}
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
                    else if (board.Secret.Count == 0)
                    {
                        ret.Add(card1.Template.Id);
                    }
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
                    //以友方随从攻击力 降序排序 的 场上的所有友方随从集合，如果该集合存在生命值大于与敌方随从攻击力
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