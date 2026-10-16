using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a deterministic, curvy road network as meshes (no prefabs).
/// Nodes sit on a jittered grid. Edges between neighbouring nodes become
/// curved road segments that are streamed in/out around the player.
/// </summary>
[RequireComponent(typeof(TerrainGenerator))]
public class RoadGenerator : MonoBehaviour
{
    [Header("Network")]
    public int seed = 12345;
    [Tooltip("Distance between intersections (grid cell size).")]
    public float roadSpacing = 120f;
    [Range(0f, 0.4f), Tooltip("How far intersections wander from the grid, as a fraction of spacing.")]
    public float nodeJitter = 0.25f;
    [Range(0f, 1f), Tooltip("Chance that a road exists between two neighbouring nodes.")]
    public float edgeChance = 0.8f;
    [Range(0f, 0.5f), Tooltip("How much roads bend, as a fraction of segment length.")]
    public float curveAmount = 0.2f;

    [Header("Road Shape")]
    public float roadWidth = 8f;
    [Tooltip("Distance between mesh samples along the road.")]
    public float sampleStep = 2f;
    [Tooltip("Lifts the road above the terrain to avoid z-fighting.")]
    public float heightOffset = 0.15f;
    [Tooltip("World metres per texture repeat along the road (V axis).")]
    public float textureTileLength = 8f;
    [Tooltip("Distance over which a road eases from the shared intersection height to its own terrain-following height.")]
    public float junctionBlendLength = 20f;
    [Tooltip("Optional. If empty, a plain grey material is created.")]
    public Material roadMaterial;

    [Header("Streaming")]
    public float generateRadius = 250f;
    public float despawnRadius = 320f;
    [Tooltip("Limits mesh builds per UpdateRoads call to avoid frame spikes.")]
    public int maxBuildsPerCall = 2;

    private TerrainGenerator terrain;
    private Transform roadRoot;
    private Material fallbackMaterial;

    // Key: (nodeX, nodeY, direction) where direction 0 = towards +X, 1 = towards +Y(Z)
    private readonly Dictionary<Vector3Int, GameObject> activeSegments = new Dictionary<Vector3Int, GameObject>();
    private readonly List<KeyValuePair<float, Vector3Int>> pending = new List<KeyValuePair<float, Vector3Int>>();
    private readonly List<Vector3Int> toRemove = new List<Vector3Int>();

    private void Awake()
    {
        terrain = GetComponent<TerrainGenerator>();
        roadRoot = new GameObject("Roads").transform;
    }

    private void OnDestroy()
    {
        foreach (var kv in activeSegments) DestroySegment(kv.Value);
        activeSegments.Clear();
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>Called every frame by WorldController.</summary>
    public void UpdateRoads(Vector3 playerPosition)
    {
        Vector2 p = new Vector2(playerPosition.x, playerPosition.z);

        // 1. Remove far-away segments
        toRemove.Clear();
        foreach (var kv in activeSegments)
        {
            if (SegmentDistance(kv.Key, p) > despawnRadius)
                toRemove.Add(kv.Key);
        }
        foreach (var key in toRemove)
        {
            DestroySegment(activeSegments[key]);
            activeSegments.Remove(key);
        }

        // 2. Find missing segments near the player
        pending.Clear();
        int range = Mathf.CeilToInt(generateRadius / roadSpacing) + 1;
        int cx = Mathf.RoundToInt(p.x / roadSpacing);
        int cy = Mathf.RoundToInt(p.y / roadSpacing);

        for (int x = cx - range; x <= cx + range; x++)
        {
            for (int y = cy - range; y <= cy + range; y++)
            {
                for (int dir = 0; dir < 2; dir++)
                {
                    var key = new Vector3Int(x, y, dir);
                    if (activeSegments.ContainsKey(key)) continue;
                    if (!EdgeExists(key)) continue;

                    float d = SegmentDistance(key, p);
                    if (d <= generateRadius)
                        pending.Add(new KeyValuePair<float, Vector3Int>(d, key));
                }
            }
        }

        // 3. Build nearest first, a few per call
        pending.Sort((a, b) => a.Key.CompareTo(b.Key));
        int builds = Mathf.Min(maxBuildsPerCall, pending.Count);
        for (int i = 0; i < builds; i++)
        {
            Vector3Int key = pending[i].Value;
            activeSegments.Add(key, BuildSegment(key));
        }
    }

    /// <summary>World-space XZ position of an intersection. Useful for placing towns.</summary>
    public Vector2 GetNodePosition(int x, int y)
    {
        float jx = (Hash01(x, y, 1) - 0.5f) * 2f * nodeJitter * roadSpacing;
        float jy = (Hash01(x, y, 2) - 0.5f) * 2f * nodeJitter * roadSpacing;
        return new Vector2(x * roadSpacing + jx, y * roadSpacing + jy);
    }

    /// <summary>True if the node has at least one road attached.</summary>
    public bool NodeHasRoad(int x, int y)
    {
        return EdgeExists(new Vector3Int(x, y, 0)) || EdgeExists(new Vector3Int(x, y, 1))
        || EdgeExists(new Vector3Int(x - 1, y, 0)) || EdgeExists(new Vector3Int(x, y - 1, 1));
    }

    /// <summary>Number of roads attached to a node: 0 = none, 1 = dead end, 2 = bend/straight, 3 = T-junction, 4 = crossing.</summary>
    public int GetNodeDegree(int x, int y)
    {
        int n = 0;
        if (EdgeExists(new Vector3Int(x, y, 0))) n++;
        if (EdgeExists(new Vector3Int(x, y, 1))) n++;
        if (EdgeExists(new Vector3Int(x - 1, y, 0))) n++;
        if (EdgeExists(new Vector3Int(x, y - 1, 1))) n++;
        return n;
    }

    /// <summary>Fills the list with the (XZ) direction of every road leaving a node. Returns the count.</summary>
    public int GetRoadDirections(int x, int y, List<Vector2> dirs)
    {
        dirs.Clear();
        Vector2 here = GetNodePosition(x, y);

        if (EdgeExists(new Vector3Int(x, y, 0))) dirs.Add((GetNodePosition(x + 1, y) - here).normalized);
        if (EdgeExists(new Vector3Int(x, y, 1))) dirs.Add((GetNodePosition(x, y + 1) - here).normalized);
        if (EdgeExists(new Vector3Int(x - 1, y, 0))) dirs.Add((GetNodePosition(x - 1, y) - here).normalized);
        if (EdgeExists(new Vector3Int(x, y - 1, 1))) dirs.Add((GetNodePosition(x, y - 1) - here).normalized);

        return dirs.Count;
    }

    /// <summary>
    /// Like GetRoadDirections, but returns the actual direction each road leaves the node
    /// (the curve's tangent) rather than the straight line to the neighbour. Returns the count.
    /// </summary>
    public int GetNodeArms(int x, int y, List<Vector2> dirs)
    {
        dirs.Clear();
        AddArm(new Vector3Int(x, y, 0), true, dirs);
        AddArm(new Vector3Int(x, y, 1), true, dirs);
        AddArm(new Vector3Int(x - 1, y, 0), false, dirs);
        AddArm(new Vector3Int(x, y - 1, 1), false, dirs);
        return dirs.Count;
    }

    void AddArm(Vector3Int key, bool nodeIsStart, List<Vector2> dirs)
    {
        if (!EdgeExists(key)) return;
        GetCurve(key, out Vector2 a, out Vector2 c, out Vector2 b);
        Vector2 d = nodeIsStart ? c - a : c - b;
        dirs.Add(d.normalized);
    }

    /// <summary>Height of the flat intersection surface at a node (the same value the road pads use).</summary>
    public float GetNodeHeight(int x, int y)
    {
        return NodeHeight(GetNodePosition(x, y));
    }

    /// <summary>Direction (XZ) from a node towards one of its connected neighbours. Handy for orienting the player.</summary>
    public bool TryGetRoadDirection(int x, int y, out Vector2 dir)
    {
        Vector2 here = GetNodePosition(x, y);
        dir = Vector2.right;

        if (EdgeExists(new Vector3Int(x, y, 0))) { dir = (GetNodePosition(x + 1, y) - here).normalized; return true; }
        if (EdgeExists(new Vector3Int(x, y, 1))) { dir = (GetNodePosition(x, y + 1) - here).normalized; return true; }
        if (EdgeExists(new Vector3Int(x - 1, y, 0))) { dir = (GetNodePosition(x - 1, y) - here).normalized; return true; }
        if (EdgeExists(new Vector3Int(x, y - 1, 1))) { dir = (GetNodePosition(x, y - 1) - here).normalized; return true; }
        return false;
    }

    // ------------------------------------------------------------------
    // Network definition (deterministic from seed)
    // ------------------------------------------------------------------

    bool EdgeExists(Vector3Int key)
    {
        return Hash01(key.x, key.y, 7 + key.z) < edgeChance;
    }

    void GetEndpoints(Vector3Int key, out Vector2 a, out Vector2 b)
    {
        a = GetNodePosition(key.x, key.y);
        b = key.z == 0 ? GetNodePosition(key.x + 1, key.y) : GetNodePosition(key.x, key.y + 1);
    }

    float SegmentDistance(Vector3Int key, Vector2 p)
    {
        GetEndpoints(key, out Vector2 a, out Vector2 b);
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    float Hash01(int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791) ^ (uint)(seed * 668265263);
            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    // ------------------------------------------------------------------
    // Mesh building
    // ------------------------------------------------------------------

    GameObject BuildSegment(Vector3Int key)
    {
        GetCurve(key, out Vector2 a, out Vector2 c, out Vector2 b);
        float len = (b - a).magnitude;

        int samples = Mathf.Max(3, Mathf.CeilToInt(len * 1.15f / sampleStep) + 1);
        var verts = new List<Vector3>(samples * 2 + 40);
        var uvs = new List<Vector2>(samples * 2 + 40);
        var tris = new List<int>(samples * 6 + 100);

        float half = roadWidth * 0.5f;
        float dist = 0f;
        Vector2 prev = a;

        // Shared heights at each end: every road meeting at a node uses the same value
        float hA = NodeHeight(a);
        float hB = NodeHeight(b);
        float blend = Mathf.Max(0.01f, Mathf.Min(junctionBlendLength, len * 0.45f));

        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)(samples - 1);
            float u = 1f - t;

            Vector2 pos = u * u * a + 2f * u * t * c + t * t * b;
            Vector2 tan = (2f * u * (c - a) + 2f * t * (b - c)).normalized;
            Vector2 right = new Vector2(tan.y, -tan.x);

            if (i > 0) dist += Vector2.Distance(prev, pos);
            prev = pos;

            Vector2 l = pos - right * half;
            Vector2 r = pos + right * half;

            // Follow the terrain in the middle, ease into the shared node height at each end
            float roadY = RoadHeight(pos, right, half);
            float dA = t * len;
            float dB = (1f - t) * len;
            roadY = Mathf.Lerp(hA, roadY, Mathf.SmoothStep(0f, 1f, dA / blend));
            roadY = Mathf.Lerp(hB, roadY, Mathf.SmoothStep(0f, 1f, dB / blend));

            verts.Add(new Vector3(l.x, roadY, l.y));
            verts.Add(new Vector3(r.x, roadY, r.y));
            float v = dist / textureTileLength;
            uvs.Add(new Vector2(0f, v));
            uvs.Add(new Vector2(1f, v));
        }

        for (int i = 0; i < samples - 1; i++)
        {
            int k = i * 2;
            tris.Add(k);     tris.Add(k + 2); tris.Add(k + 1);
            tris.Add(k + 1); tris.Add(k + 2); tris.Add(k + 3);
        }

        // Round pads at both ends cover the gaps where roads meet at angles
        AddPad(verts, uvs, tris, a, half * 1.15f, hA);
        AddPad(verts, uvs, tris, b, half * 1.15f, hB);

        var mesh = new Mesh { name = $"Road_{key.x}_{key.y}_{key.z}" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject(mesh.name);
        go.transform.SetParent(roadRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial();
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    /// <summary>
    /// One height per intersection, shared by every road and pad that touches it.
    /// Depends only on the node position, so all segments agree on it.
    /// </summary>
    float NodeHeight(Vector2 center)
    {
        float radius = roadWidth * 0.5f * 1.3f;
        float max = terrain.GetHeightAt(center.x, center.y);

        for (int i = 0; i < 8; i++)
        {
            float ang = i / 8f * Mathf.PI * 2f;
            float cos = Mathf.Cos(ang);
            float sin = Mathf.Sin(ang);

            // outer ring
            max = Mathf.Max(max, terrain.GetHeightAt(center.x + cos * radius, center.y + sin * radius));
            // inner ring
            max = Mathf.Max(max, terrain.GetHeightAt(center.x + cos * radius * 0.5f, center.y + sin * radius * 0.5f));
        }
        return max + heightOffset;
    }

    /// <summary>Highest terrain point across the road's width, so the road never sinks into a dip.</summary>
    float RoadHeight(Vector2 pos, Vector2 right, float half)
    {
        float max = float.MinValue;
        for (int k = -2; k <= 2; k++)
        {
            Vector2 s = pos + right * (half * k * 0.5f);
            max = Mathf.Max(max, terrain.GetHeightAt(s.x, s.y));
        }
        return max + heightOffset;
    }

    /// <summary>Quadratic Bezier control points of a road segment: start, control, end.</summary>
    void GetCurve(Vector3Int key, out Vector2 a, out Vector2 c, out Vector2 b)
    {
        GetEndpoints(key, out a, out b);
        Vector2 ab = b - a;
        float len = ab.magnitude;
        Vector2 perp = new Vector2(-ab.y, ab.x) / len;

        float bend = (Hash01(key.x, key.y, 10 + key.z) - 0.5f) * 2f * curveAmount * len;
        c = (a + b) * 0.5f + perp * bend;
    }

    /// <summary>Distance (XZ) from a point to the nearest road centreline. Used to keep buildings off roads.</summary>
    public float DistanceToRoad(Vector2 p)
    {
        const int curveSamples = 10;
        int cx = Mathf.RoundToInt(p.x / roadSpacing);
        int cy = Mathf.RoundToInt(p.y / roadSpacing);
        float best = float.MaxValue;

        for (int x = cx - 2; x <= cx + 2; x++)
        {
            for (int y = cy - 2; y <= cy + 2; y++)
            {
                for (int dir = 0; dir < 2; dir++)
                {
                    var key = new Vector3Int(x, y, dir);
                    if (!EdgeExists(key)) continue;

                    GetCurve(key, out Vector2 a, out Vector2 c, out Vector2 b);
                    Vector2 prev = a;
                    for (int i = 1; i <= curveSamples; i++)
                    {
                        float t = i / (float)curveSamples;
                        float u = 1f - t;
                        Vector2 q = u * u * a + 2f * u * t * c + t * t * b;
                        best = Mathf.Min(best, PointSegmentDistance(p, prev, q));
                        prev = q;
                    }
                }
            }
        }
        return best;
    }

    static float PointSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>Flat round pad at a fixed height, so every road meeting at a node shares the same surface.</summary>
    void AddPad(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector2 center, float radius, float y)
    {
        const int sides = 16;
        int centerIndex = verts.Count;

        verts.Add(new Vector3(center.x, y, center.y));
        uvs.Add(new Vector2(0.5f, 0.5f));

        for (int i = 0; i < sides; i++)
        {
            float ang = i / (float)sides * Mathf.PI * 2f;
            float x = center.x + Mathf.Cos(ang) * radius;
            float z = center.y + Mathf.Sin(ang) * radius;
            verts.Add(new Vector3(x, y, z));
            uvs.Add(new Vector2(0.5f + Mathf.Cos(ang) * 0.5f, 0.5f + Mathf.Sin(ang) * 0.5f));
        }

        for (int i = 0; i < sides; i++)
        {
            int cur = centerIndex + 1 + i;
            int next = centerIndex + 1 + (i + 1) % sides;
            tris.Add(centerIndex); tris.Add(next); tris.Add(cur);
        }
    }

    Material GetMaterial()
    {
        if (roadMaterial != null) return roadMaterial;

        if (fallbackMaterial == null)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            fallbackMaterial = new Material(s) { color = new Color(0.2f, 0.2f, 0.22f) };
        }
        return fallbackMaterial;
    }

    void DestroySegment(GameObject go)
    {
        if (go == null) return;
        var mf = go.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
        Destroy(go);
    }
}
