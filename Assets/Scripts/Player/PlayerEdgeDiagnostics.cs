using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 直方体や傾斜面のエッジ走行時における接地脱落の瞬間を検知し、
/// 速度・姿勢・法線・Ray判定結果・最寄り面との乖離を詳細に記録・可視化する検証用診断コンポーネント。
/// </summary>
[RequireComponent(typeof(PlayerGroundDetector))]
[RequireComponent(typeof(PlayerSurfaceMover))]
public class PlayerEdgeDiagnostics : MonoBehaviour
{
	public enum EAutoInputMode
	{
		Disabled,
		SlowForward,     // 低速直進入力の自動注入
		WiggleRandom,    // 角付近での高速WASD切り替え入力の自動注入
	}

	[Header("診断設定")]
	[SerializeField] private bool m_EnableDiagnostics;
	[SerializeField] private bool m_ShowOnScreenHUD;
	[SerializeField] private LayerMask m_GroundLayerMask;

	[Header("Sceneビュー表示フィルター(見やすさ調整)")]
	[SerializeField] private bool m_DrawSceneLabels;      // 3Dテキストラベル(速度、法線、最寄り面)
	[SerializeField] private bool m_DrawGhostCapsule;     // 脱落時のゴーストカプセル(赤枠)
	[SerializeField] private bool m_DrawVectors;          // 速度(青)と法線(緑)の矢印
	[SerializeField] private bool m_DrawClosestSurface;   // 最寄りGround面への最短距離と法線(白線・シアン球)
	[SerializeField] private bool m_DrawAllRays;          // 探索Rayの線(多数の線が重なるのを防ぐため個別切替)
	[SerializeField] private bool m_DrawTrail;            // 移動軌跡(水色ライン)

	[Header("自動テスト入力ハーネス")]
	[SerializeField] private EAutoInputMode m_AutoInputMode;
	[SerializeField] private float m_SlowForwardSpeedRatio; // 低速直進時の入力マグニチュード (例: 0.25f)
	[SerializeField] private float m_WiggleSwitchInterval;   // ガチャガチャ入力の切り替え間隔秒 (例: 0.08f)

	private PlayerGroundDetector m_GroundDetector;
	private PlayerSurfaceMover m_SurfaceMover;
	private Rigidbody m_Rigidbody;
	private CapsuleCollider m_CapsuleCollider;

	// 履歴・スナップショット用構造体
	public struct SnapshotData
	{
		public float Time { get; set; }
		public Vector3 Position { get; set; }
		public Quaternion Rotation { get; set; }
		public Vector3 Up { get; set; }
		public Vector3 Velocity { get; set; }
		public Vector3 MoveDirection { get; set; }
		public Vector2 Input { get; set; }
		public bool IsGrounded { get; set; }
		public Vector3 CurrentGroundNormal { get; set; }
		public int LastDetectionStep { get; set; }
		public Vector3 LastDetectedNormal { get; set; }
		public Vector3 LastDetectedPoint { get; set; }
		public float GroundGap { get; set; }
		public float NormalDiff { get; set; }
		public Vector3 SnapVelocity { get; set; }
		public List<PlayerGroundDetector.DebugRayInfo> Rays { get; set; }

		// 脱落時の最寄りコライダー解析
		public bool HasClosestCollider { get; set; }
		public string ClosestColliderName { get; set; }
		public Vector3 ClosestPoint { get; set; }
		public Vector3 ClosestNormal { get; set; }
		public float DistanceToClosest { get; set; }
		public Vector3 ClosestObjectRotation { get; set; }
	}

	private readonly Queue<SnapshotData> m_HistoryBuffer = new Queue<SnapshotData>();
	private const int MaxHistoryCount = 60; // 60フレーム分の履歴

	private bool m_WasGrounded = true;
	private SnapshotData m_LastFalloffSnapshot;
	private bool m_HasFalloffSnapshot;
	private float m_AutoInputTimer;
	private Vector2 m_CurrentAutoInput;

	private readonly StringBuilder m_LogBuilder = new StringBuilder(512);

	public SnapshotData LastFalloffSnapshot { get => m_LastFalloffSnapshot; }
	public bool HasFalloffSnapshot { get => m_HasFalloffSnapshot; }

	private void Awake()
	{
		m_GroundDetector = GetComponent<PlayerGroundDetector>();
		m_SurfaceMover = GetComponent<PlayerSurfaceMover>();
		m_Rigidbody = GetComponent<Rigidbody>();
		m_CapsuleCollider = GetComponent<CapsuleCollider>();
	}

	private void FixedUpdate()
	{
		if (!m_EnableDiagnostics)
		{
			return;
		}

		UpdateAutoInputHarness();

		// 現在フレームのスナップショットを採取
		SnapshotData currentFrame = CaptureCurrentFrame();

		// 接地脱落の検知 (直前まで接地していたが、未接地になった瞬間)
		if (m_WasGrounded && !currentFrame.IsGrounded)
		{
			AnalyzeAndRecordFalloff(currentFrame);
		}

		m_WasGrounded = currentFrame.IsGrounded;

		m_HistoryBuffer.Enqueue(currentFrame);
		if (m_HistoryBuffer.Count > MaxHistoryCount)
		{
			m_HistoryBuffer.Dequeue();
		}
	}

	private SnapshotData CaptureCurrentFrame()
	{
		SnapshotData data = new SnapshotData
		{
			Time = UnityEngine.Time.time,
			Position = transform.position,
			Rotation = transform.rotation,
			Up = transform.up,
			Velocity = m_Rigidbody != null ? m_Rigidbody.linearVelocity : Vector3.zero,
			MoveDirection = m_SurfaceMover != null ? m_SurfaceMover.CurrentMoveDirection : transform.forward,
			Input = m_SurfaceMover != null ? m_SurfaceMover.CurrentMoveInput : Vector2.zero,
			IsGrounded = m_GroundDetector != null && m_GroundDetector.IsGrounded,
			CurrentGroundNormal = m_GroundDetector != null ? m_GroundDetector.CurrentGroundNormal : Vector3.up,
			LastDetectionStep = m_GroundDetector != null ? m_GroundDetector.LastDetectionStep : 0,
			LastDetectedNormal = m_GroundDetector != null ? m_GroundDetector.LastDetectedNormal : Vector3.up,
			LastDetectedPoint = m_GroundDetector != null ? m_GroundDetector.LastDetectedPoint : Vector3.zero,
			GroundGap = m_GroundDetector != null ? m_GroundDetector.GroundGap : 0f,
			NormalDiff = m_SurfaceMover != null ? m_SurfaceMover.LastNormalDiff : 0f,
			SnapVelocity = m_SurfaceMover != null ? m_SurfaceMover.LastSnapVelocity : Vector3.zero,
			Rays = new List<PlayerGroundDetector.DebugRayInfo>()
		};

		if (m_GroundDetector != null && m_GroundDetector.DebugRays != null)
		{
			data.Rays.AddRange(m_GroundDetector.DebugRays);
		}

		return data;
	}

	private void AnalyzeAndRecordFalloff(SnapshotData current)
	{
		m_LastFalloffSnapshot = current;
		m_HasFalloffSnapshot = true;

		// 周囲の Ground コライダーを探索し、最も近い面との距離・法線を特定
		float searchRadius = 2.0f;
		int mask = m_GroundLayerMask.value == 0 ? ~0 : m_GroundLayerMask.value;
		Collider[] colliders = Physics.OverlapSphere(current.Position, searchRadius, mask, QueryTriggerInteraction.Ignore);

		float minDistance = float.MaxValue;
		Collider bestCollider = null;
		Vector3 bestClosestPoint = Vector3.zero;

		Vector3 footPos = current.Position - current.Up * (m_CapsuleCollider != null ? m_CapsuleCollider.height * 0.5f : 1.0f);

		for (int i = 0; i < colliders.Length; i++)
		{
			Collider col = colliders[i];
			if (col.gameObject == gameObject)
			{
				continue;
			}

			Vector3 closest = col.ClosestPoint(footPos);
			float dist = Vector3.Distance(footPos, closest);
			if (dist < minDistance)
			{
				minDistance = dist;
				bestCollider = col;
				bestClosestPoint = closest;
			}
		}

		if (bestCollider != null)
		{
			m_LastFalloffSnapshot.HasClosestCollider = true;
			m_LastFalloffSnapshot.ClosestColliderName = bestCollider.gameObject.name;
			m_LastFalloffSnapshot.ClosestPoint = bestClosestPoint;
			m_LastFalloffSnapshot.DistanceToClosest = minDistance;
			m_LastFalloffSnapshot.ClosestObjectRotation = bestCollider.transform.eulerAngles;

			// 最近接点における面法線の推定
			Vector3 dirFromClosest = footPos - bestClosestPoint;
			if (dirFromClosest.sqrMagnitude > 0.0001f)
			{
				m_LastFalloffSnapshot.ClosestNormal = dirFromClosest.normalized;
			}
			else
			{
				m_LastFalloffSnapshot.ClosestNormal = bestCollider.transform.up;
			}
		}

		// ログ出力
		OutputFalloffReport(m_LastFalloffSnapshot);
	}

	private void OutputFalloffReport(SnapshotData snap)
	{
		m_LogBuilder.Clear();
		m_LogBuilder.AppendLine("[PlayerEdgeDiagnostics] 接地脱落を検知しました。");
		m_LogBuilder.AppendFormat("・脱落時刻: {0:F2}s | 位置: {1}\n", snap.Time, snap.Position);
		m_LogBuilder.AppendFormat("・速度: {0:F2} m/s (Linear: {1})\n", snap.Velocity.magnitude, snap.Velocity);
		m_LogBuilder.AppendFormat("・直前の法線: {0} | プレイヤーUp: {1} | 法線角度差: {2:F1} deg\n",
			snap.CurrentGroundNormal, snap.Up, Vector3.Angle(snap.Up, snap.CurrentGroundNormal));
		m_LogBuilder.AppendFormat("・直前の判定Step: {0} (0:外れ, 1:凹角壁, 2:SphereCast, 3:近接Ray)\n", snap.LastDetectionStep);
		m_LogBuilder.AppendFormat("・探索Ray数: {0}本中 {1}本Hit\n", snap.Rays.Count, CountHitRays(snap.Rays));

		if (snap.HasClosestCollider)
		{
			m_LogBuilder.AppendFormat("・最寄りGround: [{0}] (回転: {1})\n", snap.ClosestColliderName, snap.ClosestObjectRotation);
			m_LogBuilder.AppendFormat("・最寄り面までの距離: {0:F3}m (探索半径0.6mとの差: {1:F3}m)\n",
				snap.DistanceToClosest, snap.DistanceToClosest - 0.6f);
			m_LogBuilder.AppendFormat("・推定面法線: {0}\n", snap.ClosestNormal);
		}
		else
		{
			m_LogBuilder.AppendLine("・半径2.0m以内にGroundコライダーは検出されませんでした。");
		}

		Debug.LogWarning(m_LogBuilder.ToString(), this);
	}

	private int CountHitRays(List<PlayerGroundDetector.DebugRayInfo> rays)
	{
		int count = 0;
		for (int i = 0; i < rays.Count; i++)
		{
			if (rays[i].Hit)
			{
				count++;
			}
		}
		return count;
	}

	private void UpdateAutoInputHarness()
	{
		if (m_AutoInputMode == EAutoInputMode.Disabled || m_SurfaceMover == null)
		{
			return;
		}

		float delta = UnityEngine.Time.fixedDeltaTime;
		m_AutoInputTimer += delta;

		if (m_AutoInputMode == EAutoInputMode.SlowForward)
		{
			float ratio = m_SlowForwardSpeedRatio > 0f ? m_SlowForwardSpeedRatio : 0.25f;
			m_CurrentAutoInput = new Vector2(0f, ratio);
			m_SurfaceMover.SetMoveInput(m_CurrentAutoInput, transform);
		}
		else if (m_AutoInputMode == EAutoInputMode.WiggleRandom)
		{
			float interval = m_WiggleSwitchInterval > 0f ? m_WiggleSwitchInterval : 0.08f;
			if (m_AutoInputTimer >= interval)
			{
				m_AutoInputTimer = 0f;
				// ランダムな前後左右入力
				float x = Random.Range(-1f, 1f);
				float y = Random.Range(-1f, 1f);
				m_CurrentAutoInput = new Vector2(x, y).normalized;
			}
			m_SurfaceMover.SetMoveInput(m_CurrentAutoInput, transform);
		}
	}

	private void OnGUI()
	{
		if (!m_EnableDiagnostics || !m_ShowOnScreenHUD)
		{
			return;
		}

		GUILayout.BeginArea(new Rect(10, 10, 420, 240), GUI.skin.box);
		GUILayout.Label("=== Player Edge Diagnostics HUD ===");

		bool isGrounded = m_GroundDetector != null && m_GroundDetector.IsGrounded;
		GUI.color = isGrounded ? Color.green : Color.red;
		GUILayout.Label(string.Format("Status: {0}", isGrounded ? "GROUNDED" : "AIR (FALL-OFF)"));
		GUI.color = Color.white;

		float speed = m_Rigidbody != null ? m_Rigidbody.linearVelocity.magnitude : 0f;
		float planeSpeed = m_SurfaceMover != null ? m_SurfaceMover.CurrentPlaneSpeed : 0f;
		GUILayout.Label(string.Format("Velocity: Total {0:F2} m/s | Plane {1:F2} m/s", speed, planeSpeed));

		Vector3 norm = m_GroundDetector != null ? m_GroundDetector.CurrentGroundNormal : Vector3.up;
		float angle = Vector3.Angle(transform.up, norm);
		GUILayout.Label(string.Format("Ground Normal: {0} (AngleDiff: {1:F1} deg)", norm, angle));

		int step = m_GroundDetector != null ? m_GroundDetector.LastDetectionStep : 0;
		string stepName = step == 1 ? "1 (Wall)" : (step == 2 ? "2 (Foot Sphere)" : (step == 3 ? "3 (Edge Ray)" : "0 (None)"));
		GUILayout.Label(string.Format("Last Detect Step: {0}", stepName));

		if (m_HasFalloffSnapshot)
		{
			GUILayout.Space(5);
			GUI.color = Color.yellow;
			GUILayout.Label(string.Format("Last Fall-off: {0:F1}s ago | Speed: {1:F2} m/s",
				UnityEngine.Time.time - m_LastFalloffSnapshot.Time, m_LastFalloffSnapshot.Velocity.magnitude));
			if (m_LastFalloffSnapshot.HasClosestCollider)
			{
				GUILayout.Label(string.Format("Closest Surface: {0} (Dist: {1:F3}m)",
					m_LastFalloffSnapshot.ClosestColliderName, m_LastFalloffSnapshot.DistanceToClosest));
			}
			GUI.color = Color.white;
		}

		GUILayout.EndArea();
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		if (!m_EnableDiagnostics)
		{
			return;
		}

		// 1. 直近フレームの移動軌跡 (Trail: 水色)
		if (m_DrawTrail && m_HistoryBuffer.Count > 1)
		{
			Gizmos.color = new Color(0.2f, 0.8f, 1.0f, 0.6f);
			Vector3 prev = Vector3.zero;
			bool first = true;
			foreach (SnapshotData frame in m_HistoryBuffer)
			{
				if (!first)
				{
					Gizmos.DrawLine(prev, frame.Position);
				}
				prev = frame.Position;
				first = false;
			}
		}

		// 2. 脱落発生瞬間のスナップショット固定表示
		if (m_HasFalloffSnapshot)
		{
			SnapshotData snap = m_LastFalloffSnapshot;

			// A. ゴーストカプセル (赤色)
			if (m_DrawGhostCapsule)
			{
				Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.85f);
				float height = m_CapsuleCollider != null ? m_CapsuleCollider.height : 2.0f;
				float radius = m_CapsuleCollider != null ? m_CapsuleCollider.radius : 0.5f;
				Vector3 top = snap.Position + snap.Up * (height * 0.5f - radius);
				Vector3 bottom = snap.Position - snap.Up * (height * 0.5f - radius);
				Gizmos.DrawWireSphere(top, radius);
				Gizmos.DrawWireSphere(bottom, radius);
				Gizmos.DrawLine(top + snap.Rotation * Vector3.right * radius, bottom + snap.Rotation * Vector3.right * radius);
				Gizmos.DrawLine(top - snap.Rotation * Vector3.right * radius, bottom - snap.Rotation * Vector3.right * radius);

				if (m_DrawSceneLabels)
				{
					UnityEditor.Handles.color = Color.red;
					UnityEditor.Handles.Label(top + snap.Up * 0.3f, string.Format("【脱落発生地点】\n時刻: {0:F1}s | 速度: {1:F2} m/s", snap.Time, snap.Velocity.magnitude));
				}
			}

			// B. 速度ベクトル (青色矢印) & 法線ベクトル (緑色矢印)
			if (m_DrawVectors)
			{
				// 速度ベクトル (青)
				Gizmos.color = Color.blue;
				Gizmos.DrawRay(snap.Position, snap.Velocity);
				if (m_DrawSceneLabels && snap.Velocity.sqrMagnitude > 0.01f)
				{
					UnityEditor.Handles.color = Color.cyan;
					UnityEditor.Handles.Label(snap.Position + snap.Velocity, string.Format("速度 (Velocity): {0:F2} m/s", snap.Velocity.magnitude));
				}

				// 接地面法線 (緑)
				Gizmos.color = Color.green;
				Gizmos.DrawRay(snap.Position, snap.CurrentGroundNormal * 1.5f);
				if (m_DrawSceneLabels)
				{
					UnityEditor.Handles.color = Color.green;
					UnityEditor.Handles.Label(snap.Position + snap.CurrentGroundNormal * 1.5f, "適用法線 (Ground Normal)");
				}
			}

			// C. 最寄りGroundコライダーへの最短距離ベクトル (白線・シアン球)
			if (m_DrawClosestSurface && snap.HasClosestCollider)
			{
				Vector3 footPos = snap.Position - snap.Up * (m_CapsuleCollider != null ? m_CapsuleCollider.height * 0.5f : 1.0f);
				Gizmos.color = Color.white;
				Gizmos.DrawLine(footPos, snap.ClosestPoint);

				Gizmos.color = Color.cyan;
				Gizmos.DrawWireSphere(snap.ClosestPoint, 0.12f);

				Gizmos.color = Color.green;
				Gizmos.DrawRay(snap.ClosestPoint, snap.ClosestNormal * 0.8f);

				if (m_DrawSceneLabels)
				{
					UnityEditor.Handles.color = Color.white;
					string reachStatus = snap.DistanceToClosest <= 0.6f ? "届く範囲" : "探索範囲(0.6m)外";
					UnityEditor.Handles.Label(snap.ClosestPoint + snap.ClosestNormal * 0.2f,
						string.Format("【最寄りGround面】: {0}\n足元からの距離: {1:F3}m ({2})\n面法線: {3}",
						snap.ClosestColliderName, snap.DistanceToClosest, reachStatus, snap.ClosestNormal));
				}
			}

			// D. 探索Rayの描画 (多数の線で視界が埋まるのを防ぐためトグル制御)
			if (m_DrawAllRays && snap.Rays != null)
			{
				for (int i = 0; i < snap.Rays.Count; i++)
				{
					PlayerGroundDetector.DebugRayInfo ray = snap.Rays[i];
					if (ray.Hit)
					{
						Gizmos.color = Color.magenta;
						Gizmos.DrawLine(ray.Origin, ray.HitPoint);
						Gizmos.DrawSphere(ray.HitPoint, 0.04f);
						if (m_DrawSceneLabels)
						{
							UnityEditor.Handles.color = Color.magenta;
							UnityEditor.Handles.Label(ray.HitPoint, string.Format("Ray[{0}] Hit", i));
						}
					}
					else
					{
						Gizmos.color = new Color(1f, 0.4f, 0f, 0.25f);
						Gizmos.DrawLine(ray.Origin, ray.Origin + ray.Direction * ray.Distance);
					}
				}
			}
		}
	}
#endif
}
