using System.Collections;
using Hearthstone.UI;
using UnityEngine;

public class DraftScene : PegasusScene
{
	private bool m_unloading;

	private WidgetTemplate m_arenaLandingPageWidget;

	private const string PC_GUID = "ArenaLandingPage_ControllerWidget.prefab:a815051635c0f8a439236edb15a161a7";

	private const string PHONE_GUID = "ArenaLandingPage_ControllerWidget_phone.prefab:6a510c67222739a419fde8aeb584510e";

	protected override void Awake()
	{
		base.Awake();
		string text = (UniversalInputManager.UsePhoneUI ? "ArenaLandingPage_ControllerWidget_phone.prefab:6a510c67222739a419fde8aeb584510e" : "ArenaLandingPage_ControllerWidget.prefab:a815051635c0f8a439236edb15a161a7");
		AssetLoader.Get().InstantiatePrefab(text, OnUIScreenLoaded);
	}

	public override bool IsUnloading()
	{
		return m_unloading;
	}

	public override void Unload()
	{
		m_unloading = true;
		DraftDisplay draftDisplay = DraftDisplay.Get();
		if (draftDisplay != null)
		{
			draftDisplay.Unload();
			Object.Destroy(draftDisplay.gameObject);
		}
		if (m_arenaLandingPageWidget != null)
		{
			Object.Destroy(m_arenaLandingPageWidget.gameObject);
		}
		m_unloading = false;
	}

	private void OnUIScreenLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogError($"DraftScene.OnUIScreenLoaded() - failed to load go {assetRef}");
			return;
		}
		m_arenaLandingPageWidget = go.GetComponent<WidgetTemplate>();
		StartCoroutine(NotifySceneLoadedWhenReady());
	}

	private IEnumerator NotifySceneLoadedWhenReady()
	{
		while (ArenaLandingPageManager.Get() == null)
		{
			yield return null;
		}
		while (!ArenaLandingPageManager.Get().IsLoadingComplete())
		{
			yield return null;
		}
		while (DraftDisplay.Get() == null)
		{
			yield return null;
		}
		while (!DraftDisplay.Get().IsLoadingComplete())
		{
			yield return null;
		}
		SceneMgr.Get().NotifySceneLoaded();
	}
}
