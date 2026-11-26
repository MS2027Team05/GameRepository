using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ゲームパッド操作時かつロックオン捕捉中に、入力減衰(スローダウン)および
/// スティック入力連動の緩やかなローテーショナル追従を提供するモジュール。
/// </summary>
public class PlayerAimAssist : MonoBehaviour, IAimAssist
{
	private const float m_DefaultAssistRadius = 0.18f;
	private const float m_DefaultInnerRadiusRatio = 0.35f;
	private const float m_DefaultMaxAssistDistance = 45.0f;
	private const float m_DefaultMinSlowdownMultiplier = 0.45f;
	private const float m_DefaultMaxFollowYawDegreesPerSecond = 55.0f;
	private const float m_DefaultMaxFollowPitchDegreesPerSecond = 40.0f;
	private const float m_DefaultStickActivityThreshold = 0.05f;
	private const float m_DefaultFollowDeadzoneAngle = 0.5f;
	private const float m_DefaultFollowRampAngle = 4.0f;
	private const float m_DefaultCounterInputSuppression = 1.0f;
	private const float m_CounterLookReferenceScale = 1.0f;

	[Header("カメラ")]
	[Tooltip("スクリーン位置判定に使用するカメラ(未設定時はCamera.mainを自動取得)")]
	[SerializeField] private Camera m_TargetCamera;

	[Header("アシスト有効範囲")]
	[Tooltip("アシストが有効になる画面短辺に対する正規化半径(0以下時はLockOnManagerの検索範囲を使用)")]
	[SerializeField] private float m_AssistRadius;

	[Tooltip("スローダウンが最大強度に達する内側の正規化半径(0以下時はAssistRadiusの35%を使用)")]
	[SerializeField] private float m_InnerSlowdownRadius;

	[Tooltip("エイムアシストを適用する最大ワールド距離(m、0以下時は既定値45.0)")]
	[SerializeField] private float m_MaxAssistDistance;

	[Header("入力減衰(スローダウン)設定")]
	[Tooltip("ターゲット中心付近での視点入力倍率の下限(0.1〜1.0、0以下時は既定値0.45)")]
	[SerializeField] private float m_MinSlowdownMultiplier;

	[Header("ローテーショナル追従設定")]
	[Tooltip("水平方向(接平面)の最大自動追従角速度(度/秒、0以下時は既定値55.0)")]
	[SerializeField] private float m_MaxFollowYawDegreesPerSecond;

	[Tooltip("垂直方向(Pitch)の最大自動追従角速度(度/秒、0以下時は既定値40.0)")]
	[SerializeField] private float m_MaxFollowPitchDegreesPerSecond;

	[Tooltip("ローテーショナル追従を発動する左右スティック入力の最小大きさ(0以下時は既定値0.05)")]
	[SerializeField] private float m_StickActivityThreshold;

	[Tooltip("ターゲット中心付近で追従を停止し微振動を防ぐ角度(度、0以下時は既定値0.5)")]
	[SerializeField] private float m_FollowDeadzoneAngle;

	[Tooltip("追従角速度が最大値へ立ち上がるまでの角度差(度、0以下時は既定値4.0)")]
	[SerializeField] private float m_FollowRampAngle;

	[Tooltip("ターゲットと逆方向へ視点操作した際に追従を抑制する強さ(0以下時は既定値1.0)")]
	[SerializeField] private float m_CounterInputSuppression;

	private ILockOnProvider m_LockOnProvider;
	private NetworkObject m_NetworkObject;

	private void Awake()
	{
		m_LockOnProvider = GetComponent<ILockOnProvider>();
		m_NetworkObject = GetComponent<NetworkObject>();
		EnsureTargetCamera();
	}

	/// <summary>
	/// 現在のロックオン対象とスティック入力に基づいて、視点入力の減衰倍率およびカメラ追従の補正角度(度)を算出します。
	/// </summary>
	public void EvaluateAimAssist(
		Transform cameraTransform,
		Vector3 currentUp,
		Vector2 lookInput,
		Vector2 moveInput,
		bool isUsingGamepad,
		float deltaTime,
		out float slowdownMultiplier,
		out float assistYawDelta,
		out float assistPitchDelta)
	{
		slowdownMultiplier = 1.0f;
		assistYawDelta = 0.0f;
		assistPitchDelta = 0.0f;

		if (!enabled || !isUsingGamepad || cameraTransform == null || deltaTime <= 0.0f)
		{
			return;
		}

		if (m_NetworkObject != null && m_NetworkObject.IsSpawned && !m_NetworkObject.IsOwner)
		{
			return;
		}

		if (m_LockOnProvider == null || !m_LockOnProvider.HasValidTarget)
		{
			return;
		}

		if (!m_LockOnProvider.TryGetCurrentTargetWorldPosition(out Vector3 targetWorldPos))
		{
			return;
		}

		if (!EnsureTargetCamera())
		{
			return;
		}

		Vector3 toTarget = targetWorldPos - cameraTransform.position;
		float worldDistance = toTarget.magnitude;
		float maxDistance = GetMaxAssistDistance();
		if (worldDistance <= 0.001f || worldDistance > maxDistance)
		{
			return;
		}

		if (!TryGetNormalizedScreenDistance(targetWorldPos, out float screenDistance))
		{
			return;
		}

		float assistRadius = GetAssistRadius();
		if (screenDistance > assistRadius)
		{
			return;
		}

		float innerRadius = GetInnerSlowdownRadius(assistRadius);

		// スクリーン中央からの距離に応じて入力減衰倍率を滑らかに補間
		slowdownMultiplier = CalculateSlowdownMultiplier(screenDistance, innerRadius, assistRadius);

		// 左スティックまたは右スティックに入力があるときのみ緩やかな追従を適用
		float stickThreshold = m_StickActivityThreshold > 0.0f ? m_StickActivityThreshold : m_DefaultStickActivityThreshold;
		float stickThresholdSq = stickThreshold * stickThreshold;
		bool hasStickActivity = moveInput.sqrMagnitude > stickThresholdSq || lookInput.sqrMagnitude > stickThresholdSq;
		if (!hasStickActivity)
		{
			return;
		}

		// スクリーン外周から内側に向けて追従の効きを立ち上げる
		float edgeWeight = 1.0f - Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(innerRadius, assistRadius, screenDistance));
		// 境界付近でもわずかに補助を残すことでロックオン直後の食いつきを安定化
		float followWeight = Mathf.Lerp(0.35f, 1.0f, edgeWeight);

		Vector3 up = currentUp.sqrMagnitude > 0.0001f ? currentUp.normalized : Vector3.up;
		Vector3 camForward = cameraTransform.forward.normalized;
		Vector3 targetDirection = toTarget / worldDistance;

		CalculateFollowAngleDeltas(
			camForward,
			targetDirection,
			up,
			lookInput,
			followWeight,
			deltaTime,
			out assistYawDelta,
			out assistPitchDelta);
	}

	private float CalculateSlowdownMultiplier(float screenDistance, float innerRadius, float assistRadius)
	{
		float minSlowdown = m_MinSlowdownMultiplier > 0.0f
			? Mathf.Clamp(m_MinSlowdownMultiplier, 0.1f, 1.0f)
			: m_DefaultMinSlowdownMultiplier;

		if (assistRadius <= innerRadius + 0.0001f)
		{
			return screenDistance <= assistRadius ? minSlowdown : 1.0f;
		}

		float distanceRatio = Mathf.InverseLerp(innerRadius, assistRadius, screenDistance);
		return Mathf.SmoothStep(minSlowdown, 1.0f, distanceRatio);
	}

	private void CalculateFollowAngleDeltas(
		Vector3 camForward,
		Vector3 targetDirection,
		Vector3 up,
		Vector2 lookInput,
		float followWeight,
		float deltaTime,
		out float assistYawDelta,
		out float assistPitchDelta)
	{
		assistYawDelta = 0.0f;
		assistPitchDelta = 0.0f;

		// 1. 水平方向(currentUp軸周り)の角度差算出
		Vector3 camPlanar = Vector3.ProjectOnPlane(camForward, up);
		Vector3 targetPlanar = Vector3.ProjectOnPlane(targetDirection, up);
		if (camPlanar.sqrMagnitude > 0.0001f && targetPlanar.sqrMagnitude > 0.0001f)
		{
			float yawDiff = Vector3.SignedAngle(camPlanar.normalized, targetPlanar.normalized, up);
			float maxYawSpeed = m_MaxFollowYawDegreesPerSecond > 0.0f
				? m_MaxFollowYawDegreesPerSecond
				: m_DefaultMaxFollowYawDegreesPerSecond;

			assistYawDelta = ComputeAxisFollowStep(
				yawDiff,
				lookInput.x,
				maxYawSpeed * followWeight,
				deltaTime);
		}

		// 2. 垂直方向(仰角差)の算出(上向きが正の仰角差に対し、m_Pitchは負方向が上向きのため符号を反転)
		float currentElevation = Mathf.Asin(Mathf.Clamp(Vector3.Dot(camForward, up), -1.0f, 1.0f)) * Mathf.Rad2Deg;
		float targetElevation = Mathf.Asin(Mathf.Clamp(Vector3.Dot(targetDirection, up), -1.0f, 1.0f)) * Mathf.Rad2Deg;
		float elevationDiff = targetElevation - currentElevation;

		float maxPitchSpeed = m_MaxFollowPitchDegreesPerSecond > 0.0f
			? m_MaxFollowPitchDegreesPerSecond
			: m_DefaultMaxFollowPitchDegreesPerSecond;

		float elevationStep = ComputeAxisFollowStep(
			elevationDiff,
			lookInput.y,
			maxPitchSpeed * followWeight,
			deltaTime);

		assistPitchDelta = -elevationStep;
	}

	private float ComputeAxisFollowStep(
		float angleDiff,
		float axisLookInput,
		float maxSpeedDegreesPerSecond,
		float deltaTime)
	{
		float absDiff = Mathf.Abs(angleDiff);
		float deadzone = m_FollowDeadzoneAngle > 0.0f ? m_FollowDeadzoneAngle : m_DefaultFollowDeadzoneAngle;
		if (absDiff <= deadzone)
		{
			return 0.0f;
		}

		// ターゲットから離れる方向へ視点スティックを倒している場合は追従を抑えて操作意図を優先
		float counterSuppression = m_CounterInputSuppression > 0.0f
			? m_CounterInputSuppression
			: m_DefaultCounterInputSuppression;

		float directionFilter = 1.0f;
		if (Mathf.Abs(axisLookInput) > 0.001f && Mathf.Sign(axisLookInput) != Mathf.Sign(angleDiff))
		{
			float awayStrength = Mathf.Clamp01(Mathf.Abs(axisLookInput) / m_CounterLookReferenceScale);
			directionFilter = Mathf.Clamp01(1.0f - awayStrength * counterSuppression);
		}

		if (directionFilter <= 0.001f)
		{
			return 0.0f;
		}

		float rampAngle = m_FollowRampAngle > 0.0f ? m_FollowRampAngle : m_DefaultFollowRampAngle;
		float rampEnd = Mathf.Max(deadzone + 0.1f, deadzone + rampAngle);
		float angleGain = Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(deadzone, rampEnd, absDiff));

		float maxStep = maxSpeedDegreesPerSecond * angleGain * directionFilter * deltaTime;
		float remainingToDeadzone = absDiff - deadzone;
		float stepMagnitude = Mathf.Min(maxStep, remainingToDeadzone);

		return Mathf.Sign(angleDiff) * stepMagnitude;
	}

	private bool TryGetNormalizedScreenDistance(Vector3 worldPosition, out float screenDistance)
	{
		screenDistance = float.MaxValue;
		if (m_TargetCamera == null)
		{
			return false;
		}

		Vector3 viewport = m_TargetCamera.WorldToViewportPoint(worldPosition);
		if (viewport.z <= 0.0f)
		{
			return false;
		}

		float pixelWidth = m_TargetCamera.pixelWidth;
		float pixelHeight = m_TargetCamera.pixelHeight;
		float shortSide = Mathf.Min(pixelWidth, pixelHeight);
		if (shortSide <= 0.001f)
		{
			return false;
		}

		Vector2 screenOffset = new Vector2(
			(viewport.x - 0.5f) * pixelWidth,
			(viewport.y - 0.5f) * pixelHeight);

		screenDistance = screenOffset.magnitude / shortSide;
		return true;
	}

	private float GetAssistRadius()
	{
		if (m_AssistRadius > 0.0f)
		{
			return m_AssistRadius;
		}

		if (m_LockOnProvider != null && m_LockOnProvider.ReticleSearchRadius > 0.0f)
		{
			return m_LockOnProvider.ReticleSearchRadius;
		}

		return m_DefaultAssistRadius;
	}

	private float GetInnerSlowdownRadius(float assistRadius)
	{
		if (m_InnerSlowdownRadius > 0.0f)
		{
			return Mathf.Min(m_InnerSlowdownRadius, assistRadius);
		}

		return assistRadius * m_DefaultInnerRadiusRatio;
	}

	private float GetMaxAssistDistance()
	{
		return m_MaxAssistDistance > 0.0f ? m_MaxAssistDistance : m_DefaultMaxAssistDistance;
	}

	private bool EnsureTargetCamera()
	{
		if (m_TargetCamera != null && m_TargetCamera.isActiveAndEnabled)
		{
			return true;
		}

		m_TargetCamera = Camera.main;
		if (m_TargetCamera != null && m_TargetCamera.isActiveAndEnabled)
		{
			return true;
		}

		Camera[] allCameras = Camera.allCameras;
		for (int i = 0; i < allCameras.Length; i++)
		{
			Camera cam = allCameras[i];
			if (cam != null && cam.isActiveAndEnabled)
			{
				m_TargetCamera = cam;
				return true;
			}
		}

		return false;
	}
}
