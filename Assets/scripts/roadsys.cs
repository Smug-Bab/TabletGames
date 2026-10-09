using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(InfiniteTerrainSystem))]
public class ProceduralRoadSystem : MonoBehaviour
{
    [Header("Highway Ribbon Settings")]
    public bool enableRoad = true;
    public float roadWidth = 14f;           // Main drivable road surface width[cite: 8]
    public float shoulderWidth = 4f;        // How far the edges flare outwards[cite: 8]
    public float terrainCullingWidth = 12f; // Tighter terrain cut so shoulders overlap[cite: 8]

    [Header("Height Controls")]
    public float roadHeightOffset = 0.3f;     // Adjusts road height only[cite: 8]
    public float shoulderHeightOffset = 0.0f; // Adjusts shoulder height independently[cite: 8]

    [Header("Grid & Waves")]
    public float gridSpacing = 150f;          //[cite: 8]
    public float wavesPerSegment = 1f;        //[cite: 8]
    public float curveAmplitude = 25f;        //[cite: 8]
    public float textureTiling = 0.15f;       //[cite: 8]

    [Header("Ribbon Resolution")]
    public int ribbonSegmentsPerChunk = 40;   //[cite: 8]

    private InfiniteTerrainSystem terrainSystem;

    void Awake()
    {
        terrainSystem = GetComponent<InfiniteTerrainSystem>();
    }

    // Public query method allowing the terrain to check culling boundaries autonomously[cite: 8]
    public bool IsPointInTerrainCut(float worldX, float worldZ)
    {
        float halfCulling = terrainCullingWidth * 0.5f;
        float lockedCurveFreq = (2f * Mathf.PI * wavesPerSegment) / gridSpacing;

        float nearestGridX = Mathf.Round(worldX / gridSpacing) * gridSpacing;
        float nearestGridZ = Mathf.Round(worldZ / gridSpacing) * gridSpacing;

        float targetRoadX = nearestGridX + Mathf.Sin(worldZ * lockedCurveFreq) * curveAmplitude;
        float targetRoadZ = nearestGridZ + Mathf.Sin(worldX * lockedCurveFreq) * curveAmplitude;

        float distRoadX = Mathf.Abs(worldX - targetRoadX);
        float distRoadZ = Mathf.Abs(worldZ - targetRoadZ);
        return Mathf.Min(distRoadX, distRoadZ) <= halfCulling;
    }

    // Generates road ribbons and combines them into the master mesh as Submesh 1[cite: 8]
    public void ProcessRoads(Mesh targetMesh, Vector3[] terrainVertices, Vector2[] terrainUvs, List<int> terrainTriangles)
    {
        if (terrainSystem == null) terrainSystem = GetComponent<InfiniteTerrainSystem>();
        if (!enableRoad || targetMesh == null) return;

        int width = terrainSystem.Width;
        int depth = terrainSystem.Depth;

        float halfRoad = roadWidth * 0.5f;
        float halfTotal = halfRoad + shoulderWidth;

        float chunkMinX = transform.position.x - ((width * terrainSystem.Spacing) * 0.5f);
        float chunkMaxX = transform.position.x + ((width * terrainSystem.Spacing) * 0.5f);
        float chunkMinZ = transform.position.z - ((depth * terrainSystem.Spacing) * 0.5f);
        float chunkMaxZ = transform.position.z + ((depth * terrainSystem.Spacing) * 0.5f);

        float lockedCurveFreq = (2f * Mathf.PI * wavesPerSegment) / gridSpacing;

        List<Vector3> roadVertices = new List<Vector3>();
        List<Vector2> roadUvs = new List<Vector2>();
        List<int> roadTriangles = new List<int>();

        float expansionMargin = curveAmplitude + halfTotal;
        int minGridXIndex = Mathf.FloorToInt((chunkMinX - expansionMargin) / gridSpacing);
        int maxGridXIndex = Mathf.CeilToInt((chunkMaxX + expansionMargin) / gridSpacing);
        int minGridZIndex = Mathf.FloorToInt((chunkMinZ - expansionMargin) / gridSpacing);
        int maxGridZIndex = Mathf.CeilToInt((chunkMaxZ + expansionMargin) / gridSpacing);

        void GenerateRibbon(bool isZAligned, float baseCoord, float minBound, float maxBound)
        {
            int startIndex = roadVertices.Count;
            float segmentLen = (maxBound - minBound) / ribbonSegmentsPerChunk;

            for (int i = 0; i <= ribbonSegmentsPerChunk; i++)
            {
                float tCoord = minBound + (i * segmentLen);
                float roadCenterX, roadCenterZ;
                float dxdz_or_dzdx;

                if (isZAligned)
                {
                    roadCenterX = baseCoord + Mathf.Sin(tCoord * lockedCurveFreq) * curveAmplitude;
                    roadCenterZ = tCoord;
                    dxdz_or_dzdx = curveAmplitude * lockedCurveFreq * Mathf.Cos(tCoord * lockedCurveFreq);
                }
                else
                {
                    roadCenterX = tCoord;
                    roadCenterZ = baseCoord + Mathf.Sin(tCoord * lockedCurveFreq) * curveAmplitude;
                    dxdz_or_dzdx = curveAmplitude * lockedCurveFreq * Mathf.Cos(tCoord * lockedCurveFreq);
                }

                Vector3 tangent = isZAligned ? new Vector3(dxdz_or_dzdx, 0f, 1f).normalized : new Vector3(1f, 0f, dxdz_or_dzdx).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;

                float baseTerrainY = Mathf.PerlinNoise(roadCenterX / terrainSystem.scale, roadCenterZ / terrainSystem.scale) * terrainSystem.heightMultiplier;
                float roadY = baseTerrainY + roadHeightOffset;

                Vector3 leftTotalPos = new Vector3(roadCenterX, 0f, roadCenterZ) - right * halfTotal;
                Vector3 rightTotalPos = new Vector3(roadCenterX, 0f, roadCenterZ) + right * halfTotal;

                float leftTerrainY = Mathf.PerlinNoise(leftTotalPos.x / terrainSystem.scale, leftTotalPos.z / terrainSystem.scale) * terrainSystem.heightMultiplier + shoulderHeightOffset;
                float rightTerrainY = Mathf.PerlinNoise(rightTotalPos.x / terrainSystem.scale, rightTotalPos.z / terrainSystem.scale) * terrainSystem.heightMultiplier + shoulderHeightOffset;

                Vector3 pLeftTotal = new Vector3(leftTotalPos.x - transform.position.x, leftTerrainY, leftTotalPos.z - transform.position.z);
                Vector3 pLeftRoad = new Vector3(roadCenterX - transform.position.x, roadY, roadCenterZ - transform.position.z) - right * halfRoad;
                Vector3 pRightRoad = new Vector3(roadCenterX - transform.position.x, roadY, roadCenterZ - transform.position.z) + right * halfRoad;
                Vector3 pRightTotal = new Vector3(rightTotalPos.x - transform.position.x, rightTerrainY, rightTotalPos.z - transform.position.z);

                roadVertices.Add(pLeftTotal);
                roadVertices.Add(pLeftRoad);
                roadVertices.Add(pRightRoad);
                roadVertices.Add(pRightTotal);

                float texCoord = tCoord * textureTiling;
                roadUvs.Add(new Vector2(0.0f, texCoord));
                roadUvs.Add(new Vector2(0.2f, texCoord));
                roadUvs.Add(new Vector2(0.8f, texCoord));
                roadUvs.Add(new Vector2(1.0f, texCoord));
            }

            for (int i = 0; i < ribbonSegmentsPerChunk; i++)
            {
                int b = startIndex + (i * 4);
                int n = b + 4;

                roadTriangles.Add(b); roadTriangles.Add(n); roadTriangles.Add(b + 1);
                roadTriangles.Add(n); roadTriangles.Add(n + 1); roadTriangles.Add(b + 1);

                roadTriangles.Add(b + 1); roadTriangles.Add(n + 1); roadTriangles.Add(b + 2);
                roadTriangles.Add(n + 1); roadTriangles.Add(n + 2); roadTriangles.Add(b + 2);

                roadTriangles.Add(b + 2); roadTriangles.Add(n + 2); roadTriangles.Add(b + 3);
                roadTriangles.Add(n + 2); roadTriangles.Add(n + 3); roadTriangles.Add(b + 3);
            }
        }

        for (int gX = minGridXIndex; gX <= maxGridXIndex; gX++)
        {
            GenerateRibbon(true, gX * gridSpacing, chunkMinZ, chunkMaxZ);
        }

        for (int gZ = minGridZIndex; gZ <= maxGridZIndex; gZ++)
        {
            GenerateRibbon(false, gZ * gridSpacing, chunkMinX, chunkMaxX);
        }

        // Combine terrain and road datasets into the final multi-submesh structure[cite: 8, 9]
        List<Vector3> finalVertices = new List<Vector3>(terrainVertices);
        int terrainVertCount = terrainVertices.Length; // Fixed from .Count to .Length[cite: 8]
        finalVertices.AddRange(roadVertices);

        List<Vector2> finalUvs = new List<Vector2>(terrainUvs);
        finalUvs.AddRange(roadUvs);

        List<int> finalRoadTriangles = new List<int>(roadTriangles.Count);
        for (int i = 0; i < roadTriangles.Count; i++)
        {
            finalRoadTriangles.Add(roadTriangles[i] + terrainVertCount);
        }

        targetMesh.Clear();
        targetMesh.subMeshCount = 2;
        targetMesh.SetVertices(finalVertices);
        targetMesh.SetUVs(0, finalUvs);
        targetMesh.SetTriangles(terrainTriangles, 0);
        targetMesh.SetTriangles(finalRoadTriangles, 1);
        targetMesh.RecalculateNormals();
        targetMesh.RecalculateBounds();

        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = targetMesh;
        }
    }
}
