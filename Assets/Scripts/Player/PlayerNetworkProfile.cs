using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// プレイヤーのネットワーク同期プロファイル(名前・チーム・キャラ種別・ハイライトカラー)を管理するコンポーネント。
/// 各種描画演出(ネームプレート・アウトライン・シルエット)へ変更イベントを通知します。
/// </summary>
public class PlayerNetworkProfile : NetworkBehaviour
{
	private readonly NetworkVariable<FixedString64Bytes> m_PlayerName = new NetworkVariable<FixedString64Bytes>(
		default,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server);

	private readonly NetworkVariable<ETeamType> m_Team = new NetworkVariable<ETeamType>(
		ETeamType.None,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server);

	private readonly NetworkVariable<ECharacterType> m_CharacterType = new NetworkVariable<ECharacterType>(
		ECharacterType.ModelA,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server);

	private readonly NetworkVariable<EHighlightColor> m_HighlightColor = new NetworkVariable<EHighlightColor>(
		EHighlightColor.Default,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server);

	public string PlayerName => m_PlayerName.Value.ToString();
	public ETeamType Team => m_Team.Value;
	public ECharacterType CharacterType => m_CharacterType.Value;
	public EHighlightColor HighlightColor => m_HighlightColor.Value;

	/// <summary>プロファイル(名前・チーム・カラー等)が変更された際に発火するイベント</summary>
	public event Action OnProfileChanged;

	public override void OnNetworkSpawn()
	{
		m_PlayerName.OnValueChanged += HandleNameChanged;
		m_Team.OnValueChanged += HandleTeamChanged;
		m_CharacterType.OnValueChanged += HandleCharacterTypeChanged;
		m_HighlightColor.OnValueChanged += HandleHighlightColorChanged;

		// スポーン時の初期通知
		OnProfileChanged?.Invoke();
	}

	public override void OnNetworkDespawn()
	{
		m_PlayerName.OnValueChanged -= HandleNameChanged;
		m_Team.OnValueChanged -= HandleTeamChanged;
		m_CharacterType.OnValueChanged -= HandleCharacterTypeChanged;
		m_HighlightColor.OnValueChanged -= HandleHighlightColorChanged;
	}

	/// <summary>
	/// サーバー側でロビーデータをもとにプロファイルを初期化します。
	/// </summary>
	public void InitializeProfile(LobbyPlayerData data)
	{
		if (!IsServer) return;

		m_PlayerName.Value = data.PlayerName;
		m_Team.Value = data.Team;
		m_CharacterType.Value = data.CharacterType;
		m_HighlightColor.Value = data.HighlightColor;
	}

	private void HandleNameChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
	{
		OnProfileChanged?.Invoke();
	}

	private void HandleTeamChanged(ETeamType previousValue, ETeamType newValue)
	{
		OnProfileChanged?.Invoke();
	}

	private void HandleCharacterTypeChanged(ECharacterType previousValue, ECharacterType newValue)
	{
		OnProfileChanged?.Invoke();
	}

	private void HandleHighlightColorChanged(EHighlightColor previousValue, EHighlightColor newValue)
	{
		OnProfileChanged?.Invoke();
	}
}
