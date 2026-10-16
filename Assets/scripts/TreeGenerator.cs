using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scatters primitive trees (cylinder trunk + sphere crowns) across the world: the inverse of TownGenerator.
/// - Trees only grow where towns DON'T: they stay out of every town and dead-end settlement,
///   keep clear of roads, and skip steep ground.
/// - Large-scale noise creates forests and clearings, so trees clump instead of forming an even carpet.
/// Deterministic per seed, streamed in/out around the player in square chunks.
/// </summary>
[RequireComponent(typeof(TerrainGenerator))]
[RequireComponent(typeof(RoadGenerator))]
[RequireComponent(typeof(TownGenerator))]
public class TreeGenerator : MonoBehaviour
{
    [Header("Trees")]
    [Tooltip("Optional. If empty, a brown material is created from the default primitive material.")]
    public Material trunkMaterial;
    [Tooltip("Optional. If empty, a green material is created from the default primitive material.")]
    public Material leafMaterial;
    public Vector2 trunkHeightRange = new Vector2(3f, 6f);
    public Vector2 trunkRadiusRange = new Vector2(0.25f, 0.45f);
    public Vector2 crownRadiusRange = new Vector2(1.8f, 3.5f);
    [Tooltip("Crown height as a multiple of crown width (1 = round, >1 = tall like a spruce).")]
    public Vector2 crownStretchRange = new Vector2(1.0f, 1.8f);
    [Tooltip("Number of stacked crown spheres per tree (inclusive range).")]
    public Vector2Int crownLayers = new Vector2Int(1, 3);
    public Color trunkColor = new Color(0.36f, 0.24f, 0.14f);
    public Color leafColor = new Color(0.16f, 0.42f, 0.18f);
    [Tooltip("Give trunks a collider so vehicles can hit them. Crowns never have colliders.")]
    public bool trunkColliders = true;
    [Tooltip("Combine each chunk's trees into static batches (much cheaper to render).")]
    public bool staticBatchChunks = true;

    [Header("Distribution")]
    [Tooltip("Size of one streamed chunk (m).")]
    public float chunkSize = 48f;
    [Tooltip("Spacing of candidate tree positions (m). Smaller = denser and slower.")]
    public float treeSpacing = 6f;
    [Range(0f, 0.5f), Tooltip("Random offset of each candidate, as a fraction of treeSpacing.")]
    public float jitter = 0.45f;
    [Tooltip("Scale of the forest/clearing noise. Smaller = bigger forests.")]
    public float forestScale = 0.004f;
    [Range(0f, 1f), Tooltip("Noise below this value is a clearing (only sparse trees).")]
    public float clearingThreshold = 0.35f;
    [Range(0f, 1f), Tooltip("Tree chance inside clearings. 0 = totally empty clearings.")]
    public float clearingDensity = 0.08f;

    [Header("Avoidance")]
    [Tooltip("Extra clearance between a tree's edge and the road edge.")]
    public float roadMargin = 3f;
    [Tooltip("Extra clearance around the outskirts of towns and dead ends.")]
    public float townClearance = 12f;
    [Tooltip("Skip ground steeper than this (degrees).")]
    public float maxSlope = 35f;
    [Tooltip("Only grow trees between these terrain heights (e.g. set the min above water level).")]
    public Vector2 allowedHeightRange = new Vector2(-10000f, 10000f);

    [Header("Streaming (WorldController sets these from viewDistance)")]
    public float spawnRadius = 200f;
    public float despawnRadius = 260f;
    public int maxChunksPerCall = 1;

    private TerrainGenerator terrain;
    private RoadGenerator roads;
    private TownGenerator towns;
    private Transform treeRoot;
    private Vector2 noiseOffset;

    private readonly Dictionary<Vector2Int, GameObject> activeChunks = new Dictionary<Vector2Int, GameObject>();
    private readonly List<Vector2Int> toRemove = new List<Vector2Int>();
    private readonly List<KeyValuePair<float, Vector2Int>> pending = new List<KeyValuePair<float, Vector2Int>>();
    private readonly List<Vector3> settlements = new List<Vector3>();   // x,y = centre, z = radius squared

    private void Awake()
    {
        terrain = GetComponent<TerrainGenerator>();
        roads = GetComponent<RoadGenerator>();
        towns = GetComponent<TownGenerator>();
        treeRoot = new GameObject("Trees").transform;

        noiseOffset = new Vector2(Hash01(7, 13, 21), Hash01(17, 3, 22)) * 10000f;

        if (trunkMaterial == null) trunkMaterial = MakeDefaultMaterial(trunkColor);
        if (leafMaterial == null) leafMaterial = MakeDefaultMaterial(leafColor);
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>Called every frame by WorldController.</summary>
    public void UpdateTrees(Vector3 playerPosition)
    {
        Vector2 p = new Vector2(playerPosition.x, playerPosition.z);

        // Remove far chunks
        toRemove.Clear();
        foreach (var kv in activeChunks)
        {
            if (Vector2.Distance(ChunkCenter(kv.Key), p) > despawnRadius)
                toRemove.Add(kv.Key);
        }
        foreach (var key in toRemove)
        {
            Destroy(activeChunks[key]);
            activeChunks.Remove(key);
        }

        // Find chunks that should exist
        pending.Clear();
        int range = Mathf.CeilToInt(spawnRadius / chunkSize) + 1;
        int cx = Mathf.FloorToInt(p.x / chunkSize);
        int cy = Mathf.FloorToInt(p.y / chunkSize);

        for (int x = cx - range; x <= cx + range; x++)
        {
            for (int y = cy - range; y <= cy + range; y++)
            {
                var key = new Vector2Int(x, y);
                if (activeChunks.ContainsKey(key)) continue;

                float d = Vector2.Distance(ChunkCenter(key), p);
                if (d <= spawnRadius)
                    pending.Add(new KeyValuePair<float, Vector2Int>(d, key));
            }
        }

        pending.Sort((a, b) => a.Key.CompareTo(b.Key));
        int count = Mathf.Min(maxChunksPerCall, pending.Count);
        for (int i = 0; i < count; i++)
            SpawnChunk(pending[i].Value);
    }

    // ------------------------------------------------------------------
    // Chunk generation
    // ------------------------------------------------------------------

    void SpawnChunk(Vector2Int key)
    {
        Vector2 min = new Vector2(key.x, key.y) * chunkSize;
        Vector2 max = min + Vector2.one * chunkSize;

        var rng = new System.Random((int)(HashU(key.x, key.y, 77) & 0x7FFFFFFF));

        Transform parent = new GameObject("TreeChunk_" + key.x + "_" + key.y).transform;
        parent.SetParent(treeRoot, false);

        CollectSettlements(min, max);

        int cells = Mathf.Max(1, Mathf.CeilToInt(chunkSize / treeSpacing));
        float cell = chunkSize / cells;

        for (int i = 0; i < cells; i++)
        {
            for (int j = 0; j < cells; j++)
            {
                Vector2 pos = min + new Vector2(i + 0.5f, j + 0.5f) * cell
                    + new Vector2(Rand(rng, -jitter, jitter), Rand(rng, -jitter, jitter)) * cell;

                // Pull every random value up front so a rejected tree doesn't shift the rest
                double chanceRoll = rng.NextDouble();
                float trunkH = Rand(rng, trunkHeightRange.x, trunkHeightRange.y);
                float trunkR = Rand(rng, trunkRadiusRange.x, trunkRadiusRange.y);
                float crownR = Rand(rng, crownRadiusRange.x, crownRadiusRange.y);
                float stretch = Rand(rng, crownStretchRange.x, crownStretchRange.y);
                int layers = rng.Next(crownLayers.x, crownLayers.y + 1);
                float yaw = Rand(rng, 0f, 360f);

                // Forests and clearings
                if (chanceRoll > Density(pos)) continue;

                if (InsideSettlement(pos)) continue;

                // Keep the whole crown clear of the road
                if (roads.DistanceToRoad(pos) < roads.roadWidth * 0.5f + roadMargin + crownR) continue;

                if (!GroundOk(pos, out float groundY)) continue;

                PlaceTree(parent, pos, groundY, trunkH, trunkR, crownR, stretch, layers, yaw);
            }
        }

        if (staticBatchChunks && parent.childCount > 0)
            StaticBatchingUtility.Combine(parent.gameObject);

        activeChunks.Add(key, parent.gameObject);
    }

    float Density(Vector2 pos)
    {
        float n = Mathf.PerlinNoise(pos.x * forestScale + noiseOffset.x, pos.y * forestScale + noiseOffset.y);
        if (n < clearingThreshold) return clearingDensity;
        return Mathf.Lerp(clearingDensity, 1f, Mathf.InverseLerp(clearingThreshold, 1f, n));
    }

    bool GroundOk(Vector2 pos, out float groundY)
    {
        groundY = terrain.GetHeightAt(pos.x, pos.y);
        if (groundY < allowedHeightRange.x || groundY > allowedHeightRange.y) return false;

        const float step = 1.5f;
        float hx = terrain.GetHeightAt(pos.x + step, pos.y);
        float hz = terrain.GetHeightAt(pos.x, pos.y + step);
        float gradient = Mathf.Sqrt((hx - groundY) * (hx - groundY) + (hz - groundY) * (hz - groundY)) / step;
        return Mathf.Atan(gradient) * Mathf.Rad2Deg <= maxSlope;
    }

    // ------------------------------------------------------------------
    // Settlement avoidance
    // ------------------------------------------------------------------

    /// <summary>Gathers every town / dead end whose area could touch this chunk.</summary>
    void CollectSettlements(Vector2 min, Vector2 max)
    {
        settlements.Clear();

        float townReach = roads.roadSpacing * 0.5f + towns.buildingFootprintRange.y + townClearance;
        float deadReach = DeadEndRadius() + townClearance;
        float pad = Mathf.Max(townReach, deadReach);

        int x0 = Mathf.FloorToInt((min.x - pad) / roads.roadSpacing);
        int x1 = Mathf.CeilToInt((max.x + pad) / roads.roadSpacing);
        int y0 = Mathf.FloorToInt((min.y - pad) / roads.roadSpacing);
        int y1 = Mathf.CeilToInt((max.y + pad) / roads.roadSpacing);

        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                int degree = roads.GetNodeDegree(x, y);
                if (degree == 0) continue;

                float r;
                if (degree == 1)
                {
                    r = DeadEndRadius();
                }
                else
                {
                    if (!towns.IsTown(x, y)) continue;
                    r = Mathf.Min(towns.baseRadius + degree * towns.radiusPerRoad, roads.roadSpacing * 0.5f)
                        + towns.buildingFootprintRange.y;
                }

                r += townClearance;
                Vector2 c = roads.GetNodePosition(x, y);
                settlements.Add(new Vector3(c.x, c.y, r * r));
            }
        }
    }

    float DeadEndRadius()
    {
        // Houses sit up to two rows beyond the road end
        return towns.deadEndDistance + towns.houseSpacing + towns.houseFootprintRange.y;
    }

    bool InsideSettlement(Vector2 pos)
    {
        for (int i = 0; i < settlements.Count; i++)
        {
            Vector3 s = settlements[i];
            float dx = pos.x - s.x;
            float dy = pos.y - s.y;
            if (dx * dx + dy * dy < s.z) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Tree construction
    // ------------------------------------------------------------------

    void PlaceTree(Transform parent, Vector2 pos, float groundY, float trunkH, float trunkR,
                   float crownR, float stretch, int layers, float yaw)
    {
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

        // Trunk (cylinder primitive: radius 0.5, height 2). Sunk a little so slopes show no gap.
        const float sink = 0.3f;
        float trunkFullH = trunkH + sink;
        GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.name = "Trunk";
        trunk.transform.SetParent(parent, false);
        trunk.transform.SetPositionAndRotation(new Vector3(pos.x, groundY - sink + trunkFullH * 0.5f, pos.y), rot);
        trunk.transform.localScale = new Vector3(trunkR * 2f, trunkFullH * 0.5f, trunkR * 2f);
        trunk.GetComponent<Renderer>().sharedMaterial = trunkMaterial;
        if (!trunkColliders) Destroy(trunk.GetComponent<Collider>());

        // Crown layers: each one narrower and higher than the last
        float baseY = groundY + trunkH * 0.7f;
        for (int k = 0; k < layers; k++)
        {
            float t = layers == 1 ? 0f : k / (float)(layers - 1);
            float width = crownR * 2f * Mathf.Lerp(1f, 0.55f, t);
            float height = width * stretch;

            GameObject crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.name = "Crown";
            crown.transform.SetParent(parent, false);
            crown.transform.SetPositionAndRotation(
                new Vector3(pos.x, baseY + height * 0.4f + k * crownR * stretch * 0.9f, pos.y), rot);
            crown.transform.localScale = new Vector3(width, height, width);
            crown.GetComponent<Renderer>().sharedMaterial = leafMaterial;
            Destroy(crown.GetComponent<Collider>());
        }
    }

    Material MakeDefaultMaterial(Color color)
    {
        // Copy the default primitive material so it matches whichever render pipeline is in use
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Material m = new Material(temp.GetComponent<Renderer>().sharedMaterial);
        m.color = color;
        Destroy(temp);
        return m;
    }

    // ------------------------------------------------------------------

    Vector2 ChunkCenter(Vector2Int key)
    {
        return (new Vector2(key.x, key.y) + Vector2.one * 0.5f) * chunkSize;
    }

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
