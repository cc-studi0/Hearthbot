using Hearthstone.UI;
using UnityEngine;

public class ArenaDraftScreen : MonoBehaviour
{
	private static ArenaDraftScreen s_instance;

	private Widget m_widget;

	[SerializeField]
	private DraftDisplay m_draftDisplay;

	[SerializeField]
	private GameObject m_draftDisplayContainer;

	[SerializeField]
	private GameObject m_arenaCollectionContainer;

	[SerializeField]
	private GameObject m_arenaRedraftCollectionContainer;

	[SerializeField]
	private GameObject m_midrunContainer;

	[SerializeField]
	private WidgetInstance m_revertButtonWidget;

	[SerializeField]
	private PageTurn m_pageTurn;

	private DraftManager m_draftManager;

	private void Awake()
	{
		s_instance = this;
		m_draftManager = DraftManager.Get();
		m_widget = GetComponent<WidgetTemplate>();
		if (m_draftDisplayContainer != null)
		{
			m_draftDisplayContainer.SetActive(value: true);
		}
	}

	public bool IsDraftDisplayContainerActive()
	{
		if (m_draftDisplayContainer != null)
		{
			return m_draftDisplayContainer.activeInHierarchy;
		}
		return false;
	}

	public bool IsArenaCollectionContainerActive()
	{
		if (m_arenaCollectionContainer != null)
		{
			return m_arenaCollectionContainer.activeInHierarchy;
		}
		return false;
	}

	public bool IsArenaRedraftCollectionContainerActive()
	{
		if (m_arenaRedraftCollectionContainer != null)
		{
			return m_arenaRedraftCollectionContainer.activeInHierarchy;
		}
		return false;
	}

	public bool IsMidrunContainerActive()
	{
		if (m_midrunContainer != null)
		{
			return m_midrunContainer.activeInHierarchy;
		}
		return false;
	}

	public void HideWidget()
	{
		m_widget.Hide();
	}

	public Widget GetWidget()
	{
		return m_widget;
	}

	public PageTurn GetPageTurn()
	{
		return m_pageTurn;
	}

	public DraftDisplay GetDraftDisplay()
	{
		return m_draftDisplay;
	}
}
