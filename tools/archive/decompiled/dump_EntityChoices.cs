// 从 Network 类中提取的竞技场/选择相关嵌套类
// 源: Assembly-CSharp.dll -> Network

public class CardChoice
{
	public NetCache.CardDefinition CardDef = new NetCache.CardDefinition();
	public List<NetCache.CardDefinition> PackageCardDefs = new List<NetCache.CardDefinition>();
}

public class DraftChoicesAndContents
{
	public int Slot { get; set; }
	public int RedraftSlot { get; set; }
	public List<CardChoice> Choices { get; }
	public NetCache.CardDefinition Hero { get; }
	public NetCache.CardDefinition HeroPower { get; }
	public DeckContents DeckInfo { get; }
	public int Wins { get; set; }
	public int Losses { get; set; }
	public RewardChest Chest { get; set; }
	public int MaxWins { get; set; }
	public int MaxLosses { get; set; }
	public int MaxSlot { get; set; }
	public int MaxRedraftSlot { get; set; }
	public ArenaSession Session { get; set; }
	public DraftSlotType SlotType { get; set; }
	public List<DraftSlotType> UniqueSlotTypesForDraft { get; }
	public bool IsUnderground { get; set; }
	public bool IsCrowdsFavor { get; set; }
	public long RedraftDeckId { get; set; }
	public DeckContents RedraftDeckInfo { get; }

	public DraftChoicesAndContents()
	{
		Choices = new List<CardChoice>();
		Hero = new NetCache.CardDefinition();
		HeroPower = new NetCache.CardDefinition();
		DeckInfo = new DeckContents();
		Chest = null;
		UniqueSlotTypesForDraft = new List<DraftSlotType>();
		IsUnderground = false;
		IsCrowdsFavor = false;
		RedraftDeckId = 0L;
		RedraftDeckInfo = new DeckContents();
	}
}

public class DraftChosen
{
	public List<CardChoice> NextChoices { get; set; }
	public DraftSlotType SlotType { get; set; }
	public CardChoice ChosenCard { get; set; }

	public DraftChosen()
	{
		ChosenCard = new CardChoice();
		NextChoices = new List<CardChoice>();
	}
}

public class EntityChoices
{
	public int ID { get; set; }
	public CHOICE_TYPE ChoiceType { get; set; }
	public int CountMin { get; set; }
	public int CountMax { get; set; }
	public List<int> Entities { get; set; }
	public int Source { get; set; }
	public int PlayerId { get; set; }
	public bool HideChosen { get; set; }
	public List<int> UnchoosableEntities { get; set; }

	public bool IsSingleChoice()
	{
		if (CountMax == 0)
		{
			return true;
		}
		if (CountMin == 1)
		{
			return CountMax == 1;
		}
		return false;
	}
}

public class EntitiesChosen
{
	public int ID { get; set; }
	public List<int> Entities { get; set; }
	public int PlayerId { get; set; }
	public CHOICE_TYPE ChoiceType { get; set; }
}
