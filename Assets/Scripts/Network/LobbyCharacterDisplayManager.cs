using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// ロビー画面において、各チームの台座(ペデスタル)上にキャラクター3Dモデルを表示・同期制御するマネージャークラス。
/// ネットワークスポーンを行わず、各クライアントがローカルでLobbyPlayersの同期状態を監視して描画を更新します。
/// </summary>
public class LobbyCharacterDisplayManager : MonoBehaviour
{
	[Serializable]
	public class PedestalSlot
	{
		[Header("配置・トランスフォーム情報")]
		[Tooltip("キャラクターモデルの基準位置(台座のTransform)")]
		[SerializeField] private Transform m_SpawnPoint;

		[Tooltip("台座Transformからの位置オフセット(例: Y+0.5 で台座天面に接地)")]
		[SerializeField] private Vector3 m_PositionOffset = Vector3.zero;

		[Tooltip("台座Transformからの回転オフセット(例: Y180 で手前のカメラに向ける)")]
		[SerializeField] private Vector3 m_RotationOffset = Vector3.zero;

		[Header("台座演出設定(任意)")]
		[Tooltip("台座オブジェクトのRenderer。プレイヤーの着席状態(空席/着席)に応じた点灯・発光演出に使用します")]
		[SerializeField] private Renderer m_PedestalRenderer;

		[Tooltip("プレイヤー着席時に台座へ適用するマテリアル(未設定時はマテリアル変更をスキップ)")]
		[SerializeField] private Material m_OccupiedPedestalMaterial;

		[Tooltip("空席時に台座へ適用するマテリアル(未設定時はマテリアル変更をスキップ)")]
		[SerializeField] private Material m_EmptyPedestalMaterial;

		// 動的生成されたインスタンスキャッシュ
		private GameObject m_InstantiatedModelA;
		private GameObject m_InstantiatedModelB;
		private Renderer[] m_ModelARenderers;
		private Renderer[] m_ModelBRenderers;
		private MaterialPropertyBlock m_ModelPropertyBlock;
		private MaterialPropertyBlock m_PedestalPropertyBlock;

		public Transform SpawnPoint { get => m_SpawnPoint; }
		public Renderer PedestalRenderer { get => m_PedestalRenderer; }

		/// <summary>
		/// スロットのモデル表示状態および台座演出を更新します。
		/// </summary>
		public void UpdateDisplay(
			bool isOccupied,
			LobbyPlayerData playerData,
			GameObject modelAPrefab,
			GameObject modelBPrefab,
			Material modelAMat,
			Material modelBMat,
			Color fallbackColor)
		{
			if (m_ModelPropertyBlock == null) m_ModelPropertyBlock = new MaterialPropertyBlock();
			if (m_PedestalPropertyBlock == null) m_PedestalPropertyBlock = new MaterialPropertyBlock();

			// 1. 台座の演出更新 (着席時点灯 / 空席時消灯)
			UpdatePedestalVisual(isOccupied, fallbackColor);

			// 2. 空席の場合は両モデルを非表示
			if (!isOccupied)
			{
				SetModelActive(ECharacterType.ModelA, false, modelAPrefab);
				SetModelActive(ECharacterType.ModelB, false, modelBPrefab);
				return;
			}

			// 3. キャラクターモデルの切り替え表示
			bool showModelA = playerData.CharacterType == ECharacterType.ModelA;
			SetModelActive(ECharacterType.ModelA, showModelA, modelAPrefab);
			SetModelActive(ECharacterType.ModelB, !showModelA, modelBPrefab);

			// 4. サブカラー(マテリアルまたはプロパティブロック)の適用
			ApplyHighlight(
				showModelA ? ECharacterType.ModelA : ECharacterType.ModelB,
				showModelA ? modelAMat : modelBMat,
				fallbackColor);
		}

		/// <summary>
		/// 台座オブジェクトの着席/空席ビジュアル演出を更新します。
		/// </summary>
		private void UpdatePedestalVisual(bool isOccupied, Color highlightColor)
		{
			if (m_PedestalRenderer == null) return;

			// 専用マテリアルが設定されている場合はマテリアルを差し替え
			if (isOccupied && m_OccupiedPedestalMaterial != null)
			{
				m_PedestalRenderer.sharedMaterial = m_OccupiedPedestalMaterial;
			}
			else if (!isOccupied && m_EmptyPedestalMaterial != null)
			{
				m_PedestalRenderer.sharedMaterial = m_EmptyPedestalMaterial;
			}

			// マテリアルプロパティによる発光・ハイライト演出
			m_PedestalRenderer.GetPropertyBlock(m_PedestalPropertyBlock);
			if (isOccupied)
			{
				m_PedestalPropertyBlock.SetColor("_EmissionColor", highlightColor);
				m_PedestalPropertyBlock.SetFloat("_IsOccupied", 1f);
			}
			else
			{
				m_PedestalPropertyBlock.SetColor("_EmissionColor", Color.black);
				m_PedestalPropertyBlock.SetFloat("_IsOccupied", 0f);
			}
			m_PedestalRenderer.SetPropertyBlock(m_PedestalPropertyBlock);
		}

		private void SetModelActive(ECharacterType type, bool active, GameObject prefab)
		{
			GameObject target = GetOrInstantiateModel(type, prefab);
			if (target != null && target.activeSelf != active)
			{
				target.SetActive(active);
			}
		}

		private GameObject GetOrInstantiateModel(ECharacterType type, GameObject prefab)
		{
			if (m_SpawnPoint == null) return null;

			Vector3 targetPos = m_SpawnPoint.position + m_SpawnPoint.TransformDirection(m_PositionOffset);
			Quaternion targetRot = m_SpawnPoint.rotation * Quaternion.Euler(m_RotationOffset);

			if (type == ECharacterType.ModelA)
			{
				if (m_InstantiatedModelA == null && prefab != null)
				{
					m_InstantiatedModelA = Instantiate(prefab, targetPos, targetRot, m_SpawnPoint);
					m_ModelARenderers = m_InstantiatedModelA.GetComponentsInChildren<Renderer>(true);
				}
				return m_InstantiatedModelA;
			}
			else
			{
				if (m_InstantiatedModelB == null && prefab != null)
				{
					m_InstantiatedModelB = Instantiate(prefab, targetPos, targetRot, m_SpawnPoint);
					m_ModelBRenderers = m_InstantiatedModelB.GetComponentsInChildren<Renderer>(true);
				}
				return m_InstantiatedModelB;
			}
		}

		private void ApplyHighlight(ECharacterType activeType, Material dedicatedMaterial, Color fallbackColor)
		{
			Renderer[] renderers = activeType == ECharacterType.ModelA ? m_ModelARenderers : m_ModelBRenderers;

			if (renderers == null) return;

			for (int i = 0; i < renderers.Length; i++)
			{
				if (renderers[i] == null) continue;

				// 専用マテリアルが登録されている場合はマテリアルを差し替え
				if (dedicatedMaterial != null)
				{
					renderers[i].sharedMaterial = dedicatedMaterial;
				}
				else
				{
					// テクスチャ/マテリアル未登録時はプロパティブロックでカラーを反映(フォールバック)
					renderers[i].GetPropertyBlock(m_ModelPropertyBlock);
					m_ModelPropertyBlock.SetColor("_HighlightColor", fallbackColor);
					m_ModelPropertyBlock.SetColor("_BaseColor", fallbackColor);
					renderers[i].SetPropertyBlock(m_ModelPropertyBlock);
				}
			}
		}

		public void Cleanup()
		{
			if (m_InstantiatedModelA != null)
			{
				Destroy(m_InstantiatedModelA);
				m_InstantiatedModelA = null;
			}
			if (m_InstantiatedModelB != null)
			{
				Destroy(m_InstantiatedModelB);
				m_InstantiatedModelB = null;
			}
		}
	}

	[Serializable]
	public struct HighlightColorMaterialMapping
	{
		[Tooltip("対象のカラー種別")]
		[FormerlySerializedAs("ColorType")]
		public EHighlightColor m_ColorType;

		[Tooltip("ModelAに適用する専用マテリアル(未設定時はフォールバックカラーを使用)")]
		[FormerlySerializedAs("MaterialModelA")]
		public Material m_MaterialModelA;

		[Tooltip("ModelBに適用する専用マテリアル(未設定時はフォールバックカラーを使用)")]
		[FormerlySerializedAs("MaterialModelB")]
		public Material m_MaterialModelB;
	}

	[Header("マネージャー参照")]
	[SerializeField] private LobbyManager m_LobbyManager;

	[Header("キャラクタープレハブ(動的生成時用)")]
	[SerializeField] private GameObject m_ModelAPrefab;
	[SerializeField] private GameObject m_ModelBPrefab;

	[Header("チームRed台座スロット(最大2枠)")]
	[SerializeField] private PedestalSlot[] m_RedSlots = new PedestalSlot[LobbyManager.MaxTeamMembers];

	[Header("チームBlue台座スロット(最大2枠)")]
	[SerializeField] private PedestalSlot[] m_BlueSlots = new PedestalSlot[LobbyManager.MaxTeamMembers];

	[Header("サブカラー別マテリアル設定")]
	[Tooltip("各色に対応するマテリアル登録枠。未登録時はフォールバックカラーが反映されます")]
	[SerializeField] private HighlightColorMaterialMapping[] m_HighlightMaterials;

	[Header("フォールバック用カラー定義")]
	[SerializeField] private Color m_DefaultColor = Color.white;
	[SerializeField] private Color m_CyanColor = Color.cyan;
	[SerializeField] private Color m_LimeColor = Color.green;
	[SerializeField] private Color m_YellowColor = Color.yellow;
	[SerializeField] private Color m_PurpleColor = new Color(0.7f, 0.2f, 1f);
	[SerializeField] private Color m_OrangeColor = new Color(1f, 0.5f, 0f);

	private readonly List<LobbyPlayerData> m_CachedRedPlayers = new List<LobbyPlayerData>();
	private readonly List<LobbyPlayerData> m_CachedBluePlayers = new List<LobbyPlayerData>();

	private void Awake()
	{
		if (m_LobbyManager == null)
		{
			m_LobbyManager = FindFirstObjectByType<LobbyManager>();
		}
	}

	private void Start()
	{
		if (m_LobbyManager != null)
		{
			m_LobbyManager.OnLobbyDataChanged += RefreshAllPedestals;
		}

		RefreshAllPedestals();
	}

	private void OnDestroy()
	{
		if (m_LobbyManager != null)
		{
			m_LobbyManager.OnLobbyDataChanged -= RefreshAllPedestals;
		}

		CleanupAllSlots();
	}

	/// <summary>
	/// ロビー参加者データに基づいてすべての台座スロットの表示を更新します。
	/// </summary>
	public void RefreshAllPedestals()
	{
		if (m_LobbyManager == null || NetworkManager.Singleton == null) return;

		m_CachedRedPlayers.Clear();
		m_CachedBluePlayers.Clear();

		var players = m_LobbyManager.LobbyPlayers;
		for (int i = 0; i < players.Count; i++)
		{
			var p = players[i];
			if (p.Team == ETeamType.Red)
			{
				m_CachedRedPlayers.Add(p);
			}
			else if (p.Team == ETeamType.Blue)
			{
				m_CachedBluePlayers.Add(p);
			}
		}

		// Redスロットの更新
		for (int i = 0; i < m_RedSlots.Length; i++)
		{
			if (m_RedSlots[i] == null) continue;

			bool isOccupied = i < m_CachedRedPlayers.Count;
			LobbyPlayerData data = isOccupied ? m_CachedRedPlayers[i] : default;
			GetMaterialsForColor(data.HighlightColor, out Material matA, out Material matB);
			Color fallbackColor = isOccupied ? GetColor(data.HighlightColor) : Color.white;

			m_RedSlots[i].UpdateDisplay(isOccupied, data, m_ModelAPrefab, m_ModelBPrefab, matA, matB, fallbackColor);
		}

		// Blueスロットの更新
		for (int i = 0; i < m_BlueSlots.Length; i++)
		{
			if (m_BlueSlots[i] == null) continue;

			bool isOccupied = i < m_CachedBluePlayers.Count;
			LobbyPlayerData data = isOccupied ? m_CachedBluePlayers[i] : default;
			GetMaterialsForColor(data.HighlightColor, out Material matA, out Material matB);
			Color fallbackColor = isOccupied ? GetColor(data.HighlightColor) : Color.white;

			m_BlueSlots[i].UpdateDisplay(isOccupied, data, m_ModelAPrefab, m_ModelBPrefab, matA, matB, fallbackColor);
		}
	}

	private void GetMaterialsForColor(EHighlightColor colorType, out Material matA, out Material matB)
	{
		matA = null;
		matB = null;

		if (m_HighlightMaterials == null) return;

		for (int i = 0; i < m_HighlightMaterials.Length; i++)
		{
			if (m_HighlightMaterials[i].m_ColorType == colorType)
			{
				matA = m_HighlightMaterials[i].m_MaterialModelA;
				matB = m_HighlightMaterials[i].m_MaterialModelB;
				return;
			}
		}
	}

	/// <summary>
	/// EHighlightColorに対応するフォールバックColorを取得します。
	/// </summary>
	public Color GetColor(EHighlightColor highlightColor)
	{
		switch (highlightColor)
		{
			case EHighlightColor.Cyan:
				return m_CyanColor;
			case EHighlightColor.Lime:
				return m_LimeColor;
			case EHighlightColor.Yellow:
				return m_YellowColor;
			case EHighlightColor.Purple:
				return m_PurpleColor;
			case EHighlightColor.Orange:
				return m_OrangeColor;
			case EHighlightColor.Default:
			default:
				return m_DefaultColor;
		}
	}

	private void CleanupAllSlots()
	{
		if (m_RedSlots != null)
		{
			for (int i = 0; i < m_RedSlots.Length; i++)
			{
				m_RedSlots[i]?.Cleanup();
			}
		}

		if (m_BlueSlots != null)
		{
			for (int i = 0; i < m_BlueSlots.Length; i++)
			{
				m_BlueSlots[i]?.Cleanup();
			}
		}
	}
}
