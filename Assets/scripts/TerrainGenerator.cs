using System.Collections.Generic;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    public int chunkSize = 60;
    public float heightMultiplier = 25f;
    public float noiseScale = 0.003f;
    public Material terrainMaterial;

    [Header("Town Ground")]
    [Tooltip("Material used for ground in and around towns. Leave empty to disable.")]
    public Material townGroundMaterial;
    [Tooltip("How far the town ground extends beyond the town's building radius (m).")]
    public float townGroundPadding = 12f;
    [Range(0f, 0.6f), Tooltip("Makes the edge of the town ground irregular. 0 = perfect circle.")]
    public float edgeNoise = 0.25f;
    [Tooltip("Scale of the edge noise. Smaller = broader wobbles.")]
    public float edgeNoiseScale = 0.04f;
    [Tooltip("Also texture the ground around dead-end houses.")]
    public bool includeDeadEnds = true;
    [Tooltip("Meters per texture repeat. Keep the materials' Tiling at 1 and adjust this instead.")]
    public float uvTileSize = 10f;

    [Header("Streaming")]
    [Tooltip("Chunks loaded around the player. WorldController sets this from viewDistance.")]
    public int chunkRadius = 4;
    [Tooltip("Limits chunk builds per UpdateChunks call to avoid frame spikes.")]
    public int maxChunksPerCall = 2;

    // chunkSize should be divisible by this so the vertex grid lines up across chunks.
    private const int resolution = 20;

    private readonly Dictionary<Vector2Int, GameObject> activeChunks = new Dictionary<Vector2Int, GameObject>();
    private readonly List<Vector2Int> unloadList = new List<Vector2Int>();
    private readonly List<KeyValuePair<int, Vector2Int>> missing = new List<KeyValuePair<int, Vector2Int>>();
    private Transform chunkRoot;

    // Town ground lookup (optional: only used if these components exist)
    private RoadGenerator roads;
    private TownGenerator towns;
    private readonly List<Vector3> settlements = new List<Vector3>();   // x,y = centre, z = ground radius
    private readonly List<int> baseTris = new List<int>();
    private readonly List<int> townTris = new List<int>();

    private void Awake()
    {
        chunkRoot = new GameObject("TerrainChunks").transform;
        roads = GetComponent<RoadGenerator>();
        towns = GetComponent<TownGenerator>();
    }

    /// <summary>Streams chunks in/out around the player, a few per call.</summary>
    public void UpdateChunks(Vector3 playerPosition)
    {
        UpdateChunks(playerPosition, maxChunksPerCall);
    }

    /// <summary>Builds every missing chunk around the position immediately (use at startup).</summary>
    public void LoadAllNear(Vector3 position)
    {
        UpdateChunks(position, int.MaxValue);
    }

    void UpdateChunks(Vector3 playerPosition, int budget)
    {
        int pcx = Mathf.FloorToInt(playerPosition.x / chunkSize);
        int pcz = Mathf.FloorToInt(playerPosition.z / chunkSize);

        // Unload far chunks
        int unloadDist = chunkRadius + 2;
        unloadList.Clear();
        foreach (var kv in activeChunks)
        {
            if (Mathf.Abs(kv.Key.x - pcx) > unloadDist || Mathf.Abs(kv.Key.y - pcz) > unloadDist)
                unloadList.Add(kv.Key);
        }
        foreach (var coord in unloadList)
        {
            GameObject go = activeChunks[coord];
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(go);
            activeChunks.Remove(coord);
        }

        // Find missing chunks (roughly circular area)
        missing.Clear();
        int maxSq = chunkRadius * chunkRadius + chunkRadius;
        for (int x = -chunkRadius; x <= chunkRadius; x++)
        {
            for (int z = -chunkRadius; z <= chunkRadius; z++)
            {
                int sq = x * x + z * z;
                if (sq > maxSq) continue;

                var coord = new Vector2Int(pcx + x, pcz + z);
                if (!activeChunks.ContainsKey(coord))
                    missing.Add(new KeyValuePair<int, Vector2Int>(sq, coord));
            }
        }
        if (missing.Count == 0) return;

        // Nearest first
        missing.Sort((a, b) => a.Key.CompareTo(b.Key));
        int count = Mathf.Min(budget, missing.Count);
        for (int i = 0; i < count; i++)
            SpawnChunk(missing[i].Value);
    }

    /// <summary>Raw noise height at a world position.</summary>
    float NoiseHeight(float worldX, float worldZ)
    {
        return Mathf.PerlinNoise(worldX * noiseScale, worldZ * noiseScale) * heightMultiplier;
    }

    /// <summary>
    /// Height of the actual terrain MESH at a world position (not the raw noise).
    /// Interpolates the same triangles GenerateTerrainMesh builds, so anything placed
    /// with this sits exactly on the surface, even if the chunk isn't spawned yet.
    /// </summary>
    public float GetHeightAt(float worldX, float worldZ)
    {
        float step = (float)chunkSize / resolution;

        float gx = worldX / step;
        float gz = worldZ / step;
        int ix = Mathf.FloorToInt(gx);
        int iz = Mathf.FloorToInt(gz);
        float fx = gx - ix;
        float fz = gz - iz;

        float x0 = ix * step, z0 = iz * step;
        float x1 = x0 + step, z1 = z0 + step;

        float ha = NoiseHeight(x0, z0); // (0,0)
        float hb = NoiseHeight(x0, z1); // (0,1)
        float hc = NoiseHeight(x1, z0); // (1,0)
        float hd = NoiseHeight(x1, z1); // (1,1)

        // Cell is split along the b-c diagonal: triangles (a,b,c) and (c,b,d)
        if (fx + fz <= 1f)
            return ha + (hc - ha) * fx + (hb - ha) * fz;
        else
            return hd + (hb - hd) * (1f - fx) + (hc - hd) * (1f - fz);
    }

    void SpawnChunk(Vector2Int coord)
    {
        GameObject chunkObject = new GameObject($"TerrainChunk_{coord.x}_{coord.y}");
        chunkObject.transform.SetParent(chunkRoot, false);
        chunkObject.transform.position = new Vector3(coord.x * chunkSize, 0, coord.y * chunkSize);

        MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();

        Mesh mesh = GenerateTerrainMesh(chunkObject.transform.position);
        mesh.name = chunkObject.name;
        meshFilter.sharedMesh = mesh;

        // Second submesh only exists for chunks that actually touch a town
        if (mesh.subMeshCount > 1)
            meshRenderer.sharedMaterials = new[] { terrainMaterial, townGroundMaterial };
        else
            meshRenderer.sharedMaterial = terrainMaterial;

        MeshCollider col = chunkObject.AddComponent<MeshCollider>();
        col.sharedMesh = mesh;

        activeChunks.Add(coord, chunkObject);
    }

    Mesh GenerateTerrainMesh(Vector3 worldPos)
    {
        Mesh mesh = new Mesh();
        Vector3[] vertices = new Vector3[(resolution + 1) * (resolution + 1)];
        Vector2[] uvs = new Vector2[vertices.Length];

        float stepSize = (float)chunkSize / resolution;

        for (int i = 0, z = 0; z <= resolution; z++)
        {
            for (int x = 0; x <= resolution; x++, i++)
            {
                float xCoord = worldPos.x + (x * stepSize);
                float zCoord = worldPos.z + (z * stepSize);
                vertices[i] = new Vector3(x * stepSize, NoiseHeight(xCoord, zCoord), z * stepSize);

                // World-space UVs so textures tile seamlessly across chunks
                uvs[i] = new Vector2(xCoord, zCoord) / uvTileSize;
            }
        }

        CollectSettlements(new Vector2(worldPos.x, worldPos.z),
                           new Vector2(worldPos.x + chunkSize, worldPos.z + chunkSize));

        baseTris.Clear();
        townTris.Clear();

        int row = resolution + 1;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int v = z * row + x;

                AddTriangle(vertices, worldPos, v, v + row, v + 1);
                AddTriangle(vertices, worldPos, v + 1, v + row, v + row + 1);
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;

        if (townTris.Count > 0)
        {
            mesh.subMeshCount = 2;
            mesh.SetTriangles(baseTris, 0);
            mesh.SetTriangles(townTris, 1);
        }
        else
        {
            mesh.SetTriangles(baseTris, 0);
        }

        mesh.RecalculateNormals();
        mesh.RecalculateTangents();   // needed if the materials use normal maps
        return mesh;
    }

    /// <summary>Sends a triangle to the town submesh if its centre lies inside a town's ground area.</summary>
    void AddTriangle(Vector3[] vertices, Vector3 worldPos, int a, int b, int c)
    {
        List<int> target = baseTris;

        if (settlements.Count > 0)
        {
            Vector3 centroid = (vertices[a] + vertices[b] + vertices[c]) / 3f;
            if (InsideTownGround(worldPos.x + centroid.x, worldPos.z + centroid.z))
                target = townTris;
        }

        target.Add(a);
        target.Add(b);
        target.Add(c);
    }

    // ------------------------------------------------------------------
    // Town ground
    // ------------------------------------------------------------------

    /// <summary>Gathers every town / dead end whose ground could touch this chunk.</summary>
    void CollectSettlements(Vector2 min, Vector2 max)
    {
        settlements.Clear();
        if (townGroundMaterial == null || roads == null || towns == null) return;

        float deadEndRadius = towns.deadEndDistance + towns.houseSpacing;
        float maxRadius = Mathf.Max(roads.roadSpacing * 0.5f, deadEndRadius) + townGroundPadding;
        float pad = maxRadius * (1f + edgeNoise);

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
                    if (!includeDeadEnds) continue;
                    r = deadEndRadius;
                }
                else
                {
                    if (!towns.IsTown(x, y)) continue;
                    r = Mathf.Min(towns.baseRadius + degree * towns.radiusPerRoad, roads.roadSpacing * 0.5f);
                }

                Vector2 c = roads.GetNodePosition(x, y);
                settlements.Add(new Vector3(c.x, c.y, r + townGroundPadding));
            }
        }
    }

    bool InsideTownGround(float wx, float wz)
    {
        // Irregular edge: scale every radius by a smooth noise value at this spot
        float n = Mathf.PerlinNoise(wx * edgeNoiseScale + 1000f, wz * edgeNoiseScale + 1000f);
        float k = 1f + (n - 0.5f) * 2f * edgeNoise;

        for (int i = 0; i < settlements.Count; i++)
        {
            Vector3 s = settlements[i];
            float dx = wx - s.x;
            float dy = wz - s.y;
            float r = s.z * k;
            if (dx * dx + dy * dy < r * r) return true;
        }
        return false;
    }
}
