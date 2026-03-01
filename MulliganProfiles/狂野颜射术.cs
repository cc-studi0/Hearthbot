using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBot.Mulligan
{
	[Serializable]
	public class DefaultMulliganProfile : MulliganProfile
	{
		// 留牌版本号：每次改动请递增（用于日志定位当前运行的是哪一版）
		 //TODO 策略版本号：每次改动请递增（用于日志定位当前运行的是哪一版）迭代+1后保留不重置 日期更替
				// tip:项目是c#6 不要用新语法
	private const string MulliganVersion = "2026-02-14.633"; // 速写美术家可留2张；组合加留：商贩+弹幕时可留邪翼蝠/郊狼（可多张）

		private readonly List<Card.Cards> CardsToKeep = new List<Card.Cards>();

		public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
		{
			Bot.Log($"[Mulligan] 狂野颜射术 v{MulliganVersion} | 对手职业={opponentClass}");
			CardsToKeep.Clear();

			// 留牌（按 HSReplay 统计与新构筑口径综合优化）：
			// - 必留：速写美术家(TOY_916，可留2张)、咒怨之墓(TLC_451)
			// - 高保留率：狗头人(LOOT_014)、炽烈烬火(TLC_249)、列车机务工(WW_044)
			// - 栉龙(TLC_603)：作为补充 1 费动作（起手缺少其它 1 费动作时才留）
			// - 过期货物专卖商(ULD_163)：按条件留（见下方）
			// - 灵魂弹幕(RLK_534)：只有起手同时有过期商贩才留
			// - 后手可留：维希度斯的窟穴(WON_103)
			// - 异教低阶牧师(CORE_SCH_713)：对法术/节奏职业时可留（需已有前期动作）
			// - 超光子弹幕(TIME_027)：节奏对局（内战/盗贼/猎人/骑士）或起手有蝠/郊狼时可留；且只在留弹幕时才留蝠/郊狼
			bool hasCoin = choices.Contains(Card.Cards.GAME_005);

			int sketchCount = choices.Count(x => x == Card.Cards.TOY_916);
			int lootCount = choices.Count(x => x == Card.Cards.LOOT_014);
			int dragonCount = choices.Count(x => x == Card.Cards.TLC_603);
			int spacePirateCount = choices.Count(x => x == Card.Cards.GDB_333);
			int toyDealerCount = choices.Count(x => x == Card.Cards.TOY_518);
			int southseaCount = choices.Count(x => x == Card.Cards.CORE_NEW1_022);
			int shipCannonCount = choices.Count(x => x == Card.Cards.CORE_NEW1_023);
			int merchantCount = choices.Count(x => x == Card.Cards.ULD_163);
			int photonCount = choices.Count(x => x == Card.Cards.TIME_027);
			int tramCount = choices.Count(x => x == Card.Cards.WW_044);
			int cinderCount = choices.Count(x => x == Card.Cards.TLC_249);
			int parachuteCount = choices.Count(x => x == Card.Cards.DRG_056);
			int neophyteCount = choices.Count(x => x == Card.Cards.CORE_SCH_713);
			int techCount = choices.Count(x => x == Card.Cards.ETC_418);
			int feralFlurryCount = choices.Count(x => x == Card.Cards.GIL_820);
			int soulfireCount = choices.Count(x => x == Card.Cards.EX1_308);
			int gulDanHandCount = choices.Count(x => x == Card.Cards.BT_300);
			int hellfireCount = choices.Count(x => x == Card.Cards.CORE_CS2_062);

			int barrageCount = choices.Count(x => x == Card.Cards.RLK_534);
			int tombCount = choices.Count(x => x == Card.Cards.TLC_451);
			int caveCount = choices.Count(x => x == Card.Cards.WON_103);
			int coyoteCount = choices.Count(x => x == Card.Cards.TIME_047);
			int felwingCount = choices.Count(x => x == Card.Cards.YOD_032);
			int sw091Count = choices.Count(x => x == Card.Cards.SW_091);

			bool hasMerchant = merchantCount > 0;
			bool hasBarrage = barrageCount > 0;
			bool hasTomb = tombCount > 0;
			bool hasCave = caveCount > 0;
			bool hasSketch = sketchCount > 0;
			bool hasTech = techCount > 0;

			// ===== 必留 =====
			// 新口径：灵魂弹幕只有手上有过期商贩才会留；当同时有过期商贩+灵魂弹幕，则同时留
			// [用户需求] 组合技留牌只需保留一张即可，不用全留
			bool forceKeepMerchantAndBarrage = hasMerchant && hasBarrage;
			if (forceKeepMerchantAndBarrage)
			{
				KeepCopies(Card.Cards.ULD_163, 1);
				KeepCopies(Card.Cards.RLK_534, 1);
				Bot.Log("[留牌] 必留：过期货物专卖商(ULD_163) + 灵魂弹幕(RLK_534)（组合技各留1张）");

				// 组合扩展：当起手已命中 商贩+弹幕 时，允许额外保留邪翼蝠/郊狼，且按出现张数保留（可多张）
				if (felwingCount > 0)
				{
					KeepCopies(Card.Cards.YOD_032, felwingCount);
					Bot.Log($"[留牌] 组合加留：狂暴邪翼蝠(YOD_032) x{felwingCount}（商贩+弹幕）");
				}
				if (coyoteCount > 0)
				{
					KeepCopies(Card.Cards.TIME_047, coyoteCount);
					Bot.Log($"[留牌] 组合加留：狡诈的郊狼(TIME_047) x{coyoteCount}（商贩+弹幕）");
				}
			}

			// 速写美术家：必留（可留2张）
			if (hasSketch)
			{
				int keepSketch = Math.Min(2, sketchCount);
				KeepCopies(Card.Cards.TOY_916, keepSketch);
				Bot.Log("[留牌] 必留：速写美术家(TOY_916) (可留2张)");
			}

			// 咒怨之墓：必留（但避免起手留两张过于臃肿，这里最多留1张）
			if (hasTomb)
			{
				KeepCopies(Card.Cards.TLC_451, 1);
				Bot.Log("[留牌] 必留：咒怨之墓(TLC_451)");
			}
			if(sw091Count > 0){
				KeepCopies(Card.Cards.SW_091, 1);
				Bot.Log("[留牌] 必留：恶魔之种(SW_091) (仅留1张)");
			}

			// [用户需求] 起手只留一张一费
			// 优先级：宝藏经销商 > 太空海盗 > 狗头人图书管理员 > 其他 (炽烈烬火, 列车机务工, 栉龙)
			// [v.630修改] 不再留恐怖海盗(CORE_NEW1_022)，减费不稳定且收益有限
			bool keptOneCost = false;

			if (toyDealerCount > 0)
			{
				KeepCopies(Card.Cards.TOY_518, 1);
				keptOneCost = true;
				Bot.Log("[留牌] 必留：宝藏经销商(TOY_518) (1费最高优先级，只留1张)");
			}
			else if (spacePirateCount > 0)
			{
				KeepCopies(Card.Cards.GDB_333, 1);
				keptOneCost = true;
				Bot.Log("[留牌] 必留：太空海盗(GDB_333) (1费优先级2，只留1张)");
			}
			else if (lootCount > 0)
			{
				KeepCopies(Card.Cards.LOOT_014, 1);
				keptOneCost = true;
				Bot.Log("[留牌] 必留：狗头人图书管理员(LOOT_014) (1费优先级3，只留1张)");
			}
			else if (cinderCount > 0)
			{
				KeepCopies(Card.Cards.TLC_249, 1);
				keptOneCost = true;
				Bot.Log("[留牌] 必留：炽烈烬火(TLC_249) (其他1费动作，只留1张)");
			}
			else if (tramCount > 0)
			{
				KeepCopies(Card.Cards.WW_044, 1);
				keptOneCost = true;
				Bot.Log("[留牌] 必留：列车机务工(WW_044) (其他1费动作，只留1张)");
			}
			else if (dragonCount > 0)
			{
				KeepCopies(Card.Cards.TLC_603, 1);
				keptOneCost = true;
				Bot.Log("[留牌] 必留：栉龙(TLC_603) (其他1费动作，只留1张)");
			}

			// 特例：若起手含核心海盗（宝藏经销商/太空海盗），则保留空降歹徒（DRG_056）最多两张
			// [v.630修改] 移除恐怖海盗判断
						// 新口径：即使已有其他1费，也额外保留1张狗头人图书管理员
			if (lootCount > 0 && !CardsToKeep.Contains(Card.Cards.LOOT_014))
			{
				KeepCopies(Card.Cards.LOOT_014, 1);
				Bot.Log("[留牌] 可留：狗头人图书管理员(LOOT_014) (即使已有其他1费，也额外留1张)");
			}

			if (parachuteCount > 0 && (spacePirateCount > 0 || toyDealerCount > 0))
			{
				int keepParachute = Math.Min(2, parachuteCount);
				KeepCopies(Card.Cards.DRG_056, keepParachute);
				Bot.Log($"[留牌] 特例：起手含核心海盗，保留空降歹徒(DRG_056) x{keepParachute}");
			}

			// 船载火炮：必留
			if (shipCannonCount > 0)
			{
				KeepCopies(Card.Cards.CORE_NEW1_023, shipCannonCount);
				Bot.Log($"[留牌] 必留：船载火炮(CORE_NEW1_023) x{shipCannonCount}");
			}

			// ===== 可留（条件） =====
			bool hasEarlyAction = hasTomb || keptOneCost;

			// 乐器技师：抽武器（时空之爪）的关键过牌点，按新需求加入留牌。
			// 说明：起手保留 1 张即可，避免太臃肿。
			if (hasTech)
			{
				KeepCopies(Card.Cards.ETC_418, 1);
				Bot.Log("[留牌] 可留：乐器技师(ETC_418)（抽武器/提速时空之爪）");
			}

			// 过期货物专卖商：起手有速写美术家(TOY_916)就留；或“有前期动作且有弹幕”时可留
			// [用户需求] 组合技留牌只需保留一张，不用全留
			bool shouldKeepMerchant = !forceKeepMerchantAndBarrage && hasMerchant && (hasSketch || (hasEarlyAction && hasBarrage));
			if (shouldKeepMerchant)
			{
				KeepCopies(Card.Cards.ULD_163, 1);
				if (hasSketch)
					Bot.Log("[留牌] 必留：过期货物专卖商(ULD_163)（起手有速写美术家，仅留1张）");
				else
					Bot.Log("[留牌] 可留：过期货物专卖商(ULD_163)（需有前期动作+弹幕，仅留1张）");
			}

			// 灵魂弹幕：只有起手同时拥有过期商贩时才留（若 forceKeep 分支已处理，这里不重复）
			if (!forceKeepMerchantAndBarrage && hasBarrage && hasMerchant)
			{
				KeepCopies(Card.Cards.RLK_534, 1);
				Bot.Log("[留牌] 可留：灵魂弹幕(RLK_534)（仅当起手同时有过期货物专卖商，仅留1张）");
			}

			// 地标：后手可留
			if (hasCave && hasCoin)
			{
				KeepCopies(Card.Cards.WON_103, caveCount);
				Bot.Log("[留牌] 可留：维希度斯的窟穴(WON_103)（后手留地标）");
			}

			// 异教低阶牧师：统计中等保留率，偏对抗法术/节奏职业；需已有前期动作
			bool isSpellTempoMatchup = opponentClass == Card.CClass.MAGE
				|| opponentClass == Card.CClass.ROGUE
				|| opponentClass == Card.CClass.DRUID
				|| opponentClass == Card.CClass.SHAMAN
				|| opponentClass == Card.CClass.PRIEST;
			if (neophyteCount > 0 && hasEarlyAction && isSpellTempoMatchup)
			{
				KeepCopies(Card.Cards.CORE_SCH_713, Math.Min(1, neophyteCount));
				Bot.Log("[留牌] 可留：异教低阶牧师(CORE_SCH_713)（对法术/节奏职业，且需已有前期动作）");
			}

			// 粗暴的猢狲(39.8%保留率)：对快攻/海盗职业时的场面解法
			bool isAggroMatchup = opponentClass == Card.CClass.WARLOCK
				|| opponentClass == Card.CClass.ROGUE
				|| opponentClass == Card.CClass.HUNTER
				|| opponentClass == Card.CClass.DEMONHUNTER;
			if (feralFlurryCount > 0 && hasEarlyAction && isAggroMatchup)
			{
				KeepCopies(Card.Cards.GIL_820, 1);
				Bot.Log("[留牌] 可留：粗暴的猢狲(GIL_820)（对快攻职业，且需已有前期动作）");
			}

			// 灵魂之火(6.6%保留率)：对极快攻时的低费直伤
			if (soulfireCount > 0 && hasEarlyAction && isAggroMatchup && (spacePirateCount > 0 || toyDealerCount > 0))
			{
				KeepCopies(Card.Cards.EX1_308, 1);
				Bot.Log("[留牌] 可留：灵魂之火(EX1_308)（对快攻且有海盗时）");
			}

			// 超光子弹幕：节奏对局（内战/盗贼/猎人/骑士）或起手有蝠/郊狼时可留
			bool isTempoMatchup = opponentClass == Card.CClass.WARLOCK
				|| opponentClass == Card.CClass.ROGUE
				|| opponentClass == Card.CClass.HUNTER
				|| opponentClass == Card.CClass.PALADIN;
			bool hasFelwingOrCoyote = felwingCount > 0 || coyoteCount > 0;
			bool shouldKeepPhoton = photonCount > 0 && (isTempoMatchup || hasFelwingOrCoyote);
			if (shouldKeepPhoton)
			{
				// 组合技留牌：超光子弹幕 +（蝠/郊狼）时，只需要留 1 张弹幕即可
				// （避免起手双弹幕过于臃肿，且 combo 线路只吃到第一张弹幕）
				int photonKeepCount = photonCount;
				if (hasFelwingOrCoyote)
					photonKeepCount = 1;

				KeepCopies(Card.Cards.TIME_027, photonKeepCount);
				Bot.Log("[留牌] 可留：超光子弹幕(TIME_027)（节奏对局/或起手有蝠/郊狼）");

				// 仅在留弹幕时才留蝠/郊狼；若已命中“商贩+弹幕”组合分支则不重复加留
				if (felwingCount > 0 && !forceKeepMerchantAndBarrage)
				{
					KeepCopies(Card.Cards.YOD_032, felwingCount);
					Bot.Log("[留牌] 搭配留：狂暴邪翼蝠(YOD_032)（仅在留超光子弹幕时）");
				}
				if (coyoteCount > 0 && !forceKeepMerchantAndBarrage)
				{
					KeepCopies(Card.Cards.TIME_047, coyoteCount);
					Bot.Log("[留牌] 搭配留：狡诈的郊狼(TIME_047)（仅在留超光子弹幕时）");
				}
			}

			// 总结输出：把最终留牌打印出来
			try
			{
				var keepNames = CardsToKeep.Select(id => CardTemplate.LoadFromId(id).NameCN + "(" + id + ")");
				Bot.Log("[留牌] 最终保留：" + string.Join(", ", keepNames));
			}
			catch
			{
				// ignore
			}

			return CardsToKeep;
		}

		private void Keep(Card.Cards card)
		{
			if (!CardsToKeep.Contains(card))
				CardsToKeep.Add(card);
		}

		// 允许保留同名多张（例如双 TIME_027、双 1 费等）
		private void KeepCopies(Card.Cards card, int count)
		{
			if (count <= 0) return;
			for (int i = 0; i < count; i++)
				CardsToKeep.Add(card);
		}
	}
}

