using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// プレイヤーのカメラ演出を担当するモジュール。
/// FOV変更やカメラシェイクなど、カメラに関する演出を管理します。
/// </summary>
public class PlayerCameraEffect : MonoBehaviour, ICameraEffect
{
	[Header("Cinemachine Camera")]
	[SerializeField] private CinemachineCamera m_CinemachineCamera;

	[Header("カメラシェイク")]
	[SerializeField] private CinemachineImpulseSource m_ImpulseSource;

	[Header("通常時FOV")]
	[SerializeField] private float m_NormalFOV = 60.0f;

	[Header("高速移動時FOV")]
	[SerializeField] private float m_FallFOV = 70.0f;

	[Header("FOV変更時間")]
	[SerializeField] private float m_FOVChangeDuration = 0.25f;

	[Header("FOV変更")]
	[SerializeField]
	private AnimationCurve m_FOVCurve =
		AnimationCurve.EaseInOut(0.0f, 0.0f, 1.0f, 1.0f);

	private Coroutine m_FOVCoroutine;

	/// <summary>
	/// 高速移動開始時のカメラ演出。
	/// FOVを広げ、カメラシェイクを発生させます。
	/// </summary>
	public void PlayFallEffect()
	{
		// FOVを高速移動用へ変更
		StartFOVChange(m_FallFOV);

		// カメラシェイク
		PlayCameraShake();
	}

	/// <summary>
	/// 外部から呼び出されたとき、
	/// 高速移動時のFOVを通常値へ戻します。
	/// </summary>
	public void RestoreFOV()
	{
		StartFOVChange(m_NormalFOV);
	}

	/// <summary>
	/// カメラシェイクを発生させます。
	/// </summary>
	private void PlayCameraShake()
	{
		if (m_ImpulseSource == null)
		{
			return;
		}

		m_ImpulseSource.GenerateImpulse();
	}

	/// <summary>
	/// 指定したFOVまで徐々に変更します。
	/// </summary>
	private void StartFOVChange(float targetFOV)
	{
		if (m_CinemachineCamera == null)
		{
			return;
		}

		if (m_FOVCoroutine != null)
		{
			StopCoroutine(m_FOVCoroutine);
		}

		m_FOVCoroutine =
			StartCoroutine(ChangeFOVRoutine(targetFOV));
	}

	private System.Collections.IEnumerator ChangeFOVRoutine(
		float targetFOV)
	{
		float startFOV =
			m_CinemachineCamera.Lens.FieldOfView;

		float elapsedTime = 0.0f;

		while (elapsedTime < m_FOVChangeDuration)
		{
			elapsedTime += Time.deltaTime;

			float normalizedTime =
				Mathf.Clamp01(
					elapsedTime / m_FOVChangeDuration);

			float curveValue =
				m_FOVCurve.Evaluate(normalizedTime);

			m_CinemachineCamera.Lens.FieldOfView =
				Mathf.Lerp(
					startFOV,
					targetFOV,
					curveValue);

			yield return null;
		}

		m_CinemachineCamera.Lens.FieldOfView =
			targetFOV;

		m_FOVCoroutine = null;
	}
}
