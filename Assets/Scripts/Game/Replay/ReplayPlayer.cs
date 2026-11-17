using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 保存されたリプレイクリップを、表示専用のプレイヤー/ボールアクターとゴールカメラ視点で再生し、
/// 終了後に元のカメラと表示状態へ復帰させるコンポーネント。
/// </summary>
public sealed class ReplayPlayer : MonoBehaviour
{
	private const float m_DefaultGoalCameraFov = 55.0f;
	private const float m_DefaultCameraPositionSmoothTime = 0.15f;
	private const float m_DefaultCameraLookAtSmoothTime = 0.12f;
	private const float m_DefaultPlaybackSpeed = 1.0f;
	private const float m_TeleportSnapDistanceSqr = 100.0f;

	[Header("表示用アクターPrefab設定")]
	[Tooltip("リプレイ中のプレイヤー表示に使用するPrefabです(Assets/Prefabs/LobbyPlayerDisplay.prefab)。")]
	[SerializeField] private LobbyPlayerDisplay m_PlayerActorPrefab;
	[Tooltip("リプレイ中のボール表示に使用する見た目専用Prefabです(任意: 未設定時はシーン内のBallから外観を複製)。")]
	[SerializeField] private GameObject m_BallActorPrefab;

	[Header("カメラ設定")]
	[Tooltip("プレイヤー端末(Display 1)でリプレイ再生に使用するカメラです(任意: 未設定時は自動生成)。")]
	[SerializeField] private Camera m_ReplayCamera;
	[Tooltip("Redチーム得点時に使用するゴールカメラアンカー上書き(任意: 未設定時はSpectatorCameraDirectorを使用)。")]
	[SerializeField] private Transform m_RedGoalAnchorOverride;
	[Tooltip("Blueチーム得点時に使用するゴールカメラアンカー上書き(任意: 未設定時はSpectatorCameraDirectorを使用)。")]
	[SerializeField] private Transform m_BlueGoalAnchorOverride;
	[SerializeField] private float m_GoalCameraFov;
	[SerializeField] private float m_CameraPositionSmoothTime;
	[SerializeField] private float m_CameraLookAtSmoothTime;

	[Header("再生設定")]
	[Tooltip("リプレイの再生倍率です(0以下で等倍1.0)。")]
	[SerializeField] private float m_PlaybackSpeed;

	/// <summary>
	/// シーン内でリプレイ再生中かどうかを取得します。
	/// </summary>
	public static bool IsReplayActive { get; private set; }

	/// <summary>
	/// このコンポーネントが現在再生中かどうかを取得します。
	/// </summary>
	public bool IsPlaying => m_IsPlaying;

	private readonly LobbyPlayerDisplay[] m_PlayerActors = new LobbyPlayerDisplay[ReplayFrameSlot.m_MaxPlayers];
	private readonly ulong[] m_ActorClientIds = new ulong[ReplayFrameSlot.m_MaxPlayers];
	private readonly List<Renderer> m_HiddenRenderers = new List<Renderer>(64);
	private readonly List<Canvas> m_HiddenCanvases = new List<Canvas>(16);
	private readonly List<Behaviour> m_HiddenVisualBehaviours = new List<Behaviour>(16);

	private GameObject m_ActorRoot;
	private GameObject m_BallActorInstance;
	private TrailLightningBolt[] m_BallTrailBolts;
	private Camera m_AutoCreatedReplayCamera;
	private Camera m_ActiveDisplay1Camera;
	private Camera m_DisabledMainCamera;
	private GameObject m_TempGoalAnchorObject;

	private SpectatorCameraDirector m_CachedSpectatorDirector;
	private BallController m_CachedRealBall;
	private GoalController[] m_CachedGoalControllers;

	private ReplayClipData m_CurrentClip;
	private Action m_OnCompletedCallback;
	private Transform m_ActiveGoalAnchor;
	private bool m_IsPlaying;
	private bool m_HasTriggeredGoalEffect;
	private float m_PlaybackTime;
	private int m_FrameCursor;
	private Vector3 m_CameraVelocity;
	private Vector3 m_LookAtVelocity;
	private Vector3 m_SmoothedLookAtPoint;

	private void OnDisable()
	{
		if (m_IsPlaying)
		{
			Stop();
		}
	}

	private void OnDestroy()
	{
		if (m_IsPlaying)
		{
			Stop();
		}

		if (m_TempGoalAnchorObject != null)
		{
			Destroy(m_TempGoalAnchorObject);
			m_TempGoalAnchorObject = null;
		}
	}

	/// <summary>
	/// 指定されたクリップの再生を開始します。
	/// </summary>
	public bool Play(ReplayClipData clip, Action onCompleted = null)
	{
		if (clip == null || !clip.IsValid)
		{
			return false;
		}

		if (m_IsPlaying)
		{
			Stop();
		}

		EnsureActorsPrepared();
		if (m_BallActorInstance == null)
		{
			return false;
		}

		m_CurrentClip = clip;
		m_OnCompletedCallback = onCompleted;
		m_PlaybackTime = 0f;
		m_FrameCursor = 0;
		m_IsPlaying = true;
		m_HasTriggeredGoalEffect = false;
		IsReplayActive = true;

		HideRealWorldVisuals();
		SetupActorsForClip(clip);
		ApplyFrameAtIndex(0);
		ActivateReplayCameras(clip.ScoringTeam);

		return true;
	}

	/// <summary>
	/// 再生を即時停止し、元のゲーム画面・カメラ・表示状態へ復帰させます。
	/// </summary>
	public void Stop()
	{
		if (!m_IsPlaying)
		{
			return;
		}

		m_IsPlaying = false;
		m_HasTriggeredGoalEffect = false;
		IsReplayActive = false;
		m_CurrentClip = null;
		m_OnCompletedCallback = null;

		StopReplayGoalEffects();
		RestoreCameras();
		DeactivateActors();
		RestoreRealWorldVisuals();
	}

	private void Update()
	{
		if (!m_IsPlaying || m_CurrentClip == null)
		{
			return;
		}

		float speed = m_PlaybackSpeed > 0.01f ? m_PlaybackSpeed : m_DefaultPlaybackSpeed;
		m_PlaybackTime += Time.deltaTime * speed;

		if (m_PlaybackTime >= m_CurrentClip.Duration)
		{
			ApplyFrameAtIndex(m_CurrentClip.FrameCount - 1);
			CompletePlayback();
			return;
		}

		while (m_FrameCursor + 1 < m_CurrentClip.FrameCount - 1 &&
			m_CurrentClip.GetFrame(m_FrameCursor + 1).m_Time < m_PlaybackTime)
		{
			m_FrameCursor++;
		}

		ReplayFrameSlot frameA = m_CurrentClip.GetFrame(m_FrameCursor);
		ReplayFrameSlot frameB = m_CurrentClip.GetFrame(Mathf.Min(m_FrameCursor + 1, m_CurrentClip.FrameCount - 1));
		float t = Mathf.InverseLerp(frameA.m_Time, frameB.m_Time, m_PlaybackTime);

		ApplyInterpolatedFrame(frameA, frameB, t);
		UpdateReplayCamera();

		if (!m_HasTriggeredGoalEffect && m_PlaybackTime >= m_CurrentClip.GoalTime)
		{
			m_HasTriggeredGoalEffect = true;
			TriggerReplayGoalEffect(m_CurrentClip.ScoringTeam);
		}
	}

	private void CompletePlayback()
	{
		Action callback = m_OnCompletedCallback;
		Stop();
		callback?.Invoke();
	}

	private void EnsureActorsPrepared()
	{
		if (m_ActorRoot == null)
		{
			m_ActorRoot = new GameObject("ReplayActorsRoot");
			m_ActorRoot.transform.SetParent(transform, false);
		}

		if (m_PlayerActorPrefab == null)
		{
#if UNITY_EDITOR
			m_PlayerActorPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<LobbyPlayerDisplay>("Assets/Prefabs/LobbyPlayerDisplay.prefab");
#endif
		}

		if (m_PlayerActorPrefab != null)
		{
			for (int i = 0; i < m_PlayerActors.Length; i++)
			{
				if (m_PlayerActors[i] != null)
				{
					continue;
				}

				LobbyPlayerDisplay actor = Instantiate(m_PlayerActorPrefab, m_ActorRoot.transform);
				actor.name = $"ReplayPlayerActor_{i}";
				actor.gameObject.SetActive(false);
				m_PlayerActors[i] = actor;
			}
		}

		if (m_BallActorInstance == null)
		{
			m_BallActorInstance = CreateVisualBallActor();
			if (m_BallActorInstance != null)
			{
				m_BallActorInstance.SetActive(false);
				m_BallTrailBolts = m_BallActorInstance.GetComponentsInChildren<TrailLightningBolt>(true);
			}
		}
	}

	private GameObject CreateVisualBallActor()
	{
		if (m_BallActorPrefab != null)
		{
			GameObject instance = Instantiate(m_BallActorPrefab, m_ActorRoot.transform);
			instance.name = "ReplayBallActor";
			StripNonVisualComponents(instance);
			return instance;
		}

		if (m_CachedRealBall == null)
		{
			m_CachedRealBall = FindFirstObjectByType<BallController>();
		}

		GameObject ballRoot = new GameObject("ReplayBallActor");
		ballRoot.transform.SetParent(m_ActorRoot.transform, false);

		if (m_CachedRealBall != null)
		{
			ballRoot.transform.localScale = m_CachedRealBall.transform.localScale;
			bool clonedAnyVisual = false;

			for (int i = 0; i < m_CachedRealBall.transform.childCount; i++)
			{
				Transform child = m_CachedRealBall.transform.GetChild(i);
				if (child == null)
				{
					continue;
				}

				// 当たり判定センサー以外の描画用・エフェクト用子階層を複製します。
				if (child.GetComponent<Renderer>() == null &&
					child.GetComponentInChildren<Renderer>(true) == null &&
					child.GetComponentInChildren<ALightningBolt>(true) == null)
				{
					continue;
				}

				GameObject visualChild = Instantiate(child.gameObject, ballRoot.transform, false);
				visualChild.name = child.name;
				StripNonVisualComponents(visualChild);
				clonedAnyVisual = true;
			}

			if (clonedAnyVisual)
			{
				return ballRoot;
			}
		}

		// フォールバック: シーン上にボールが見つからない場合の基本球体生成
		GameObject fallbackSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		fallbackSphere.name = "FallbackBallMesh";
		fallbackSphere.transform.SetParent(ballRoot.transform, false);
		Collider fallbackCol = fallbackSphere.GetComponent<Collider>();
		if (fallbackCol != null)
		{
			Destroy(fallbackCol);
		}
		return ballRoot;
	}

	private static void StripNonVisualComponents(GameObject root)
	{
		MonoBehaviour[] scripts = root.GetComponentsInChildren<MonoBehaviour>(true);
		for (int i = 0; i < scripts.Length; i++)
		{
			MonoBehaviour script = scripts[i];
			if (script == null)
			{
				continue;
			}

			if (script is ALightningBolt)
			{
				continue;
			}

			if (script is BallController || script is BallPhysicsController ||
				script is BallDebugRespawner || script is BallSpectatorHighlight ||
				script is Unity.Netcode.NetworkBehaviour)
			{
				Destroy(script);
			}
		}

		Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
		{
			if (colliders[i] != null)
			{
				Destroy(colliders[i]);
			}
		}

		Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			if (bodies[i] != null)
			{
				Destroy(bodies[i]);
			}
		}
	}

	private void SetupActorsForClip(ReplayClipData clip)
	{
		for (int i = 0; i < m_ActorClientIds.Length; i++)
		{
			m_ActorClientIds[i] = ulong.MaxValue;
			if (m_PlayerActors[i] != null)
			{
				m_PlayerActors[i].ResetAnimationState();
				m_PlayerActors[i].gameObject.SetActive(false);
			}
		}

		int count = Mathf.Min(clip.PlayerMetaCount, m_PlayerActors.Length);
		for (int i = 0; i < count; i++)
		{
			ReplayPlayerMeta meta = clip.GetPlayerMeta(i);
			LobbyPlayerDisplay actor = m_PlayerActors[i];
			if (!meta.m_IsValid || actor == null)
			{
				continue;
			}

			m_ActorClientIds[i] = meta.m_ClientId;
			actor.gameObject.SetActive(true);
			Color teamColor = GetTeamDisplayColor(meta.m_Team);
			actor.ApplyPlayerData(meta.ToLobbyPlayerData(), teamColor);
			actor.ResetAnimationState();
		}

		if (m_BallActorInstance != null)
		{
			m_BallActorInstance.SetActive(true);
		}
	}

	private void DeactivateActors()
	{
		for (int i = 0; i < m_PlayerActors.Length; i++)
		{
			if (m_PlayerActors[i] != null)
			{
				m_PlayerActors[i].SetBillboardCameraOverride(null);
				m_PlayerActors[i].ResetAnimationState();
				m_PlayerActors[i].gameObject.SetActive(false);
			}
			m_ActorClientIds[i] = ulong.MaxValue;
		}

		if (m_BallActorInstance != null)
		{
			m_BallActorInstance.SetActive(false);
		}
	}

	private void ApplyFrameAtIndex(int frameIndex)
	{
		if (m_CurrentClip == null || frameIndex < 0 || frameIndex >= m_CurrentClip.FrameCount)
		{
			return;
		}

		ReplayFrameSlot frame = m_CurrentClip.GetFrame(frameIndex);
		ApplyInterpolatedFrame(frame, frame, 0f);
	}

	private void ApplyInterpolatedFrame(ReplayFrameSlot frameA, ReplayFrameSlot frameB, float t)
	{
		if (m_BallActorInstance != null && frameA.m_Ball.m_IsValid)
		{
			if (frameB.m_Ball.m_IsValid)
			{
				Vector3 diff = frameB.m_Ball.m_Position - frameA.m_Ball.m_Position;
				if (diff.sqrMagnitude > m_TeleportSnapDistanceSqr)
				{
					m_BallActorInstance.transform.SetPositionAndRotation(frameB.m_Ball.m_Position, frameB.m_Ball.m_Rotation);
				}
				else
				{
					m_BallActorInstance.transform.SetPositionAndRotation(
						Vector3.Lerp(frameA.m_Ball.m_Position, frameB.m_Ball.m_Position, t),
						Quaternion.Slerp(frameA.m_Ball.m_Rotation, frameB.m_Ball.m_Rotation, t));
				}
			}
			else
			{
				m_BallActorInstance.transform.SetPositionAndRotation(frameA.m_Ball.m_Position, frameA.m_Ball.m_Rotation);
			}

			bool showTrail = t < 0.5f ? frameA.m_Ball.m_IsShotInFlight : frameB.m_Ball.m_IsShotInFlight;
			SetBallTrailEnabled(showTrail);
		}

		for (int actorIndex = 0; actorIndex < m_PlayerActors.Length; actorIndex++)
		{
			LobbyPlayerDisplay actor = m_PlayerActors[actorIndex];
			ulong clientId = m_ActorClientIds[actorIndex];
			if (actor == null || clientId == ulong.MaxValue)
			{
				continue;
			}

			if (!TryFindPlayerPose(frameA, clientId, out ReplayPlayerPose poseA))
			{
				if (actor.gameObject.activeSelf)
				{
					actor.gameObject.SetActive(false);
				}
				continue;
			}

			if (!actor.gameObject.activeSelf)
			{
				actor.gameObject.SetActive(true);
			}

			if (TryFindPlayerPose(frameB, clientId, out ReplayPlayerPose poseB))
			{
				Vector3 diff = poseB.m_Position - poseA.m_Position;
				if (diff.sqrMagnitude > m_TeleportSnapDistanceSqr)
				{
					actor.transform.SetPositionAndRotation(poseB.m_Position, poseB.m_Rotation);
				}
				else
				{
					actor.transform.SetPositionAndRotation(
						Vector3.Lerp(poseA.m_Position, poseB.m_Position, t),
						Quaternion.Slerp(poseA.m_Rotation, poseB.m_Rotation, t));
				}

				bool isMoving = t < 0.5f ? poseA.m_IsMoving : poseB.m_IsMoving;
				actor.SetMovingAnimation(isMoving);
			}
			else
			{
				actor.transform.SetPositionAndRotation(poseA.m_Position, poseA.m_Rotation);
				actor.SetMovingAnimation(poseA.m_IsMoving);
			}
		}
	}

	private void SetBallTrailEnabled(bool enabled)
	{
		if (m_BallTrailBolts == null)
		{
			return;
		}

		for (int i = 0; i < m_BallTrailBolts.Length; i++)
		{
			if (m_BallTrailBolts[i] != null && m_BallTrailBolts[i].enabled != enabled)
			{
				m_BallTrailBolts[i].enabled = enabled;
			}
		}
	}

	private static bool TryFindPlayerPose(ReplayFrameSlot frame, ulong clientId, out ReplayPlayerPose pose)
	{
		int count = Mathf.Min(frame.m_PlayerCount, ReplayFrameSlot.m_MaxPlayers);
		for (int i = 0; i < count; i++)
		{
			if (frame.m_Players[i].m_IsActive && frame.m_Players[i].m_ClientId == clientId)
			{
				pose = frame.m_Players[i];
				return true;
			}
		}

		pose = default;
		return false;
	}

	private void HideRealWorldVisuals()
	{
		m_HiddenRenderers.Clear();
		m_HiddenCanvases.Clear();
		m_HiddenVisualBehaviours.Clear();

		PlayerNetworkProfile[] profiles = FindObjectsByType<PlayerNetworkProfile>(FindObjectsSortMode.None);
		for (int i = 0; i < profiles.Length; i++)
		{
			if (profiles[i] == null)
			{
				continue;
			}

			HideHierarchyVisuals(profiles[i].transform);
		}

		if (m_CachedRealBall == null)
		{
			m_CachedRealBall = FindFirstObjectByType<BallController>();
		}

		if (m_CachedRealBall != null)
		{
			HideHierarchyVisuals(m_CachedRealBall.transform);
		}
	}

	private void HideHierarchyVisuals(Transform root)
	{
		// 独自にLineRendererやParticleを出し入れするエフェクト制御は、コンポーネント単位で停止・復元します。
		PlayerRecoveryEffectController[] recoveryEffects = root.GetComponentsInChildren<PlayerRecoveryEffectController>(true);
		for (int i = 0; i < recoveryEffects.Length; i++)
		{
			PlayerRecoveryEffectController effect = recoveryEffects[i];
			if (effect != null && effect.enabled)
			{
				effect.StopAll();
				effect.enabled = false;
				m_HiddenVisualBehaviours.Add(effect);
			}
		}

		ALightningBolt[] bolts = root.GetComponentsInChildren<ALightningBolt>(true);
		for (int i = 0; i < bolts.Length; i++)
		{
			ALightningBolt bolt = bolts[i];
			if (bolt != null && bolt.enabled)
			{
				bolt.enabled = false;
				m_HiddenVisualBehaviours.Add(bolt);
			}
		}

		Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++)
		{
			Renderer renderer = renderers[i];
			if (renderer == null || !renderer.enabled)
			{
				continue;
			}

			if (renderer.GetComponentInParent<PlayerRecoveryEffectController>(true) != null ||
				renderer.GetComponentInParent<ALightningBolt>(true) != null)
			{
				continue;
			}

			renderer.enabled = false;
			m_HiddenRenderers.Add(renderer);
		}

		Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
		for (int i = 0; i < canvases.Length; i++)
		{
			Canvas canvas = canvases[i];
			if (canvas != null && canvas.enabled)
			{
				canvas.enabled = false;
				m_HiddenCanvases.Add(canvas);
			}
		}
	}

	private void TriggerReplayGoalEffect(ETeamType scoringTeam)
	{
		if (scoringTeam != ETeamType.Red && scoringTeam != ETeamType.Blue)
		{
			return;
		}

		EnsureGoalControllersCached();
		for (int i = 0; i < m_CachedGoalControllers.Length; i++)
		{
			GoalController goal = m_CachedGoalControllers[i];
			if (goal != null && goal.ScoringTeam == scoringTeam)
			{
				goal.PlayReplayGoalEffect();
			}
		}
	}

	private void StopReplayGoalEffects()
	{
		if (m_CachedGoalControllers == null)
		{
			return;
		}

		for (int i = 0; i < m_CachedGoalControllers.Length; i++)
		{
			if (m_CachedGoalControllers[i] != null)
			{
				m_CachedGoalControllers[i].StopReplayGoalEffect();
			}
		}
	}

	private void EnsureGoalControllersCached()
	{
		if (m_CachedGoalControllers == null || m_CachedGoalControllers.Length == 0)
		{
			m_CachedGoalControllers = FindObjectsByType<GoalController>(FindObjectsSortMode.None);
		}
	}

	private void RestoreRealWorldVisuals()
	{
		for (int i = 0; i < m_HiddenRenderers.Count; i++)
		{
			if (m_HiddenRenderers[i] != null)
			{
				m_HiddenRenderers[i].enabled = true;
			}
		}
		m_HiddenRenderers.Clear();

		for (int i = 0; i < m_HiddenCanvases.Count; i++)
		{
			if (m_HiddenCanvases[i] != null)
			{
				m_HiddenCanvases[i].enabled = true;
			}
		}
		m_HiddenCanvases.Clear();

		for (int i = 0; i < m_HiddenVisualBehaviours.Count; i++)
		{
			if (m_HiddenVisualBehaviours[i] != null)
			{
				m_HiddenVisualBehaviours[i].enabled = true;
			}
		}
		m_HiddenVisualBehaviours.Clear();
	}

	private void ActivateReplayCameras(ETeamType scoringTeam)
	{
		m_ActiveGoalAnchor = ResolveGoalAnchor(scoringTeam);
		m_CameraVelocity = Vector3.zero;
		m_LookAtVelocity = Vector3.zero;

		if (m_CachedSpectatorDirector == null)
		{
			m_CachedSpectatorDirector = FindFirstObjectByType<SpectatorCameraDirector>();
		}

		if (m_CachedSpectatorDirector != null)
		{
			m_CachedSpectatorDirector.SetReplayOverride(
				true,
				scoringTeam,
				m_BallActorInstance != null ? m_BallActorInstance.transform : null);
		}

		if (SpectatorDisplayManager.IsDisplay1Spectator)
		{
			m_ActiveDisplay1Camera = null;
			SetActorsBillboardCamera(SpectatorDisplayManager.ActiveSpectatorCamera);
			return;
		}

		Camera display1Camera = GetOrCreateDisplay1ReplayCamera();
		m_ActiveDisplay1Camera = display1Camera;

		Camera mainCam = Camera.main;
		if (mainCam != null && mainCam != display1Camera)
		{
			if (display1Camera != null)
			{
				display1Camera.cullingMask = mainCam.cullingMask;
			}

			if (mainCam.enabled)
			{
				m_DisabledMainCamera = mainCam;
				m_DisabledMainCamera.enabled = false;
			}
		}

		if (display1Camera != null)
		{
			display1Camera.targetDisplay = 0;
			display1Camera.fieldOfView = GetGoalCameraFov();
			display1Camera.gameObject.SetActive(true);
			display1Camera.enabled = true;

			if (m_ActiveGoalAnchor != null)
			{
				display1Camera.transform.position = m_ActiveGoalAnchor.position;
			}

			Vector3 initialLookAt = m_BallActorInstance != null ? m_BallActorInstance.transform.position : Vector3.zero;
			m_SmoothedLookAtPoint = initialLookAt;
			ApplyLookAtRotation(display1Camera, m_SmoothedLookAtPoint);
		}

		SetActorsBillboardCamera(display1Camera);
	}

	private void UpdateReplayCamera()
	{
		if (m_ActiveDisplay1Camera == null || !m_ActiveDisplay1Camera.enabled)
		{
			return;
		}

		if (m_ActiveGoalAnchor != null)
		{
			float posSmooth = m_CameraPositionSmoothTime > 0.001f
				? m_CameraPositionSmoothTime
				: m_DefaultCameraPositionSmoothTime;

			m_ActiveDisplay1Camera.transform.position = Vector3.SmoothDamp(
				m_ActiveDisplay1Camera.transform.position,
				m_ActiveGoalAnchor.position,
				ref m_CameraVelocity,
				posSmooth);
		}

		Vector3 targetLookAt = m_BallActorInstance != null ? m_BallActorInstance.transform.position : Vector3.zero;
		float lookSmooth = m_CameraLookAtSmoothTime > 0.001f
			? m_CameraLookAtSmoothTime
			: m_DefaultCameraLookAtSmoothTime;

		m_SmoothedLookAtPoint = Vector3.SmoothDamp(
			m_SmoothedLookAtPoint,
			targetLookAt,
			ref m_LookAtVelocity,
			lookSmooth);

		ApplyLookAtRotation(m_ActiveDisplay1Camera, m_SmoothedLookAtPoint);
	}

	private void RestoreCameras()
	{
		if (m_CachedSpectatorDirector != null)
		{
			m_CachedSpectatorDirector.SetReplayOverride(false, ETeamType.None, null);
		}

		if (m_ActiveDisplay1Camera != null)
		{
			m_ActiveDisplay1Camera.enabled = false;
			if (m_ActiveDisplay1Camera == m_AutoCreatedReplayCamera)
			{
				m_ActiveDisplay1Camera.gameObject.SetActive(false);
			}
			m_ActiveDisplay1Camera = null;
		}

		if (m_DisabledMainCamera != null)
		{
			m_DisabledMainCamera.enabled = true;
			m_DisabledMainCamera = null;
		}

		m_ActiveGoalAnchor = null;
	}

	private Camera GetOrCreateDisplay1ReplayCamera()
	{
		if (m_ReplayCamera != null)
		{
			return m_ReplayCamera;
		}

		if (m_AutoCreatedReplayCamera == null)
		{
			GameObject camObj = new GameObject("ReplayCamera");
			camObj.transform.SetParent(transform, false);
			m_AutoCreatedReplayCamera = camObj.AddComponent<Camera>();
			m_AutoCreatedReplayCamera.depth = 10f;
			m_AutoCreatedReplayCamera.nearClipPlane = 0.3f;
			m_AutoCreatedReplayCamera.farClipPlane = 1000f;
			m_AutoCreatedReplayCamera.targetDisplay = 0;
			camObj.SetActive(false);
		}

		return m_AutoCreatedReplayCamera;
	}

	private Transform ResolveGoalAnchor(ETeamType scoringTeam)
	{
		if (scoringTeam == ETeamType.Red && m_RedGoalAnchorOverride != null)
		{
			return m_RedGoalAnchorOverride;
		}

		if (scoringTeam == ETeamType.Blue && m_BlueGoalAnchorOverride != null)
		{
			return m_BlueGoalAnchorOverride;
		}

		if (m_CachedSpectatorDirector == null)
		{
			m_CachedSpectatorDirector = FindFirstObjectByType<SpectatorCameraDirector>();
		}

		if (m_CachedSpectatorDirector != null)
		{
			Transform directorAnchor = m_CachedSpectatorDirector.GetGoalAnchor(scoringTeam);
			if (directorAnchor != null)
			{
				return directorAnchor;
			}
		}

		EnsureGoalControllersCached();

		for (int i = 0; i < m_CachedGoalControllers.Length; i++)
		{
			GoalController goal = m_CachedGoalControllers[i];
			if (goal != null && goal.ScoringTeam == scoringTeam)
			{
				if (m_TempGoalAnchorObject == null)
				{
					m_TempGoalAnchorObject = new GameObject("ReplayFallbackGoalAnchor");
				}

				Vector3 anchorPos = goal.transform.position - (goal.transform.forward * 4.0f) + (Vector3.up * 4.0f);
				m_TempGoalAnchorObject.transform.position = anchorPos;
				m_TempGoalAnchorObject.transform.LookAt(goal.transform.position);
				return m_TempGoalAnchorObject.transform;
			}
		}

		return null;
	}

	private void SetActorsBillboardCamera(Camera cam)
	{
		for (int i = 0; i < m_PlayerActors.Length; i++)
		{
			if (m_PlayerActors[i] != null)
			{
				m_PlayerActors[i].SetBillboardCameraOverride(cam);
			}
		}
	}

	private float GetGoalCameraFov()
	{
		return m_GoalCameraFov > 1.0f ? m_GoalCameraFov : m_DefaultGoalCameraFov;
	}

	private static void ApplyLookAtRotation(Camera cam, Vector3 lookAtPoint)
	{
		if (cam == null)
		{
			return;
		}

		Vector3 lookDir = lookAtPoint - cam.transform.position;
		if (lookDir.sqrMagnitude > 0.001f)
		{
			cam.transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
		}
	}

	private static Color GetTeamDisplayColor(ETeamType team)
	{
		return team switch
		{
			ETeamType.Red => new Color(1.0f, 0.4f, 0.4f),
			ETeamType.Blue => new Color(0.4f, 0.6f, 1.0f),
			_ => Color.white
		};
	}
}
