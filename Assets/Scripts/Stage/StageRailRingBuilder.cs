using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Stage
{
	/// <summary>
	/// フィールド外周に両ゴールを囲む水平楕円レール(3パターン)および外側足場ブロック群を一括オーサリングするジェネレーターコンポーネント。
	/// 既存の箱PrefabやBoxColliderセグメントを用い、非均等スケール歪みのないクリーンな階層を生成します。
	/// </summary>
	[ExecuteInEditMode]
	[AddComponentMenu("Stage/Stage Rail Ring Builder")]
	public sealed class StageRailRingBuilder : MonoBehaviour
	{
		[Header("楕円リング幾何設定")]
		[Tooltip("リング中心座標(XZ水平面)")]
		[SerializeField] private Vector3 m_CenterPosition = new Vector3(0f, 100f, 0f);

		[Tooltip("X軸方向(フィールド横幅)の半径")]
		[SerializeField] private float m_RadiusX = 85.0f;

		[Tooltip("Z軸方向(ゴール間)の半径。両ゴール(Z=±90)を外側から覆うサイズを設定します")]
		[SerializeField] private float m_RadiusZ = 120.0f;

		[Tooltip("レールの断面サイズ(X: 進行方向直交の幅, Y: 上下厚み)")]
		[SerializeField] private Vector2 m_RailCrossSection = new Vector2(3.5f, 1.2f);

		[Header("途切れ目なし (Continuous) 設定")]
		[Tooltip("1周を分割して近似する直方体の本数(16〜32本程度)")]
		[Range(12, 48)]
		[SerializeField] private int m_ContinuousSegmentCount = 24;

		[Header("5分割 (Split5) 設定")]
		[Tooltip("直方体が占める角度の割合(0.5〜0.9)。残りが切れ目(隙間)となります")]
		[Range(0.4f, 0.9f)]
		[SerializeField] private float m_Split5FillRatio = 0.72f;

		[Header("4分割 + 往復キューブ (Split4 + Shuttle) 設定")]
		[Tooltip("固定足場(駅)が占める角度の割合(0.1〜0.4)。残りの広い区間をキューブが往復します")]
		[Range(0.08f, 0.45f)]
		[SerializeField] private float m_Split4StationRatio = 0.20f;

		[Tooltip("往復キューブのサイズ(幅, 高さ, 奥行)")]
		[SerializeField] private Vector3 m_ShuttleBlockSize = new Vector3(4.0f, 1.5f, 5.0f);

		[Tooltip("往復キューブの片道所要秒数")]
		[SerializeField] private float m_ShuttleOneWayDuration = 4.5f;

		[Tooltip("往復キューブが駅に到着した際の一時停止待機秒数")]
		[SerializeField] private float m_ShuttleWaitDuration = 1.0f;

		[Header("外側足場ブロック (Outer Footholds) 設定")]
		[Tooltip("足場ブロックの生成数(片側あたり。赤陣営側で生成し、青陣営側へ点対称反転します)")]
		[Range(4, 24)]
		[SerializeField] private int m_OuterFootholdCountPerSide = 10;

		[Tooltip("レール外周からの追加オフセット距離の範囲(Min, Max)")]
		[SerializeField] private Vector2 m_OuterDistanceRange = new Vector2(5.0f, 16.0f);

		[Tooltip("基準高さからの上下ランダムオフセット範囲(Min, Max)")]
		[SerializeField] private Vector2 m_OuterHeightRange = new Vector2(-4.0f, 6.0f);

		[Tooltip("足場ブロックのランダム回転(傾き)の最大角度(各軸±度)")]
		[Range(0f, 45f)]
		[SerializeField] private float m_MaxRandomTiltAngle = 20.0f;

		[Tooltip("外側足場ブロック配置の乱数シード")]
		[SerializeField] private int m_OuterFootholdSeed = 12345;

		[Header("マテリアル・Prefab設定")]
		[Tooltip("赤陣営側(Z < 0)のレール・足場に適用するマテリアル")]
		[SerializeField] private Material m_RedSideMaterial;

		[Tooltip("青陣営側(Z > 0)のレール・足場に適用するマテリアル")]
		[SerializeField] private Material m_BlueSideMaterial;

		[Tooltip("中立・往復ブロックに適用するマテリアル")]
		[SerializeField] private Material m_NeutralMaterial;

		[Tooltip("チーム色を使用せず、レール全体を単色グレーにする場合のマテリアル")]
		[SerializeField] private Material m_DefaultGrayMaterial;

		[Tooltip("外側足場に使用する赤側Prefab(未設定時はCubeを自動生成)")]
		[SerializeField] private GameObject m_RedBoxPrefab;

		[Tooltip("外側足場に使用する青側Prefab(未設定時はCubeを自動生成)")]
		[SerializeField] private GameObject m_BlueBoxPrefab;

		[Header("生成先ルートTransform")]
		[SerializeField] private Transform m_ContinuousRoot;
		[SerializeField] private Transform m_Split5Root;
		[SerializeField] private Transform m_Split4ShuttleRoot;
		[SerializeField] private Transform m_OuterFootholdsRoot;

		public Transform ContinuousRoot => m_ContinuousRoot;
		public Transform Split5Root => m_Split5Root;
		public Transform Split4ShuttleRoot => m_Split4ShuttleRoot;
		public Transform OuterFootholdsRoot => m_OuterFootholdsRoot;

		private const int GroundLayerIndex = 7;

#if UNITY_EDITOR
		/// <summary>
		/// 全パターンの直方体および足場を階層下に一括生成します。
		/// </summary>
		public void BuildAllPatterns()
		{
			EnsureRoots();

			BuildContinuousRing();
			BuildSplit5Ring();
			BuildSplit4WithShuttle();
			BuildOuterFootholds();

			EditorUtility.SetDirty(gameObject);
		}

		public void ClearAll()
		{
			ClearChildren(m_ContinuousRoot);
			ClearChildren(m_Split5Root);
			ClearChildren(m_Split4ShuttleRoot);
			ClearChildren(m_OuterFootholdsRoot);
		}

		private void Reset()
		{
			LoadDefaultMaterials();
		}

		private void LoadDefaultMaterials()
		{
#if UNITY_EDITOR
			if (m_DefaultGrayMaterial == null)
			{
				m_DefaultGrayMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Rail_Gray.mat");
			}
			if (m_RedSideMaterial == null)
			{
				m_RedSideMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Rail_TeamRed.mat");
			}
			if (m_BlueSideMaterial == null)
			{
				m_BlueSideMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Rail_TeamBlue.mat");
			}
			if (m_NeutralMaterial == null)
			{
				m_NeutralMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Rail_Gray.mat");
			}
#endif
		}

		private Material GetRailMaterial(float zPos)
		{
			if (m_DefaultGrayMaterial != null)
			{
				return m_DefaultGrayMaterial;
			}
			return zPos < m_CenterPosition.z ? m_RedSideMaterial : m_BlueSideMaterial;
		}

		private void EnsureRoots()
		{
			m_ContinuousRoot = EnsureChildRoot("Rail_Continuous_Root", m_ContinuousRoot);
			m_Split5Root = EnsureChildRoot("Rail_Split5_Root", m_Split5Root);
			m_Split4ShuttleRoot = EnsureChildRoot("Rail_Split4_Shuttle_Root", m_Split4ShuttleRoot);
			m_OuterFootholdsRoot = EnsureChildRoot("OuterFootholds_Root", m_OuterFootholdsRoot);
		}

		private Transform EnsureChildRoot(string name, Transform current)
		{
			if (current != null)
			{
				return current;
			}

			Transform found = transform.Find(name);
			if (found != null)
			{
				return found;
			}

			GameObject go = new GameObject(name);
			go.transform.SetParent(transform, false);
			go.transform.localPosition = Vector3.zero;
			go.transform.localRotation = Quaternion.identity;
			go.transform.localScale = Vector3.one;
			return go.transform;
		}

		private void ClearChildren(Transform parent)
		{
			if (parent == null)
			{
				return;
			}

			for (int i = parent.childCount - 1; i >= 0; i--)
			{
				DestroyImmediate(parent.GetChild(i).gameObject);
			}
		}

		private Vector3 EvaluateEllipsePoint(float angleRad, float extraRadius = 0f)
		{
			float rx = m_RadiusX + extraRadius;
			float rz = m_RadiusZ + extraRadius;
			return m_CenterPosition + new Vector3(Mathf.Sin(angleRad) * rx, 0f, Mathf.Cos(angleRad) * rz);
		}

		private Vector3 EvaluateEllipseTangent(float angleRad)
		{
			// d/d(angle) (Sin*rx, 0, Cos*rz) = (Cos*rx, 0, -Sin*rz)
			return new Vector3(Mathf.Cos(angleRad) * m_RadiusX, 0f, -Mathf.Sin(angleRad) * m_RadiusZ).normalized;
		}

		private void BuildContinuousRing()
		{
			ClearChildren(m_ContinuousRoot);

			int count = Mathf.Max(8, m_ContinuousSegmentCount);
			float step = (Mathf.PI * 2.0f) / count;

			for (int i = 0; i < count; i++)
			{
				float startAngle = i * step;
				float endAngle = (i + 1) * step;

				Vector3 p0 = EvaluateEllipsePoint(startAngle);
				Vector3 p1 = EvaluateEllipsePoint(endAngle);

				Vector3 mid = (p0 + p1) * 0.5f;
				Vector3 forward = (p1 - p0).normalized;
				// 端同士の隙間を埋めるため長さをわずかにオーバーラップさせる
				float length = Vector3.Distance(p0, p1) + 0.1f;

				Material mat = GetRailMaterial(mid.z);
				CreateRailSegment(m_ContinuousRoot, $"Segment_{i:D2}", mid, forward, length, m_RailCrossSection, mat);
			}
		}

		private void BuildSplit5Ring()
		{
			ClearChildren(m_Split5Root);

			// 赤->青(5個) + 青->赤(5個) = 計10区間
			int totalSegments = 10;
			float slotAngle = (Mathf.PI * 2.0f) / totalSegments;
			float fillRatio = Mathf.Clamp(m_Split5FillRatio, 0.2f, 0.95f);

			for (int i = 0; i < totalSegments; i++)
			{
				float centerAngle = (i + 0.5f) * slotAngle;
				float halfSpan = (slotAngle * fillRatio) * 0.5f;

				float startAngle = centerAngle - halfSpan;
				float endAngle = centerAngle + halfSpan;

				Vector3 p0 = EvaluateEllipsePoint(startAngle);
				Vector3 p1 = EvaluateEllipsePoint(endAngle);

				Vector3 mid = (p0 + p1) * 0.5f;
				Vector3 forward = (p1 - p0).normalized;
				float length = Vector3.Distance(p0, p1);

				Material mat = GetRailMaterial(mid.z);
				CreateRailSegment(m_Split5Root, $"Split5_Seg_{i:D2}", mid, forward, length, m_RailCrossSection, mat);
			}
		}

		private void BuildSplit4WithShuttle()
		{
			ClearChildren(m_Split4ShuttleRoot);

			// 赤->青(4個) + 青->赤(4個) = 計8区間
			int totalStations = 8;
			float slotAngle = (Mathf.PI * 2.0f) / totalStations;
			float stationRatio = Mathf.Clamp(m_Split4StationRatio, 0.05f, 0.45f);

			var stationEnds = new List<(Vector3 exitPoint, Vector3 nextEnterPoint)>();

			for (int i = 0; i < totalStations; i++)
			{
				float centerAngle = i * slotAngle;
				float halfSpan = (slotAngle * stationRatio) * 0.5f;

				float startAngle = centerAngle - halfSpan;
				float endAngle = centerAngle + halfSpan;

				Vector3 p0 = EvaluateEllipsePoint(startAngle);
				Vector3 p1 = EvaluateEllipsePoint(endAngle);

				Vector3 mid = (p0 + p1) * 0.5f;
				Vector3 forward = (p1 - p0).normalized;
				float length = Vector3.Distance(p0, p1);

				Material mat = GetRailMaterial(mid.z);
				// 駅は足場幅を少し広め(1.2倍)に設定
				Vector2 stationCrossSection = new Vector2(m_RailCrossSection.x * 1.25f, m_RailCrossSection.y);
				CreateRailSegment(m_Split4ShuttleRoot, $"Station_{i:D2}", mid, forward, length, stationCrossSection, mat);

				// 次の駅の入口角度
				float nextCenterAngle = (i + 1) * slotAngle;
				float nextStartAngle = nextCenterAngle - halfSpan;
				Vector3 nextEnter = EvaluateEllipsePoint(nextStartAngle);

				stationEnds.Add((p1, nextEnter));
			}

			// 各駅の間に往復シャトルブロックを配置
			for (int i = 0; i < stationEnds.Count; i++)
			{
				var (pExit, pNextEnter) = stationEnds[i];
				Vector3 shuttleStart = pExit;
				Vector3 shuttleEnd = pNextEnter;

				GameObject shuttleGo = new GameObject($"Shuttle_{i:D2}");
				shuttleGo.layer = GroundLayerIndex;
				shuttleGo.transform.SetParent(m_Split4ShuttleRoot, false);
				shuttleGo.transform.position = (shuttleStart + shuttleEnd) * 0.5f;

				Vector3 forward = (shuttleEnd - shuttleStart).normalized;
				if (forward.sqrMagnitude > 0.001f)
				{
					shuttleGo.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
				}

				BoxCollider box = shuttleGo.AddComponent<BoxCollider>();
				box.size = m_ShuttleBlockSize;

				// 見た目用キューブ
				GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
				visual.name = "Visual";
				visual.transform.SetParent(shuttleGo.transform, false);
				visual.transform.localPosition = Vector3.zero;
				visual.transform.localRotation = Quaternion.identity;
				visual.transform.localScale = m_ShuttleBlockSize;
				DestroyImmediate(visual.GetComponent<Collider>());

				Material shuttleMat = m_NeutralMaterial != null ? m_NeutralMaterial : GetRailMaterial(shuttleGo.transform.position.z);
				if (shuttleMat != null)
				{
					visual.GetComponent<Renderer>().sharedMaterial = shuttleMat;
				}

				StageShuttleBlock shuttle = shuttleGo.AddComponent<StageShuttleBlock>();
				shuttle.SetWaypoints(shuttleStart, shuttleEnd, m_CenterPosition, StageShuttleBlock.ETrajectoryMode.Linear);

				// インスペクターのシリアライズフィールドに初期値を反映
				SerializedObject so = new SerializedObject(shuttle);
				so.FindProperty("m_PointA").vector3Value = shuttleStart;
				so.FindProperty("m_PointB").vector3Value = shuttleEnd;
				so.FindProperty("m_ArcCenter").vector3Value = m_CenterPosition;
				so.FindProperty("m_OneWayDuration").floatValue = m_ShuttleOneWayDuration;
				so.FindProperty("m_WaitDurationAtEnds").floatValue = m_ShuttleWaitDuration;
				so.FindProperty("m_InitialPhase").floatValue = (float)i / stationEnds.Count;
				so.ApplyModifiedProperties();
			}
		}

		private void BuildOuterFootholds()
		{
			ClearChildren(m_OuterFootholdsRoot);

			int count = Mathf.Max(2, m_OuterFootholdCountPerSide);
			var rng = new System.Random(m_OuterFootholdSeed);

			// 赤陣営側 (角度 π/2 〜 3π/2、Z < 0 側) に配置
			for (int i = 0; i < count; i++)
			{
				float t = ((float)i + (float)rng.NextDouble() * 0.8f) / count;
				// 赤陣営側の角度範囲 (π/2から3π/2まで = 90度〜270度)
				float angle = Mathf.PI * 0.5f + t * Mathf.PI;

				float extraDist = Mathf.Lerp(m_OuterDistanceRange.x, m_OuterDistanceRange.y, (float)rng.NextDouble());
				float heightOffset = Mathf.Lerp(m_OuterHeightRange.x, m_OuterHeightRange.y, (float)rng.NextDouble());

				Vector3 redPos = EvaluateEllipsePoint(angle, extraDist);
				redPos.y += heightOffset;

				// ランダムな傾き
				float rx = ((float)rng.NextDouble() * 2f - 1f) * m_MaxRandomTiltAngle;
				float ry = ((float)rng.NextDouble() * 2f - 1f) * m_MaxRandomTiltAngle;
				float rz = ((float)rng.NextDouble() * 2f - 1f) * m_MaxRandomTiltAngle;
				Quaternion redRot = Quaternion.Euler(rx, ry, rz);

				// 赤側オブジェクト生成
				CreateFootholdObject(m_OuterFootholdsRoot, $"Foothold_Red_{i:D2}", redPos, redRot, m_RedBoxPrefab, m_RedSideMaterial);

				// 原点中心での180度点対称コピー(青陣営側)
				Vector3 bluePos = new Vector3(-redPos.x, redPos.y, -redPos.z);
				Quaternion blueRot = Quaternion.Euler(0f, 180f, 0f) * redRot;
				CreateFootholdObject(m_OuterFootholdsRoot, $"Foothold_Blue_{i:D2}", bluePos, blueRot, m_BlueBoxPrefab, m_BlueSideMaterial);
			}
		}

		private void CreateFootholdObject(Transform parent, string name, Vector3 pos, Quaternion rot, GameObject prefab, Material fallbackMat)
		{
			GameObject go;
			if (prefab != null)
			{
				go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
				go.name = name;
				go.transform.position = pos;
				go.transform.rotation = rot;
			}
			else
			{
				go = new GameObject(name);
				go.layer = GroundLayerIndex;
				go.transform.SetParent(parent, false);
				go.transform.position = pos;
				go.transform.rotation = rot;

				BoxCollider box = go.AddComponent<BoxCollider>();
				Vector3 size = new Vector3(3f, 3f, 3f);
				box.size = size;

				GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
				visual.name = "Visual";
				visual.transform.SetParent(go.transform, false);
				visual.transform.localPosition = Vector3.zero;
				visual.transform.localRotation = Quaternion.identity;
				visual.transform.localScale = size;
				DestroyImmediate(visual.GetComponent<Collider>());

				if (fallbackMat != null)
				{
					visual.GetComponent<Renderer>().sharedMaterial = fallbackMat;
				}
			}
		}

		private GameObject CreateRailSegment(Transform parent, string name, Vector3 pos, Vector3 forward, float length, Vector2 crossSection, Material mat)
		{
			GameObject go = new GameObject(name);
			go.layer = GroundLayerIndex;
			go.transform.SetParent(parent, false);
			go.transform.position = pos;
			if (forward.sqrMagnitude > 0.001f)
			{
				go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
			}

			// 親TransformはScale (1, 1, 1) を維持し、BoxColliderに実寸サイズを設定
			BoxCollider box = go.AddComponent<BoxCollider>();
			Vector3 fullSize = new Vector3(crossSection.x, crossSection.y, length);
			box.size = fullSize;

			// 見た目用キューブメッシュ
			GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
			visual.name = "Visual";
			visual.transform.SetParent(go.transform, false);
			visual.transform.localPosition = Vector3.zero;
			visual.transform.localRotation = Quaternion.identity;
			visual.transform.localScale = fullSize;
			DestroyImmediate(visual.GetComponent<Collider>());

			if (mat != null)
			{
				visual.GetComponent<Renderer>().sharedMaterial = mat;
			}

			return go;
		}

		private void OnDrawGizmosSelected()
		{
			Gizmos.color = new Color(1f, 0.92f, 0.016f, 0.4f);
			int segments = 48;
			float step = (Mathf.PI * 2f) / segments;
			Vector3 prev = EvaluateEllipsePoint(0f);
			for (int i = 1; i <= segments; i++)
			{
				Vector3 next = EvaluateEllipsePoint(i * step);
				Gizmos.DrawLine(prev, next);
				prev = next;
			}
		}
#endif
	}
}

