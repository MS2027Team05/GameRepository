using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// エイムモード時のレティクル表示・軌道予測線描画を担当するモジュール。
/// </summary>
public class PlayerAimGuide : MonoBehaviour, IAimGuide
{
	[Header("照準UI")]
	[SerializeField] private GameObject m_AimReticle;

	[Header("予測線")]
	[SerializeField] private LineRenderer m_AimLine;

	[Header("着地マーカー")]
	[SerializeField] private GameObject m_LandingMarker;

	[Header("落下制御")]
	private IGravityMover m_GravityMover;
	private IGroundDetector m_GroundDetector;

	[Header("Raycast設定")]
	[SerializeField] private LayerMask m_AimTargetLayers;
	[SerializeField] private float m_MaxAimDistance = 100.0f;

	[Header("エイム浮上設定")]
	[SerializeField] private float m_AimFloatHeight = 1.0f;
	[SerializeField] private float m_AimFloatDuration = 0.5f; // 浮上にかける時間

	[Header("カメラ補正")]
	[SerializeField] private Transform m_CinemachineCameraTransform;
	[SerializeField] private Vector3 m_AimCameraShoulderOffset;

	[Header("エイム中の半透明化")]
	[SerializeField, Range(0.1f, 1.0f)]
	private float m_AimOpacity = 0.35f;

	// エイム状態
	private bool m_IsAiming;

	// 浮上アニメーション
	private Sequence m_AimFloatTween;

	// 元の位置へ戻るアニメーション中かどうか
	private bool m_IsReturning;

	// エイム開始時のPlayerの位置
	private Vector3 m_AimStartPosition;

	// エイム開始時の浮上方向
	private Vector3 m_AimFloatDirection;

	// エイム開始時のカメラのローカル位置
	private Vector3 m_AimStartCameraLocalPosition;

	// 半透明化するRendererと元のマテリアル
	private Renderer[] m_PlayerRenderers;
	private Material[][] m_OriginalMaterials;
	private Material[][] m_TransparentMaterials;

	// 予測線の終点
	private Vector3 m_AimTargetPoint;

	// 予測線に沿った実際の移動方向
	public Vector3 AimMoveDirection { get; private set; }

	// 移動中専用のエイムかどうか
	private bool m_IsMidFallAimGuide;

	private Rigidbody m_Rigidbody;

	private void Awake()
	{
		m_Rigidbody = GetComponent<Rigidbody>();
		m_GravityMover = GetComponent<IGravityMover>();
		m_GroundDetector = GetComponent<IGroundDetector>();

		// カメラの通常時のローカル位置を最初に保存する
		if (m_CinemachineCameraTransform != null)
		{
			m_AimStartCameraLocalPosition =
				m_CinemachineCameraTransform.localPosition;
		}

		// 予測線はワールド座標で描画する
		if (m_AimLine != null)
		{
			m_AimLine.useWorldSpace = true;
			m_AimLine.positionCount = 0;
		}

		// Playerの見た目用マテリアルを準備
		CachePlayerMaterials();

		SetAimGuideActive(false);
		HideLandingMarker();
	}

	/// <summary>
	/// Playerの見た目用マテリアルを保存し、半透明用の複製を作成します。
	/// </summary>
	private void CachePlayerMaterials()
	{
		Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
		List<Renderer> targetRenderers = new List<Renderer>();

		foreach (Renderer targetRenderer in allRenderers)
		{
			// 照準UI・予測線・着地マーカー・LineRenderer・テキスト用Rendererは対象外
			if (targetRenderer == null ||
				targetRenderer == m_AimLine ||
				targetRenderer is LineRenderer ||
				targetRenderer is TrailRenderer ||
				targetRenderer is ParticleSystemRenderer ||
				targetRenderer.GetComponent<TMPro.TMP_Text>() != null ||
				targetRenderer.GetComponent<TMPro.TMP_SubMeshUI>() != null ||
				IsChildOfObject(targetRenderer.transform, m_AimReticle) ||
				IsChildOfObject(targetRenderer.transform, m_LandingMarker))
			{
				continue;
			}

			targetRenderers.Add(targetRenderer);
		}

		m_PlayerRenderers = targetRenderers.ToArray();
		m_OriginalMaterials = new Material[m_PlayerRenderers.Length][];
		m_TransparentMaterials = new Material[m_PlayerRenderers.Length][];

		for (int i = 0; i < m_PlayerRenderers.Length; i++)
		{
			Material[] currentMaterials = m_PlayerRenderers[i].sharedMaterials;
			List<Material> baseMaterials = new List<Material>();

			for (int j = 0; j < currentMaterials.Length; j++)
			{
				Material mat = currentMaterials[j];
				if (mat == null) continue;

				// アウトライン・シルエット用シェーダーは半透明化の対象外とする
				if (mat.shader != null &&
					(mat.shader.name.Contains("PlayerOutline") ||
					 mat.shader.name.Contains("OccludedSilhouette") ||
					 mat.shader.name.Contains("ToolOccludedMask")))
				{
					continue;
				}

				baseMaterials.Add(mat);
			}

			// 元の本体マテリアルを保存
			m_OriginalMaterials[i] = baseMaterials.ToArray();

			// 元のマテリアルを変更しないように複製
			m_TransparentMaterials[i] = new Material[m_OriginalMaterials[i].Length];

			for (int j = 0; j < m_OriginalMaterials[i].Length; j++)
			{
				Material original = m_OriginalMaterials[i][j];

				if (original == null)
				{
					continue;
				}

				Material transparent = new Material(original);
				SetMaterialOpacity(transparent, m_AimOpacity);

				m_TransparentMaterials[i][j] = transparent;
			}
		}
	}

	/// <summary>
	/// 指定したオブジェクト自身または子に属するかを確認します。
	/// </summary>
	private bool IsChildOfObject(Transform target, GameObject parentObject)
	{
		return parentObject != null &&
			(target == parentObject.transform ||
			target.IsChildOf(parentObject.transform));
	}

	/// <summary>
	/// マテリアルを半透明表示に設定します。
	/// URP/Lit系マテリアルを想定しています。
	/// </summary>
	private void SetMaterialOpacity(Material material, float opacity)
	{
		// URPのSurface TypeをTransparentに設定
		if (material.HasProperty("_Surface"))
		{
			material.SetFloat("_Surface", 1.0f);
		}

		// Alphaブレンドに設定
		if (material.HasProperty("_Blend"))
		{
			material.SetFloat("_Blend", 0.0f);
		}

		if (material.HasProperty("_SrcBlend"))
		{
			material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
		}

		if (material.HasProperty("_DstBlend"))
		{
			material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
		}

		if (material.HasProperty("_ZWrite"))
		{
			material.SetFloat("_ZWrite", 0.0f);
		}

		if (material.HasProperty("_AlphaClip"))
		{
			material.SetFloat("_AlphaClip", 0.0f);
		}

		material.SetOverrideTag("RenderType", "Transparent");
		material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
		material.DisableKeyword("_ALPHATEST_ON");
		material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
		material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

		// 色のアルファ値を設定
		if (material.HasProperty("_BaseColor"))
		{
			Color color = material.GetColor("_BaseColor");
			color.a = opacity;
			material.SetColor("_BaseColor", color);
		}
		else if (material.HasProperty("_Color"))
		{
			Color color = material.GetColor("_Color");
			color.a = opacity;
			material.SetColor("_Color", color);
		}
	}

	/// <summary>
	/// Playerのマテリアルを半透明用に切り替えます。
	/// </summary>
	private void SetPlayerTransparent(bool transparent)
	{
		if (m_PlayerRenderers == null)
		{
			return;
		}

		var silhouette = GetComponent<PlayerSilhouette>();

		if (transparent)
		{
			// PlayerSilhouetteが存在する場合はアウトラインを合成して半透明化
			if (silhouette != null)
			{
				silhouette.ApplyAimTransparentState(true, m_PlayerRenderers, m_TransparentMaterials);
			}
			else
			{
				for (int i = 0; i < m_PlayerRenderers.Length; i++)
				{
					if (m_PlayerRenderers[i] == null)
					{
						continue;
					}

					m_PlayerRenderers[i].sharedMaterials = m_TransparentMaterials[i];
				}
			}
		}
		else
		{
			// エイム終了時: PlayerSilhouetteが存在する場合はエイム状態解除で完全復元
			if (silhouette != null)
			{
				silhouette.ApplyAimTransparentState(false, null, null);
			}
			else
			{
				for (int i = 0; i < m_PlayerRenderers.Length; i++)
				{
					if (m_PlayerRenderers[i] == null)
					{
						continue;
					}

					m_PlayerRenderers[i].sharedMaterials = m_OriginalMaterials[i];
				}
			}
		}
	}

	/// <summary>
	/// エイムガイドの表示・非表示を切り替えます。
	/// falseの場合は移動キャンセルとして扱います。
	/// </summary>
	public void ShowAimGuide(bool show)
	{
		if (show)
		{
			StartAimGuide();
		}
		else
		{
			// 通常のエイム解除は移動キャンセルとして扱う
			CancelAimGuide();
		}
	}

	/// <summary>
	/// エイムを開始します。
	/// </summary>
	private void StartAimGuide()
	{
		// 浮上中、または戻るアニメーション中は開始しない
		if (m_IsAiming || m_IsReturning)
		{
			return;
		}

		// Playerの浮上前の位置を保存
		m_AimStartPosition = transform.position;

		// 現在立っている面の法線方向を取得
		Vector3 groundNormal = m_GroundDetector != null
			? m_GroundDetector.CurrentGroundNormal
			: transform.up;

		// 法線が取得できない場合はPlayerの上方向を使用
		if (groundNormal.sqrMagnitude <= 0.0001f)
		{
			groundNormal = transform.up;
		}

		m_AimFloatDirection = groundNormal.normalized;

		// Playerの浮上量を計算
		Vector3 floatOffset =
			m_AimFloatDirection * m_AimFloatHeight;


		// カメラの補正先を計算する
		Vector3 targetCameraLocalPosition = m_AimStartCameraLocalPosition;

		if (m_CinemachineCameraTransform != null)
		{
			// カメラの親座標を基準に浮上量を変換する
			Transform cameraParent = m_CinemachineCameraTransform.parent;

			Vector3 localFloatOffset = cameraParent != null
				? cameraParent.InverseTransformVector(floatOffset)
				: floatOffset;

			// カメラ自身の右・上・前を基準に肩越しオフセットを計算する
			Vector3 shoulderWorldOffset =
				m_CinemachineCameraTransform.right * m_AimCameraShoulderOffset.x
				+ m_CinemachineCameraTransform.up * m_AimCameraShoulderOffset.y
				+ m_CinemachineCameraTransform.forward * m_AimCameraShoulderOffset.z;

			// ワールド方向のオフセットをカメラの親座標系へ変換する
			Vector3 localShoulderOffset = cameraParent != null
				? cameraParent.InverseTransformVector(shoulderWorldOffset)
				: shoulderWorldOffset;

			// 浮上分だけカメラを下げ、肩越し位置へ移動する
			targetCameraLocalPosition =
				m_AimStartCameraLocalPosition
				- localFloatOffset
				+ localShoulderOffset;
		}

		// エイム中の物理移動を停止
		m_GravityMover?.StartAimHover();

		// エイム開始時にPlayerを半透明化
		SetPlayerTransparent(true);

		m_IsAiming = true;

		// Playerとカメラを同時に滑らかに移動させる
		if (m_AimFloatTween != null && m_AimFloatTween.IsActive())
		{
			m_AimFloatTween.Kill();
		}

		m_AimFloatTween = DOTween.Sequence();
		m_AimFloatTween.Append(
			transform.DOMove(m_AimStartPosition + floatOffset,
				Mathf.Max(0.01f, m_AimFloatDuration))
			.SetEase(Ease.OutCubic));

		if (m_CinemachineCameraTransform != null)
		{
			m_AimFloatTween.Join(
				m_CinemachineCameraTransform
					.DOLocalMove(targetCameraLocalPosition,
						Mathf.Max(0.01f, m_AimFloatDuration))
					.SetEase(Ease.OutCubic));
		}

		m_AimFloatTween.OnUpdate(() =>
		{
			if (m_Rigidbody != null)
			{
				m_Rigidbody.position = transform.position;
			}
		});

		SetAimGuideActive(true);
		ClearAimLine();
	}

	/// <summary>
	/// 移動中専用のエイムを開始します。
	/// Playerを浮上させず、物理移動も停止しません。
	/// </summary>
	public void StartMidFallAimGuide()
	{
		// すでにエイム中、または戻るアニメーション中なら開始しない
		if (m_IsAiming || m_IsReturning)
		{
			return;
		}

		// 通常のエイムと区別する
		m_IsMidFallAimGuide = true;
		m_IsAiming = true;

		// 移動中エイム開始時のカメラ位置を保存
		if (m_CinemachineCameraTransform != null)
		{
			m_AimStartCameraLocalPosition =
				m_CinemachineCameraTransform.localPosition;

			// カメラの右・上・前方向を基準に肩越し位置を計算
			Vector3 shoulderWorldOffset =
				m_CinemachineCameraTransform.right * m_AimCameraShoulderOffset.x
				+ m_CinemachineCameraTransform.up * m_AimCameraShoulderOffset.y
				+ m_CinemachineCameraTransform.forward * m_AimCameraShoulderOffset.z;

			Transform cameraParent = m_CinemachineCameraTransform.parent;

			Vector3 localShoulderOffset = cameraParent != null
				? cameraParent.InverseTransformVector(shoulderWorldOffset)
				: shoulderWorldOffset;

			Vector3 targetCameraLocalPosition =
				m_AimStartCameraLocalPosition + localShoulderOffset;

			// カメラだけを肩越し位置へ移動
			m_CinemachineCameraTransform.DOLocalMove(
				targetCameraLocalPosition,
				Mathf.Max(0.01f, m_AimFloatDuration))
				.SetEase(Ease.OutCubic);
		}

		// 移動中はPlayerの透明化もしない
		SetAimGuideActive(true);
		ClearAimLine();
	}

	/// <summary>
	/// 移動キャンセル時のエイム解除です。
	/// Playerとカメラを滑らかに元の位置へ戻します。
	/// </summary>
	public void CancelAimGuide()
	{
		if (!m_IsAiming || m_IsReturning)
		{
			return;
		}

		// キャンセルした瞬間に予測線と着地先マーカーを消す
		ClearAimLine();

		// 浮上アニメーションを停止
		if (m_AimFloatTween != null && m_AimFloatTween.IsActive())
		{
			m_AimFloatTween.Kill();
			m_AimFloatTween = null;
		}

		// 戻るアニメーション中にする
		m_IsReturning = true;

		// 現在位置から元の位置へ滑らかに戻す
		float returnDuration = Mathf.Max(0.01f, m_AimFloatDuration);

		m_AimFloatTween = DOTween.Sequence();

		// Playerを浮上前の位置へ戻す
		m_AimFloatTween.Append(
			transform.DOMove(m_AimStartPosition, returnDuration)
				.SetEase(Ease.InOutCubic));

		// カメラも通常時の位置へ同時に戻す
		if (m_CinemachineCameraTransform != null)
		{
			m_AimFloatTween.Join(
				m_CinemachineCameraTransform
					.DOLocalMove(
						m_AimStartCameraLocalPosition,
						returnDuration)
					.SetEase(Ease.InOutCubic));
		}

		m_AimFloatTween.OnUpdate(() =>
		{
			if (m_Rigidbody != null)
			{
				m_Rigidbody.position = transform.position;
			}
		});

		// 戻り終わってからエイムを解除する
		m_AimFloatTween.OnComplete(() =>
		{
			m_AimFloatTween = null;
			m_IsReturning = false;

			FinishAimGuide();
		});
	}

	/// <summary>
	/// 移動によるエイム解除です。
	/// Playerの現在位置を維持し、カメラだけを元の位置に戻します。
	/// </summary>
	public void EndAimGuideForMovement()
	{
		if (!m_IsAiming)
		{
			return;
		}

		// 左クリック時は浮上アニメーションを最後まで完了させてから移動へ移る
		if (m_AimFloatTween != null && m_AimFloatTween.IsActive())
		{
			m_AimFloatTween.Complete();
			m_AimFloatTween = null;
		}

		// Playerの位置は変更しない
		// カメラだけをPlayerの子としての通常位置に戻す
		RestoreCameraPosition();

		FinishAimGuide();
	}

	/// <summary>
	/// カメラのローカル位置を復元します。
	/// </summary>
	private void RestoreCameraPosition()
	{
		if (m_CinemachineCameraTransform != null)
		{
			m_CinemachineCameraTransform.localPosition =
				m_AimStartCameraLocalPosition;
		}
	}

	/// <summary>
	/// エイム終了時の共通処理です。
	/// </summary>
	private void FinishAimGuide()
	{
		// エイム中の物理移動停止を解除
		m_GravityMover?.StopAimHover();

		// 戻るアニメーション状態を解除
		m_IsReturning = false;

		// エイム終了時にPlayerの元のマテリアルへ戻す
		SetPlayerTransparent(false);

		m_IsAiming = false;

		SetAimGuideActive(false);
		ClearAimLine();
	}

	/// <summary>
	/// 移動中専用のエイムを終了します。
	/// 移動を停止せず、カメラだけを通常位置へ戻します。
	/// </summary>
	public void EndMidFallAimGuide()
	{
		if (!m_IsMidFallAimGuide)
		{
			return;
		}

		// カメラの移動アニメーションがあれば停止
		if (m_CinemachineCameraTransform != null)
		{
			m_CinemachineCameraTransform.DOKill();

			// カメラだけを通常位置へ戻す
			m_CinemachineCameraTransform.localPosition =
				m_AimStartCameraLocalPosition;
		}

		// エイム状態を解除
		m_IsMidFallAimGuide = false;
		m_IsAiming = false;
		m_IsReturning = false;

		// 予測線・マーカーを消す
		SetAimGuideActive(false);
		ClearAimLine();
	}

	/// <summary>
	/// 場外復帰時にエイムの演出と内部状態を即時解除します。
	/// プレイヤー座標は復帰処理側で設定するため、このメソッドでは変更しません。
	/// </summary>
	public void ResetAimGuideForRespawn()
	{
		if (m_AimFloatTween != null && m_AimFloatTween.IsActive())
		{
			m_AimFloatTween.Kill();
		}

		m_AimFloatTween = null;

		if (m_CinemachineCameraTransform != null)
		{
			m_CinemachineCameraTransform.DOKill();
			RestoreCameraPosition();
		}

		m_GravityMover?.StopAimHover();
		SetPlayerTransparent(false);

		m_IsAiming = false;
		m_IsReturning = false;
		m_IsMidFallAimGuide = false;
		AimMoveDirection = Vector3.zero;
		m_AimTargetPoint = Vector3.zero;

		SetAimGuideActive(false);
		ClearAimLine();
	}

	/// <summary>
	/// カメラ中央からRaycastし、予測線と着地マーカーを更新します。
	/// </summary>
	public void UpdateAimDirection(Vector3 direction)
	{
		if (!m_IsAiming || m_IsReturning)
		{
			ClearAimLine();
			return;
		}

		Camera mainCamera = Camera.main;

		Vector3 rayOrigin;
		Vector3 rayDirection;

		if (mainCamera != null)
		{
			// 実際に画面を描画しているカメラを使用
			rayOrigin = mainCamera.transform.position;
			rayDirection = mainCamera.transform.forward;
		}
		else
		{
			rayOrigin = m_CinemachineCameraTransform != null
				? m_CinemachineCameraTransform.position
				: transform.position;

			rayDirection = direction;
		}

		if (rayDirection.sqrMagnitude <= 0.0001f)
		{
			ClearAimLine();
			return;
		}

		rayDirection.Normalize();

		// 1. カメラ中央からRaycast
		bool hasHit = Physics.Raycast(
			rayOrigin,
			rayDirection,
			out RaycastHit hit,
			m_MaxAimDistance,
			m_AimTargetLayers,
			QueryTriggerInteraction.Ignore);

		if (!hasHit)
		{
			// 着地点がない場合は予測線とマーカーを非表示
			ClearAimLine();
			return;
		}

		Vector3 targetPoint = hit.point;
		Vector3 targetNormal = hit.normal;

		// 2. 角(エッジ)のフチを狙った際、足元が安全に乗るように面の内側へ安全インセット
		// 接平面上で、カメラ視線の射影方向(面の奥側)に向かって安全マージン分(0.35m)シフトを試みる
		Vector3 forwardOnPlane = Vector3.ProjectOnPlane(rayDirection, targetNormal);
		if (forwardOnPlane.sqrMagnitude > 0.001f)
		{
			forwardOnPlane.Normalize();
			Vector3 testPoint = targetPoint + forwardOnPlane * 0.35f;
			// シフト先にも同じ面(地面)が存在するかチェック
			if (Physics.Raycast(testPoint + targetNormal * 0.5f, -targetNormal, out RaycastHit insetHit, 1.0f, m_AimTargetLayers, QueryTriggerInteraction.Ignore))
			{
				if (Vector3.Dot(insetHit.normal, targetNormal) > 0.8f)
				{
					// 安全な面の内側が確認できた場合、着地点を面の内側に補正
					targetPoint = insetHit.point;
					targetNormal = insetHit.normal;
				}
			}
		}

		// 3. プレイヤー本体から目標地点への直進ライン上に手前の角が干渉していないか検証
		Vector3 playerPos = transform.position;
		Vector3 toTarget = targetPoint - playerPos;
		float targetDist = toTarget.magnitude;
		if (targetDist > 0.2f)
		{
			Vector3 flyDir = toTarget / targetDist;
			// プレイヤーの足元・カプセル厚み(0.25m半径)を考慮したSphereCastで手前角の干渉をチェック
			if (Physics.SphereCast(playerPos, 0.25f, flyDir, out RaycastHit obstacleHit, targetDist - 0.1f, m_AimTargetLayers, QueryTriggerInteraction.Ignore))
			{
				// 手前の角や側面に引っかかる場合、手前の接触面を実際の着地点として適用
				targetPoint = obstacleHit.point;
				targetNormal = obstacleHit.normal;
			}
		}

		// 予測線の終点へ向かう方向を計算
		m_AimTargetPoint = targetPoint;

		Vector3 moveDirection = m_AimTargetPoint - transform.position;

		if (moveDirection.sqrMagnitude > 0.001f)
		{
			AimMoveDirection = moveDirection.normalized;
		}

		// Playerの位置から着地点まで予測線を描画
		UpdateAimLine(transform.position, targetPoint);

		// 着地点にマーカーを表示
		UpdateLandingMarker(targetPoint, targetNormal);

		// 着地点と法線をGravityMoverへ渡す
		if (m_GravityMover != null)
		{
			m_GravityMover.SetFallingTarget(targetPoint, targetNormal);
		}
	}

	/// <summary>
	/// 照準UI・予測線・マーカーの表示を切り替えます。
	/// </summary>
	private void SetAimGuideActive(bool active)
	{
		if (m_AimReticle != null)
		{
			m_AimReticle.SetActive(active);
		}

		if (m_AimLine != null)
		{
			m_AimLine.enabled = active;
		}

		if (m_LandingMarker != null)
		{
			m_LandingMarker.SetActive(active);
		}
	}

	/// <summary>
	/// ワールド座標で予測線を描画します。
	/// </summary>
	private void UpdateAimLine(
		Vector3 startPoint,
		Vector3 endPoint)
	{
		if (m_AimLine == null)
		{
			return;
		}

		m_AimLine.useWorldSpace = true;
		m_AimLine.positionCount = 2;
		m_AimLine.SetPosition(0, startPoint);
		m_AimLine.SetPosition(1, endPoint);
		m_AimLine.enabled = true;
	}

	/// <summary>
	/// 着地点にマーカーを表示し、面の法線に合わせます。
	/// </summary>
	private void UpdateLandingMarker(
		Vector3 position,
		Vector3 normal)
	{
		if (m_LandingMarker == null)
		{
			return;
		}

		const float markerOffset = 0.06f;

		m_LandingMarker.transform.position =
			position + normal * markerOffset;

		m_LandingMarker.transform.rotation =
			Quaternion.FromToRotation(Vector3.up, normal);

		m_LandingMarker.SetActive(true);
	}

	/// <summary>
	/// 着地マーカーを非表示にします。
	/// </summary>
	private void HideLandingMarker()
	{
		if (m_LandingMarker != null)
		{
			m_LandingMarker.SetActive(false);
		}
	}

	/// <summary>
	/// 予測線と着地マーカーを消去します。
	/// </summary>
	private void ClearAimLine()
	{
		if (m_AimLine != null)
		{
			m_AimLine.positionCount = 0;
		}

		HideLandingMarker();
	}

	private void OnDestroy()
	{
		// 浮上アニメーションを停止
		if (m_AimFloatTween != null && m_AimFloatTween.IsActive())
		{
			m_AimFloatTween.Kill();
		}

		// 実行時に生成した半透明用マテリアルを破棄
		if (m_TransparentMaterials == null)
		{
			return;
		}

		foreach (Material[] materials in m_TransparentMaterials)
		{
			foreach (Material material in materials)
			{
				if (material != null)
				{
					Destroy(material);
				}
			}
		}
	}
}
