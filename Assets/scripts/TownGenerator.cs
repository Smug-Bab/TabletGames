using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns primitive cube buildings on a grid of lots around road intersections.
/// - Towns form at intersections with 2+ roads. Radius grows with road count.
/// - Lots are skipped if they sit on a road, on steep ground, or in a neighbouring town's territory,
///   so each town fills whatever space its surroundings allow.
/// - Dead-end roads always get a few small houses beyond the end.
/// Deterministic per seed, streamed in/out around the player.
/// </summary>
[RequireComponent(typeof(TerrainGenerator))]
[RequireComponent(typeof(RoadGenerator))]
public class TownGenerator : MonoBehaviour
{
    [Header("Buildings")]
    [Tooltip("Optional material for the cube buildings.")]
    public Material buildingMaterial;
    public Vector2 buildingHeightRange = new Vector2(6f, 25f);
    public Vector2 buildingFootprintRange = new Vector2(8f, 16f);

    [Header("Town Layout (grid of lots)")]
    [Range(0f, 1f), Tooltip("Chance that an intersection with 2+ roads becomes a town.")]
    public float townChance = 0.5f;
    [Tooltip("Distance between lot centres. Must be larger than the footprint max plus street width.")]
    public float lotSpacing = 26f;
    [Tooltip("Gap kept free between neighbouring buildings (the streets).")]
    public float streetWidth = 8f;
    [Tooltip("Town radius = base + roads * perRoad (capped at half the road spacing).")]
    public float baseRadius = 18f;
    public float radiusPerRoad = 10f;
    [Range(0f, 1f), Tooltip("Chance a lot at the town edge is filled (centre is always 100%).")]
    public float edgeFill = 0.55f;
    [Range(0.1f, 1f), Tooltip("Height multiplier at the town edge (centre is 1).")]
    public float edgeHeightScale = 0.35f;
    public float lotJitter = 1.5f;
    [Tooltip("Extra clearance between a building and the nearest road edge.")]
    public float roadMargin = 3f;
    [Tooltip("Skip lots where the ground varies more than this across the footprint.")]
    public float maxHeightDifference = 3.5f;

    [Header("Dead Ends")]
    public Vector2Int deadEndHouses = new Vector2Int(1, 3);
    public Vector2 houseFootprintRange = new Vector2(6f, 10f);
    public Vector2 houseHeightRange = new Vector2(4f, 8f);
    public float houseSpacing = 14f;
    [Tooltip("Distance from the road end to the first house.")]
    public float deadEndDistance = 14f;

    [Header("Streaming (WorldController sets these from viewDistance)")]
    public float spawnRadius = 200f;
    public float despawnRadius = 260f;
    public int maxTownsPerCall = 1;

    // (row beyond the road end, column sideways)
    private static readonly Vector2Int[] DeadEndSlots =
    {
        new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(1, -1)
    };

    private TerrainGenerator terrain;
    private RoadGenerator roads;
    private Transform townRoot;

    private readonly Dictionary<Vector2Int, GameObject> activeTowns = new Dictionary<Vector2Int, GameObject>();
    private readonly List<Vector2Int> toRemove = new List<Vector2Int>();
    private readonly List<KeyValuePair<float, Vector2Int>> pending = new List<KeyValuePair<float, Vector2Int>>();
    private readonly List<Vector2> dirBuffer = new List<Vector2>();

    private void Awake()
    {
        terrain = GetComponent<TerrainGenerator>();
        roads = GetComponent<RoadGenerator>();
        townRoot = new GameObject("Towns").transform;
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>True if the intersection at this grid node is a town (2+ roads, passes the chance roll).</summary>
    public bool IsTown(int x, int y)
    {
        return roads.GetNodeDegree(x, y) >= 2 && Hash01(x, y, 5) < townChance;
    }

    /// <summary>True if exactly one road ends at this node.</summary>
    public bool IsDeadEnd(int x, int y)
    {
        return roads.GetNodeDegree(x, y) == 1;
    }

    /// <summary>Finds the town closest to a world XZ position (dead ends don't count).</summary>
    public bool FindNearestTown(Vector2 from, int searchNodes, out Vector2Int node)
    {
        node = Vector2Int.zero;
        float best = float.MaxValue;
        bool found = false;

        int cx = Mathf.RoundToInt(from.x / roads.roadSpacing);
        int cy = Mathf.RoundToInt(from.y / roads.roadSpacing);

        for (int x = cx - searchNodes; x <= cx + searchNodes; x++)
        {
            for (int y = cy - searchNodes; y <= cy + searchNodes; y++)
            {
                if (!IsTown(x, y)) continue;

                float d = (roads.GetNodePosition(x, y) - from).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    node = new Vector2Int(x, y);
                    found = true;
                }
            }
        }
        return found;
    }

    /// <summary>Called every frame by WorldController.</summary>
    public void UpdateTowns(Vector3 playerPosition)
    {
        Vector2 p = new Vector2(playerPosition.x, playerPosition.z);

        // Remove far settlements
        toRemove.Clear();
        foreach (var kv in activeTowns)
        {
            if (Vector2.Distance(roads.GetNodePosition(kv.Key.x, kv.Key.y), p) > despawnRadius)
                toRemove.Add(kv.Key);
        }
        foreach (var key in toRemove)
        {
            Destroy(activeTowns[key]);
            activeTowns.Remove(key);
        }

        // Find settlements that should exist
        pending.Clear();
        int range = Mathf.CeilToInt(spawnRadius / roads.roadSpacing) + 1;
        int cx = Mathf.RoundToInt(p.x / roads.roadSpacing);
        int cy = Mathf.RoundToInt(p.y / roads.roadSpacing);

        for (int x = cx - range; x <= cx + range; x++)
        {
            for (int y = cy - range; y <= cy + range; y++)
            {
                var key = new Vector2Int(x, y);
                if (activeTowns.ContainsKey(key)) continue;
                if (!IsTown(x, y) && !IsDeadEnd(x, y)) continue;

                float d = Vector2.Distance(roads.GetNodePosition(x, y), p);
                if (d <= spawnRadius)
                    pending.Add(new KeyValuePair<float, Vector2Int>(d, key));
            }
        }

        pending.Sort((a, b) => a.Key.CompareTo(b.Key));
        int count = Mathf.Min(maxTownsPerCall, pending.Count);
        for (int i = 0; i < count; i++)
            SpawnSettlement(pending[i].Value);
    }

    // ------------------------------------------------------------------
    // Layout
    // ------------------------------------------------------------------

    void SpawnSettlement(Vector2Int key)
    {
        int degree = roads.GetRoadDirections(key.x, key.y, dirBuffer);
        if (degree == 0) return;

        bool deadEnd = degree == 1;
        var rng = new System.Random((int)(HashU(key.x, key.y, 99) & 0x7FFFFFFF));
        Vector2 center = roads.GetNodePosition(key.x, key.y);

        Transform parent = new GameObject((deadEnd ? "DeadEnd_" : "Town_") + key.x + "_" + key.y).transform;
        parent.SetParent(townRoot, false);

        // The lot grid follows the first road leaving the intersection
        Vector2 axis = dirBuffer[0];
        Vector2 side = new Vector2(-axis.y, axis.x);
        Quaternion rot = Quaternion.LookRotation(new Vector3(axis.x, 0f, axis.y));

        if (deadEnd)
            BuildDeadEnd(parent, key, center, axis, side, rot, rng);
        else
            BuildTown(parent, key, center, axis, side, rot, degree, rng);

        activeTowns.Add(key, parent.gameObject);
    }

    void BuildTown(Transform parent, Vector2Int key, Vector2 center, Vector2 axis, Vector2 side,
                   Quaternion rot, int degree, System.Random rng)
    {
        float radius = Mathf.Min(baseRadius + degree * radiusPerRoad, roads.roadSpacing * 0.5f);
        int n = Mathf.CeilToInt(radius / lotSpacing);
        float maxFootprint = Mathf.Max(4f, lotSpacing - streetWidth);

        for (int i = -n; i <= n; i++)
        {
            for (int j = -n; j <= n; j++)
            {
                Vector2 local = new Vector2(i, j) * lotSpacing;
                float dist = local.magnitude;
                if (dist > radius) continue;
                float t = dist / radius;

                // Denser and taller towards the centre
                if (rng.NextDouble() > Mathf.Lerp(1f, edgeFill, t)) continue;

                float w = Mathf.Min(Rand(rng, buildingFootprintRange.x, buildingFootprintRange.y), maxFootprint);
                float d = Mathf.Min(Rand(rng, buildingFootprintRange.x, buildingFootprintRange.y), maxFootprint);
                float h = Mathf.Max(3f, Rand(rng, buildingHeightRange.x, buildingHeightRange.y) * Mathf.Lerp(1f, edgeHeightScale, t));

                Vector2 pos = center + axis * local.x + side * local.y
                + new Vector2(Rand(rng, -lotJitter, lotJitter), Rand(rng, -lotJitter, lotJitter));

                TryPlace(parent, key, center, pos, rot, w, d, h);
            }
        }
    }

    void BuildDeadEnd(Transform parent, Vector2Int key, Vector2 center, Vector2 axis, Vector2 side,
                      Quaternion rot, System.Random rng)
    {
        int count = Mathf.Min(rng.Next(deadEndHouses.x, deadEndHouses.y + 1), DeadEndSlots.Length);

        for (int i = 0; i < count; i++)
        {
            Vector2Int slot = DeadEndSlots[i];

            // Beyond the road end (opposite the road direction); rot already faces the road
            Vector2 pos = center - axis * (deadEndDistance + slot.x * houseSpacing) + side * (slot.y * houseSpacing);

            float w = Rand(rng, houseFootprintRange.x, houseFootprintRange.y);
            float d = Rand(rng, houseFootprintRange.x, houseFootprintRange.y);
            float h = Rand(rng, houseHeightRange.x, houseHeightRange.y);

            TryPlace(parent, key, center, pos, rot, w, d, h);
        }
    }

    // ------------------------------------------------------------------
    // Placement
    // ------------------------------------------------------------------

    bool TryPlace(Transform parent, Vector2Int key, Vector2 center, Vector2 pos, Quaternion rot,
                  float w, float d, float h)
    {
        // Stay inside this node's territory (don't invade a neighbouring town)
        float own = (pos - center).sqrMagnitude;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                Vector2 other = roads.GetNodePosition(key.x + dx, key.y + dy);
                if ((pos - other).sqrMagnitude < own) return false;
            }
        }

        // Keep clear of every road, curved ones included
        float halfDiag = 0.5f * Mathf.Sqrt(w * w + d * d);
        if (roads.DistanceToRoad(pos) < roads.roadWidth * 0.5f + roadMargin + halfDiag) return false;

        // Ground under the footprint must be reasonably flat
        Vector3 right = rot * Vector3.right;
        Vector3 fwd = rot * Vector3.forward;
        float minY = terrain.GetHeightAt(pos.x, pos.y);
        float maxY = minY;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 corner = right * (sx * w * 0.5f) + fwd * (sz * d * 0.5f);
                float y = terrain.GetHeightAt(pos.x + corner.x, pos.y + corner.z);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
        }
        if (maxY - minY > maxHeightDifference) return false;

        // Sunk 1m into the ground so slopes don't show gaps underneath
        float bottom = minY - 1f;
        float top = maxY + h;

        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "Building";
        box.transform.SetParent(parent, false);
        box.transform.position = new Vector3(pos.x, (top + bottom) * 0.5f, pos.y);
        box.transform.rotation = rot;
        box.transform.localScale = new Vector3(w, top - bottom, d);

        if (buildingMaterial != null)
            box.GetComponent<Renderer>().sharedMaterial = buildingMaterial;

        return true;
    }

    // ------------------------------------------------------------------

    static float Rand(System.Random rng, float min, float max)
    {
        return min + (float)rng.NextDouble() * (max - min);
    }

    uint HashU(int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393) ^ (uint)(y * 668265263) ^ ((uint)salt * 2246822519u) ^ (uint)(roads.seed * 1274126177);
            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;
            return h;
        }
    }

    float Hash01(int x, int y, int salt)
    {
        return (HashU(x, y, salt) & 0xFFFFFF) / (float)0x1000000;
    }
}
