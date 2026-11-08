using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ロビー画面において、各チームの台座(ペデスタル)上に表示特化キャラクターPrefabを配置・同期制御するマネージャークラス。
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

		[Tooltip("台座Transformからの位置オフセット(例: Y+0.1 で台座天面に接地)")]
		[SerializeField] private Vector3 m_PositionOffset;

		[Tooltip("台座Transformからの回転オフセット(例: Y180 で手前のカメラに向ける)")]
		[SerializeField] private Vector3 m_RotationOffset;

		[Header("台座演出設定(任意)")]
		[Tooltip("台座オブジェクトのRenderer。プレイヤーの着席状態(空席/着席)に応じた点灯・発光演出に使用します")]
		[SerializeField] private Renderer m_PedestalRenderer;

		[Tooltip("プレイヤー着席時に台座へ適用するマテリアル(未設定時はマテリアル変更をスキップ)")]
		[SerializeField] private Material m_OccupiedPedestalMaterial;

		[Tooltip("空席時に台座へ適用するマテリアル(未設定時はマテリアル変更をスキップ)")]
		[SerializeField] private Material m_EmptyPedestalMaterial;

		private LobbyPlayerDisplay m_DisplayInstance;
		private MaterialPropertyBlock m_PedestalPropertyBlock;

		public Transform SpawnPoint { get => m_SpawnPoint; }
		public Renderer PedestalRenderer { get => m_PedestalRenderer; }

		/// <summary>
		/// キャラクターモデルが配置される目標ワールド座標を取得します。
		/// </summary>
		public Vector3 GetTargetWorldPosition()
		{
			if (m_SpawnPoint == null)
			{
				return Vector3.zero;
			}

			return m_SpawnPoint.position + m_SpawnPoint.TransformDirection(m_PositionOffset);
		}

		/// <summary>
		/// スロットのキャラクター表示状態・頭上ネームおよび台座演出を更新します。
		/// </summary>
		public void UpdateDisplay(
			bool isOccupied,
			LobbyPlayerData playerData,
			LobbyPlayerDisplay displayPrefab,
			Color pedestalEmissionColor,
			Color nameTextColor,
			Transform parentTransform = null)
		{
			if (m_PedestalPropertyBlock == null)
			{
				m_PedestalPropertyBlock = new MaterialPropertyBlock();
			}

			UpdatePedestalVisual(isOccupied, pedestalEmissionColor);

			if (!isOccupied)
			{
				if (m_DisplayInstance != null && m_DisplayInstance.gameObject.activeSelf)
				{
					m_DisplayInstance.gameObject.SetActive(false);
				}
				return;
			}

			LobbyPlayerDisplay display = GetOrInstantiateDisplay(displayPrefab, parentTransform);
			if (display == null)
			{
				return;
			}

			if (!display.gameObject.activeSelf)
			{
				display.gameObject.SetActive(true);
			}

			display.ApplyPlayerData(playerData, nameTextColor);
		}

		/// <summary>
		/// 台座オブジェクトの着席/空席ビジュアル演出を更新します。
		/// </summary>
		private void UpdatePedestalVisual(bool isOccupied, Color emissionColor)
		{
			if (m_PedestalRenderer == null)
			{
				return;
			}

			if (isOccupied && m_OccupiedPedestalMaterial != null)
			{
				m_PedestalRenderer.sharedMaterial = m_OccupiedPedestalMaterial;
			}
			else if (!isOccupied && m_EmptyPedestalMaterial != null)
			{
				m_PedestalRenderer.sharedMaterial = m_EmptyPedestalMaterial;
			}

			m_PedestalRenderer.GetPropertyBlock(m_PedestalPropertyBlock);
			if (isOccupied)
			{
				m_PedestalPropertyBlock.SetColor("_EmissionColor", emissionColor);
				m_PedestalPropertyBlock.SetFloat("_IsOccupied", 1f);
			}
			else
			{
				m_PedestalPropertyBlock.SetColor("_EmissionColor", Color.black);
				m_PedestalPropertyBlock.SetFloat("_IsOccupied", 0f);
			}
			m_PedestalRenderer.SetPropertyBlock(m_PedestalPropertyBlock);
		}

		/// <summary>
		/// スロットに紐づくキャラクター表示用Prefabインスタンスを取得または生成します。
		/// 親TransformとしてCharacterStand(localScale 1,1,1)を指定し、非均等スケールによる歪みを防止します。
		/// </summary>
		private LobbyPlayerDisplay GetOrInstantiateDisplay(LobbyPlayerDisplay displayPrefab, Transform parentTransform)
		{
			if (m_SpawnPoint == null)
			{
				return null;
			}

			if (m_DisplayInstance == null && displayPrefab != null)
			{
				Vector3 targetPos = m_SpawnPoint.position + m_SpawnPoint.TransformDirection(m_PositionOffset);
				Quaternion targetRot = m_SpawnPoint.rotation * Quaternion.Euler(m_RotationOffset);
				Transform parent = parentTransform != null ? parentTransform : m_SpawnPoint;
				m_DisplayInstance = Instantiate(displayPrefab, targetPos, targetRot, parent);
			}

			return m_DisplayInstance;
		}

		public void Cleanup()
		{
			if (m_DisplayInstance != null)
			{
				Destroy(m_DisplayInstance.gameObject);
				m_DisplayInstance = null;
			}
		}
	}

	[Header("マネージャー・カタログ参照")]
	[SerializeField] private LobbyManager m_LobbyManager;
	[SerializeField] private PlayerAppearanceCatalog m_AppearanceCatalog;

	[Header("ロビー表示専用プレハブ")]
	[SerializeField] private LobbyPlayerDisplay m_DisplayPlayerPrefab;

	[Header("チームRed台座スロット(最大2枠)")]
	[SerializeField] private PedestalSlot[] m_RedSlots;

	[Header("チームBlue台座スロット(最大2枠)")]
	[SerializeField] private PedestalSlot[] m_BlueSlots;

	private readonly List<LobbyPlayerData> m_CachedRedPlayers = new List<LobbyPlayerData>();
	private readonly List<LobbyPlayerData> m_CachedBluePlayers = new List<LobbyPlayerData>();

	private void Awake()
	{
		if (m_LobbyManager == null)
		{
			m_LobbyManager = FindFirstObjectByType<LobbyManager>();
		}
	}

	private void OnEnable()
	{
		if (m_LobbyManager != null)
		{
			m_LobbyManager.OnLobbyDataChanged += RefreshAllPedestals;
		}

		RefreshAllPedestals();
	}

	private void OnDisable()
	{
		if (m_LobbyManager != null)
		{
			m_LobbyManager.OnLobbyDataChanged -= RefreshAllPedestals;
		}
	}

	private void OnDestroy()
	{
		CleanupAllSlots();
	}

	/// <summary>
	/// ロビー参加者データに基づいてすべての台座スロットの表示を更新します。
	/// </summary>
	public void RefreshAllPedestals()
	{
		if (m_LobbyManager == null || NetworkManager.Singleton == null)
		{
			return;
		}

		m_CachedRedPlayers.Clear();
		m_CachedBluePlayers.Clear();

		NetworkList<LobbyPlayerData> players = m_LobbyManager.LobbyPlayers;
		for (int i = 0; i < players.Count; i++)
		{
			LobbyPlayerData p = players[i];
			if (p.Team == ETeamType.Red)
			{
				m_CachedRedPlayers.Add(p);
			}
			else if (p.Team == ETeamType.Blue)
			{
				m_CachedBluePlayers.Add(p);
			}
		}

		UpdateTeamSlots(m_RedSlots, m_CachedRedPlayers, ETeamType.Red);
		UpdateTeamSlots(m_BlueSlots, m_CachedBluePlayers, ETeamType.Blue);
	}

	private void UpdateTeamSlots(PedestalSlot[] slots, List<LobbyPlayerData> teamPlayers, ETeamType team)
	{
		if (slots == null)
		{
			return;
		}

		Color nameTextColor = m_AppearanceCatalog != null
			? m_AppearanceCatalog.GetTeamDisplayColor(team)
			: Color.white;

		for (int i = 0; i < slots.Length; i++)
		{
			if (slots[i] == null)
			{
				continue;
			}

			bool isOccupied = i < teamPlayers.Count;
			LobbyPlayerData data = isOccupied ? teamPlayers[i] : default;

			Color emissionColor = Color.white;
			if (isOccupied && m_AppearanceCatalog != null && m_AppearanceCatalog.TryGetPreviewColor(data.HighlightColor, out Color previewColor))
			{
				emissionColor = previewColor;
			}

			slots[i].UpdateDisplay(isOccupied, data, m_DisplayPlayerPrefab, emissionColor, nameTextColor, transform);
		}
	}

	/// <summary>
	/// 指定したクライアントIDのプレイヤーが配置されている台座スロットの目標ワールド座標を取得します。
	/// </summary>
	/// <param name="clientId">対象クライアントID</param>
	/// <param name="worldPosition">台座スロットの目標ワールド座標</param>
	/// <returns>スロットが見つかった場合true</returns>
	public bool TryGetPlayerSlotWorldPosition(ulong clientId, out Vector3 worldPosition)
	{
		worldPosition = Vector3.zero;

		if (m_RedSlots != null)
		{
			for (int i = 0; i < m_CachedRedPlayers.Count && i < m_RedSlots.Length; i++)
			{
				if (m_CachedRedPlayers[i].ClientId == clientId)
				{
					if (m_RedSlots[i] != null)
					{
						worldPosition = m_RedSlots[i].GetTargetWorldPosition();
						return true;
					}
				}
			}
		}

		if (m_BlueSlots != null)
		{
			for (int i = 0; i < m_CachedBluePlayers.Count && i < m_BlueSlots.Length; i++)
			{
				if (m_CachedBluePlayers[i].ClientId == clientId)
				{
					if (m_BlueSlots[i] != null)
					{
						worldPosition = m_BlueSlots[i].GetTargetWorldPosition();
						return true;
					}
				}
			}
		}

		return false;
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
