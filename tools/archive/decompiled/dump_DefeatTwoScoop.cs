using System.Collections;
using UnityEngine;

public class DefeatTwoScoop : EndGameTwoScoop
{
	public GameObject m_rightTrumpet;

	public GameObject m_rightBanner;

	public GameObject m_rightBannerShred;

	public GameObject m_rightCloud;

	public GameObject m_leftTrumpet;

	public GameObject m_leftBanner;

	public GameObject m_leftBannerFront;

	public GameObject m_leftCloud;

	public GameObject m_crown;

	public GameObject m_defeatBanner;

	protected override void ShowImpl()
	{
		Entity hero = GameState.Get().GetFriendlySidePlayer().GetHero();
		if (hero != null)
		{
			m_heroActor.SetFullDefFromEntity(hero);
			m_heroActor.UpdateAllComponents();
		}
		m_heroActor.TurnOffCollider();
		GameEntity gameEntity = GameState.Get().GetGameEntity();
		string bannerLabel = gameEntity.GetDefeatScreenBannerText();
		if (GameMgr.Get().LastGameData.GameResult == TAG_PLAYSTATE.TIED)
		{
			bannerLabel = gameEntity.GetTieScreenBannerText();
		}
		SetBannerLabel(bannerLabel);
		GetComponent<PlayMakerFSM>().SendEvent("Action");
		iTween.FadeTo(base.gameObject, 1f, 0.25f);
		base.gameObject.transform.localScale = new Vector3(EndGameTwoScoop.START_SCALE_VAL, EndGameTwoScoop.START_SCALE_VAL, EndGameTwoScoop.START_SCALE_VAL);
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("scale", new Vector3(EndGameTwoScoop.END_SCALE_VAL, EndGameTwoScoop.END_SCALE_VAL, EndGameTwoScoop.END_SCALE_VAL));
		tweenHashTable.Add("time", 0.5f);
		tweenHashTable.Add("oncomplete", "PunchEndGameTwoScoop");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		tweenHashTable.Add("easetype", iTween.EaseType.easeOutBounce);
		iTween.ScaleTo(base.gameObject, tweenHashTable);
		Hashtable tweenHashTable2 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable2.Add("position", base.gameObject.transform.position + new Vector3(0.005f, 0.005f, 0.005f));
		tweenHashTable2.Add("time", 1.5f);
		tweenHashTable2.Add("oncomplete", "TokyoDriftTo");
		tweenHashTable2.Add("oncompletetarget", base.gameObject);
		iTween.MoveTo(base.gameObject, tweenHashTable2);
		AnimateCrownTo();
		AnimateLeftTrumpetTo();
		AnimateRightTrumpetTo();
		StartCoroutine(AnimateAll());
		m_heroActor.LegendaryHeroPortrait?.RaiseAnimationEvent(LegendaryHeroAnimations.Defeat);
		using DefLoader.DisposableCardDef disposableCardDef = hero.ShareDisposableCardDef();
		GameObject gameObject = disposableCardDef.CardDef?.gameObject;
		if (gameObject != null && gameObject.TryGetComponent<HeroCardSwitcher>(out var component))
		{
			component.OnGameDefeat(m_heroActor);
		}
	}

	protected override void ResetPositions()
	{
		base.gameObject.transform.localPosition = EndGameTwoScoop.START_POSITION;
		base.gameObject.transform.eulerAngles = new Vector3(0f, 180f, 0f);
		m_rightTrumpet.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
		m_rightTrumpet.transform.localEulerAngles = new Vector3(0f, -180f, 0f);
		m_leftTrumpet.transform.localEulerAngles = new Vector3(0f, -180f, 0f);
		m_rightBanner.transform.localScale = new Vector3(1f, 1f, -0.0375f);
		m_rightBannerShred.transform.localScale = new Vector3(1f, 1f, 0.05f);
		m_rightCloud.transform.localPosition = new Vector3(-0.036f, -0.3f, 0.46f);
		m_leftCloud.transform.localPosition = new Vector3(-0.047f, -0.3f, 0.41f);
		m_crown.transform.localEulerAngles = new Vector3(-0.026f, 17f, 0.2f);
		m_defeatBanner.transform.localEulerAngles = new Vector3(0f, 180f, 0f);
	}

	private IEnumerator AnimateAll()
	{
		yield return new WaitForSeconds(0.25f);
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("scale", new Vector3(1f, 1f, 1.1f));
		tweenHashTable.Add("time", 0.25f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.easeOutBounce);
		iTween.ScaleTo(m_rightTrumpet, tweenHashTable);
		Hashtable tweenHashTable2 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable2.Add("z", 1f);
		tweenHashTable2.Add("delay", 0.5f);
		tweenHashTable2.Add("time", 1f);
		tweenHashTable2.Add("islocal", true);
		tweenHashTable2.Add("easetype", iTween.EaseType.easeOutElastic);
		iTween.ScaleTo(m_rightBanner, tweenHashTable2);
		Hashtable tweenHashTable3 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable3.Add("z", 1f);
		tweenHashTable3.Add("delay", 0.5f);
		tweenHashTable3.Add("time", 1f);
		tweenHashTable3.Add("islocal", true);
		tweenHashTable3.Add("easetype", iTween.EaseType.easeOutElastic);
		iTween.ScaleTo(m_rightBannerShred, tweenHashTable3);
		Hashtable tweenHashTable4 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable4.Add("x", -0.81f);
		tweenHashTable4.Add("time", 5f);
		tweenHashTable4.Add("islocal", true);
		tweenHashTable4.Add("easetype", iTween.EaseType.easeOutCubic);
		tweenHashTable4.Add("oncomplete", "CloudTo");
		tweenHashTable4.Add("oncompletetarget", base.gameObject);
		iTween.MoveTo(m_rightCloud, tweenHashTable4);
		Hashtable tweenHashTable5 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable5.Add("x", 0.824f);
		tweenHashTable5.Add("time", 5f);
		tweenHashTable5.Add("islocal", true);
		tweenHashTable5.Add("easetype", iTween.EaseType.easeOutCubic);
		iTween.MoveTo(m_leftCloud, tweenHashTable5);
		Hashtable tweenHashTable6 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable6.Add("rotation", new Vector3(0f, 183f, 0f));
		tweenHashTable6.Add("time", 0.5f);
		tweenHashTable6.Add("delay", 0.75f);
		tweenHashTable6.Add("islocal", true);
		tweenHashTable6.Add("easetype", iTween.EaseType.easeOutBounce);
		iTween.RotateTo(m_defeatBanner, tweenHashTable6);
	}

	private void AnimateLeftTrumpetTo()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("rotation", new Vector3(0f, -184f, 0f));
		tweenHashTable.Add("time", 5f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.easeInOutCirc);
		tweenHashTable.Add("oncomplete", "AnimateLeftTrumpetFro");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.RotateTo(m_leftTrumpet, tweenHashTable);
	}

	private void AnimateLeftTrumpetFro()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("rotation", new Vector3(0f, -180f, 0f));
		tweenHashTable.Add("time", 5f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.easeInOutCirc);
		tweenHashTable.Add("oncomplete", "AnimateLeftTrumpetTo");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.RotateTo(m_leftTrumpet, tweenHashTable);
	}

	private void AnimateRightTrumpetTo()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("rotation", new Vector3(0f, -172f, 0f));
		tweenHashTable.Add("time", 8f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.easeInOutCirc);
		tweenHashTable.Add("oncomplete", "AnimateRightTrumpetFro");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.RotateTo(m_rightTrumpet, tweenHashTable);
	}

	private void AnimateRightTrumpetFro()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("rotation", new Vector3(0f, -180f, 0f));
		tweenHashTable.Add("time", 8f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.easeInOutCirc);
		tweenHashTable.Add("oncomplete", "AnimateRightTrumpetTo");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.RotateTo(m_rightTrumpet, tweenHashTable);
	}

	private void TokyoDriftTo()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("position", EndGameTwoScoop.START_POSITION + new Vector3(0.2f, 0.2f, 0.2f));
		tweenHashTable.Add("time", 10f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.linear);
		tweenHashTable.Add("oncomplete", "TokyoDriftFro");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.MoveTo(base.gameObject, tweenHashTable);
	}

	private void TokyoDriftFro()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("position", EndGameTwoScoop.START_POSITION);
		tweenHashTable.Add("time", 10f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.linear);
		tweenHashTable.Add("oncomplete", "TokyoDriftTo");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.MoveTo(base.gameObject, tweenHashTable);
	}

	private void CloudTo()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("x", -0.38f);
		tweenHashTable.Add("time", 10f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.linear);
		tweenHashTable.Add("oncomplete", "CloudFro");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.MoveTo(m_rightCloud, tweenHashTable);
		Hashtable tweenHashTable2 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable2.Add("x", 0.443f);
		tweenHashTable2.Add("time", 10f);
		tweenHashTable2.Add("islocal", true);
		tweenHashTable2.Add("easetype", iTween.EaseType.linear);
		iTween.MoveTo(m_leftCloud, tweenHashTable2);
	}

	private void CloudFro()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("x", -0.81f);
		tweenHashTable.Add("time", 10f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.linear);
		tweenHashTable.Add("oncomplete", "CloudTo");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.MoveTo(m_rightCloud, tweenHashTable);
		Hashtable tweenHashTable2 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable2.Add("x", 0.824f);
		tweenHashTable2.Add("time", 10f);
		tweenHashTable2.Add("islocal", true);
		tweenHashTable2.Add("easetype", iTween.EaseType.linear);
		iTween.MoveTo(m_leftCloud, tweenHashTable2);
	}

	private void AnimateCrownTo()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("rotation", new Vector3(0f, 1.8f, 0f));
		tweenHashTable.Add("time", 0.75f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.linear);
		tweenHashTable.Add("oncomplete", "AnimateCrownFro");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.RotateTo(m_crown, tweenHashTable);
	}

	private void AnimateCrownFro()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("rotation", new Vector3(0f, 17f, 0f));
		tweenHashTable.Add("time", 0.75f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.linear);
		tweenHashTable.Add("oncomplete", "AnimateCrownTo");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.RotateTo(m_crown, tweenHashTable);
	}
}
You are not using the latest version of the tool, please update.
Latest version is '10.0.1.8346' (yours is '10.0.0.8330')
