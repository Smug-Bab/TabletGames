using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class InfiniteTerrainSystem : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public ProceduralRoadSystem roadSystem;

    [Header("Material")]
    public Material terrainMaterial;
    public Material roadMaterial;
    public Material townMaterial;

    [Header("Chunk & Terrain")]
    public int width = 128;
    public int depth = 128;
    public float spacing = 1.2f;
    public float scale = 60f;
    public float heightMultiplier = 12f;
    public float updateThreshold = 5f;
    public float terrainSeed = 17.43f;
    [Range(1, 4)] public int octaves = 2;
    [Range(0.1f, 1f)] public float persistence = 0.5f;
    [Range(1.5f, 3f)] public float lacunarity = 2f;

    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private MeshRenderer meshRenderer;
    private Mesh mesh;
    private Vector3 lastPlayerPosition;

    public Mesh Mesh => mesh;
    public int Width => width;
    public int Depth => depth;
    public float Spacing => spacing;
    public float Scale => scale;
    public float HeightMultiplier => heightMultiplier;

    private void Start()
    {
        terrainSeed = WorldNoise.RandomSeed();

        if (roadSystem == null)
        {
            roadSystem = GetComponent<ProceduralRoadSystem>();
        }

        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();
        meshRenderer = GetComponent<MeshRenderer>();
        mesh = new Mesh();
        meshFilter.mesh = mesh;
        ApplyMaterialOverrides();

        if (player == null)
        {
            player = transform;
        }

        UpdateChunkPosition();
        GenerateTerrain();
        lastPlayerPosition = player.position;
    }

    private void Update()
    {
        if (Vector3.Distance(new Vector3(player.position.x, 0f, player.position.z), new Vector3(lastPlayerPosition.x, 0f, lastPlayerPosition.z)) > updateThreshold)
        {
            UpdateChunkPosition();
            GenerateTerrain();
            lastPlayerPosition = player.position;
        }
    }

    private void ApplyMaterialOverrides()
    {
        if (meshRenderer == null) return;

        Material[] materials = new Material[3];
        materials[0] = terrainMaterial != null ? terrainMaterial : (meshRenderer.materials != null && meshRenderer.materials.Length > 0 ? meshRenderer.materials[0] : null);
        materials[1] = roadMaterial != null ? roadMaterial : (meshRenderer.materials != null && meshRenderer.materials.Length > 1 ? meshRenderer.materials[1] : null);
        materials[2] = townMaterial != null ? townMaterial : (meshRenderer.materials != null && meshRenderer.materials.Length > 2 ? meshRenderer.materials[2] : null);

        if (materials[0] != null || materials[1] != null || materials[2] != null)
        {
            meshRenderer.materials = materials;
        }
    }

    private void UpdateChunkPosition()
    {
        transform.position = new Vector3(Mathf.Floor(player.position.x), 0f, Mathf.Floor(player.position.z));
    }

    private float SampleHeight(float worldX, float worldZ)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f / scale;
        float totalWeight = 0f;

        for (int octave = 0; octave < octaves; octave++)
        {
            value += WorldNoise.Perlin2D(worldX, worldZ, terrainSeed, frequency) * amplitude;
            totalWeight += amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return (value / Mathf.Max(totalWeight, 0.0001f)) * heightMultiplier;
    }

    public void GenerateTerrain()
    {
        int vertexCols = width + 1;
        int vertexRows = depth + 1;
        int totalVerts = vertexCols * vertexRows;

        Vector3[] vertices = new Vector3[totalVerts];
        Vector2[] uvs = new Vector2[totalVerts];
        List<int> terrainTriangles = new List<int>(width * depth * 6);

        float halfWidth = (width * spacing) * 0.5f;
        float halfDepth = (depth * spacing) * 0.5f;

        int vertexIndex = 0;
        for (int z = 0; z < vertexRows; z++)
        {
            for (int x = 0; x < vertexCols; x++)
            {
                float localX = (x * spacing) - halfWidth;
                float localZ = (z * spacing) - halfDepth;

                float worldX = transform.position.x + localX;
                float worldZ = transform.position.z + localZ;

                float height = SampleHeight(worldX, worldZ);
                if (roadSystem != null && roadSystem.enabled)
                {
                    float roadInfluence = roadSystem.GetRoadInfluence(worldX, worldZ);
                    if (roadInfluence > 0f)
                    {
                        float roadSurface = roadSystem.GetRoadSurfaceHeight(worldX, worldZ);
                        float slumpDepth = roadSystem.GetTerrainSlump(worldX, worldZ);
                        height = Mathf.Min(height, roadSurface - slumpDepth);
                    }
                }

                vertices[vertexIndex] = new Vector3(localX, height, localZ);
                uvs[vertexIndex] = new Vector2((float)x / width, (float)z / depth);
                vertexIndex++;
            }
        }

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int i0 = z * vertexCols + x;
                int i1 = (z + 1) * vertexCols + x;
                int i2 = z * vertexCols + (x + 1);
                int i3 = (z + 1) * vertexCols + (x + 1);

                float sampleX = transform.position.x + ((x + 0.5f) * spacing) - halfWidth;
                float sampleZ = transform.position.z + ((z + 0.5f) * spacing) - halfDepth;

                terrainTriangles.Add(i0);
                terrainTriangles.Add(i1);
                terrainTriangles.Add(i2);

                terrainTriangles.Add(i2);
                terrainTriangles.Add(i1);
                terrainTriangles.Add(i3);
            }
        }

        mesh.Clear();
        mesh.subMeshCount = 1;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.SetTriangles(terrainTriangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (meshCollider != null)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
    }
}
