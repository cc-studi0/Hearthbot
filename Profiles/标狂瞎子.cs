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
    public class SWDemonhunter  : Profile
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
            {Card.Cards.WW_403, 3},//袋底藏沙 WW_403
            {Card.Cards.GDB_473, 2},//猎头 GDB_473
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
    private const string ProfileVersion = "2026-01-11.1";
      public ProfileParameters GetParameters(Board board)
      {
            _log = "";
            var p = new ProfileParameters(BaseProfile.Rush) { DiscoverSimulationValueThresholdPercent = -10 }; 

            try
            {
                int enemyHealth = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
                int friendHealth = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
                AddLog($"================ 标狂瞎子 决策日志 v{ProfileVersion} ================");
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
            int minionNumber=board.Hand.Count(card => card.Type == Card.CType.MINION);
             // 场上可以攻击的随从数量
						int canAttackCount = board.MinionFriend.Count(x => x.CanAttack);
            // 可攻击海盗数量
            int canAttackPirates=board.MinionFriend.Count(card => card.CanAttack && (card.IsRace(Card.CRace.PIRATE)||card.IsRace(Card.CRace.ALL)));
              // 手牌海盗数量
						int NumberHandPirates=board.Hand.Count(card => card.IsRace(Card.CRace.PIRATE))+board.Hand.Count(card => card.IsRace(Card.CRace.ALL));
               // 场上海盗数量
            int NumPirates=board.MinionFriend.Count(card => card.IsRace(Card.CRace.PIRATE))+board.MinionFriend.Count(card => card.IsRace(Card.CRace.ALL));
             // 除去空降匪徒
            int removeAirborneBandits=NumberHandPirates-board.Hand.Count(x => x.Template.Id == Card.Cards.DRG_056);
             //奥秘数量
            int numberMysteries = board.Secret.Count;
             // 船载火炮 GVG_075 
            var shipborneArtillery = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.GVG_075);
            //TOY_505	玩具船
            var toyBoat = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TOY_505);
            // 敌方海盗数量
            int enemyPirates=board.MinionEnemy.Count(card => card.IsRace(Card.CRace.PIRATE))+board.MinionEnemy.Count(card => card.IsRace(Card.CRace.ALL));
            int canAttackMINIONs=board.MinionFriend.Count(card => card.CanAttack);
						// 定义敌方血量小于三血的随从
						int enemyMinionLessThreeHealth = board.MinionEnemy.Count(x => x.CurrentHealth <= 3);
								// 定义手上所有随从数量
						int allMinionCount = CountSpecificRacesInHand(board);
						// 判断手上是否有自残牌 影随员工 WORK_032 桑拿常客 VAC_418
						bool hasSelfDamageCard = board.Hand.Any(card => card.Template.Id == Card.Cards.WORK_032 || card.Template.Id == Card.Cards.VAC_418);
						// 计算坟场亡语随从数量  球霸野猪人 TOY_642 贪婪的地狱猎犬 EDR_891 蒂尔德拉，反抗军头目 GDB_117
						int NumberOfDeadFollowers = board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.GDB_117)
						+ board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.TOY_642)
						+ board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.TLC_468)//黏团焦油 TLC_468
						+ board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.EDR_891);
						AddLog($"坟场亡语随从数量: {NumberOfDeadFollowers}");
						// 判断手上,场上,坟场中玛瑟里顿（未发售版） TOY_647和伊利达雷审判官 CS3_020 的数量
						int toy647Count = board.Hand.Count(card => card.Template.Id == Card.Cards.TOY_647)
						+ board.MinionFriend.Count(card => card.Template.Id == Card.Cards.TOY_647)
						+ board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.TOY_647);
						int cs3_020Count = board.Hand.Count(card => card.Template.Id == Card.Cards.CS3_020)
						+ board.MinionFriend.Count(card => card.Template.Id == Card.Cards.CS3_020)
						+ board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.CS3_020);
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

#region Card.Cards.HERO_10bp 英雄技能
        // p.PlayOrderModifiers.AddOrUpdate(Card.Cards.HERO_10bp, new Modifier(-50));
				
				int hpModifier = 0;

				// 如果场上有伴唱机 TOY_528 ,提高技能使用优先级
				if(board.HasCardOnBoard(Card.Cards.TOY_528)){
					hpModifier = -99; 
					AddLog("伴唱机提高技能使用优先级 -99");
				}

				// 优化逻辑：当法力值大于等于2且手上有树精时，先下树精再技能
				if (board.HasCardInHand(Card.Cards.EDR_469))
				{
						bool hasSerpent = board.HasCardInHand(Card.Cards.TIME_022);
						bool shouldPlayTreant = false;
						
						if (hasSerpent)
						{
								// 有巨蛇，5费才打Combo
								if (board.ManaAvailable >= 5) shouldPlayTreant = true;
						}
						else
						{
								// 无巨蛇，2费打
								if (board.ManaAvailable >= 2) shouldPlayTreant = true;
						}
						
						if (shouldPlayTreant)
						{
								hpModifier = 500; // 降低技能优先级，确保先下树精
								AddLog("手上有树精，推迟技能使用");
						}
				}

				p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_10bp, new Modifier(hpModifier));
#endregion

// 红牌 TOY_644：禁止休眠己方随从
#region
if(board.HasCardInHand(Card.Cards.TOY_644)){
                // 敌方无随从时，避免浪费红牌/误点己方随从
                if (board.MinionEnemy.Count == 0)
                {
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_644, new Modifier(9999));
                        AddLog("红牌 TOY_644：敌方无随从，禁用 +9999");
                }

                foreach (var minion in board.MinionFriend)
                {
                        p.CastSpellsModifiers.AddOrUpdate(minion.Template.Id, new Modifier(9999, Card.Cards.TOY_644));
                        AddLog($"红牌 TOY_644：禁止对己方 {minion.Template.Name} 使用 +9999");
                }
}
#endregion


// 如果 累世巨蛇 TIME_022 的法力值为4 提高其使用优先级
#region
			 if (board.Hand.Exists(x=>x.CurrentCost==4 && x.Template.Id==Card.Cards.TIME_022)
			 )
			 {
					 p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_022, new Modifier(-350));	
					 p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_022, new Modifier(999));	
					 AddLog("累世巨蛇 TIME_022 法力值为4 提高使用优先级");
			 }else{
					 p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_022, new Modifier(-99));	
			 }
#endregion


// 迷时战刃 TIME_444 
#region
			 if (board.HasCardInHand(Card.Cards.TIME_444)
			// 手上没武器
			&&(board.WeaponFriend == null||(board.WeaponFriend != null))
			 )
			 {
					 p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.TIME_444, new Modifier(-5));
					 AddLog("迷时战刃 TIME_444 -5");
					 // 如果手上有迷时战刃 TIME_444,提高使用优先级
					 p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_444, new Modifier(999));	
			 }else{
					 p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.TIME_444, new Modifier(150));
			 }
#endregion


#region 高崖跳水 VAC_926
				if(board.HasCardInHand(Card.Cards.VAC_926)
				// 场上随从数小于等于5
				&& board.MinionFriend.Count <= 5
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_926, new Modifier(-550)); 
					AddLog("高崖跳水 VAC_926 -550");
				}
#endregion

#region 美术家可丽菲罗 TOY_703
	if(board.HasCardInHand(Card.Cards.TOY_703)
	// 场上随从数*8>=敌方英雄血量
	&& board.MinionFriend.Count * 8 >= board.HeroEnemy.CurrentHealth
	){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_703, new Modifier(-350));
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_703, new Modifier(999));
		AddLog("美术家可丽菲罗 TOY_703 -350");
	}else{
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_703, new Modifier(350));
	}
#endregion

#region 玛瑟里顿（未发售版） TOY_647
	if(board.HasCardInHand(Card.Cards.TOY_647)
	){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_647, new Modifier(-350));
		AddLog("玛瑟里顿（未发售版） TOY_647 -350");
	}
#endregion

#region 黏团焦油 TLC_468
	if(board.HasCardInHand(Card.Cards.TLC_468)
	){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_468, new Modifier(-350));
		AddLog("黏团焦油 TLC_468 -350");
	}
#endregion

#region 伊利达雷审判官 CS3_020
	if(board.HasCardOnBoard(Card.Cards.CS3_020)
	){
		p.AttackOrderModifiers.AddOrUpdate(Card.Cards.CS3_020, new Modifier(-50));
	}
#endregion

#region 活体烈焰 FIR_929
  //  提高智慧圣契 BT_025出牌顺序
	if(board.HasCardInHand(Card.Cards.FIR_929)
	){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.FIR_929, new Modifier(-150));
		AddLog("活体烈焰-150");
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.FIR_929, new Modifier(999));
	}
#endregion

#region 基尔加丹 GDB_145
  //  提高智慧圣契 BT_025出牌顺序
	if(board.HasCardInHand(Card.Cards.GDB_145)
	&&board.FriendDeckCount <=3
	){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_145, new Modifier(-350));
		AddLog("基尔加丹-350");
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_145, new Modifier(999));
	}else{
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_145, new Modifier(350));
	}
#endregion

#region 昆虫利爪 TLC_833
       if (board.HasCardInHand(Card.Cards.TLC_833)
			// 手上没武器
			&&(board.WeaponFriend == null||(board.WeaponFriend != null
			&&board.WeaponFriend.Template.Id == Card.Cards.CORE_BAR_330)))//獠牙锥刃 CORE_BAR_330
       {
           p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.TLC_833, new Modifier(-5));
					 AddLog("虫害侵扰 TLC_833 -5");
					 // 如果手上有虫害侵扰 TLC_833,提高使用优先级
					 p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_833, new Modifier(999));	
       }
#endregion

#region 虫害侵扰 TLC_902
       if (board.HasCardInHand(Card.Cards.TLC_902))
       {
           p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_902, new Modifier(-350));
					 AddLog("虫害侵扰 TLC_902 -350");
					 // 如果手上有虫害侵扰 TLC_902,提高使用优先级
					 p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_902, new Modifier(999));	
       }
#endregion

#region TOY_028, 200 }, // 团队之灵
       if (board.HasCardInHand(Card.Cards.TOY_028))
       {
           p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_028, new Modifier(999));
       }
#endregion

#region 格里什掘洞虫 TLC_840
       if (board.HasCardInHand(Card.Cards.TLC_840))
       {
           p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_840, new Modifier(-150));
           AddLog("格里什掘洞虫 TLC_840 -150");
			 }
#endregion

#region 红牌 TOY_644
           p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_644, new Modifier(999));
#endregion

#region 末日使者安布拉 TLC_106
			// 坟场亡语随从大于等于5，使用，都则不用
			if (NumberOfDeadFollowers >= 5
			&& board.HasCardInHand(Card.Cards.TLC_106)
			// 随从数小于等于4
			&& board.MinionFriend.Count <= 4
			)
			{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_106, new Modifier(-350));
					AddLog("末日使者安布拉 TLC_106 -350");
			}
			else
			{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_106, new Modifier(350));
			}
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_106, new Modifier(-150));
#endregion

#region 沉睡的林精 EDR_469
				// 如果手上有沉睡的林精 EDR_469
				if (board.HasCardInHand(Card.Cards.EDR_469))
				{
						int treantModifier = 0;
						bool hasSerpent = board.HasCardInHand(Card.Cards.TIME_022);
						
						if (hasSerpent)
						{
								if (board.ManaAvailable >= 5)
								{
										treantModifier = -200;
										AddLog("5费Combo: 树精 + 巨蛇");
								}
								else
								{
										treantModifier = 500; // 保留给Combo
										AddLog("保留树精等待巨蛇Combo");
								}
						}
						else
						{
								if (board.ManaAvailable >= 2)
								{
										treantModifier = -150;
										AddLog("2费: 树精 + 技能");
								}
								else
								{
										treantModifier = 500; // 不打，亏节奏
										AddLog("法力不足以激活树精，不打");
								}
						}

						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_469, new Modifier(treantModifier));
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_469, new Modifier(3999));
				}
#endregion

#region 夜影花茶 VAC_404 VAC_404t1 VAC_404t2
				// 如果手上有夜影花茶 VAC_404,提高使用优先级
				if (board.HasCardInHand(Card.Cards.VAC_404)
		
				)
				{
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_404, new Modifier(-20));
						AddLog("夜影花茶 VAC_404 -20");
				}
				// 如果手上有夜影花茶 VAC_404t1,提高使用优先级
				if (board.HasCardInHand(Card.Cards.VAC_404t1)
				
				)
				{
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_404t1, new Modifier(-20));
						AddLog("夜影花茶 VAC_404t1 -20");
				}
				// 如果手上有夜影花茶 VAC_404t2,提高使用优先级
				if (board.HasCardInHand(Card.Cards.VAC_404t2)
			)
				{
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_404t2, new Modifier(-20));
						AddLog("夜影花茶 VAC_404t2 -20");
				}
#endregion

#region 梦想策划师杰弗里斯 WORK_027
				// 如果手上有梦想策划师杰弗里斯 WORK_027,提高使用优先级
				if (board.HasCardInHand(Card.Cards.WORK_027))
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WORK_027, new Modifier(-350));
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WORK_027, new Modifier(999));
						AddLog("梦想策划师杰弗里斯 WORK_027 -350");
				}
	p.ChoicesModifiers.AddOrUpdate(SmartBot.Plugins.API.Card.Cards.WORK_027, new Modifier( 100, 3 ));

#endregion

#region 穆克拉 CORE_EX1_014
	// 如果手上有穆克拉 CORE_EX1_014,提高使用优先级
	if (board.HasCardInHand(Card.Cards.CORE_EX1_014))
	{
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_014, new Modifier(-350));
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_014, new Modifier(999));
			AddLog("穆克拉 CORE_EX1_014 -350");
	}
#endregion

#region 球霸野猪人 TOY_642
	// 如果手上有球霸野猪人 TOY_642,提高使用优先级
	if (board.HasCardInHand(Card.Cards.TOY_642))
	{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_642, new Modifier(-350));
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_642, new Modifier(999));
			AddLog("球霸野猪人 TOY_642 -350");
	}
#endregion

#region 梦魇之王萨维斯 EDR_856
	if (board.HasCardInHand(Card.Cards.EDR_856))
				{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_856, new Modifier(-350));
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_856, new Modifier(999));
					AddLog("梦魇之王萨维斯 EDR_856 -350");
				}
#endregion

#region 伊利达雷研习 CORE_YOP_001
	if (board.HasCardInHand(Card.Cards.CORE_YOP_001))
				{
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_YOP_001, new Modifier(-150));
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_YOP_001, new Modifier(999));
					AddLog("伊利达雷研习 CORE_YOP_001 -150");
				}
#endregion

#region 幽灵视觉 CORE_BT_491
	if (board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.CORE_BT_491)
	// 手牌数小于等于7
	&& board.Hand.Count <= 7
	)
				{
					// 如果手上有幽灵视觉 CORE_BT_491,提高使用优先级
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_BT_491, new Modifier(-350));
					AddLog("幽灵视觉 CORE_BT_491 -350");
				}
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_BT_491, new Modifier(999));
#endregion

#region 盲盒 TOY_643
	if (board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TOY_643)
		// 手牌数小于等于7
	&& board.Hand.Count <= 7
	)
				{
					// 如果手上有盲盒 TOY_643,提高使用优先级
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_643, new Modifier(-350));
					AddLog("盲盒 TOY_643 -350");
				}
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_643, new Modifier(999));
#endregion

#region 硬币 GAME_005
     		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(55));//硬币 GAME_005
#endregion

#region 乘务员
// 定义所有乘务员卡牌
var CabinList = new List<Card.Cards> { 
    Card.Cards.GDB_471t,
    Card.Cards.GDB_471t2, 
    Card.Cards.GDB_471t3, 
    Card.Cards.GDB_471t4, 
    Card.Cards.GDB_471t5, 
    Card.Cards.GDB_471t6, 
    Card.Cards.GDB_471t7,
    Card.Cards.GDB_471t8 
};

// 获取当前场上随从数量和可用空间
int currentBoardCount = board.MinionFriend.Count;
int maxBoardSpace = 7 - currentBoardCount;

// 收集标记状态的乘务员组
var cabinGroups = new List<List<Card.Cards>>();
var currentGroup = new List<Card.Cards>();

foreach (var card in board.Hand.Where(card => card.Type == Card.CType.MINION))
{
    if (CabinList.Contains(card.Template.Id))
    {
        if (GetTag(card, Card.GAME_TAG.POWERED_UP) == 1)
        {
            currentGroup.Add(card.Template.Id);
        }
        else if (currentGroup.Count > 0)
        {
            cabinGroups.Add(new List<Card.Cards>(currentGroup));
            currentGroup.Clear();
        }
    }
    else if (currentGroup.Count > 0)
    {
        cabinGroups.Add(new List<Card.Cards>(currentGroup));
        currentGroup.Clear();
    }
}

if (currentGroup.Count > 0)
{
    cabinGroups.Add(currentGroup);
}

// 去除重复组合
var uniqueGroups = cabinGroups
    .GroupBy(group => string.Join(",", group.OrderBy(x => x)))
    .Select(g => g.First().ToList())
    .ToList();

// 记录发现的组合
foreach (var group in uniqueGroups)
{
    AddLog($"找到乘务员组: {string.Join(", ", group)} (数量: {group.Count})");
}

// 按与目标数量7的接近程度排序
var groupedBySizeAndSpace = uniqueGroups
    .Where(group => group.Count >= 2) // 至少2个乘务员
    .Select(group => new {
        Group = group,
        // 计算(当前场上随从数量 + 组合数量)与7的差距
        Distance = Math.Abs(currentBoardCount + group.Count - 7),
        TotalCount = currentBoardCount + group.Count
    })
    .OrderBy(g => g.Distance) // 优先选择与7最接近的组合
    .ThenByDescending(g => g.Group.Count) // 在距离相同的情况下,选择组合数量较多的
    .ToList();

if (groupedBySizeAndSpace.Any())
{
    // 获取最优组合
    var bestOption = groupedBySizeAndSpace.First();
    var bestGroup = bestOption.Group;
    
    // 修改选择最佳乘务员的逻辑
    Card.Cards SelectBestCabin(List<Card.Cards> group)
    {
        // 如果组合会导致超过7个随从，选择最后一个乘务员
        if (bestOption.TotalCount > 7)
        {
            // 确保不会超出索引范围
            return group[group.Count - 1];
        }
        // 如果不会超过7个随从，选择第一个乘务员
        return group[0];
    }

    var bestCabin = SelectBestCabin(bestGroup);

    // 设置使用优先级
    int priorityModifier = -500 * bestGroup.Count;
    p.CastMinionsModifiers.AddOrUpdate(bestCabin, new Modifier(priorityModifier));
    p.PlayOrderModifiers.AddOrUpdate(bestCabin, new Modifier(-150));

    AddLog($"选择乘务员组: {string.Join(", ", bestGroup)}");
    AddLog($"使用: {bestCabin}, 优先级: {priorityModifier}");
    AddLog($"场上随从: {currentBoardCount}, 目标数量: {bestOption.TotalCount}, 与7的差距: {bestOption.Distance}");
}
else
{
    foreach (var cabin in CabinList)
    {
        p.CastMinionsModifiers.AddOrUpdate(cabin, new Modifier(200));
    }
    // AddLog($"无可用乘务员组合 (场上随从: {currentBoardCount}, 剩余空间: {maxBoardSpace})");
}
#endregion

#region 獠牙锥刃 CORE_BAR_330
     		// 提高獠牙锥刃 CORE_BAR_330的攻击优先级
				p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.CORE_BAR_330, new Modifier(-50));
				p.AttackOrderModifiers.AddOrUpdate(Card.Cards.CORE_BAR_330, new Modifier(-50));
				// 手上没武器,提高獠牙锥刃 CORE_BAR_330使用优先级
				if(board.WeaponFriend == null
				&& board.HasCardInHand(Card.Cards.CORE_BAR_330)
				){
					p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.CORE_BAR_330, new Modifier(-5));
					AddLog("獠牙锥刃 CORE_BAR_330 -5");
				}
#endregion

#region 恐怖收割 EDR_840
     		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_840, new Modifier(1999));
				// 场上随从小于等于6,手牌数小于等于9,提高恐怖收割使用优先级
				if(
					board.MinionFriend.Count <= 6
					&&board.Hand.Count <= 9
					&&board.HasCardInHand(Card.Cards.EDR_840)
					){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_840, new Modifier(-350));
					AddLog("恐怖收割 EDR_840 -350");
					}
#endregion

#region 飞龙之眠 EDR_820
     		// 场上随从数小于等于5,提高飞龙之眠 EDR_820使用优先级
					if(
					board.MinionFriend.Count <= 5
					&&board.HasCardInHand(Card.Cards.EDR_820)
					){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_820, new Modifier(-150));
					AddLog("飞龙之眠 EDR_820 -150");
					}
#endregion




#region 猎头 GDB_473
     		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_473, new Modifier(3999));
				// 判断手牌中的最后一张牌是不是乘务员,是的话提高使用优先级
				// 判断手牌中的最后一张牌是不是乘务员
				// var lastCardInHand = board.Hand.LastOrDefault();
				// if (lastCardInHand != null && CabinList.Contains(lastCardInHand.Template.Id))
				// {
				// 		// 如果最后一张牌是乘务员，提高猎头 GDB_473 的使用优先级
				// 		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_473, new Modifier(-20));
				// 		AddLog("手牌最后一张是乘务员，提高猎头 GDB_473 使用优先级 -20");
				// }
				// else
				// {
				// 		// 否则降低猎头 GDB_473 的使用优先级
				// 		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_473, new Modifier(100));
				// }
#endregion

#region 沃罗尼招募官 GDB_471
				// 提高手中沃罗尼招募官 GDB_471使用优先级
				if(board.HasCardInHand(Card.Cards.GDB_471)){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_471, new Modifier(-350)); 
					AddLog("沃罗尼招募官 GDB_471 -350");
				}
				// 如果场上有沃罗尼招募官 GDB_471,手牌数小于等于8,降低其送死优先级
				if(board.HasCardOnBoard(Card.Cards.GDB_471)
				){
				p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.GDB_471, new Modifier(350));
				AddLog("沃罗尼招募官 GDB_471不送死");
				}
#endregion

#region 泼漆彩鳍鱼人 TOY_517
				// 判断场上,手上,坟场里是不是有蒂尔德拉，反抗军头目 GDB_117
				int countGDB_117 = board.MinionFriend.Count(card => card.Template.Id == Card.Cards.GDB_117)
					+ board.Hand.Count(card => card.Template.Id == Card.Cards.GDB_117)
					+ board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.GDB_117);
     		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_517, new Modifier(3999));
				// 提高使用优先级
				if(board.HasCardInHand(Card.Cards.TOY_517)
				&&countGDB_117==0
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_517, new Modifier(-150)); 
					AddLog("泼漆彩鳍鱼人 -150");
				}
#endregion

#region 蒂尔德拉，反抗军头目 GDB_117
				// 如果场上有蒂尔德拉，反抗军头目 GDB_117,手牌数小于等于8,提高其送死优先级
				if(board.HasCardOnBoard(Card.Cards.GDB_117)
				&&board.Hand.Count<=8
				){
				p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.GDB_117, new Modifier(-5)); 
				p.AttackOrderModifiers.AddOrUpdate(Card.Cards.GDB_117, new Modifier(999)); 
				AddLog("蒂尔德拉，反抗军头目送死");
				}
				// 提高使用优先级
				if(board.HasCardInHand(Card.Cards.GDB_117)){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_117, new Modifier(-350)); 
					AddLog("蒂尔德拉，反抗军头目 -350");
				}
#endregion

#region 贪婪的地狱猎犬 EDR_891
// 蒂尔德拉，反抗军头目 GDB_117
// 定义坟场中蒂尔德拉，反抗军头目数量
int countGDB_117Graveyard = board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.GDB_117)
+ board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.TOY_642)//球霸野猪人 TOY_642
+ board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.TLC_468)//黏团焦油 TLC_468
;
// 如果坟场有贪婪的地狱猎犬 EDR_891,场上随从小于等于6,提高贪婪的地狱猎犬 EDR_891使用优先级
if(
		countGDB_117Graveyard >= 1
		&&board.HasCardInHand(Card.Cards.EDR_891)
		){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_891, new Modifier(-350)); 
				AddLog("贪婪的地狱猎犬 -350");
				}else{	
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_891, new Modifier(999));
				}
				// 如果场上随从小于等于5,主动送贪婪的地狱猎犬 EDR_891
				// if(board.MinionFriend.Count<=5
				// &&board.HasCardOnBoard(Card.Cards.EDR_891)
				// ){
				// p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.EDR_891, new Modifier(-5));
				// p.AttackOrderModifiers.AddOrUpdate(Card.Cards.EDR_891, new Modifier(999));
				// AddLog("贪婪的地狱猎犬送死");
				// }
				AddLog("球霸野猪人坟场数量: "+countGDB_117Graveyard);
#endregion

#region MIS_102	退货政策
        var tuihuo = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.MIS_102);
// 蒂尔德拉，反抗军头目 GDB_117
// 定义坟场中蒂尔德拉，反抗军头目数量
int ReturnPolicy = board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.EDR_891)//贪婪的地狱猎犬 EDR_891
;
AddLog("坟场贪婪的地狱猎犬数量: " + ReturnPolicy);
// 如果坟场有贪婪的地狱猎犬 EDR_891,场上随从小于等于6,提高贪婪的地狱猎犬 EDR_891使用优先级
if(
		ReturnPolicy >= 1
		&&tuihuo != null
		&&board.MinionFriend.Count <= 5
		){
        p.ComboModifier = new ComboSet(tuihuo.Id);
				AddLog("退货政策出");
				}else{	
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_102, new Modifier(350));
				}
#endregion

#region 残暴的魔蝠 EDR_892
;
// 如果坟场有贪婪的地狱猎犬,场上随从小于等于6,提高贪婪的地狱猎犬 EDR_891使用优先级
if(
		ReturnPolicy >= 1
		&&board.HasCardInHand(Card.Cards.EDR_892)
		&&board.MinionFriend.Count <= 5
		){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_892, new Modifier(-350));
				AddLog("残暴的魔蝠 EDR_892 -350");
				}else{	
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_892, new Modifier(350));
				}
#endregion

#region 展馆茶壶 CORE_WON_142
 // 记录当前随从种类数量
    // AddLog($"当前随从种类数量: {allMinionCount}");
if(board.HasCardInHand(Card.Cards.CORE_WON_142))
{
   
    
    // 根据随从种类调整优先级
    if (allMinionCount >= 2)
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_142, new Modifier(-350));
        AddLog("展馆茶壶 -350: 随从种类大于3，提高优先级");
    }
    else
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_142, new Modifier(350));
        AddLog("展馆茶壶 350: 随从种类少于等于3，降低优先级");
    }
}
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_WON_142, new Modifier(550));
#endregion

#region 讲故事的始祖龟 TLC_254
if(board.HasCardInHand(Card.Cards.TLC_254))
{
   
    
    // 根据随从种类调整优先级
    if (allMinionCount >= 2)
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_254, new Modifier(-350));
        AddLog("讲故事的始祖龟 -350: 随从种类大于3，提高优先级");
    }
    else
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_254, new Modifier(350));
        AddLog("讲故事的始祖龟 350: 随从种类少于等于3，降低优先级");
    }
}
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_254, new Modifier(350));
#endregion


#region VAC_414	炽热火炭
      // 敌方小于3血的随从越多,炽热火炭的优先级越高
				if(board.HasCardInHand(Card.Cards.VAC_414)
				&&enemyMinionLessThreeHealth >= 3
				){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_414, new Modifier(-5*enemyMinionLessThreeHealth));
				AddLog("炽热火炭"+(-5*enemyMinionLessThreeHealth));
				}else{
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_414, new Modifier(150));
				}
#endregion

#region 极限追逐者阿兰娜 VAC_501 与 针灸 VAC_419
// 检查场上是否有极限追逐者阿兰娜
if (board.HasCardOnBoard(Card.Cards.VAC_501))
{
    // 提高针灸 VAC_419 的优先级
    if (board.HasCardInHand(Card.Cards.VAC_419))
    {
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_419, new Modifier(-150));
        AddLog("场上有极限追逐者阿兰娜，提高针灸使用优先级 -150");
    }

    // 检查我方场上是否有增加法术伤害的随从
    if (board.MinionFriend.Any(minion => minion.SpellPower > 0))
    {
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_419, new Modifier(-200));
        AddLog("我方场上有法术伤害随从，进一步提高针灸优先级 -200");
    }
}
    if(board.HasCardInHand(Card.Cards.VAC_501)
		// 如果敌方没有随从,降低使用优先级
		&&board.MinionEnemy.Count==0
    )
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_501, new Modifier(350));
        AddLog("极限追逐者阿兰娜 350");
    }else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_501, new Modifier(300));
		}
#endregion

#region 针灸 VAC_419
		if(board.HasCardInHand(Card.Cards.VAC_419)
		&&hasSelfDamageCard==false
		){
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_419, new Modifier(150));
			AddLog("针灸 VAC_419 150");
		}
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_419, new Modifier(999)); 
#endregion

#region 袋底藏沙 WW_403
         if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.WW_403)

		){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_403, new Modifier(-20)); 
        AddLog("袋底藏沙 -20");
        }else{
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_403, new Modifier(130)); 					
				}
#endregion

#region 战斗邪犬 CORE_BT_351
         if(board.HasCardInHand(Card.Cards.CORE_BT_351)
		){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_BT_351, new Modifier(-150)); 
        AddLog("战斗邪犬 -150");
        }
				// 场上有战斗邪犬,不送
				if(board.HasCardOnBoard(Card.Cards.CORE_BT_351)){
					// 不送
					p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.CORE_BT_351, new Modifier(150));
				}
#endregion

#region WW_331	奇迹推销员
         if(board.HasCardInHand(Card.Cards.WW_331)
		){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_331, new Modifier(150)); 
        AddLog("奇迹推销员 150");
        }
#endregion

 #region  WW_391 淘金客
        if(board.HasCardInHand(Card.Cards.WW_391)){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_391, new Modifier(-99)); 
        AddLog("淘金客-99");
        }
#endregion

#region CORE_EX1_586 海巨人
         if(board.Hand.Exists(x=>x.CurrentCost<=4 && x.Template.Id==Card.Cards.CORE_EX1_586)
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_586, new Modifier(-99));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_586, new Modifier(999));
        AddLog("海巨人-99");
        }
#endregion

#region 音响工程师普兹克 ETC_425
        // 提高优先级
        if(board.HasCardInHand(Card.Cards.ETC_425)
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_425, new Modifier(-99));
          AddLog("音响工程师普兹克-99");
        }
#endregion

#region 戈贡佐姆 VAC_955
        // 提高优先级
        if(board.HasCardInHand(Card.Cards.VAC_955)
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_955, new Modifier(-150));
          AddLog("戈贡佐姆-150");
        }

#endregion

#region TTN_860	无人机拆解器
        if(board.HasCardInHand(Card.Cards.TTN_860)){
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_860, new Modifier(150)); 
                AddLog("无人机拆解器 150");
        }
#endregion

#region WW_331t	蛇油
		// 前期不用蛇油 
		// 如果牌库为空,不交易蛇油
		if(board.HasCardInHand(Card.Cards.WW_331t)
		&&board.FriendDeckCount<=2
		){
		p.TradeModifiers.AddOrUpdate(Card.Cards.WW_331t, new Modifier(999));
		AddLog("牌库为空不交易蛇油");
		}
#endregion

#region VAC_955t 美味奶酪
			foreach(var card in board.Hand)
      {
        if(card.Template.Id == Card.Cards.VAC_955t)
        {
						var tag = quantityOverflowLava(card);
						// 如果当前手牌数小于等于3,且最大回合数大于等于8,提高优先级
						if (board.MinionFriend.Count<=4
						&& board.HasCardInHand(Card.Cards.VAC_955t))
						{
								p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_955t, new Modifier(-50*(10-HandCount)));
								AddLog("美味奶酪"+(-50*(10-HandCount)));
						}else{
								p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_955t, new Modifier(999));
						}
					}
			}
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_955t, new Modifier(1999));
#endregion

#region CORE_BT_187	凯恩·日怒
    // 如果我方攻击力大于等于敌方英雄生命值,且对方有墙,提高凯恩优先级
    if(board.HasCardInHand(Card.Cards.CORE_BT_187)
    &&myAttack+3>=BoardHelper.GetEnemyHealthAndArmor(board)
    &&board.MinionEnemy.Any(minion => minion.IsTaunt)
    ){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_BT_187, new Modifier(-999));
    // 走脸
    p.GlobalAggroModifier = 999;
    AddLog("凯恩·日怒 斩杀");
    }else{
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_BT_187, new Modifier(350));		
		}
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_BT_187, new Modifier(-999));
#endregion

#region 疾速矿锄 DEEP_014
        // 一费有一费随从,降低疾速矿锄的攻击优先级
        if(board.HasCardInHand(Card.Cards.DEEP_014)
        &&board.MaxMana==1
        &&board.WeaponFriend == null 
        &&board.Hand.Exists(x=>x.CurrentCost==1 && x.Template.Type == Card.CType.MINION)
        ){
        p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.DEEP_014, new Modifier(150));
        AddLog("一费有一费随从,降低疾速矿锄的使用优先级");
        }else{
        p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.DEEP_014, new Modifier(-25));
        }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DEEP_014, new Modifier(999));
      
        if(board.WeaponFriend != null 
        && board.WeaponFriend.Template.Id == Card.Cards.DEEP_014
        &&board.MinionEnemy.Count==0
        &&board.MaxMana==3
        &&(
        !board.HasCardOnBoard(Card.Cards.VAC_929)
        &&board.HasCardInHand(Card.Cards.VAC_929))
        ){
         p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.DEEP_014, new Modifier(350));
        AddLog("武器滞后");
        }
        // 如果手里有船载火炮，降低疾速矿锄的优先级
        if(board.HasCardInHand(Card.Cards.GVG_075)
        &&board.HasCardInHand(Card.Cards.DEEP_014)
        ){
        p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.DEEP_014, new Modifier(150));
        AddLog("手里有船载火炮，降低疾速矿锄的优先级");
        }
#endregion

#region 伞降咒符 VAC_925
        var parachuteSpell = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.VAC_925);
        // 如果一费有宝石经销商,降低伞降咒符的使用优先级
        if(board.HasCardInHand(Card.Cards.VAC_925)
        &&board.MaxMana==1
        // 手上有一费随从
        &&board.Hand.Exists(x=>x.CurrentCost==1 && x.Type == Card.CType.MINION)
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_925, new Modifier(300));
        AddLog("有1费随从,降低伞降咒符的使用优先级");
        }else if(board.HasCardInHand(Card.Cards.VAC_925)
        &&board.HasCardInHand(Card.Cards.VAC_938)//粗暴的猢狲 VAC_938
        &&board.HasCardInHand(Card.Cards.GAME_005)//GAME_005
        &&board.MaxMana==1
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_925, new Modifier(300));
        AddLog("一费有猢狲有硬币,降低伞降咒符的使用优先级");
        }else if(parachuteSpell!=null
        &&board.MaxMana==2
        &&friendCount<5
        ){
        p.ComboModifier = new ComboSet(parachuteSpell.Id); 
        // p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_925, new Modifier(-999));
        AddLog("伞降咒符出");
        }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_925, new Modifier(-350));
        }
        // 如果有伞降符咒,降低心灵按摩师 VAC_512 优先级
        if(board.HasCardInHand(Card.Cards.VAC_925)
        &&board.HasCardInHand(Card.Cards.VAC_512)
        &&board.MaxMana>1
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_512, new Modifier(150));
        AddLog("有伞降符咒,降低心灵按摩师优先级");
        }
        // 如果场上随从数量大于等于5,降低伞降咒符的使用优先级
        if(board.HasCardInHand(Card.Cards.VAC_925)
        &&friendCount>5
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_925, new Modifier(999));
        AddLog("场上随从数量大于等于6,降低伞降咒符的使用优先级");
        }
#endregion

#region 滑矛布袋手偶 MIS_710
if(board.HasCardInHand(Card.Cards.MIS_710)){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.MIS_710, new Modifier(-999));
		AddLog("滑矛布袋手偶-999");
}
		// 如果场上有滑矛布袋手偶 ,降低其攻击顺序
		if(board.HasCardOnBoard(Card.Cards.MIS_710)
		){
		p.AttackOrderModifiers.AddOrUpdate(Card.Cards.MIS_710, new Modifier(-50));
		p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.MIS_710, new Modifier(350));
		AddLog("场上有滑矛布袋手偶 ,降低其攻击顺序,不送");
		}
#endregion

#region 团队之灵 TOY_028
 // 如果场上有滑矛布袋手偶 MIS_710,提高团队之灵 TOY_028 优先级
    if(board.HasCardOnBoard(Card.Cards.MIS_710)
    &&board.HasCardInHand(Card.Cards.TOY_028)
    ){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_028, new Modifier(-99));
    AddLog("团队之灵 -99");
    }
#endregion

#region 飞行员帕奇斯 VAC_933
    if(board.HasCardInHand(Card.Cards.VAC_933)){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_933, new Modifier(-999)); 
			AddLog("飞行员帕奇斯 -999");
			// 如果手上有矿锄,降低矿锄攻击优先值
			if(board.WeaponFriend != null 
			&& board.WeaponFriend.Template.Id == Card.Cards.DEEP_014
			){
			p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.DEEP_014, new Modifier(350));
			AddLog("手上有飞行员,矿锄攻击滞后");
			}
    }
#endregion

#region //TOY_330t11 奇利亚斯豪华版3000型
    if (board.HasCardInHand(Card.Cards.TOY_330t11) )
    {
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(-200));
    AddLog("奇利亚斯豪华版3000型"+(-200));
    }
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(999));
#endregion

#region TOY_518	宝藏经销商
    if(board.HasCardInHand(Card.Cards.TOY_518)){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(-150)); 
    AddLog("宝藏经销商 -150");
    }
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(4999)); 
    p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(-50));
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
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_938, new Modifier(350));
    }
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_938, new Modifier(999)); 
#endregion

#region 南海船长 CORE_NEW1_027
     if (canAttackPirates >= 1 
    &&board.HasCardInHand(Card.Cards.CORE_NEW1_027)
    )
    {
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_027, new Modifier(canAttackPirates*-40));
    AddLog("南海船长" + (canAttackPirates*-40));
    }
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_027, new Modifier(999));
#endregion

#region 突破邪火 JAM_017
// 如果被标记,提高优先级
    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.JAM_017)
    ){
    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.JAM_017, new Modifier(-150));   
    AddLog("突破邪火 -150");
    }
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.JAM_017, new Modifier(999));   
#endregion

#region 飞翼滑翔 VAC_928
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_928, new Modifier(1999));   
    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.VAC_928)
    ){
    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_928, new Modifier(-350));   
    AddLog("飞翼滑翔 1 -350");
    }
    // 敌方手牌数大于等于7,提高优先级
    if(board.HasCardInHand(Card.Cards.VAC_928)
    &&board.EnemyCardCount>=7
    ){
    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_928, new Modifier(-350));   
    AddLog("飞翼滑翔 2 -350");
    }
     if(board.HasCardInHand(Card.Cards.VAC_928)
    &&board.Hand.Count<=6
    ){
    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_928, new Modifier(-350));   
    AddLog("飞翼滑翔 3 -350");
    }
#endregion

#region  通信组乘务员 GDB_471t5
        if(board.HasCardOnBoard(Card.Cards.GDB_471t5)){
					// 遍历手牌中的法术,降低对通信组乘务员的使用优先级
					foreach (var item in board.Hand)
					{
						if(item.Type==Card.CType.SPELL){
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_471t5, new Modifier(999));
							AddLog("不对通信组乘务员使用"+item.Template.NameCN);
						}
					}
        }
#endregion

#region VAC_927	狂飙邪魔
    p.AttackOrderModifiers.AddOrUpdate(Card.Cards.VAC_927, new Modifier(-5));  
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_927, new Modifier(4999));
    if (board.HasCardInHand(Card.Cards.VAC_927) 
    && canAttackPirates >= 1
    )
    {
        // 如果手牌上有狂飙邪魔，且场上海盗数量大于2，则打出这张牌优先级为-50
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_927, new Modifier(canAttackPirates*-50));
    AddLog("狂飙邪魔" + (canAttackPirates*-50));
    }
    else
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_927, new Modifier(150));
    }
		// 如果场上有狂飙邪魔,降低英雄攻击顺序
		if(board.HasCardOnBoard(Card.Cards.VAC_927)
		){
		p.WeaponsAttackModifiers.AddOrUpdate(board.HeroFriend.Id, new Modifier(350));
		p.AttackOrderModifiers.AddOrUpdate(Card.Cards.VAC_927, new Modifier(-30));
		AddLog("场上有狂飙邪魔,降低英雄攻击顺序,降低狂飙邪魔攻击顺序");
		}
		
#endregion

#region 心灵按摩师 VAC_512     
	p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_512, new Modifier(-50)); 
    if(board.HasCardInHand(Card.Cards.VAC_512)
    &&board.HasCardInHand(Card.Cards.TOY_518)//TOY_518	宝藏经销商
    ){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_512, new Modifier(350));
    AddLog("心灵按摩师350");
    }else{
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_512, new Modifier(-99));
    }
    // 如果场上有极限追逐者阿兰娜 VAC_501,主动送心灵按摩师打伤害
    if(board.HasCardOnBoard(Card.Cards.VAC_501)
    &&board.HasCardOnBoard(Card.Cards.VAC_512)
    ){
    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_512, new Modifier(-100)); 
    AddLog("有阿兰娜送心灵按摩师打伤害");
    }
		// 如果敌方场上有雷霆之主,不用心灵按摩师
		if(board.HasCardOnBoard(Card.Cards.VAC_512)
		&&board.HasCardOnBoard(Card.Cards.TTN_800)//雷霆之神高戈奈斯 TTN_800
		){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_512, new Modifier(350));
		AddLog("有雷霆之神高戈奈斯,不用心灵按摩师");
		}
#endregion

#region 椰子火炮手 VAC_532
        p.AttackOrderModifiers.AddOrUpdate(Card.Cards.VAC_532, new Modifier(-50));
				if(board.HasCardOnBoard(Card.Cards.VAC_532)){
				// 不送椰子火炮手
				p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_532, new Modifier(150));
				}
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_532, new Modifier(999));
#endregion

#region 恶魔变形 CORE_BT_429
		// 如果场上有伴唱机,提高恶魔变形 CORE_BT_429使用优先级
		if(board.HasCardOnBoard(Card.Cards.TOY_528)
		&&board.HasCardInHand(Card.Cards.CORE_BT_429)
		){
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_BT_429, new Modifier(-350));
		AddLog("伴唱机 恶魔变形");
		}else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_BT_429, new Modifier(-99));
    }
    if(board.Ability.Template.Id == Card.Cards.CORE_BT_429  
    ){
    p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.BT_429p, new Modifier(-350,board.HeroEnemy.Id));
    p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.BT_429p, new Modifier(350,board.HeroFriend.Id));
    AddLog("恶魔变形打脸");
    }
#endregion

// 确保已经在适当的函数内加入以下逻辑

#region 混乱打击 CORE_BT_035
  	p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_BT_035, new Modifier(999));
// 提高优先级
if (board.HasCardInHand(Card.Cards.CORE_BT_035))
{
    // 检查当前回合是否适合打出混乱打击
    if (board.HeroFriend.CurrentAtk == 0 || board.HeroFriend.CurrentHealth <= 10)
    {
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_BT_035, new Modifier(-200));
        AddLog("混乱打击 - 提高优先级，在当前回合打出");
    }
    else
    {
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_BT_035, new Modifier(100));
        AddLog("混乱打击 - 优先级降低，保留到适合的回合");
    }
}
#endregion

#region VAC_929	惊险悬崖
    var sheerCliffs = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.VAC_929);
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_929, new Modifier(5999));
    if (board.HasCardInHand(Card.Cards.VAC_929) 
        && sheerCliffs != null
        && friendCount < 7
				&&(enemyAttack<=myAttack||enemyAttack<=8)
        && (!(canAttackPirates >= 3 && (board.HasCardInHand(Card.Cards.VAC_938) || board.HasCardInHand(Card.Cards.CORE_NEW1_027)||board.HasCardInHand(Card.Cards.GVG_075)))))//船载火炮 GVG_075 
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_929, new Modifier(-150));
        AddLog("惊险悬崖出");
    }
    // 如果场上有惊险悬崖,且随从数小于等于5,提高优先级
    if(board.HasCardOnBoard(Card.Cards.VAC_929)
    &&friendCount<=5
    ){
    p.LocationsModifiers.AddOrUpdate(Card.Cards.VAC_929, new Modifier(-99));
    AddLog("惊险悬崖优先使用");
    }
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_929, new Modifier(3999));
#endregion

#region 影随员工 WORK_032
    // 场上随从数大于等于6,或者没有亮,降低使用优先级
    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) != 1 && card.Template.Id == Card.Cards.WORK_032)
    &&friendCount<6
    ){
    //如果手上有针灸,提高针灸使用优先级
    if(board.HasCardInHand(Card.Cards.VAC_419)
    ){
    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_419, new Modifier(-150));
    AddLog("针灸-150");
    }
		}
     if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.WORK_032)
    &&friendCount<6
    ){
    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WORK_032, new Modifier(-350));
    AddLog("影随员工 -350");
    }
#endregion

#region 桑拿常客 VAC_418
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_418, new Modifier(-50));
// 遍历手牌，如果桑拿常客，费用越低使用优先级越高，5费150,4费130,0费-150
foreach (var card in board.Hand)
{
    if (card.Template.Id == Card.Cards.VAC_418)
    {
        int priority = card.Template.Cost switch
        {
            5 => 200,
            4 => 150,
						3 => -10,
						2 => -20,
						1 => -30,
            0 => -40,
            _ => 0
        };
        p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-priority));
        AddLog($"桑拿常客 {card.Template.Id} {priority}");
    }
}
#endregion

#region 狱火订书器 WORK_016
    if (board.HasCardInHand(Card.Cards.WORK_016)
   			&& board.WeaponFriend != null 
        && board.WeaponFriend.Template.Id != Card.Cards.WORK_016
    )
    {
        p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.WORK_016, new Modifier(-350));
        AddLog("狱火订书器1 -350");
    }
    if (board.HasCardInHand(Card.Cards.WORK_016)
   			&& board.WeaponFriend == null 
    )
    {
        p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.WORK_016, new Modifier(-350));
        AddLog("狱火订书器2 -350");
    }
		 if ( board.WeaponFriend != null 
        && board.WeaponFriend.Template.Id == Card.Cards.WORK_016
    )
    {
      	p.AttackOrderModifiers.AddOrUpdate(Card.Cards.WORK_016, new Modifier(3999));
    }
#endregion

/*
狂野逻辑
*/
#region AV_204 裂魔者库尔特鲁斯
        if (board.HasCardInHand(Card.Cards.AV_204))
        {
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.AV_204, new Modifier(150));
        AddLog("裂魔者库尔特鲁斯优先级 150");
        }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.AV_204p, new Modifier(999)); //AV_204p	陨烬之怒
        p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.AV_204p, new Modifier(50)); 
#endregion

#region LOOT_541t	国王的赎金
        if(board.HasCardInHand(Card.Cards.LOOT_541t)
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.LOOT_541t, new Modifier(-200));//LOOT_541t	国王的赎金
        }
#endregion

#region BT_753	法力燃烧
    // 下一轮可斩杀,提高使用优先级
        if(board.HasCardInHand(Card.Cards.BT_753)
        &&BoardHelper.HasPotentialLethalNextTurn(board)
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.BT_753, new Modifier(-150));
        AddLog("下一轮可斩杀,法力燃烧 -150");
        }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.BT_753, new Modifier(200));
        }
#endregion

#region REV_509 放大战刃
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.REV_509, new Modifier(1999));
        if (board.HasCardInHand(Card.Cards.REV_509) && board.Hand.Count <= 3
        &&board.WeaponFriend != null )
        {
            // 如果手上有放大战刃且手牌数量小于等于3，优先级设置为-50
            p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.REV_509, new Modifier(-20));
            AddLog("放大战刃 -20");
        }
        // 如果手上有疾速矿锄,且当前手牌数为3,降低使用优先级
        if(board.HasCardInHand(Card.Cards.REV_509)
        &&board.Hand.Count>=3
        &&board.WeaponFriend != null 
        &&board.WeaponFriend.Template.Id == Card.Cards.DEEP_014
        ){
            p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.REV_509, new Modifier(350));
            AddLog("手牌数为3,降低放大战刃的使用优先级");
        }
        // 如果手上有刀,降低放大战刃的优先级
        if(board.HasCardInHand(Card.Cards.REV_509)
        // 有武器
        &&board.WeaponFriend != null
        // 武器不是放大战刃
        &&board.WeaponFriend.Template.Id != Card.Cards.REV_509
        // 且手牌数大于3
        &&board.Hand.Count>=3
        ){
        p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.REV_509, new Modifier(150));
        AddLog("手里有刀,降低放大战刃的优先级");
        }
        // 如果手牌数小于3,提高放大战刃的优先级
        if(board.HasCardInHand(Card.Cards.REV_509)
        &&board.Hand.Count<3
        ){
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.REV_509, new Modifier(-99));
        AddLog("手牌数小于3,提高放大战刃攻击优先级");
        }
#endregion


#region 南海船工 CORE_CS2_146 
        if(board.HasCardInHand(Card.Cards.CORE_CS2_146)
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_CS2_146, new Modifier(300)); 
        Bot.Log("南海船工 300");
        }
#endregion

#region 船载火炮 GVG_075 
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(3999)); 
        p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(150)); 
        if(shipborneArtillery!=null
        &&NumberHandPirates>=1
        &&board.MaxMana ==1
        &&enemyAttack-myAttack<3
        ){
            p.ComboModifier = new ComboSet(shipborneArtillery.Id);
                AddLog("1费船载火炮出");
        }
        if(board.MaxMana<=2
        &&board.HasCardInHand(Card.Cards.GVG_075)
        &&NumberHandPirates>=1
        &&enemyAttack-myAttack<3
        ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(-250));
            AddLog("船载火炮出-250");
        }
        // 有伞降咒符 VAC_925或海盗
        if(board.HasCardInHand(Card.Cards.GVG_075)
        &&(NumberHandPirates>=1||board.HasCardInHand(Card.Cards.VAC_925))
        &&board.MaxMana >=3
        &&enemyAttack-myAttack<3
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(-250)); 
        AddLog("船载火炮出-250");
        }
        // 如果场上已经有船载火炮,手里有海盗,降低手里船载火炮优先级
        if(board.HasCardOnBoard(Card.Cards.GVG_075)
        &&NumberHandPirates>=1
        &&board.HasCardInHand(Card.Cards.GVG_075)
        &&enemyAttack-myAttack>3
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(150));
        AddLog("如果场上已经有船载火炮,手里有海盗,降低手里船载火炮优先级");
        }
#endregion

#region AV_661	征战平原
        if(board.HasCardInHand(Card.Cards.AV_661)
        &&(canAttackCount>=2)
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.AV_661, new Modifier(canAttackCount * -33));   
        AddLog("征战平原"+(canAttackCount * -33));
        }
        // 如果手上武器为放大战刃,提高征战平原优先级
        if(board.HasCardInHand(Card.Cards.AV_661)
        &&board.WeaponFriend != null
        &&board.WeaponFriend.Template.Id == Card.Cards.REV_509
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.AV_661, new Modifier(-150));
        AddLog("手上武器为放大战刃,提高征战平原优先级");
        }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.AV_661, new Modifier(150));   
        }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.AV_661, new Modifier(3999));
#endregion


#region TOY_505	玩具船
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_505, new Modifier(3999)); 
        if(toyBoat!=null
        &&NumberHandPirates>=1
        &&board.MaxMana ==1
        &&enemyAttack<3
        ){
            p.ComboModifier = new ComboSet(toyBoat.Id);
        }
        if(board.HasCardInHand(Card.Cards.TOY_505)
        &&NumberHandPirates>=1
        &&board.MaxMana >1
        ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_505, new Modifier(-250)); 
        }
        if(board.HasCardOnBoard(Card.Cards.TOY_505)
        ){
            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TOY_505, new Modifier(150)); 
        }
#endregion

#region NX2_050	错误产物
// 提高优先级
        if(board.HasCardInHand(Card.Cards.NX2_050)
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.NX2_050, new Modifier(-150));   
        AddLog("错误产物 -150");
        }
        p.AttackOrderModifiers.AddOrUpdate(Card.Cards.NX2_050, new Modifier(100));   
#endregion

#region 海盗帕奇斯 CFM_637 
        if(board.HasCardInHand(Card.Cards.CFM_637)
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CFM_637, new Modifier(200)); 
        AddLog("海盗帕奇斯 200");
        }
#endregion

#region DRG_056 空降歹徒
        // 如果手上没有除空降歹徒之外的其他海盗,提高空降歹徒使用优先级
        if(board.HasCardInHand(Card.Cards.DRG_056)
        &&removeAirborneBandits==0
        ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(150)); 
            AddLog("空降歹徒 150");
        }else{
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(350)); 
        }
#endregion

#region UNG_807 葛拉卡爬行蟹
    //   如果敌方没有海盗,降低优先级
        if(board.HasCardInHand(Card.Cards.UNG_807)
        &&enemyPirates==0
        // 且为牧师 瞎子 贼
        &&(board.EnemyClass == Card.CClass.WARRIOR|| board.EnemyClass == Card.CClass.PRIEST|| board.EnemyClass == Card.CClass.ROGUE)
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.UNG_807, new Modifier(350));
        AddLog("葛拉卡爬行蟹 350");
        }
#endregion



#region GDB_474	折跃驱动器
        if(board.HasCardInHand(Card.Cards.GDB_474)
        &&board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.GDB_474)
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_474, new Modifier(-150));
        AddLog("折跃驱动器-150");
        }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_474, new Modifier(-20));
        }
#endregion

#region 灼燃之心 DEEP_011
        if(board.HasCardInHand(Card.Cards.DEEP_011)
         ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.DEEP_011, new Modifier(150));
        AddLog("灼燃之心 150");
					// 遍历敌方随从,降低对血量少于4的随从的g优先级
					foreach (var item in board.MinionEnemy)
					{
						if(item.CurrentHealth<=2
						){
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.DEEP_011, new Modifier(350,item.Template.Id));
							AddLog("降低灼燃之心对"+item.Template.NameCN+"的优先级");
						}
					}
					// 遍历我方随从,降低对其使用优先级
					foreach (var item in board.MinionFriend)
					{
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.DEEP_011, new Modifier(350,item.Template.Id));
						AddLog("降低灼燃之心对"+item.Template.NameCN+"的优先级");
					}
				 }
#endregion

#region 伴唱机 TOY_528
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_528, new Modifier(1999));
				// 如果场上有伴唱机,降低伴唱机优先级
				if(board.HasCardOnBoard(Card.Cards.TOY_528)
				&&board.HasCardInHand(Card.Cards.TOY_528)
				){
         p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_528, new Modifier(350));
        AddLog("伴唱机 350");
        }else if(board.HasCardOnBoard(Card.Cards.TOY_528)
				// 如果场上有伴唱机,我方英雄技能为恶魔变形,提高对敌方英雄的使用优先级
				&&board.Ability.Template.Id == Card.Cards.BT_429p//BT_429p
				){
				 p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.BT_429p, new Modifier(-350,board.HeroEnemy.Id));
				AddLog("伴唱机 恶魔变形");
				}else if(board.HasCardInHand(Card.Cards.TOY_528)
				&&board.Ability.Template.Id == Card.Cards.BT_429p//BT_429p
				){
				 p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_528, new Modifier(-150));
				AddLog("有恶魔变形出伴唱机");
				}else if(board.HasCardInHand(Card.Cards.TOY_528)
				){
         p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_528, new Modifier(-150));
        AddLog("伴唱机 -150");
        }
#endregion

#region 焦渴的亡命徒 WW_407
				// 如果没标记
				if(
					board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) != 1 && card.Template.Id == Card.Cards.WW_407)
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_407, new Modifier(350));
					AddLog("焦渴的亡命徒 350");
				}
#endregion

#region 势如破竹 TTN_841
				// 如果手上有势如破竹,降低优先级
				if(board.HasCardInHand(Card.Cards.TTN_841)
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TTN_841, new Modifier(150));
					AddLog("势如破竹 150");
				}
#endregion

#region 低沉摇摆 ETC_413
				if(board.HasCardInHand(Card.Cards.ETC_413)
				){
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_413, new Modifier(150));
					AddLog("低沉摇摆顺序 150");
				}
#endregion


#region 卡牌施放逻辑封装与执行

// 方法封装
// 检查手牌中是否有指定的卡牌
bool HasCardInHand(Card.Cards cardId) => board.Hand.Any(card => card.Template.Id == cardId);

// 获取手牌中指定的卡牌，如果不存在则返回 null
Card GetCardInHand(Card.Cards cardId) => board.Hand.FirstOrDefault(card => card.Template.Id == cardId);

// 判断一个法术牌是否可以在当前可用法力值下使用
bool IsSpellPlayable(Card card, int availableMana) =>
    card.Type == Card.CType.SPELL && card.CurrentCost <= availableMana;

// 获取所有可以在保留一定法力值后使用的法术牌，并按法力值从低到高排序
IEnumerable<Card> GetPlayableSpells(int reservedMana) =>
    board.Hand.Where(card => IsSpellPlayable(card, board.ManaAvailable - reservedMana))
              .OrderBy(card => card.CurrentCost);

// 设置组合优先级
void SetComboPriority(Card minion, IEnumerable<Card> spells)
{
    foreach (var spell in spells)
    {
        p.ComboModifier = new ComboSet((int)minion.Id, (int)spell.Id);
        AddLog($"设置组合优先级: {minion.Template.NameCN} 与法术({spell.Template.NameCN})");
    }
}

// 处理以太先知逻辑
void HandleEtherOracleLogic(Card.Cards etherOracleCard,string etherOracleName)
{
    var etherOracle = GetCardInHand(etherOracleCard);
    if (board.Hand.Count >= 9
		&&etherOracle != null)
    {
        AddLog($"手牌过多，暂不打出{etherOracleName}。");
        return;
    }

    if (etherOracle != null)
    {
        if (board.MinionFriend.Count <= 6 
 					&&!board.MinionEnemy.Exists(x => x.Template.Id == Card.Cards.WORK_040)// 敌方场上没有笨拙的杂役
					) 
        {
            // 筛选法术并排除部分条件
            var playableSpells = GetPlayableSpells(etherOracle.CurrentCost)
								.Where(item => item.Template.Id != Card.Cards.VAC_955t ||
                    board.MinionFriend.Count <= 4) // 针对美味奶酪的限制
								.Where(item => item.Template.Id != Card.Cards.DEEP_011)// 不为 灼燃之心 DEEP_011
                .ToList();

            if (playableSpells.Any())
            {
                SetComboPriority(etherOracle, playableSpells);
            }
            else
            {
                p.CastMinionsModifiers.AddOrUpdate(etherOracleCard, new Modifier(999));
                AddLog($"未找到可用法术，降低{etherOracleName}优先级。");
            }
        }
        else
        {
            p.CastMinionsModifiers.AddOrUpdate(etherOracleCard, new Modifier(999));
            AddLog($"条件不满足，未打出{etherOracleName}。");
        }
    }

    // 确保场上以太先知已施放，并提高法术优先级
    var etherOracleOnBoard = board.MinionFriend.FirstOrDefault(x => x.Template.Id == etherOracleCard && x.HasSpellburst);
    if (etherOracleOnBoard != null)
    {
        var playableSpells = GetPlayableSpells(0)
								// 不为 灼燃之心 DEEP_011
						.Where(item => item.Template.Id != Card.Cards.DEEP_011)
            .ToList();

        if (playableSpells.Any())
        {
            SetComboPriority(etherOracleOnBoard, playableSpells);

            foreach (var spell in playableSpells)
            {
                p.CastSpellsModifiers.AddOrUpdate(spell.Template.Id, new Modifier(-500));
                AddLog($"提高法术({spell.Template.NameCN})的优先级。");
            }
        }
    }
}

// 执行卡牌逻辑
void ExecuteCardLogic()
{
    HandleEtherOracleLogic(Card.Cards.GDB_310,"虚灵神谕者");// 虚灵神谕者 GDB_310
}
ExecuteCardLogic();

#endregion

#region 重新思考
// 定义需要排除的卡牌集合

var excludedCards = new HashSet<string>
{
    Card.Cards.GAME_005.ToString(),
    Card.Cards.GDB_310.ToString()//虚灵神谕者 GDB_310
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

#region 奇利亚斯豪华版3000型 TOY_330t12
			// 提高使用优先级
			if(board.HasCardInHand(Card.Cards.TOY_330t12)
			){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_330t12, new Modifier(-150));
				AddLog("奇利亚斯豪华版3000型 -150");
			}
#endregion

#region 奇利亚斯豪华版3000型 TOY_330t11
			// 提高使用优先级
			if(board.HasCardInHand(Card.Cards.TOY_330t11)
			){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_330t11, new Modifier(-150));
				AddLog("奇利亚斯豪华版3000型 -150");
			}
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