using System.Collections;
using UnityEngine;

public class VictoryTwoScoop : EndGameTwoScoop
{
	public GameObject m_godRays;

	public GameObject m_godRays2;

	public GameObject m_rightTrumpet;

	public GameObject m_rightBanner;

	public GameObject m_rightCloud;

	public GameObject m_rightLaurel;

	public GameObject m_leftTrumpet;

	public GameObject m_leftBanner;

	public GameObject m_leftCloud;

	public GameObject m_leftLaurel;

	public GameObject m_crown;

	public AudioSource m_fireworksAudio;

	private const float GOD_RAY_ANGLE = 20f;

	private const float GOD_RAY_DURATION = 20f;

	private const float LAUREL_ROTATION = 2f;

	protected EntityDef m_overrideHeroEntityDef;

	protected DefLoader.DisposableCardDef m_overrideHeroCardDef;

	public void StopFireworksAudio()
	{
		if (m_fireworksAudio != null)
		{
			SoundManager.Get().Stop(m_fireworksAudio);
		}
	}

	public void SetOverrideHero(EntityDef overrideHero)
	{
		if (overrideHero != null)
		{
			if (!overrideHero.IsHero())
			{
				Log.Gameplay.PrintError("VictoryTwoScoop.SetOverrideHero() - passed EntityDef {0} is not a hero!", overrideHero);
				return;
			}
			DefLoader.DisposableCardDef cardDef = DefLoader.Get().GetCardDef(overrideHero.GetCardId());
			if (cardDef == null)
			{
				Log.Gameplay.PrintError("VictoryTwoScoop.SetOverrideHero() - passed EntityDef {0} does not have a CardDef!", overrideHero);
			}
			else
			{
				m_overrideHeroEntityDef = overrideHero;
				m_overrideHeroCardDef?.Dispose();
				m_overrideHeroCardDef = cardDef;
			}
		}
		else
		{
			m_overrideHeroEntityDef = null;
			m_overrideHeroCardDef?.Dispose();
			m_overrideHeroCardDef = null;
		}
	}

	public override void OnDestroy()
	{
		m_overrideHeroCardDef?.Dispose();
		m_overrideHeroCardDef = null;
		base.OnDestroy();
	}

	protected override void ShowImpl()
	{
		SetupHeroActor();
		SetupBannerText();
		PlayShowAnimations();
	}

	protected override void ResetPositions()
	{
		base.gameObject.transform.localPosition = EndGameTwoScoop.START_POSITION;
		base.gameObject.transform.eulerAngles = new Vector3(0f, 180f, 0f);
		if (m_rightTrumpet != null)
		{
			m_rightTrumpet.transform.localPosition = new Vector3(0.23f, -0.6f, 0.16f);
			m_rightTrumpet.transform.localScale = new Vector3(1f, 1f, 1f);
		}
		if (m_leftTrumpet != null)
		{
			m_leftTrumpet.transform.localPosition = new Vector3(-0.23f, -0.6f, 0.16f);
			m_leftTrumpet.transform.localScale = new Vector3(-1f, 1f, 1f);
		}
		if (m_rightBanner != null)
		{
			m_rightBanner.transform.localScale = new Vector3(1f, 1f, 0.08f);
		}
		if (m_leftBanner != null)
		{
			m_leftBanner.transform.localScale = new Vector3(1f, 1f, 0.08f);
		}
		if (m_rightCloud != null)
		{
			m_rightCloud.transform.localPosition = new Vector3(-0.2f, -0.8f, 0.26f);
		}
		if (m_leftCloud != null)
		{
			m_leftCloud.transform.localPosition = new Vector3(0.16f, -0.8f, 0.2f);
		}
		if (m_godRays != null)
		{
			m_godRays.transform.localEulerAngles = new Vector3(0f, 29f, 0f);
		}
		if (m_godRays2 != null)
		{
			m_godRays2.transform.localEulerAngles = new Vector3(0f, -29f, 0f);
		}
		if (m_crown != null)
		{
			m_crown.transform.localPosition = new Vector3(-0.041f, -0.04f, -0.834f);
		}
		if (m_rightLaurel != null)
		{
			m_rightLaurel.transform.localEulerAngles = new Vector3(0f, -90f, 0f);
			m_rightLaurel.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
		}
		if (m_leftLaurel != null)
		{
			m_leftLaurel.transform.localEulerAngles = new Vector3(0f, 90f, 0f);
			m_leftLaurel.transform.localScale = new Vector3(-0.7f, 0.7f, 0.7f);
		}
	}

	public override void StopAnimating()
	{
		StopCoroutine("AnimateAll");
		iTween.Stop(base.gameObject, includechildren: true);
		StartCoroutine(ResetPositionsForGoldEvent());
	}

	protected void SetupHeroActor()
	{
		if (m_overrideHeroEntityDef != null && m_overrideHeroCardDef != null)
		{
			m_heroActor.SetEntityDef(m_overrideHeroEntityDef);
			m_heroActor.SetCardDef(m_overrideHeroCardDef);
			m_heroActor.UpdateAllComponents();
		}
		else
		{
			Entity hero = GameState.Get().GetFriendlySidePlayer().GetHero();
			if (hero != null)
			{
				m_heroActor.SetFullDefFromEntity(hero);
				m_heroActor.UpdateAllComponents();
				using DefLoader.DisposableCardDef disposableCardDef = hero.ShareDisposableCardDef();
				GameObject gameObject = disposableCardDef.CardDef?.gameObject;
				if (gameObject != null && gameObject.TryGetComponent<HeroCardSwitcher>(out var component))
				{
					component.OnGameVictory(m_heroActor);
				}
			}
		}
		m_heroActor.TurnOffCollider();
	}

	protected void SetupBannerText()
	{
		string victoryScreenBannerText = GameState.Get().GetGameEntity().GetVictoryScreenBannerText();
		SetBannerLabel(victoryScreenBannerText);
	}

	protected override Vector3 GetXPBarPosition()
	{
		if (m_heroActor.GetCustomFrameRequiresMetaCalibration())
		{
			return m_heroActor.GetCustomeFrameEndGameVictoryXPBarPosition();
		}
		return m_xpBarLocalPosition;
	}

	protected void PlayShowAnimations()
	{
		GetComponent<PlayMakerFSM>().SendEvent("Action");
		iTween.FadeTo(base.gameObject, 1f, 0.25f);
		base.gameObject.transform.localScale = new Vector3(EndGameTwoScoop.START_SCALE_VAL, EndGameTwoScoop.START_SCALE_VAL, EndGameTwoScoop.START_SCALE_VAL);
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("scale", new Vector3(EndGameTwoScoop.END_SCALE_VAL, EndGameTwoScoop.END_SCALE_VAL, EndGameTwoScoop.END_SCALE_VAL));
		tweenHashTable.Add("time", 0.5f);
		tweenHashTable.Add("oncomplete", "PunchEndGameTwoScoop");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.ScaleTo(base.gameObject, tweenHashTable);
		Hashtable tweenHashTable2 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable2.Add("position", base.gameObject.transform.position + new Vector3(0.005f, 0.005f, 0.005f));
		tweenHashTable2.Add("time", 1.5f);
		tweenHashTable2.Add("oncomplete", "TokyoDriftTo");
		tweenHashTable2.Add("oncompletetarget", base.gameObject);
		iTween.MoveTo(base.gameObject, tweenHashTable2);
		AnimateGodraysTo();
		AnimateCrownTo();
		StartCoroutine(AnimateAll());
		m_heroActor.LegendaryHeroPortrait?.RaiseAnimationEvent(LegendaryHeroAnimations.Victory);
	}

	private IEnumerator AnimateAll()
	{
		yield return new WaitForSeconds(0.25f);
		float num = 0.4f;
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("position", new Vector3(-0.52f, -0.6f, -0.23f));
		tweenHashTable.Add("time", num);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.easeOutElastic);
		iTween.MoveTo(m_rightTrumpet, tweenHashTable);
		Hashtable tweenHashTable2 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable2.Add("position", new Vector3(0.44f, -0.6f, -0.23f));
		tweenHashTable2.Add("time", num);
		tweenHashTable2.Add("islocal", true);
		tweenHashTable2.Add("easetype", iTween.EaseType.easeOutElastic);
		iTween.MoveTo(m_leftTrumpet, tweenHashTable2);
		Hashtable tweenHashTable3 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable3.Add("scale", new Vector3(1.1f, 1.1f, 1.1f));
		tweenHashTable3.Add("time", 0.25f);
		tweenHashTable3.Add("delay", 0.3f);
		tweenHashTable3.Add("islocal", true);
		tweenHashTable3.Add("easetype", iTween.EaseType.easeOutBounce);
		iTween.ScaleTo(m_rightTrumpet, tweenHashTable3);
		Hashtable tweenHashTable4 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable4.Add("scale", new Vector3(-1.1f, 1.1f, 1.1f));
		tweenHashTable4.Add("time", 0.25f);
		tweenHashTable4.Add("delay", 0.3f);
		tweenHashTable4.Add("islocal", true);
		tweenHashTable4.Add("easetype", iTween.EaseType.easeOutBounce);
		iTween.ScaleTo(m_leftTrumpet, tweenHashTable4);
		Hashtable tweenHashTable5 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable5.Add("z", 1f);
		tweenHashTable5.Add("delay", 0.24f);
		tweenHashTable5.Add("time", 1f);
		tweenHashTable5.Add("islocal", true);
		tweenHashTable5.Add("easetype", iTween.EaseType.easeOutElastic);
		iTween.ScaleTo(m_rightBanner, tweenHashTable5);
		Hashtable tweenHashTable6 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable6.Add("z", 1f);
		tweenHashTable6.Add("delay", 0.24f);
		tweenHashTable6.Add("time", 1f);
		tweenHashTable6.Add("islocal", true);
		tweenHashTable6.Add("easetype", iTween.EaseType.easeOutElastic);
		iTween.ScaleTo(m_leftBanner, tweenHashTable6);
		Hashtable tweenHashTable7 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable7.Add("x", -1.227438f);
		tweenHashTable7.Add("time", 5f);
		tweenHashTable7.Add("islocal", true);
		tweenHashTable7.Add("easetype", iTween.EaseType.easeOutCubic);
		tweenHashTable7.Add("oncomplete", "CloudTo");
		tweenHashTable7.Add("oncompletetarget", base.gameObject);
		iTween.MoveTo(m_rightCloud, tweenHashTable7);
		Hashtable tweenHashTable8 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable8.Add("x", 1.053244f);
		tweenHashTable8.Add("time", 5f);
		tweenHashTable8.Add("islocal", true);
		tweenHashTable8.Add("easetype", iTween.EaseType.easeOutCubic);
		iTween.MoveTo(m_leftCloud, tweenHashTable8);
		Hashtable tweenHashTable9 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable9.Add("rotation", new Vector3(0f, 2f, 0f));
		tweenHashTable9.Add("time", 0.5f);
		tweenHashTable9.Add("islocal", true);
		tweenHashTable9.Add("easetype", iTween.EaseType.easeOutElastic);
		tweenHashTable9.Add("oncomplete", "LaurelWaveTo");
		tweenHashTable9.Add("oncompletetarget", base.gameObject);
		iTween.RotateTo(m_rightLaurel, tweenHashTable9);
		Hashtable tweenHashTable10 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable10.Add("scale", new Vector3(1f, 1f, 1f));
		tweenHashTable10.Add("time", 0.25f);
		tweenHashTable10.Add("islocal", true);
		tweenHashTable10.Add("easetype", iTween.EaseType.easeOutBounce);
		iTween.ScaleTo(m_rightLaurel, tweenHashTable10);
		Hashtable tweenHashTable11 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable11.Add("rotation", new Vector3(0f, -2f, 0f));
		tweenHashTable11.Add("time", 0.5f);
		tweenHashTable11.Add("islocal", true);
		tweenHashTable11.Add("easetype", iTween.EaseType.easeOutElastic);
		iTween.RotateTo(m_leftLaurel, tweenHashTable11);
		Hashtable tweenHashTable12 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable12.Add("scale", new Vector3(-1f, 1f, 1f));
		tweenHashTable12.Add("time", 0.25f);
		tweenHashTable12.Add("islocal", true);
		tweenHashTable12.Add("easetype", iTween.EaseType.easeOutBounce);
		iTween.ScaleTo(m_leftLaurel, tweenHashTable12);
	}

	protected void TokyoDriftTo()
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
		tweenHashTable.Add("x", -0.92f);
		tweenHashTable.Add("time", 10f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.linear);
		tweenHashTable.Add("oncomplete", "CloudFro");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.MoveTo(m_rightCloud, tweenHashTable);
		Hashtable tweenHashTable2 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable2.Add("x", 0.82f);
		tweenHashTable2.Add("time", 10f);
		tweenHashTable2.Add("islocal", true);
		tweenHashTable2.Add("easetype", iTween.EaseType.linear);
		iTween.MoveTo(m_leftCloud, tweenHashTable2);
	}

	private void CloudFro()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("x", -1.227438f);
		tweenHashTable.Add("time", 10f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.linear);
		tweenHashTable.Add("oncomplete", "CloudTo");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.MoveTo(m_rightCloud, tweenHashTable);
		Hashtable tweenHashTable2 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable2.Add("x", 1.053244f);
		tweenHashTable2.Add("time", 10f);
		tweenHashTable2.Add("islocal", true);
		tweenHashTable2.Add("easetype", iTween.EaseType.linear);
		iTween.MoveTo(m_leftCloud, tweenHashTable2);
	}

	private void LaurelWaveTo()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("rotation", new Vector3(0f, 0f, 0f));
		tweenHashTable.Add("time", 10f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.linear);
		tweenHashTable.Add("oncomplete", "LaurelWaveFro");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.RotateTo(m_rightLaurel, tweenHashTable);
		Hashtable tweenHashTable2 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable2.Add("rotation", new Vector3(0f, 0f, 0f));
		tweenHashTable2.Add("time", 10f);
		tweenHashTable2.Add("islocal", true);
		tweenHashTable2.Add("easetype", iTween.EaseType.linear);
		iTween.RotateTo(m_leftLaurel, tweenHashTable2);
	}

	private void LaurelWaveFro()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("rotation", new Vector3(0f, 2f, 0f));
		tweenHashTable.Add("time", 10f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.linear);
		tweenHashTable.Add("oncomplete", "LaurelWaveTo");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.RotateTo(m_rightLaurel, tweenHashTable);
		Hashtable tweenHashTable2 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable2.Add("rotation", new Vector3(0f, -2f, 0f));
		tweenHashTable2.Add("time", 10f);
		tweenHashTable2.Add("islocal", true);
		tweenHashTable2.Add("easetype", iTween.EaseType.linear);
		iTween.RotateTo(m_leftLaurel, tweenHashTable2);
	}

	protected void AnimateCrownTo()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("z", -0.78f);
		tweenHashTable.Add("time", 5f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.easeInOutBack);
		tweenHashTable.Add("oncomplete", "AnimateCrownFro");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.MoveTo(m_crown, tweenHashTable);
	}

	private void AnimateCrownFro()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("z", -0.834f);
		tweenHashTable.Add("time", 5f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.easeInOutBack);
		tweenHashTable.Add("oncomplete", "AnimateCrownTo");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.MoveTo(m_crown, tweenHashTable);
	}

	protected void AnimateGodraysTo()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("rotation", new Vector3(0f, -20f, 0f));
		tweenHashTable.Add("time", 20f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.linear);
		tweenHashTable.Add("oncomplete", "AnimateGodraysFro");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.RotateTo(m_godRays, tweenHashTable);
		Hashtable tweenHashTable2 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable2.Add("rotation", new Vector3(0f, 20f, 0f));
		tweenHashTable2.Add("time", 20f);
		tweenHashTable2.Add("islocal", true);
		tweenHashTable2.Add("easetype", iTween.EaseType.linear);
		iTween.RotateTo(m_godRays2, tweenHashTable2);
	}

	private void AnimateGodraysFro()
	{
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("rotation", new Vector3(0f, 20f, 0f));
		tweenHashTable.Add("time", 20f);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.linear);
		tweenHashTable.Add("oncomplete", "AnimateGodraysTo");
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.RotateTo(m_godRays, tweenHashTable);
		Hashtable tweenHashTable2 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable2.Add("rotation", new Vector3(0f, -20f, 0f));
		tweenHashTable2.Add("time", 20f);
		tweenHashTable2.Add("islocal", true);
		tweenHashTable2.Add("easetype", iTween.EaseType.linear);
		iTween.RotateTo(m_godRays2, tweenHashTable2);
	}

	private IEnumerator ResetPositionsForGoldEvent()
	{
		yield return new WaitForEndOfFrame();
		yield return new WaitForEndOfFrame();
		float num = 0.25f;
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("position", new Vector3(-1.211758f, -0.8f, -0.2575677f));
		tweenHashTable.Add("time", num);
		tweenHashTable.Add("islocal", true);
		tweenHashTable.Add("easetype", iTween.EaseType.linear);
		iTween.MoveTo(m_rightCloud, tweenHashTable);
		Hashtable tweenHashTable2 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable2.Add("position", new Vector3(1.068925f, -0.8f, -0.197469f));
		tweenHashTable2.Add("time", num);
		tweenHashTable2.Add("islocal", true);
		tweenHashTable2.Add("easetype", iTween.EaseType.linear);
		iTween.MoveTo(m_leftCloud, tweenHashTable2);
		m_rightLaurel.transform.localRotation = Quaternion.Euler(Vector3.zero);
		Hashtable tweenHashTable3 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable3.Add("position", new Vector3(0.1723f, -0.206f, 0.753f));
		tweenHashTable3.Add("time", num);
		tweenHashTable3.Add("islocal", true);
		tweenHashTable3.Add("easetype", iTween.EaseType.linear);
		iTween.MoveTo(m_rightLaurel, tweenHashTable3);
		m_leftLaurel.transform.localRotation = Quaternion.Euler(Vector3.zero);
		Hashtable tweenHashTable4 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable4.Add("position", new Vector3(-0.2201783f, -0.318f, 0.753f));
		tweenHashTable4.Add("time", num);
		tweenHashTable4.Add("islocal", true);
		tweenHashTable4.Add("easetype", iTween.EaseType.linear);
		iTween.MoveTo(m_leftLaurel, tweenHashTable4);
		Hashtable tweenHashTable5 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable5.Add("z", -0.9677765f);
		tweenHashTable5.Add("time", num);
		tweenHashTable5.Add("islocal", true);
		tweenHashTable5.Add("easetype", iTween.EaseType.easeInOutBack);
		iTween.MoveTo(m_crown, tweenHashTable5);
		Hashtable tweenHashTable6 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable6.Add("rotation", new Vector3(0f, 20f, 0f));
		tweenHashTable6.Add("time", num);
		tweenHashTable6.Add("islocal", true);
		tweenHashTable6.Add("easetype", iTween.EaseType.linear);
		iTween.RotateTo(m_godRays, tweenHashTable6);
		Hashtable tweenHashTable7 = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable7.Add("rotation", new Vector3(0f, -20f, 0f));
		tweenHashTable7.Add("time", num);
		tweenHashTable7.Add("islocal", true);
		tweenHashTable7.Add("easetype", iTween.EaseType.linear);
		iTween.RotateTo(m_godRays2, tweenHashTable7);
	}
}
You are not using the latest version of the tool, please update.
Latest version is '10.0.1.8346' (yours is '10.0.0.8330')
