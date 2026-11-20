using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BallSurfaceとして使用するMeshの構造情報を保持します。
/// </summary>
/// <remarks>
/// Meshを構成するTriangleについて、
/// 各Triangleの頂点・法線・隣接Triangle・Edge形状を
/// 事前に解析して保持します。
///
/// Surface Physics実行中にMesh全体を探索し直すのではなく、
/// 現在Triangleとその隣接Triangleだけを辿れる状態を
/// 作ることが目的です。
///
/// UVやNormalの分割によって同じ位置に別頂点が存在する場合にも
/// Surfaceを接続できるよう、Edge判定には頂点Indexではなく
/// 頂点位置を使用します。
/// </remarks>
public sealed class BallSurfaceMeshData
{
	/// <summary>
	/// Surface上のEdge形状です。
	/// </summary>
	public enum EEdgeType
	{
		/// <summary>
		/// 隣接Triangleが存在しない境界Edgeです。
		/// 閉じたBallSurfaceでは存在しないことを想定します。
		/// </summary>
		Boundary,

		/// <summary>
		/// 隣接Triangleとほぼ同一平面上にあるEdgeです。
		/// </summary>
		Flat,

		/// <summary>
		/// Triangle同士の角度が小さく、
		/// 滑らかな曲面を構成するEdgeです。
		/// </summary>
		Smooth,

		/// <summary>
		/// Surface外側へ張り出している凸Edgeです。
		/// Cubeの外側の角などが該当します。
		/// </summary>
		Convex,

		/// <summary>
		/// Surface内側へ入り込んでいる凹Edgeです。
		/// L字形状の内角などが該当します。
		/// </summary>
		Concave,

		/// <summary>
		/// TriangleのWinding不整合などによって
		/// Surface Physicsで安全に扱えないEdgeです。
		/// </summary>
		Invalid,

		/// <summary>
		/// 3枚以上のTriangleが接続されているEdgeです。
		/// Surface Physicsでは進行先を一意に決定できません。
		/// </summary>
		NonManifold
	}

	/// <summary>
	/// 1つのMesh頂点へ接続しているConvex Edge情報です。
	/// </summary>
	public readonly struct ConvexEdgeReference
	{
		/// <summary>
		/// Edgeを保持しているTriangle Indexです。
		/// </summary>
		public int TriangleIndex
		{
			get;
		}

		/// <summary>
		/// Triangle内のEdge Indexです。
		/// </summary>
		public int EdgeIndex
		{
			get;
		}

		/// <summary>
		/// 基準頂点とは反対側にあるEdge端点のローカル座標です。
		/// </summary>
		public Vector3 OtherLocalVertex
		{
			get;
		}

		public ConvexEdgeReference(
			int triangleIndex,
			int edgeIndex,
			Vector3 otherLocalVertex)
		{
			TriangleIndex =
				triangleIndex;

			EdgeIndex =
				edgeIndex;

			OtherLocalVertex =
				otherLocalVertex;
		}
	}

	/// <summary>
	/// 同じ位置に存在する頂点を
	/// 同一頂点として扱う際の許容誤差です。
	/// </summary>
	private const float m_VertexPositionTolerance = 0.00001f;

	/// <summary>
	/// Triangle同士を同一平面として扱う
	/// 法線内積の下限値です。
	/// </summary>
	private const float m_FlatNormalDotThreshold = 0.9999f;

	/// <summary>
	/// この角度以下のTriangle間Edgeを
	/// 滑らかな曲面として扱います。
	/// </summary>
	private const float m_SmoothEdgeMaximumAngle = 30.0f;

	/// <summary>
	/// Meshの向き判定に使用する最小体積です。
	/// </summary>
	private const float m_MinimumSignedVolume = 0.000001f;

	/// <summary>
	/// Flat / Convex / Concaveを判定する際の
	/// Plane距離許容値です。
	/// </summary>
	private const float m_EdgePlaneTolerance = 0.00001f;

	/// <summary>
	/// Meshのローカル頂点座標です。
	/// </summary>
	private readonly Vector3[] m_Vertices;

	/// <summary>
	/// Triangleを構成する頂点Indexです。
	/// 3要素で1Triangleを表します。
	/// </summary>
	private readonly int[] m_TriangleVertexIndices;

	/// <summary>
	/// 各Triangleのローカル法線です。
	/// </summary>
	private readonly Vector3[] m_TriangleNormals;

	/// <summary>
	/// 各Triangleの各頂点に対応する、
	/// Sharp Edgeを跨がずに平均したローカルSmooth Normalです。
	/// 3要素で1Triangleを表します。
	/// </summary>
	private readonly Vector3[] m_TriangleCornerNormals;

	/// <summary>
	/// 各Triangleの各Edgeに接続されている
	/// 隣接TriangleのIndexです。
	///
	/// 接続先が存在しない場合は-1です。
	/// </summary>
	private readonly int[] m_NeighborTriangleIndices;

	/// <summary>
	/// 隣接Triangle側のどのEdgeと接続されているかを保持します。
	///
	/// 接続先が存在しない場合は-1です。
	/// </summary>
	private readonly int[] m_NeighborEdgeIndices;

	/// <summary>
	/// Triangleごとの各Edge形状です。
	/// </summary>
	private readonly EEdgeType[] m_EdgeTypes;

	/// <summary>
	/// 頂点位置ごとに接続している物理Convex Edgeを保持します。
	/// </summary>
	/// <remarks>
	/// UV Seam等で同位置に重複頂点が存在しても、
	/// VertexPositionKeyによって同一頂点として扱います。
	///
	/// Vertex Crossing時にMesh全体を走査せず、
	/// その頂点へ接続しているEdgeだけ取得するためのCacheです。
	/// </remarks>
	private readonly Dictionary<VertexPositionKey, ConvexEdgeReference[]>
		m_ConvexEdgesByVertex;

	private readonly int m_BoundaryEdgeCount;
	private readonly int m_NonManifoldEdgeCount;
	private readonly int m_DegenerateTriangleCount;
	private readonly int m_InvalidEdgeCount;
	private readonly int m_FlatEdgeCount;
	private readonly int m_SmoothEdgeCount;
	private readonly int m_ConvexEdgeCount;
	private readonly int m_ConcaveEdgeCount;

	private readonly float m_SignedVolume;
	private readonly bool m_HasOutwardWinding;

	/// <summary>
	/// Meshに含まれるTriangle数を取得します。
	/// </summary>
	public int TriangleCount
	{
		get
		{
			return m_TriangleVertexIndices.Length / 3;
		}
	}

	/// <summary>
	/// 隣接Triangleが存在しない境界Edge数を取得します。
	/// </summary>
	public int BoundaryEdgeCount
	{
		get
		{
			return m_BoundaryEdgeCount;
		}
	}

	/// <summary>
	/// 3枚以上のTriangleが接続されているEdge数を取得します。
	/// </summary>
	public int NonManifoldEdgeCount
	{
		get
		{
			return m_NonManifoldEdgeCount;
		}
	}

	/// <summary>
	/// 面積を持たないTriangle数を取得します。
	/// </summary>
	public int DegenerateTriangleCount
	{
		get
		{
			return m_DegenerateTriangleCount;
		}
	}

	/// <summary>
	/// Winding不整合などによって
	/// 正常に分類できなかったEdge数を取得します。
	/// </summary>
	public int InvalidEdgeCount
	{
		get
		{
			return m_InvalidEdgeCount;
		}
	}

	/// <summary>
	/// 平坦なEdge数を取得します。
	/// </summary>
	public int FlatEdgeCount
	{
		get
		{
			return m_FlatEdgeCount;
		}
	}

	/// <summary>
	/// 滑らかな曲面として扱われるEdge数を取得します。
	/// </summary>
	public int SmoothEdgeCount
	{
		get
		{
			return m_SmoothEdgeCount;
		}
	}

	/// <summary>
	/// 凸Edge数を取得します。
	/// </summary>
	public int ConvexEdgeCount
	{
		get
		{
			return m_ConvexEdgeCount;
		}
	}

	/// <summary>
	/// 凹Edge数を取得します。
	/// </summary>
	public int ConcaveEdgeCount
	{
		get
		{
			return m_ConcaveEdgeCount;
		}
	}

	/// <summary>
	/// Meshの符号付き体積を取得します。
	/// </summary>
	public float SignedVolume
	{
		get
		{
			return m_SignedVolume;
		}
	}

	/// <summary>
	/// TriangleのWindingが外向きであると
	/// 判定された場合はtrueです。
	/// </summary>
	public bool HasOutwardWinding
	{
		get
		{
			return m_HasOutwardWinding;
		}
	}

	/// <summary>
	/// Meshが閉じたManifold構造であるか取得します。
	/// </summary>
	public bool IsClosedManifold
	{
		get
		{
			return m_BoundaryEdgeCount == 0 &&
				m_NonManifoldEdgeCount == 0;
		}
	}

	/// <summary>
	/// 現在のSurface Physicsで使用可能な
	/// Mesh構造であるか取得します。
	/// </summary>
	public bool IsSupportedSurface
	{
		get
		{
			return IsClosedManifold &&
				m_DegenerateTriangleCount == 0 &&
				m_InvalidEdgeCount == 0 &&
				m_HasOutwardWinding;
		}
	}

	/// <summary>
	/// MeshからSurface Physics用の構造情報を生成します。
	/// </summary>
	/// <param name="mesh">
	/// BallSurfaceとして使用するMeshです。
	/// </param>
	public BallSurfaceMeshData(Mesh mesh)
	{
		if (mesh == null)
		{
			throw new ArgumentNullException(
				nameof(mesh));
		}

		m_Vertices =
			mesh.vertices;

		m_TriangleVertexIndices =
			mesh.triangles;

		int triangleCount =
			m_TriangleVertexIndices.Length / 3;

		m_TriangleNormals =
			new Vector3[triangleCount];

		m_TriangleCornerNormals =
			new Vector3[triangleCount * 3];

		m_NeighborTriangleIndices =
			new int[triangleCount * 3];

		m_NeighborEdgeIndices =
			new int[triangleCount * 3];

		m_EdgeTypes =
			new EEdgeType[triangleCount * 3];

		for (int i = 0;
			i < m_NeighborTriangleIndices.Length;
			i++)
		{
			m_NeighborTriangleIndices[i] =
				-1;

			m_NeighborEdgeIndices[i] =
				-1;

			m_EdgeTypes[i] =
				EEdgeType.Boundary;
		}

		m_DegenerateTriangleCount =
			BuildTriangleNormals();

		m_SignedVolume =
			CalculateSignedVolume();

		m_HasOutwardWinding =
			m_SignedVolume >
			m_MinimumSignedVolume;

		Dictionary<EdgeKey, EdgeBuildState> edgeStates =
			BuildTriangleAdjacency();

		int boundaryEdgeCount =
			0;

		int nonManifoldEdgeCount =
			0;

		int invalidEdgeCount =
			0;

		int flatEdgeCount =
			0;

		int smoothEdgeCount =
			0;

		int convexEdgeCount =
			0;

		int concaveEdgeCount =
			0;

		foreach (KeyValuePair<EdgeKey, EdgeBuildState> pair
			in edgeStates)
		{
			EdgeBuildState edgeState =
				pair.Value;

			if (edgeState.m_Count == 1)
			{
				boundaryEdgeCount++;
				continue;
			}

			if (edgeState.m_Count > 2)
			{
				nonManifoldEdgeCount++;
				continue;
			}

			switch (edgeState.m_EdgeType)
			{
				case EEdgeType.Flat:
					flatEdgeCount++;
					break;

				case EEdgeType.Smooth:
					smoothEdgeCount++;
					break;

				case EEdgeType.Convex:
					convexEdgeCount++;
					break;

				case EEdgeType.Concave:
					concaveEdgeCount++;
					break;

				case EEdgeType.Invalid:
					invalidEdgeCount++;
					break;
			}
		}

		m_BoundaryEdgeCount =
			boundaryEdgeCount;

		m_NonManifoldEdgeCount =
			nonManifoldEdgeCount;

		m_InvalidEdgeCount =
			invalidEdgeCount;

		m_FlatEdgeCount =
			flatEdgeCount;

		m_SmoothEdgeCount =
			smoothEdgeCount;

		m_ConvexEdgeCount =
			convexEdgeCount;

		m_ConcaveEdgeCount =
			concaveEdgeCount;

		m_ConvexEdgesByVertex =
			BuildConvexEdgesByVertex();

		BuildSmoothCornerNormals();
	}

	/// <summary>
	/// 指定Triangleを構成する3頂点の
	/// ローカル座標を取得します。
	/// </summary>
	public void GetTriangleLocalVertices(
		int triangleIndex,
		out Vector3 vertexA,
		out Vector3 vertexB,
		out Vector3 vertexC)
	{
		int triangleStartIndex =
			triangleIndex * 3;

		vertexA =
			m_Vertices[
				m_TriangleVertexIndices[
					triangleStartIndex]];

		vertexB =
			m_Vertices[
				m_TriangleVertexIndices[
					triangleStartIndex + 1]];

		vertexC =
			m_Vertices[
				m_TriangleVertexIndices[
					triangleStartIndex + 2]];
	}

	/// <summary>
	/// 指定Triangleのローカル法線を取得します。
	/// </summary>
	public Vector3 GetTriangleLocalNormal(
		int triangleIndex)
	{
		return m_TriangleNormals[
			triangleIndex];
	}

	/// <summary>
	/// 指定Triangleの3頂点に対応する
	/// Smooth Normalをローカル座標で取得します。
	/// </summary>
	public void GetTriangleLocalCornerNormals(
		int triangleIndex,
		out Vector3 normalA,
		out Vector3 normalB,
		out Vector3 normalC)
	{
		int triangleStartIndex =
			triangleIndex * 3;

		normalA =
			m_TriangleCornerNormals[
				triangleStartIndex];

		normalB =
			m_TriangleCornerNormals[
				triangleStartIndex + 1];

		normalC =
			m_TriangleCornerNormals[
				triangleStartIndex + 2];
	}

	/// <summary>
	/// 指定Triangleの指定Edge形状を取得します。
	/// </summary>
	public EEdgeType GetEdgeType(
		int triangleIndex,
		int edgeIndex)
	{
		int index =
			GetTriangleEdgeArrayIndex(
				triangleIndex,
				edgeIndex);

		return m_EdgeTypes[
			index];
	}

	/// <summary>
	/// 指定Triangleの指定Edgeに接続されている
	/// 隣接Triangleを取得します。
	/// </summary>
	public bool TryGetNeighbor(
		int triangleIndex,
		int edgeIndex,
		out int neighborTriangleIndex,
		out int neighborEdgeIndex)
	{
		int index =
			GetTriangleEdgeArrayIndex(
				triangleIndex,
				edgeIndex);

		neighborTriangleIndex =
			m_NeighborTriangleIndices[
				index];

		neighborEdgeIndex =
			m_NeighborEdgeIndices[
				index];

		return neighborTriangleIndex >= 0;
	}

	/// <summary>
	/// 指定Triangleの指定Edgeを構成する
	/// 2頂点を取得します。
	/// </summary>
	public void GetEdgeLocalVertices(
		int triangleIndex,
		int edgeIndex,
		out Vector3 edgeStart,
		out Vector3 edgeEnd)
	{
		GetTriangleLocalVertices(
			triangleIndex,
			out Vector3 vertexA,
			out Vector3 vertexB,
			out Vector3 vertexC);

		switch (edgeIndex)
		{
			case 0:
				edgeStart =
					vertexA;

				edgeEnd =
					vertexB;
				break;

			case 1:
				edgeStart =
					vertexB;

				edgeEnd =
					vertexC;
				break;

			case 2:
				edgeStart =
					vertexC;

				edgeEnd =
					vertexA;
				break;

			default:
				throw new ArgumentOutOfRangeException(
					nameof(edgeIndex));
		}
	}

	/// <summary>
	/// 指定ローカル頂点へ接続しているConvex Edge一覧を取得します。
	/// </summary>
	/// <remarks>
	/// 返される配列はMesh構築時に生成されたCacheです。
	/// 呼び出しごとの新規配列生成やMesh全走査は行いません。
	/// </remarks>
	public bool TryGetIncidentConvexEdges(
		Vector3 localVertexPosition,
		out ConvexEdgeReference[] convexEdges)
	{
		VertexPositionKey vertexKey =
			new VertexPositionKey(
				localVertexPosition);

		return m_ConvexEdgesByVertex.TryGetValue(
			vertexKey,
			out convexEdges);
	}

	/// <summary>
	/// Convex Edgeを頂点位置ごとに事前Cacheします。
	/// </summary>
	private Dictionary<VertexPositionKey, ConvexEdgeReference[]>
		BuildConvexEdgesByVertex()
	{
		Dictionary<VertexPositionKey, List<ConvexEdgeReference>> buildMap =
			new Dictionary<VertexPositionKey, List<ConvexEdgeReference>>();

		HashSet<EdgeKey> registeredEdges =
			new HashSet<EdgeKey>();

		for (int triangleIndex = 0;
			triangleIndex < TriangleCount;
			triangleIndex++)
		{
			for (int edgeIndex = 0;
				edgeIndex < 3;
				edgeIndex++)
			{
				if (GetEdgeType(
						triangleIndex,
						edgeIndex) !=
					EEdgeType.Convex)
				{
					continue;
				}

				GetEdgeLocalVertices(
					triangleIndex,
					edgeIndex,
					out Vector3 edgeStart,
					out Vector3 edgeEnd);

				VertexPositionKey startKey =
					new VertexPositionKey(
						edgeStart);

				VertexPositionKey endKey =
					new VertexPositionKey(
						edgeEnd);

				EdgeKey edgeKey =
					new EdgeKey(
						startKey,
						endKey);

				// 同じ物理Edgeは隣接する2Triangleから見えるため、
				// 1本として1回だけ登録します。
				if (!registeredEdges.Add(
					edgeKey))
				{
					continue;
				}

				AddConvexEdgeReference(
					buildMap,
					startKey,
					new ConvexEdgeReference(
						triangleIndex,
						edgeIndex,
						edgeEnd));

				AddConvexEdgeReference(
					buildMap,
					endKey,
					new ConvexEdgeReference(
						triangleIndex,
						edgeIndex,
						edgeStart));
			}
		}

		Dictionary<VertexPositionKey, ConvexEdgeReference[]> result =
			new Dictionary<VertexPositionKey, ConvexEdgeReference[]>(
				buildMap.Count);

		foreach (KeyValuePair<VertexPositionKey, List<ConvexEdgeReference>> pair
			in buildMap)
		{
			result.Add(
				pair.Key,
				pair.Value.ToArray());
		}

		return result;
	}

	/// <summary>
	/// Convex Edgeの頂点Cacheへ1件追加します。
	/// </summary>
	private void AddConvexEdgeReference(
		Dictionary<VertexPositionKey, List<ConvexEdgeReference>> buildMap,
		VertexPositionKey vertexKey,
		ConvexEdgeReference edgeReference)
	{
		if (!buildMap.TryGetValue(
			vertexKey,
			out List<ConvexEdgeReference> edgeList))
		{
			edgeList =
				new List<ConvexEdgeReference>(
					4);

			buildMap.Add(
				vertexKey,
				edgeList);
		}

		edgeList.Add(
			edgeReference);
	}

	/// <summary>
	/// 各Triangleの法線を事前計算します。
	/// </summary>
	private int BuildTriangleNormals()
	{
		int degenerateTriangleCount =
			0;

		for (int triangleIndex = 0;
			triangleIndex < TriangleCount;
			triangleIndex++)
		{
			GetTriangleLocalVertices(
				triangleIndex,
				out Vector3 vertexA,
				out Vector3 vertexB,
				out Vector3 vertexC);

			Vector3 edgeAB =
				vertexB - vertexA;

			Vector3 edgeAC =
				vertexC - vertexA;

			Vector3 normal =
				Vector3.Cross(
					edgeAB,
					edgeAC);

			if (normal.sqrMagnitude <=
				Mathf.Epsilon)
			{
				m_TriangleNormals[
					triangleIndex] =
					Vector3.zero;

				degenerateTriangleCount++;
				continue;
			}

			m_TriangleNormals[
				triangleIndex] =
				normal.normalized;
		}

		return degenerateTriangleCount;
	}

	/// <summary>
	/// 閉じたMeshのTriangle Windingから
	/// 符号付き体積を計算します。
	/// </summary>
	/// <remarks>
	/// 外向きWindingの閉じたMeshでは
	/// 正の値になることを前提とします。
	/// </remarks>
	private float CalculateSignedVolume()
	{
		double signedVolumeTimesSix =
			0.0;

		for (int triangleIndex = 0;
			triangleIndex < TriangleCount;
			triangleIndex++)
		{
			GetTriangleLocalVertices(
				triangleIndex,
				out Vector3 vertexA,
				out Vector3 vertexB,
				out Vector3 vertexC);

			signedVolumeTimesSix +=
				Vector3.Dot(
					vertexA,
					Vector3.Cross(
						vertexB,
						vertexC));
		}

		return (float)(
			signedVolumeTimesSix /
			6.0);
	}

	/// <summary>
	/// 全TriangleのEdgeを調べ、
	/// 隣接Triangle情報とEdge形状を構築します。
	/// </summary>
	private Dictionary<EdgeKey, EdgeBuildState>
		BuildTriangleAdjacency()
	{
		Dictionary<EdgeKey, EdgeBuildState> edgeStates =
			new Dictionary<EdgeKey, EdgeBuildState>(
				TriangleCount * 3);

		for (int triangleIndex = 0;
			triangleIndex < TriangleCount;
			triangleIndex++)
		{
			GetTriangleLocalVertices(
				triangleIndex,
				out Vector3 vertexA,
				out Vector3 vertexB,
				out Vector3 vertexC);

			RegisterEdge(
				triangleIndex,
				0,
				vertexA,
				vertexB,
				edgeStates);

			RegisterEdge(
				triangleIndex,
				1,
				vertexB,
				vertexC,
				edgeStates);

			RegisterEdge(
				triangleIndex,
				2,
				vertexC,
				vertexA,
				edgeStates);
		}

		return edgeStates;
	}

	/// <summary>
	/// 1本のEdgeを隣接情報へ登録します。
	/// </summary>
	private void RegisterEdge(
		int triangleIndex,
		int edgeIndex,
		Vector3 edgeStart,
		Vector3 edgeEnd,
		Dictionary<EdgeKey, EdgeBuildState> edgeStates)
	{
		VertexPositionKey startKey =
			new VertexPositionKey(
				edgeStart);

		VertexPositionKey endKey =
			new VertexPositionKey(
				edgeEnd);

		EdgeKey edgeKey =
			new EdgeKey(
				startKey,
				endKey);

		if (!edgeStates.TryGetValue(
			edgeKey,
			out EdgeBuildState edgeState))
		{
			edgeStates.Add(
				edgeKey,
				new EdgeBuildState(
					triangleIndex,
					edgeIndex,
					startKey,
					endKey));

			return;
		}

		// 2枚目のTriangleが見つかった場合、
		// 双方向の隣接情報を設定します。
		if (edgeState.m_Count == 1)
		{
			SetNeighbor(
				edgeState.m_FirstTriangleIndex,
				edgeState.m_FirstEdgeIndex,
				triangleIndex,
				edgeIndex);

			SetNeighbor(
				triangleIndex,
				edgeIndex,
				edgeState.m_FirstTriangleIndex,
				edgeState.m_FirstEdgeIndex);

			EEdgeType edgeType;

			bool hasOppositeWinding =
				edgeState.m_FirstStartKey.Equals(
					endKey) &&
				edgeState.m_FirstEndKey.Equals(
					startKey);

			if (!hasOppositeWinding)
			{
				edgeType =
					EEdgeType.Invalid;
			}
			else
			{
				edgeType =
					CalculateSharedEdgeType(
						edgeState.m_FirstTriangleIndex,
						edgeState.m_FirstEdgeIndex,
						triangleIndex,
						edgeIndex);
			}

			SetEdgeType(
				edgeState.m_FirstTriangleIndex,
				edgeState.m_FirstEdgeIndex,
				edgeType);

			SetEdgeType(
				triangleIndex,
				edgeIndex,
				edgeType);

			edgeState.m_SecondTriangleIndex =
				triangleIndex;

			edgeState.m_SecondEdgeIndex =
				edgeIndex;

			edgeState.m_EdgeType =
				edgeType;

			edgeState.m_Count =
				2;

			edgeStates[
				edgeKey] =
				edgeState;

			return;
		}

		// 3枚以上のTriangleが同じEdgeへ接続される場合は
		// 進行先を一意に決定できないため、
		// Non-Manifold Edgeとして扱います。
		if (edgeState.m_Count == 2)
		{
			ClearNeighbor(
				edgeState.m_FirstTriangleIndex,
				edgeState.m_FirstEdgeIndex);

			ClearNeighbor(
				edgeState.m_SecondTriangleIndex,
				edgeState.m_SecondEdgeIndex);

			SetEdgeType(
				edgeState.m_FirstTriangleIndex,
				edgeState.m_FirstEdgeIndex,
				EEdgeType.NonManifold);

			SetEdgeType(
				edgeState.m_SecondTriangleIndex,
				edgeState.m_SecondEdgeIndex,
				EEdgeType.NonManifold);
		}

		SetEdgeType(
			triangleIndex,
			edgeIndex,
			EEdgeType.NonManifold);

		edgeState.m_EdgeType =
			EEdgeType.NonManifold;

		edgeState.m_Count++;

		edgeStates[
			edgeKey] =
				edgeState;
	}

	/// <summary>
	/// 2つのTriangleが共有しているEdgeの形状を判定します。
	/// </summary>
	/// <remarks>
	/// Edge方向と法線のCross Productによる符号判定は、
	/// Triangleの頂点配置やEdge方向に影響されやすいため使用しません。
	///
	/// 代わりに、それぞれのTriangle Planeに対して
	/// 相手Triangleの反対側頂点がどちら側に存在するかを調べます。
	///
	/// Outward Windingされた閉じたMeshでは、
	///
	/// ・両方の反対頂点がSurface内部側 → Convex
	/// ・両方がほぼPlane上              → Flat
	/// ・両方がSurface外部側            → Concave
	///
	/// となります。
	/// </remarks>
	private EEdgeType CalculateSharedEdgeType(
		int firstTriangleIndex,
		int firstEdgeIndex,
		int secondTriangleIndex,
		int secondEdgeIndex)
	{
		Vector3 firstNormal =
			GetTriangleLocalNormal(
				firstTriangleIndex);

		Vector3 secondNormal =
			GetTriangleLocalNormal(
				secondTriangleIndex);

		if (firstNormal.sqrMagnitude <=
				Mathf.Epsilon ||
			secondNormal.sqrMagnitude <=
				Mathf.Epsilon)
		{
			return EEdgeType.Invalid;
		}

		float normalDot =
			Mathf.Clamp(
				Vector3.Dot(
					firstNormal,
					secondNormal),
				-1.0f,
				1.0f);

		if (normalDot >=
			m_FlatNormalDotThreshold)
		{
			return EEdgeType.Flat;
		}

		float normalAngle =
			Mathf.Acos(
				normalDot) *
			Mathf.Rad2Deg;

		if (normalAngle <=
			m_SmoothEdgeMaximumAngle)
		{
			return EEdgeType.Smooth;
		}

		GetEdgeLocalVertices(
			firstTriangleIndex,
			firstEdgeIndex,
			out Vector3 edgeStart,
			out _);

		Vector3 firstOppositeVertex =
			GetOppositeLocalVertex(
				firstTriangleIndex,
				firstEdgeIndex);

		Vector3 secondOppositeVertex =
			GetOppositeLocalVertex(
				secondTriangleIndex,
				secondEdgeIndex);

		float secondVertexPlaneDistance =
			Vector3.Dot(
				secondOppositeVertex -
					edgeStart,
				firstNormal);

		float firstVertexPlaneDistance =
			Vector3.Dot(
				firstOppositeVertex -
					edgeStart,
				secondNormal);

		if (firstVertexPlaneDistance <=
				m_EdgePlaneTolerance &&
			secondVertexPlaneDistance <=
				m_EdgePlaneTolerance)
		{
			return EEdgeType.Convex;
		}

		if (firstVertexPlaneDistance >=
				-m_EdgePlaneTolerance &&
			secondVertexPlaneDistance >=
				-m_EdgePlaneTolerance)
		{
			return EEdgeType.Concave;
		}

		return EEdgeType.Invalid;
	}

	/// <summary>
	/// Flat / Smooth Edgeだけを辿ってTriangle法線を平均し、
	/// 各Triangle頂点の連続したSurface Normalを構築します。
	/// </summary>
	private void BuildSmoothCornerNormals()
	{
		int[] visitStamps =
			new int[TriangleCount];

		int[] triangleQueue =
			new int[TriangleCount];

		int visitStamp =
			0;

		for (int triangleIndex = 0;
			triangleIndex < TriangleCount;
			triangleIndex++)
		{
			GetTriangleLocalVertices(
				triangleIndex,
				out Vector3 vertexA,
				out Vector3 vertexB,
				out Vector3 vertexC);

			BuildSmoothCornerNormal(
				triangleIndex,
				0,
				vertexA,
				visitStamps,
				triangleQueue,
				ref visitStamp);

			BuildSmoothCornerNormal(
				triangleIndex,
				1,
				vertexB,
				visitStamps,
				triangleQueue,
				ref visitStamp);

			BuildSmoothCornerNormal(
				triangleIndex,
				2,
				vertexC,
				visitStamps,
				triangleQueue,
				ref visitStamp);
		}
	}

	/// <summary>
	/// 1つのTriangle頂点について、
	/// Sharp Edgeを越えずに接続しているTriangle法線を平均します。
	/// </summary>
	private void BuildSmoothCornerNormal(
		int sourceTriangleIndex,
		int sourceCornerIndex,
		Vector3 vertexPosition,
		int[] visitStamps,
		int[] triangleQueue,
		ref int visitStamp)
	{
		visitStamp++;

		if (visitStamp == int.MaxValue)
		{
			Array.Clear(
				visitStamps,
				0,
				visitStamps.Length);

			visitStamp =
				1;
		}

		int queueReadIndex =
			0;

		int queueWriteIndex =
			0;

		triangleQueue[
			queueWriteIndex++] =
				sourceTriangleIndex;

		visitStamps[
			sourceTriangleIndex] =
			visitStamp;

		Vector3 accumulatedNormal =
			Vector3.zero;

		while (queueReadIndex <
			queueWriteIndex)
		{
			int triangleIndex =
				triangleQueue[
					queueReadIndex++];

			GetTriangleLocalVertices(
				triangleIndex,
				out Vector3 triangleVertexA,
				out Vector3 triangleVertexB,
				out Vector3 triangleVertexC);

			// 面積の大きいTriangleほど寄与を大きくするため、
			// 正規化前のCross Productを加算します。
			Vector3 weightedNormal =
				Vector3.Cross(
					triangleVertexB -
						triangleVertexA,
					triangleVertexC -
						triangleVertexA);

			if (weightedNormal.sqrMagnitude >
				Mathf.Epsilon)
			{
				accumulatedNormal +=
					weightedNormal;
			}

			for (int edgeIndex = 0;
				edgeIndex < 3;
				edgeIndex++)
			{
				EEdgeType edgeType =
					GetEdgeType(
						triangleIndex,
						edgeIndex);

				if (edgeType !=
						EEdgeType.Flat &&
					edgeType !=
						EEdgeType.Smooth)
				{
					continue;
				}

				GetEdgeLocalVertices(
					triangleIndex,
					edgeIndex,
					out Vector3 edgeStart,
					out Vector3 edgeEnd);

				if (!IsSameVertexPosition(
						vertexPosition,
						edgeStart) &&
					!IsSameVertexPosition(
						vertexPosition,
						edgeEnd))
				{
					continue;
				}

				if (!TryGetNeighbor(
					triangleIndex,
					edgeIndex,
					out int neighborTriangleIndex,
					out _))
				{
					continue;
				}

				if (visitStamps[
					neighborTriangleIndex] ==
					visitStamp)
				{
					continue;
				}

				if (!TriangleContainsVertexPosition(
					neighborTriangleIndex,
					vertexPosition))
				{
					continue;
				}

				visitStamps[
					neighborTriangleIndex] =
					visitStamp;

				triangleQueue[
					queueWriteIndex++] =
					neighborTriangleIndex;
			}
		}

		if (accumulatedNormal.sqrMagnitude <=
			Mathf.Epsilon)
		{
			accumulatedNormal =
				m_TriangleNormals[
					sourceTriangleIndex];
		}
		else
		{
			accumulatedNormal.Normalize();
		}

		m_TriangleCornerNormals[
			sourceTriangleIndex * 3 +
			sourceCornerIndex] =
			accumulatedNormal;
	}

	/// <summary>
	/// Triangleが指定位置の頂点を持っているか確認します。
	/// </summary>
	private bool TriangleContainsVertexPosition(
		int triangleIndex,
		Vector3 vertexPosition)
	{
		GetTriangleLocalVertices(
			triangleIndex,
			out Vector3 vertexA,
			out Vector3 vertexB,
			out Vector3 vertexC);

		return IsSameVertexPosition(
				vertexPosition,
				vertexA) ||
			IsSameVertexPosition(
				vertexPosition,
				vertexB) ||
			IsSameVertexPosition(
				vertexPosition,
				vertexC);
	}

	/// <summary>
	/// 2頂点をSurface Mesh用の位置許容値で比較します。
	/// </summary>
	private bool IsSameVertexPosition(
		Vector3 first,
		Vector3 second)
	{
		float toleranceSquared =
			m_VertexPositionTolerance *
			m_VertexPositionTolerance;

		return (
			first -
			second
		).sqrMagnitude <=
			toleranceSquared;
	}


	/// <summary>
	/// Triangle同士の隣接情報を設定します。
	/// </summary>
	private void SetNeighbor(
		int triangleIndex,
		int edgeIndex,
		int neighborTriangleIndex,
		int neighborEdgeIndex)
	{
		int index =
			GetTriangleEdgeArrayIndex(
				triangleIndex,
				edgeIndex);

		m_NeighborTriangleIndices[
			index] =
				neighborTriangleIndex;

		m_NeighborEdgeIndices[
			index] =
				neighborEdgeIndex;
	}

	/// <summary>
	/// 指定Edgeの隣接情報を解除します。
	/// </summary>
	private void ClearNeighbor(
		int triangleIndex,
		int edgeIndex)
	{
		int index =
			GetTriangleEdgeArrayIndex(
				triangleIndex,
				edgeIndex);

		m_NeighborTriangleIndices[
			index] =
				-1;

		m_NeighborEdgeIndices[
			index] =
				-1;
	}

	/// <summary>
	/// 指定Edgeの形状を設定します。
	/// </summary>
	private void SetEdgeType(
		int triangleIndex,
		int edgeIndex,
		EEdgeType edgeType)
	{
		int index =
			GetTriangleEdgeArrayIndex(
				triangleIndex,
				edgeIndex);

		m_EdgeTypes[
			index] =
				edgeType;
	}

	/// <summary>
	/// TriangleとEdgeの組み合わせを
	/// 配列Indexへ変換します。
	/// </summary>
	private int GetTriangleEdgeArrayIndex(
		int triangleIndex,
		int edgeIndex)
	{
		return triangleIndex * 3 +
			edgeIndex;
	}

	/// <summary>
	/// 頂点位置を誤差込みで比較するためのKeyです。
	/// </summary>
	private readonly struct VertexPositionKey :
		IEquatable<VertexPositionKey>
	{
		private readonly int m_X;
		private readonly int m_Y;
		private readonly int m_Z;

		public VertexPositionKey(
			Vector3 position)
		{
			m_X =
				Quantize(
					position.x);

			m_Y =
				Quantize(
					position.y);

			m_Z =
				Quantize(
					position.z);
		}

		public bool Equals(
			VertexPositionKey other)
		{
			return m_X == other.m_X &&
				m_Y == other.m_Y &&
				m_Z == other.m_Z;
		}

		public override bool Equals(
			object obj)
		{
			return obj is VertexPositionKey other &&
				Equals(
					other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash =
					17;

				hash =
					hash * 31 +
					m_X;

				hash =
					hash * 31 +
					m_Y;

				hash =
					hash * 31 +
					m_Z;

				return hash;
			}
		}

		public static int Compare(
			VertexPositionKey first,
			VertexPositionKey second)
		{
			if (first.m_X !=
				second.m_X)
			{
				return first.m_X.CompareTo(
					second.m_X);
			}

			if (first.m_Y !=
				second.m_Y)
			{
				return first.m_Y.CompareTo(
					second.m_Y);
			}

			return first.m_Z.CompareTo(
				second.m_Z);
		}

		private static int Quantize(
			float value)
		{
			return Mathf.RoundToInt(
				value /
				m_VertexPositionTolerance);
		}
	}

	/// <summary>
	/// 頂点Indexではなく頂点位置で
	/// 1本のEdgeを識別するためのKeyです。
	/// </summary>
	private readonly struct EdgeKey :
		IEquatable<EdgeKey>
	{
		private readonly VertexPositionKey m_First;
		private readonly VertexPositionKey m_Second;

		public EdgeKey(
			VertexPositionKey first,
			VertexPositionKey second)
		{
			if (VertexPositionKey.Compare(
				first,
				second) <= 0)
			{
				m_First =
					first;

				m_Second =
					second;
			}
			else
			{
				m_First =
					second;

				m_Second =
					first;
			}
		}

		public bool Equals(
			EdgeKey other)
		{
			return m_First.Equals(
				other.m_First) &&
				m_Second.Equals(
					other.m_Second);
		}

		public override bool Equals(
			object obj)
		{
			return obj is EdgeKey other &&
				Equals(
					other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				return m_First.GetHashCode() *
					397 ^
					m_Second.GetHashCode();
			}
		}
	}

	/// <summary>
	/// 隣接関係構築中だけ使用するEdge情報です。
	/// </summary>
	private struct EdgeBuildState
	{
		public int m_Count;

		public int m_FirstTriangleIndex;
		public int m_FirstEdgeIndex;

		public int m_SecondTriangleIndex;
		public int m_SecondEdgeIndex;

		public VertexPositionKey m_FirstStartKey;
		public VertexPositionKey m_FirstEndKey;

		public EEdgeType m_EdgeType;

		public EdgeBuildState(
			int triangleIndex,
			int edgeIndex,
			VertexPositionKey startKey,
			VertexPositionKey endKey)
		{
			m_Count =
				1;

			m_FirstTriangleIndex =
				triangleIndex;

			m_FirstEdgeIndex =
				edgeIndex;

			m_SecondTriangleIndex =
				-1;

			m_SecondEdgeIndex =
				-1;

			m_FirstStartKey =
				startKey;

			m_FirstEndKey =
				endKey;

			m_EdgeType =
				EEdgeType.Boundary;
		}
	}

	/// <summary>
	/// 指定Triangleの指定Edgeに含まれない、
	/// 反対側の頂点を取得します。
	/// </summary>
	/// <remarks>
	/// Triangle頂点をA,B,Cとした場合、
	///
	/// Edge 0 = A-B → C
	/// Edge 1 = B-C → A
	/// Edge 2 = C-A → B
	///
	/// を返します。
	/// </remarks>
	private Vector3 GetOppositeLocalVertex(
		int triangleIndex,
		int edgeIndex)
	{
		GetTriangleLocalVertices(
			triangleIndex,
			out Vector3 vertexA,
			out Vector3 vertexB,
			out Vector3 vertexC);

		switch (edgeIndex)
		{
			case 0:
				return vertexC;

			case 1:
				return vertexA;

			case 2:
				return vertexB;

			default:
				return Vector3.zero;
		}
	}
}
