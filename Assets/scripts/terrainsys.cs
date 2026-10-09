using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class InfiniteTerrainSystem : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public ProceduralRoadSystem roadSystem;

    [Header("Grid & Terrain Settings")]
    public int width = 128;
    public int depth = 128;
    public float spacing = 1.2f;
    public float scale = 60f;
    public float heightMultiplier = 12f;
    public float updateThreshold = 5f;

    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private Mesh mesh;
    private Vector3 lastPlayerPosition;

    public Mesh Mesh => mesh;
    public int Width => width;
    public int Depth => depth;
    public float Spacing => spacing;

    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();
        mesh = new Mesh();
        meshFilter.mesh = mesh;

        if (player == null)
        {
            player = transform;
        }

        UpdateChunkPosition();
        GenerateTerrain();
        lastPlayerPosition = player.position;
    }

    void Update()
    {
        if (Vector3.Distance(new Vector3(player.position.x, 0, player.position.z),
            new Vector3(lastPlayerPosition.x, 0, lastPlayerPosition.z)) > updateThreshold)
        {
            UpdateChunkPosition();
            GenerateTerrain();
            lastPlayerPosition = player.position;
        }
    }

    void UpdateChunkPosition()
    {
        transform.position = new Vector3(
            Mathf.Floor(player.position.x),
                                         0,
                                         Mathf.Floor(player.position.z)
        );
    }

    public void GenerateTerrain()
    {
        int vertexCols = width + 1;
        int vertexRows = depth + 1;
        int totalVerts = vertexCols * vertexRows;

        Vector3[] vertices = new Vector3[totalVerts];
        Vector2[] uvs = new Vector2[totalVerts];
        List<int> terrainTriangles = new List<int>();

        float halfWidthGrid = (width * spacing) * 0.5f;
        float halfDepthGrid = (depth * spacing) * 0.5f;

        // Terrain system generates its own vertices and heights independently[cite: 9]
        int vertIndex = 0;
        for (int z = 0; z < vertexRows; z++)
        {
            for (int x = 0; x < vertexCols; x++)
            {
                float localX = (x * spacing) - halfWidthGrid;
                float localZ = (z * spacing) - halfDepthGrid;

                float worldX = transform.position.x + localX;
                float worldZ = transform.position.z + localZ;

                float noiseY = Mathf.PerlinNoise(worldX / scale, worldZ / scale) * heightMultiplier;

                vertices[vertIndex] = new Vector3(localX, noiseY, localZ);
                uvs[vertIndex] = new Vector2((float)x / width, (float)z / depth);
                vertIndex++;
            }
        }

        // Build terrain triangles, querying the road system only for culling boundaries
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int i0 = z * vertexCols + x;
                int i1 = (z + 1) * vertexCols + x;
                int i2 = z * vertexCols + (x + 1);
                int i3 = (z + 1) * vertexCols + (x + 1);

                float qWorldX = transform.position.x + ((x + 0.5f) * spacing) - halfWidthGrid;
                float qWorldZ = transform.position.z + ((z + 0.5f) * spacing) - halfDepthGrid;

                if (roadSystem != null && roadSystem.enabled && roadSystem.IsPointInTerrainCut(qWorldX, qWorldZ))
                {
                    continue; // Skip triangle generation inside road corridors
                }

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

        // Hand off the terrain mesh data to the road system for submesh combination[cite: 8, 9]
        if (roadSystem != null && roadSystem.enabled)
        {
            roadSystem.ProcessRoads(mesh, vertices, uvs, terrainTriangles);
        }
        else if (meshCollider != null)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
    }
}
