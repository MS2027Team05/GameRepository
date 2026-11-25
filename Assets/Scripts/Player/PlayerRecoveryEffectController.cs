using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// リソースの急速回復と味方パスによる回復を、登録した電流スクリプトまたはデバッグ稲妻で可視化します。
/// PlayerResourceControllerから呼ばれる表示専用クラスで、回復計算やネットワーク同期は行いません。
/// </summary>
public class PlayerRecoveryEffectController : MonoBehaviour
{
	[Header("回復エフェクトの差し替え")]
	[Tooltip("急速回復中に有効化する電流コンポーネント。プレイヤー配下のSphereSurfaceLightningBoltなどを登録します。未設定の場合はデバッグ稲妻を使用します。")]
	[SerializeField] private ALightningBolt m_RecoveryEffect;
	[Tooltip("味方パス回復時に有効化する電流コンポーネント。対象のGameObjectはアクティブにしておきます。未設定の場合はデバッグ稲妻を使用します。")]
	[SerializeField] private ALightningBolt m_PassEffect;
	[Tooltip("味方パス回復エフェクトを表示する時間です(秒)。登録した電流とデバッグ稲妻の両方に適用します。0より大きい値が必要です。")]
	[SerializeField] private float m_PassBurstDuration;

	[Header("デバッグ稲妻の表示範囲(プレイヤーのローカル座標)")]
	[Tooltip("デバッグ用稲妻の中心位置です。このオブジェクトを基準としたローカル座標で指定します。")]
	[SerializeField] private Vector3 m_LightningCenter;
	[Tooltip("デバッグ用稲妻がプレイヤーの周囲に広がる半径です(Unity単位)。")]
	[SerializeField] private float m_LightningRadius;
	[Tooltip("デバッグ用稲妻が上下に分布する高さです(Unity単位)。")]
	[SerializeField] private float m_LightningHeight;

	[Header("デバッグ稲妻の見た目")]
	[Tooltip("デバッグ用稲妻の白い芯の太さです(Unity単位)。周囲の光はこれより太く描画します。")]
	[SerializeField] private float m_LightningWidth;
	[Tooltip("デバッグ用稲妻の形状を更新する間隔です(秒)。0より大きい値が必要です。")]
	[SerializeField] private float m_LightningRefreshInterval;

	private const int m_ContinuousBoltCount = 7;
	private const int m_PassBoltCount = 12;
	private const int m_BoltPointCount = 8;
	private const float m_GlowWidthMultiplier = 3.8f;

	private static readonly Color m_CoreColor = new Color(0.91f, 0.98f, 1.0f, 1.0f);
	private static readonly Color m_GlowColor = new Color(0.10f, 0.82f, 1.0f, 0.48f);
	private static readonly Color m_PassCoreColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
	private static readonly Color m_PassGlowColor = new Color(0.28f, 0.98f, 1.0f, 0.60f);

	private LineRenderer[] m_CoreLines;
	private LineRenderer[] m_GlowLines;
	private Vector3[][] m_BoltPoints;
	private Material m_RuntimeMaterial;
	private bool m_IsRegenerating;
	private bool m_HasWarnedInvalidSettings;
	private float m_FlickerElapsed;
	private float m_PassBurstRemaining;
	private ALightningBolt m_ControlledRecoveryEffect;
	private ALightningBolt m_ControlledPassEffect;

	private void Awake()
	{
		StopAll();
	}

	/// <summary>急速回復の継続表示を切り替えます。パス回復の表示は独立して管理します。</summary>
	public void SetRegenerating(bool isRegenerating)
	{
		if (!isActiveAndEnabled || m_IsRegenerating == isRegenerating)
		{
			return;
		}

		m_IsRegenerating = isRegenerating;
		UpdateScriptEffects();
		if (isRegenerating)
		{
			UpdateRecoveryEffect(true);
		}
		else
		{
			SetBoltRangeVisible(0, m_ContinuousBoltCount, false);
		}
	}

	/// <summary>味方パス受取時のエフェクトを指定時間だけ表示します。</summary>
	public void PlayPassBonus()
	{
		if (!isActiveAndEnabled)
		{
			return;
		}
		if (m_PassBurstDuration <= 0f)
		{
			Debug.LogWarning("パス回復エフェクトのPass Burst Durationを0より大きい値に設定してください。", this);
			return;
		}

		m_PassBurstRemaining = m_PassBurstDuration;
		m_FlickerElapsed = 0.0f;
		UpdateScriptEffects();
		UpdatePassEffect(true);
	}

	private void Update()
	{
		if (!m_IsRegenerating && m_PassBurstRemaining <= 0.0f)
		{
			UpdateScriptEffects();
			return;
		}

		if (m_PassBurstRemaining > 0.0f)
		{
			m_PassBurstRemaining = Mathf.Max(0.0f, m_PassBurstRemaining - Time.deltaTime);
			if (m_PassBurstRemaining <= 0.0f)
			{
				SetBoltRangeVisible(m_ContinuousBoltCount, m_PassBoltCount, false);
			}
		}

		UpdateScriptEffects();
		m_FlickerElapsed += Time.deltaTime;
		bool refreshLightning = m_FlickerElapsed >= m_LightningRefreshInterval;
		if (refreshLightning)
		{
			m_FlickerElapsed = 0.0f;
		}

		if (m_IsRegenerating)
		{
			UpdateRecoveryEffect(refreshLightning);
		}

		if (m_PassBurstRemaining > 0.0f)
		{
			UpdatePassEffect(refreshLightning);
		}
	}

	private void UpdateRecoveryEffect(bool refreshLightning)
	{
		if (m_RecoveryEffect != null)
		{
			SetBoltRangeVisible(0, m_ContinuousBoltCount, false);
		}
		else if (refreshLightning && EnsureLightningCreated())
		{
			RefreshContinuousBolts();
		}
	}

	private void UpdatePassEffect(bool refreshLightning)
	{
		if (m_PassEffect != null)
		{
			SetBoltRangeVisible(m_ContinuousBoltCount, m_PassBoltCount, false);
		}
		else if (refreshLightning && EnsureLightningCreated())
		{
			RefreshPassBolts();
		}
	}

	private void UpdateScriptEffects()
	{
		// 実行中に参照を差し替えた場合は、以前の電流を停止します。
		if (m_ControlledRecoveryEffect != m_RecoveryEffect && m_ControlledRecoveryEffect != m_PassEffect)
		{
			SetEffectEnabled(m_ControlledRecoveryEffect, false);
		}
		if (m_ControlledPassEffect != m_RecoveryEffect && m_ControlledPassEffect != m_PassEffect)
		{
			SetEffectEnabled(m_ControlledPassEffect, false);
		}
		m_ControlledRecoveryEffect = m_RecoveryEffect;
		m_ControlledPassEffect = m_PassEffect;

		bool isPassPlaying = m_PassBurstRemaining > 0f;
		// 同じコンポーネントを両方に登録した場合は、どちらかの表示条件が成立している間だけ有効にします。
		bool isShared = m_RecoveryEffect == m_PassEffect;
		SetEffectEnabled(m_RecoveryEffect, isActiveAndEnabled && (m_IsRegenerating || (isShared && isPassPlaying)));
		if (!isShared)
		{
			SetEffectEnabled(m_PassEffect, isActiveAndEnabled && isPassPlaying);
		}
	}

	private static void SetEffectEnabled(ALightningBolt effect, bool isEnabled)
	{
		if (effect != null && effect.enabled != isEnabled)
		{
			effect.enabled = isEnabled;
		}
	}

	/// <summary>Spawn/DespawnおよびデバッグOFF時に演出を完全停止します。</summary>
	public void StopAll()
	{
		m_IsRegenerating = false;
		m_PassBurstRemaining = 0.0f;
		m_FlickerElapsed = 0.0f;
		UpdateScriptEffects();

		SetBoltRangeVisible(0, m_ContinuousBoltCount + m_PassBoltCount, false);
	}

	private bool EnsureLightningCreated()
	{
		if (m_CoreLines != null)
		{
			return true;
		}

		if (m_LightningRadius <= 0.0f || m_LightningHeight <= 0.0f ||
			m_LightningWidth <= 0.0f || m_LightningRefreshInterval <= 0.0f)
		{
			if (!m_HasWarnedInvalidSettings)
			{
				Debug.LogWarning("[PlayerRecoveryEffectController] 稲妻のInspector設定値が0以下です。READMEの値を設定してください。", this);
				m_HasWarnedInvalidSettings = true;
			}
			return false;
		}

		// Sprite用Shaderは頂点カラーとアルファに対応するため、太いシアンの光と白い芯を重ねられます。
		Shader shader = Shader.Find("Sprites/Default");
		if (shader == null)
		{
			Debug.LogWarning("[PlayerRecoveryEffectController] Sprites/Default Shaderが見つかりません。稲妻の描画をスキップします。", this);
			return false;
		}

		m_RuntimeMaterial = new Material(shader);
		m_RuntimeMaterial.name = "DebugRecoveryElectric_Transient";
		m_RuntimeMaterial.hideFlags = HideFlags.DontSave;

		int totalCount = m_ContinuousBoltCount + m_PassBoltCount;
		m_CoreLines = new LineRenderer[totalCount];
		m_GlowLines = new LineRenderer[totalCount];
		m_BoltPoints = new Vector3[totalCount][];

		for (int i = 0; i < totalCount; i++)
		{
			bool isPass = i >= m_ContinuousBoltCount;
			m_BoltPoints[i] = new Vector3[m_BoltPointCount];
			m_GlowLines[i] = CreateBoltLine("ElectricGlow_" + i, isPass, true);
			m_CoreLines[i] = CreateBoltLine("ElectricCore_" + i, isPass, false);
		}

		return true;
	}

	private LineRenderer CreateBoltLine(string objectName, bool isPass, bool isGlow)
	{
		GameObject lineObject = new GameObject(objectName);
		lineObject.transform.SetParent(transform, false);
		LineRenderer line = lineObject.AddComponent<LineRenderer>();
		line.sharedMaterial = m_RuntimeMaterial;
		line.useWorldSpace = false;
		line.alignment = LineAlignment.View;
		line.textureMode = LineTextureMode.Stretch;
		line.positionCount = m_BoltPointCount;
		line.numCapVertices = 2;
		line.numCornerVertices = 2;
		line.shadowCastingMode = ShadowCastingMode.Off;
		line.receiveShadows = false;
		line.sortingOrder = isGlow ? 0 : 1;

		float width = m_LightningWidth * (isPass ? 1.6f : 1.0f);
		line.startWidth = isGlow ? width * m_GlowWidthMultiplier : width;
		line.endWidth = line.startWidth;
		Color color = isPass
			? (isGlow ? m_PassGlowColor : m_PassCoreColor)
			: (isGlow ? m_GlowColor : m_CoreColor);
		line.startColor = color;
		line.endColor = color;
		line.enabled = false;
		return line;
	}

	private void RefreshContinuousBolts()
	{
		if (m_CoreLines == null)
		{
			return;
		}

		for (int i = 0; i < m_ContinuousBoltCount; i++)
		{
			// 少数の稲妻を瞬間的に消して、通電らしい不規則な明滅を作ります。
			if (Random.value < 0.12f)
			{
				SetBoltVisible(i, false);
				continue;
			}

			float startAngle = Random.Range(-Mathf.PI, Mathf.PI);
			float sweep = Random.Range(0.65f, 1.65f) * (Random.value < 0.5f ? -1.0f : 1.0f);
			float startY = Random.Range(-0.5f, 0.5f) * m_LightningHeight;
			float endY = Mathf.Clamp(startY + Random.Range(-0.65f, 0.65f) * m_LightningHeight,
				-m_LightningHeight * 0.5f, m_LightningHeight * 0.5f);

			Vector3[] positions = m_BoltPoints[i];
			for (int j = 0; j < m_BoltPointCount; j++)
			{
				float t = (float)j / (m_BoltPointCount - 1);
				float angle = startAngle + sweep * t + Random.Range(-0.24f, 0.24f);
				float radius = m_LightningRadius * Random.Range(0.82f, 1.16f);
				float y = Mathf.Lerp(startY, endY, t) + Random.Range(-0.12f, 0.12f) * m_LightningHeight;
				positions[j] = m_LightningCenter + new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
			}

			ApplyBoltPositions(i, positions);
		}
	}

	private void RefreshPassBolts()
	{
		if (m_CoreLines == null)
		{
			return;
		}

		float progress = 1.0f - m_PassBurstRemaining / m_PassBurstDuration;
		float endRadius = m_LightningRadius * Mathf.Lerp(1.15f, 2.05f, progress);
		for (int i = m_ContinuousBoltCount; i < m_CoreLines.Length; i++)
		{
			Vector3 direction = Random.onUnitSphere;
			Vector3[] positions = m_BoltPoints[i];
			for (int j = 0; j < m_BoltPointCount; j++)
			{
				float t = (float)j / (m_BoltPointCount - 1);
				Vector3 jitter = (j == 0 || j == m_BoltPointCount - 1)
					? Vector3.zero
					: Random.insideUnitSphere * (m_LightningRadius * 0.20f);
				positions[j] = m_LightningCenter + direction * Mathf.Lerp(0.12f, endRadius, t) + jitter;
			}
			ApplyBoltPositions(i, positions);
		}
	}

	private void ApplyBoltPositions(int index, Vector3[] positions)
	{
		m_GlowLines[index].SetPositions(positions);
		m_CoreLines[index].SetPositions(positions);
		SetBoltVisible(index, true);
	}

	private void SetBoltVisible(int index, bool isVisible)
	{
		if (m_CoreLines == null)
		{
			return;
		}

		m_GlowLines[index].enabled = isVisible;
		m_CoreLines[index].enabled = isVisible;
	}

	private void SetBoltRangeVisible(int start, int count, bool isVisible)
	{
		if (m_CoreLines == null)
		{
			return;
		}

		int end = Mathf.Min(start + count, m_CoreLines.Length);
		for (int i = start; i < end; i++)
		{
			SetBoltVisible(i, isVisible);
		}
	}

	private void OnDisable()
	{
		StopAll();
	}

	private void OnDestroy()
	{
		if (m_RuntimeMaterial == null)
		{
			return;
		}

		if (Application.isPlaying)
		{
			Destroy(m_RuntimeMaterial);
		}
		else
		{
			DestroyImmediate(m_RuntimeMaterial);
		}
	}
}
