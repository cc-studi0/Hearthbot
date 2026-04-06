using System.Collections.Generic;
using Hearthstone;
using UnityEngine;

public class ArenaInputManager : CollectionInputMgr
{
	private static ArenaInputManager s_instance;

	private int m_selectedIndex = -1;

	public new static ArenaInputManager Get()
	{
		return s_instance;
	}

	protected override void Awake()
	{
		s_instance = this;
		InputMgr.s_instances.Add(this);
		UniversalInputManager.Get().RegisterMouseOnOrOffScreenListener(OnMouseOnOrOffScreen);
	}

	protected override void OnDestroy()
	{
		InputMgr.s_instances.Remove(this);
		s_instance = null;
	}

	protected override void DropCard(bool dragCanceled)
	{
		PegCursor.Get().SetMode(PegCursor.Mode.STOPDRAG);
		if (!(m_heldCardVisual == null))
		{
			if (!dragCanceled)
			{
				m_cardDroppedCallback?.Invoke();
			}
			m_cardDroppedCallback = null;
			m_heldCardVisual.Hide();
			if (m_scrollBar != null)
			{
				m_scrollBar.Pause(pause: false);
			}
		}
	}

	public override bool HandleKeyboardInput()
	{
		DraftDisplay draftDisplay = DraftDisplay.Get();
		if (draftDisplay == null)
		{
			return false;
		}
		bool flag = draftDisplay.IsInHeroSelectMode();
		if (InputCollection.GetKeyUp(KeyCode.Escape) && flag)
		{
			draftDisplay.DoHeroCancelAnimation();
			return true;
		}
		if (!HearthstoneApplication.IsInternal())
		{
			return false;
		}
		List<DraftCardVisual> cardVisuals = DraftDisplay.Get().GetCardVisuals();
		if (cardVisuals == null)
		{
			return false;
		}
		if (cardVisuals.Count == 0)
		{
			return false;
		}
		int num = -1;
		if (InputCollection.GetKeyUp(KeyCode.Alpha1))
		{
			num = 0;
		}
		else if (InputCollection.GetKeyUp(KeyCode.Alpha2))
		{
			num = 1;
		}
		else if (InputCollection.GetKeyUp(KeyCode.Alpha3))
		{
			num = 2;
		}
		if (num < 0)
		{
			return false;
		}
		if (cardVisuals.Count < num + 1)
		{
			return false;
		}
		if (flag && m_selectedIndex == num)
		{
			draftDisplay.ClickConfirmButton();
			m_selectedIndex = -1;
			return true;
		}
		DraftCardVisual draftCardVisual = cardVisuals[num];
		if (draftCardVisual == null)
		{
			return false;
		}
		if (flag)
		{
			draftDisplay.DoHeroCancelAnimation();
		}
		m_selectedIndex = num;
		draftCardVisual.ChooseThisCard();
		return true;
	}
}
