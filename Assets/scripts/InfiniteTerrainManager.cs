using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class InfiniteTerrainWithWavyRoads : MonoBehaviour
{
    [Header("References")]
    public Transform player;

    [Header("Grid & Terrain Settings")]
    public int width = 128;              // Number of grid squares along X
    public int depth = 128;              // Number of grid squares along Z
    public float spacing = 1.2f;        // Size of each grid square
    public float scale = 60f;           // Perlin noise zoom level for hills
    public float heightMultiplier = 12f;// Hill height
    public float updateThreshold = 5f;  // Distance player moves before chunk shifts

    [Header("Curved Grid Roads & Intersections")]
    public bool enableRoad = true;
    public float roadWidth = 10f;
    public float roadBlendDistance = 16f;  // Smooth transition slope into hills
    public float roadHeight = 0.5f;
    public float gridSpacing = 150f;      // Distance between intersections (grid size)
    public float curveFrequency = 0.015f; // How wavy/curved the roads are
    public float curveAmplitude = 25f;    // How far side-to-side the roads swing
    public float textureTiling = 0.15f;

    private Mesh mesh;
    private Vector3 lastPlayerPosition;

    void Start()
    {
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;

        if (player == null)
        {
            if (Camera.main != null) player = Camera.main.transform;
            else { enabled = false; return; }
        }

        UpdateChunkPosition();
        GenerateWorld();
        lastPlayerPosition = player.position;
    }

    void Update()
    {
        if (Vector3.Distance(new Vector3(player.position.x, 0, player.position.z),
            new Vector3(lastPlayerPosition.x, 0, lastPlayerPosition.z)) > updateThreshold)
        {
            UpdateChunkPosition();
            GenerateWorld();
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

    void GenerateWorld()
    {
        int vertexCols = width + 1;
        int vertexRows = depth + 1;
        int totalVerts = vertexCols * vertexRows;

        Vector3[] vertices = new Vector3[totalVerts];
        Vector2[] uvs = new Vector2[totalVerts];
        bool[,] isRoadVertex = new bool[vertexRows, vertexCols];

        float halfWidthGrid = (width * spacing) * 0.5f;
        float halfDepthGrid = (depth * spacing) * 0.5f;

        // ==========================================
        // STEP 1: Generate Standard Square Terrain Grid
        // ==========================================
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

        // ==========================================
        // STEP 2: Carve Curved Grid Roads & Intersections
        // ==========================================
        if (enableRoad)
        {
            vertIndex = 0;
            for (int z = 0; z < vertexRows; z++)
            {
                for (int x = 0; x < vertexCols; x++)
                {
                    Vector3 v = vertices[vertIndex];
                    float worldX = transform.position.x + v.x;
                    float worldZ = transform.position.z + v.z;

                    // Calculate curved road centerlines using wavy offsets based on world position
                    float targetRoadX = Mathf.Round(worldX / gridSpacing) * gridSpacing + Mathf.Sin(worldZ * curveFrequency) * curveAmplitude;
                    float targetRoadZ = Mathf.Round(worldZ / gridSpacing) * gridSpacing + Mathf.Cos(worldX * curveFrequency) * curveAmplitude;

                    float distRoadX = Mathf.Abs(worldX - targetRoadX);
                    float distRoadZ = Mathf.Abs(worldZ - targetRoadZ);

                    // The distance to whichever road axis is closest
                    float dstToRoad = Mathf.Min(distRoadX, distRoadZ);

                    float halfRoad = roadWidth * 0.5f;

                    if (dstToRoad <= halfRoad)
                    {
                        // Flat road surface
                        v.y = roadHeight;
                        isRoadVertex[z, x] = true;

                        // Proper directional UV mapping depending on direction
                        if (distRoadX < distRoadZ)
                        {
                            float u = (distRoadX + halfRoad) / roadWidth;
                            uvs[vertIndex] = new Vector2(u, worldZ * textureTiling);
                        }
                        else
                        {
                            float u = (distRoadZ + halfRoad) / roadWidth;
                            uvs[vertIndex] = new Vector2(u, worldX * textureTiling);
                        }
                    }
                    else if (dstToRoad < halfRoad + roadBlendDistance)
                    {
                        // Smooth transition slope into hills
                        float originalNoiseY = v.y;
                        float t = (dstToRoad - halfRoad) / roadBlendDistance;
                        float smoothT = t * t * (3f - 2f * t);

                        v.y = Mathf.Lerp(roadHeight, originalNoiseY, smoothT);
                        isRoadVertex[z, x] = false;
                    }
                    else
                    {
                        isRoadVertex[z, x] = false;
                    }

                    vertices[vertIndex] = v;
                    vertIndex++;
                }
            }
        }

        // ==========================================
        // STEP 3: Build Triangles & Assign Submeshes
        // ==========================================
        List<int> terrainTriangles = new List<int>();
        List<int> roadTriangles = new List<int>();

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int i0 = z * vertexCols + x;
                int i1 = (z + 1) * vertexCols + x;
                int i2 = z * vertexCols + (x + 1);
                int i3 = (z + 1) * vertexCols + (x + 1);

                bool quadIsRoad = enableRoad &&
                isRoadVertex[z, x] &&
                isRoadVertex[z + 1, x] &&
                isRoadVertex[z, x + 1] &&
                isRoadVertex[z + 1, x + 1];

                List<int> targetList = quadIsRoad ? roadTriangles : terrainTriangles;

                targetList.Add(i0);
                targetList.Add(i1);
                targetList.Add(i2);

                targetList.Add(i2);
                targetList.Add(i1);
                targetList.Add(i3);
            }
        }

        mesh.Clear();
        mesh.subMeshCount = 2;
        mesh.vertices = vertices;
        mesh.SetTriangles(terrainTriangles, 0);
        mesh.SetTriangles(roadTriangles, 1);
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
}
