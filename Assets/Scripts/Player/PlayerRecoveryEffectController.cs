using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// デバッグ用: リソースの急速回復と味方パスによる回復を、青白い稲妻で可視化します。
/// PlayerResourceControllerから呼ばれる表示専用クラスで、回復計算やネットワーク同期は行いません。
/// </summary>
public class PlayerRecoveryEffectController : MonoBehaviour
{
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
	[Tooltip("味方パス受取時の放射状の稲妻を表示する時間です(秒)。0より大きい値が必要です。")]
	[SerializeField] private float m_PassBurstDuration;

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

	/// <summary>急速回復中は稲妻を継続表示し、停止中はすべて隠します。</summary>
	public void SetRegenerating(bool isRegenerating)
	{
		if (m_IsRegenerating == isRegenerating)
		{
			return;
		}

		m_IsRegenerating = isRegenerating;
		if (isRegenerating)
		{
			if (EnsureLightningCreated())
			{
				RefreshContinuousBolts();
			}
		}
		else
		{
			SetBoltRangeVisible(0, m_ContinuousBoltCount, false);
		}
	}

	/// <summary>味方パス受取時は、胴体から外側へ伸びる稲妻を短時間だけ表示します。</summary>
	public void PlayPassBonus()
	{
		if (!EnsureLightningCreated())
		{
			return;
		}

		m_PassBurstRemaining = m_PassBurstDuration;
		m_FlickerElapsed = 0.0f;
		RefreshPassBolts();
	}

	private void Update()
	{
		if (!m_IsRegenerating && m_PassBurstRemaining <= 0.0f)
		{
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

		if (m_CoreLines == null)
		{
			return;
		}

		m_FlickerElapsed += Time.deltaTime;
		if (m_FlickerElapsed < m_LightningRefreshInterval)
		{
			return;
		}

		m_FlickerElapsed = 0.0f;
		if (m_IsRegenerating)
		{
			RefreshContinuousBolts();
		}

		if (m_PassBurstRemaining > 0.0f)
		{
			RefreshPassBolts();
		}
	}

	/// <summary>Spawn/DespawnおよびデバッグOFF時に演出を完全停止します。</summary>
	public void StopAll()
	{
		m_IsRegenerating = false;
		m_PassBurstRemaining = 0.0f;
		m_FlickerElapsed = 0.0f;

		SetBoltRangeVisible(0, m_ContinuousBoltCount + m_PassBoltCount, false);
	}

	private bool EnsureLightningCreated()
	{
		if (m_CoreLines != null)
		{
			return true;
		}

		if (m_LightningRadius <= 0.0f || m_LightningHeight <= 0.0f ||
			m_LightningWidth <= 0.0f || m_LightningRefreshInterval <= 0.0f ||
			m_PassBurstDuration <= 0.0f)
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
