using System;
using UnityEngine;

/// <summary>
/// 展示環境における各端末のプレイヤー設定(キャラ・カラー・固定チーム・接続先IP・視点感度)を
/// ローカル(PlayerPrefs)に永続化し、自動適用するためのストレージクラス。
/// ※プレイヤーネームは来場者ごとに異なるためキャッシュ対象外とします。
/// </summary>
public static class LocalPlayerProfilePreferences
{
	private const string m_KeyCharacterType = "Exhibition_CharacterType";
	private const string m_KeyHighlightColor = "Exhibition_HighlightColor";
	private const string m_KeyAssignedTeam = "Exhibition_AssignedTeam";
	private const string m_KeyTargetIp = "Exhibition_TargetIP";
	private const string m_KeyLookSensitivity = "Exhibition_LookSensitivity";

	private static bool m_HasCachedLookSensitivity;
	private static float m_CachedLookSensitivity;

	/// <summary>
	/// 視点感度倍率が変更された際に通知します。
	/// </summary>
	public static event Action<float> OnLookSensitivityChanged;

	/// <summary>
	/// 保存されたキャラクターモデル種別を取得します。
	/// </summary>
	public static ECharacterType GetCharacterType()
	{
		int value = PlayerPrefs.GetInt(m_KeyCharacterType, (int)ECharacterType.Man);
		return System.Enum.IsDefined(typeof(ECharacterType), (byte)value)
			? (ECharacterType)(byte)value
			: ECharacterType.Man;
	}

	/// <summary>
	/// キャラクターモデル種別を保存します。
	/// </summary>
	public static void SetCharacterType(ECharacterType characterType)
	{
		PlayerPrefs.SetInt(m_KeyCharacterType, (int)characterType);
		PlayerPrefs.Save();
	}

	/// <summary>
	/// 保存されたハイライトカラー種別を取得します。
	/// </summary>
	public static EHighlightColor GetHighlightColor()
	{
		int value = PlayerPrefs.GetInt(m_KeyHighlightColor, (int)EHighlightColor.Black);
		return System.Enum.IsDefined(typeof(EHighlightColor), (byte)value)
			? (EHighlightColor)(byte)value
			: EHighlightColor.Black;
	}

	/// <summary>
	/// ハイライトカラー種別を保存します。
	/// </summary>
	public static void SetHighlightColor(EHighlightColor highlightColor)
	{
		PlayerPrefs.SetInt(m_KeyHighlightColor, (int)highlightColor);
		PlayerPrefs.Save();
	}

	/// <summary>
	/// 端末に固定割り当てられたチーム種別を取得します。
	/// 未設定の場合はNoneを返却します。
	/// </summary>
	public static ETeamType GetAssignedTeam()
	{
		int value = PlayerPrefs.GetInt(m_KeyAssignedTeam, (int)ETeamType.None);
		return System.Enum.IsDefined(typeof(ETeamType), (byte)value)
			? (ETeamType)(byte)value
			: ETeamType.None;
	}

	/// <summary>
	/// 端末の固定所属チームを保存します。
	/// </summary>
	public static void SetAssignedTeam(ETeamType team)
	{
		PlayerPrefs.SetInt(m_KeyAssignedTeam, (int)team);
		PlayerPrefs.Save();
	}

	/// <summary>
	/// 保存された接続先IPアドレスを取得します。
	/// </summary>
	public static string GetTargetIp(string fallbackIp = "127.0.0.1")
	{
		return PlayerPrefs.GetString(m_KeyTargetIp, fallbackIp);
	}

	/// <summary>
	/// 接続先IPアドレスを保存します。
	/// </summary>
	public static void SetTargetIp(string ip)
	{
		if (!string.IsNullOrEmpty(ip))
		{
			PlayerPrefs.SetString(m_KeyTargetIp, ip.Trim());
			PlayerPrefs.Save();
		}
	}

	/// <summary>
	/// 保存された視点感度倍率(マウス・ゲームパッド共通)を取得します。
	/// </summary>
	public static float GetLookSensitivity(float defaultValue = 1.0f)
	{
		if (m_HasCachedLookSensitivity)
		{
			return m_CachedLookSensitivity;
		}

		float clampedDefault = Mathf.Max(0.05f, defaultValue);
		float saved = PlayerPrefs.GetFloat(m_KeyLookSensitivity, clampedDefault);
		m_CachedLookSensitivity = saved >= 0.05f ? saved : clampedDefault;
		m_HasCachedLookSensitivity = true;
		return m_CachedLookSensitivity;
	}

	/// <summary>
	/// 視点感度倍率(マウス・ゲームパッド共通)を保存し、変更を通知します。
	/// </summary>
	public static void SetLookSensitivity(float sensitivity)
	{
		float clamped = Mathf.Clamp(sensitivity, 0.05f, 10.0f);
		m_CachedLookSensitivity = clamped;
		m_HasCachedLookSensitivity = true;
		PlayerPrefs.SetFloat(m_KeyLookSensitivity, clamped);
		PlayerPrefs.Save();
		OnLookSensitivityChanged?.Invoke(clamped);
	}

	/// <summary>
	/// キャラクター設定を一括保存します(名前は除外)。
	/// </summary>
	public static void SaveProfile(ECharacterType characterType, EHighlightColor highlightColor, ETeamType team)
	{
		PlayerPrefs.SetInt(m_KeyCharacterType, (int)characterType);
		PlayerPrefs.SetInt(m_KeyHighlightColor, (int)highlightColor);
		PlayerPrefs.SetInt(m_KeyAssignedTeam, (int)team);
		PlayerPrefs.Save();
	}
}
