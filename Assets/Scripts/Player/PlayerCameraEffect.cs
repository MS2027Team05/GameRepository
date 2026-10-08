using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// プレイヤーのカメラ演出および視点回転操作を担当するモジュール。
/// FOV変更やカメラシェイク、接地面法線に追従した三人称視点カメラの旋回を管理します。
/// </summary>
public class PlayerCameraEffect : MonoBehaviour, ICameraEffect
{
	[Header("Cinemachine Camera")]
	[SerializeField] private CinemachineCamera m_CinemachineCamera;

	[Header("カメラピボット(未指定時はCinemachineCameraの親オブジェクト)")]
	[SerializeField] private Transform m_CameraPivot;

	[Header("カメラシェイク")]
	[SerializeField] private CinemachineImpulseSource m_ImpulseSource;

	[Header("通常時FOV")]
	[SerializeField] private float m_NormalFOV;

	[Header("高速移動時FOV")]
	[SerializeField] private float m_FallFOV;

	[Header("FOV変更時間")]
	[SerializeField] private float m_FOVChangeDuration;

	[Header("視点操作感度・角度制限")]
	[SerializeField] private float m_MouseSensitivity;
	[SerializeField] private float m_MinPitchAngle;
	[SerializeField] private float m_MaxPitchAngle;

	[Header("接地面法線への姿勢追従速度")]
	[SerializeField] private float m_NormalAlignSpeed;

	[Header("FOV変更")]
	[SerializeField]
	private AnimationCurve m_FOVCurve;

	private Coroutine m_FOVCoroutine;
	private PlayerInputReceiver m_InputReceiver;
	private IGravityMover m_GravityMover;
	private NetworkObject m_NetworkObject;
	private Vector3 m_CurrentUp = Vector3.up;
	private Vector3 m_PlanarForward = Vector3.forward;
	private float m_Pitch = 12.0f;
	private bool m_IsInitialized;

	private void Awake()
	{
		m_InputReceiver = GetComponent<PlayerInputReceiver>();
		m_GravityMover = GetComponent<IGravityMover>();
		m_NetworkObject = GetComponent<NetworkObject>();

		if (m_CameraPivot == null && m_CinemachineCamera != null)
		{
			m_CameraPivot = m_CinemachineCamera.transform.parent;
		}
	}

	private void Start()
	{
		// マルチプレイ環境下では自端末(IsOwner)のみカメラピボットを独立・回転制御
		if (m_NetworkObject != null && !m_NetworkObject.IsOwner)
		{
			return;
		}

		if (m_CameraPivot != null)
		{
			// 親(Player)の回転に引きずられないよう階層を独立化
			m_CameraPivot.SetParent(null);

			m_CurrentUp = (m_GravityMover != null && m_GravityMover.CurrentGroundNormal.sqrMagnitude > 0.001f)
				? m_GravityMover.CurrentGroundNormal
				: transform.up;

			m_PlanarForward = Vector3.ProjectOnPlane(transform.forward, m_CurrentUp);
			if (m_PlanarForward.sqrMagnitude < 0.001f)
			{
				m_PlanarForward = Vector3.ProjectOnPlane(Vector3.forward, m_CurrentUp);
				if (m_PlanarForward.sqrMagnitude < 0.001f)
				{
					m_PlanarForward = Vector3.Cross(m_CurrentUp, Vector3.right);
				}
			}
			m_PlanarForward.Normalize();

			m_Pitch = 12.0f;
			m_IsInitialized = true;
		}
	}

	private void LateUpdate()
	{
		if (!m_IsInitialized || m_CameraPivot == null)
		{
			return;
		}

		// プレイヤーの位置へ追従(回転は引き継がない)
		m_CameraPivot.position = transform.position;

		// 目標とする接地面法線(未接地時はtransform.up)
		Vector3 targetUp = (m_GravityMover != null && m_GravityMover.CurrentGroundNormal.sqrMagnitude > 0.001f)
			? m_GravityMover.CurrentGroundNormal
			: transform.up;

		// カメラのUpベクトルを法線方向へ滑らかに追従補間
		float alignSpeed = m_NormalAlignSpeed > 0f ? m_NormalAlignSpeed : 12.0f;
		Vector3 newUp = Vector3.Slerp(m_CurrentUp, targetUp, alignSpeed * Time.deltaTime).normalized;

		// 法線の変化に合わせて水平視線ベクトル(m_PlanarForward)を回転追従
		Quaternion upRotation = Quaternion.FromToRotation(m_CurrentUp, newUp);
		m_PlanarForward = (upRotation * m_PlanarForward).normalized;
		m_CurrentUp = newUp;

		// マウス操作によるYaw(水平旋回)・Pitch(垂直仰角)の更新
		if (m_InputReceiver != null && Cursor.lockState == CursorLockMode.Locked)
		{
			Vector2 look = m_InputReceiver.LookInput;

			// 水平旋回: 現在のUp(法線)を軸として接平面上の前方ベクトルを回転
			float yawAngle = look.x * m_MouseSensitivity;
			m_PlanarForward = (Quaternion.AngleAxis(yawAngle, m_CurrentUp) * m_PlanarForward).normalized;

			// 垂直仰角: 接平面に対するチルト角を更新
			m_Pitch -= look.y * m_MouseSensitivity;
			m_Pitch = Mathf.Clamp(m_Pitch, m_MinPitchAngle, m_MaxPitchAngle);
		}

		// 水平基底姿勢の決定(Up = m_CurrentUp, Forward = m_PlanarForward)
		Quaternion baseRotation = Quaternion.LookRotation(m_PlanarForward, m_CurrentUp);

		// 接平面に対するPitch(チルト)角をローカルX軸回転として合成
		m_CameraPivot.rotation = baseRotation * Quaternion.Euler(m_Pitch, 0.0f, 0.0f);
	}

	private void OnDestroy()
	{
		// 独立させたピボットオブジェクトのクリーンアップ
		if (m_CameraPivot != null && m_CameraPivot.parent == null)
		{
			Destroy(m_CameraPivot.gameObject);
		}
	}

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

		m_CinemachineCamera.Lens.FieldOfView = targetFOV;
		m_FOVCoroutine = null;
	}
}
