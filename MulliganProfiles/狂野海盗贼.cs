using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBot.Mulligan
{
	[Serializable]
	public class WildPirateRogueMulliganProfile : MulliganProfile
	{
		// 留牌版本号：每次改动请递增（用于日志定位当前运行的是哪一版）
		private const string MulliganVersion = "2026-01-05.1";

		private readonly List<Card.Cards> CardsToKeep = new List<Card.Cards>();

		public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
		{
			Bot.Log($"[Mulligan] 狂野海盗贼 v{MulliganVersion} | 对手职业={opponentClass}");
			CardsToKeep.Clear();

			// 留牌策略：
			// 必留：1费海盗（南海船工/鱼排斗士/旗标骷髅/宝藏经销商）
			// 必留：换挡漂移（抽牌+减费）
			// 后手可留：奖品掠夺者（连击触发简单）、空降歹徒（可降费）
			// 有1费海盗时可留：空降歹徒、船载火炮（配合连招）
			// 先手有1费海盗时也可留：玩具船（配合站场）
			// 其余默认不留

			bool hasCoin = choices.Contains(Card.Cards.GAME_005);

			int deckhandCount = choices.Count(x => x == Card.Cards.CORE_CS2_146);    // 南海船工
			int filletfighterCount = choices.Count(x => x == Card.Cards.TSC_963);    // 鱼排斗士
			int jollyrogerCount = choices.Count(x => x == Card.Cards.NX2_006);       // 旗标骷髅
			int treasureCount = choices.Count(x => x == Card.Cards.TOY_518);         // 宝藏经销商
			int gearshiftCount = choices.Count(x => x == Card.Cards.TTN_922);        // 换挡漂移
			int plundererCount = choices.Count(x => x == Card.Cards.DMF_519);        // 奖品掠夺者
			int parachuteCount = choices.Count(x => x == Card.Cards.DRG_056);        // 空降歹徒
			int cannonCount = choices.Count(x => x == Card.Cards.GVG_075);           // 船载火炮
			int toyboatCount = choices.Count(x => x == Card.Cards.TOY_505);          // 玩具船

			// 统计1费海盗总数
			int oneCostPiratesCount = deckhandCount + filletfighterCount + jollyrogerCount + treasureCount;

			// ===== 必留：1费海盗 =====
			KeepCopies(Card.Cards.CORE_CS2_146, deckhandCount);     // 南海船工
			KeepCopies(Card.Cards.TSC_963, filletfighterCount);      // 鱼排斗士
			KeepCopies(Card.Cards.NX2_006, jollyrogerCount);         // 旗标骷髅
			KeepCopies(Card.Cards.TOY_518, treasureCount);           // 宝藏经销商
			if (deckhandCount > 0) Bot.Log("[留牌] 必留：南海船工(CORE_CS2_146)");
			if (filletfighterCount > 0) Bot.Log("[留牌] 必留：鱼排斗士(TSC_963)");
			if (jollyrogerCount > 0) Bot.Log("[留牌] 必留：旗标骷髅(NX2_006)");
			if (treasureCount > 0) Bot.Log("[留牌] 必留：宝藏经销商(TOY_518)");

			// ===== 必留：换挡漂移（抽牌+减费）=====
			KeepCopies(Card.Cards.TTN_922, gearshiftCount);          // 换挡漂移
			if (gearshiftCount > 0) Bot.Log("[留牌] 必留：换挡漂移(TTN_922)");

			// ===== 后手可留：奖品掠夺者 =====
			if (hasCoin && plundererCount > 0)
			{
				KeepCopies(Card.Cards.DMF_519, plundererCount);
				Bot.Log("[留牌] 后手可留：奖品掠夺者(DMF_519)（连击易触发）");
			}

			// ===== 有1费海盗时可留：空降歹徒、船载火炮 =====
			if (oneCostPiratesCount > 0)
			{
				if (parachuteCount > 0)
				{
					KeepCopies(Card.Cards.DRG_056, parachuteCount);
					Bot.Log("[留牌] 有1费海盗可留：空降歹徒(DRG_056)");
				}

				// 后手有1费海盗可留船/炮（用于配合连招）
				if (hasCoin)
				{
					if (cannonCount > 0)
					{
						KeepCopies(Card.Cards.GVG_075, cannonCount);
						Bot.Log("[留牌] 后手+1费海盗可留：船载火炮(GVG_075)");
					}

					// 有炮不留船
					if (toyboatCount > 0 && cannonCount == 0)
					{
						KeepCopies(Card.Cards.TOY_505, toyboatCount);
						Bot.Log("[留牌] 后手+1费海盗可留：玩具船(TOY_505)");
					}
				}
			}

			// ===== 先手有1费海盗时也可留：玩具船（但不留船载火炮）=====
			if (!hasCoin && oneCostPiratesCount > 0 && toyboatCount > 0 && cannonCount == 0)
			{
				KeepCopies(Card.Cards.TOY_505, toyboatCount);
				Bot.Log("[留牌] 先手+1费海盗可留：玩具船(TOY_505)");
			}

			// 总结输出
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

		private void KeepCopies(Card.Cards card, int count)
		{
			if (count <= 0) return;
			for (int i = 0; i < count; i++)
				CardsToKeep.Add(card);
		}
	}
}
