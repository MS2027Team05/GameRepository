using UnityEngine;

public partial class BallPhysicsController
{
	// 詳細診断を有効にした場合だけ使用する直近Surface更新履歴です。
	private const int m_SurfaceTraceCapacity = 12;
	private System.Text.StringBuilder[] m_SurfaceTrace;
	private int m_SurfaceTraceNext;
	private int m_SurfaceTraceCount;
	private int m_SurfaceTraceCurrent = -1;

	/// <summary>
	/// 物理更新または初回Surface進入の診断記録を開始します。
	/// </summary>
	[System.Diagnostics.Conditional("BALL_SURFACE_DIAGNOSTICS")]
	private void BeginSurfaceTrace(string eventName, bool clearHistory = false)
	{
		if (m_SurfaceTrace == null)
		{
			m_SurfaceTrace = new System.Text.StringBuilder[m_SurfaceTraceCapacity];
		}

		if (clearHistory)
		{
			m_SurfaceTraceNext = 0;
			m_SurfaceTraceCount = 0;
		}

		m_SurfaceTraceCurrent = m_SurfaceTraceNext;
		m_SurfaceTraceNext = (m_SurfaceTraceNext + 1) % m_SurfaceTraceCapacity;
		m_SurfaceTraceCount = Mathf.Min(m_SurfaceTraceCount + 1, m_SurfaceTraceCapacity);
		System.Text.StringBuilder record = m_SurfaceTrace[m_SurfaceTraceCurrent];
		if (record == null)
		{
			record = new System.Text.StringBuilder(2048);
			m_SurfaceTrace[m_SurfaceTraceCurrent] = record;
		}

		record.Clear();
		Vector3 center = m_Rigidbody.position + transform.TransformVector(m_BallCollider.center);
		record.AppendLine($"[Step] Event={eventName} FixedTime={Time.fixedTime:F6} Frame={Time.frameCount}");
		record.AppendLine($"BeforeTriangle={m_SurfaceSimulation.CurrentTriangleIndex} Center={center.ToString("F6")} BallRadius={GetBallWorldRadius():F6}");
		if (m_SurfaceSimulation.TryGetWorldVelocity(out Vector3 velocity))
		{
			record.AppendLine($"BeforeVelocity={velocity.ToString("F6")}");
		}
	}

	/// <summary>
	/// 現在の診断記録へ情報を追加します。詳細診断が無効なら引数も評価されません。
	/// </summary>
	[System.Diagnostics.Conditional("BALL_SURFACE_DIAGNOSTICS")]
	private void RecordSurfaceTrace(string message)
	{
		if (m_SurfaceTraceCurrent >= 0 && m_SurfaceTrace != null)
		{
			m_SurfaceTrace[m_SurfaceTraceCurrent].AppendLine(message);
		}
	}

	/// <summary>
	/// 安全停止直前に履歴を古い順でまとめて出力します。
	/// </summary>
	[System.Diagnostics.Conditional("BALL_SURFACE_DIAGNOSTICS")]
	private void DumpSurfaceTrace()
	{
		if (m_SurfaceTrace == null || m_SurfaceTraceCount == 0)
		{
			return;
		}

		System.Text.StringBuilder report = new System.Text.StringBuilder();
		report.AppendLine($"[BallSurface][ObstacleTrace] Ball={name} State={m_MotionState} Records={m_SurfaceTraceCount}");
		int first = (m_SurfaceTraceNext - m_SurfaceTraceCount + m_SurfaceTraceCapacity) % m_SurfaceTraceCapacity;
		for (int index = 0; index < m_SurfaceTraceCount; index++)
		{
			report.Append(m_SurfaceTrace[(first + index) % m_SurfaceTraceCapacity]);
		}

		Debug.LogWarning(report.ToString(), this);
		m_SurfaceTraceCount = 0;
		m_SurfaceTraceCurrent = -1;
		m_SurfaceTraceNext = 0;
	}
}
