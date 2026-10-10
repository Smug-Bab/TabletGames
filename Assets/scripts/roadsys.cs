using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(InfiniteTerrainSystem), typeof(TownSystem))]
public class ProceduralRoadSystem : MonoBehaviour
{
    [Header("References")]
    public TownSystem townSystem;

    [Header("Material")]
    public Material roadMaterial;

    [Header("Road Surface")]
    public bool enableRoad = false;
    public float roadWidth = 9f;
    public float shoulderAngle = 26f;
    public float terrainCutWidth = 12f;
    public float roadHeightOffset = 0.35f;
    public float shoulderHeightOffset = 0f;

    [Header("Road Network")]
    public float roadSeed = 41.7f;

    private struct Road
    {
        public Vector2 a, b;
        public float width;
    }

    private InfiniteTerrainSystem terrainSystem;
    private MeshRenderer roadRenderer;
    private readonly List<Road> roads = new List<Road>();

    private void Awake()
    {
        terrainSystem = GetComponent<InfiniteTerrainSystem>();
        roadRenderer = GetComponent<MeshRenderer>();
        if (townSystem == null) townSystem = GetComponent<TownSystem>();
        if (roadSeed <= 0f) roadSeed = WorldNoise.RandomSeed();
        ApplyRoadMaterialOverride();
        RefreshRoadNetwork();
    }

    private void ApplyRoadMaterialOverride()
    {
        if (roadRenderer == null || roadMaterial == null) return;

        Material[] materials = roadRenderer.materials;
        if (materials == null || materials.Length <= 1)
        {
            materials = new Material[2];
        }

        materials[1] = roadMaterial;
        roadRenderer.materials = materials;
    }

    public void RefreshRoadNetwork()
    {
        roads.Clear();
        if (!enableRoad) return;

        if (townSystem == null || !townSystem.HasTowns) return;

        foreach (TownSystem.TownArea town in townSystem.Towns)
        {
            if (town.majorRoadPoints == null || town.majorRoadPoints.Count == 0) continue;
            for (int i = 0; i < town.majorRoadPoints.Count; i++)
            {
                AddRoad(town.center, town.majorRoadPoints[i], 1.35f);
                AddRoad(town.majorRoadPoints[i], town.majorRoadPoints[(i + 1) % town.majorRoadPoints.Count], 1.35f);
                if (town.minorRoadPoints == null || town.minorRoadPoints.Count == 0) continue;
                Vector2 m = town.minorRoadPoints[i % town.minorRoadPoints.Count];
                Vector2 outward = m + (m - town.center).normalized * Mathf.Lerp(30f, 90f, WorldNoise.Hash01(i + 17f, roadSeed + 2.3f, roadSeed));
                AddRoad(town.majorRoadPoints[i], m, 1f);
                AddRoad(m, outward, 1f);
            }
        }

        Vector2 origin = Vector2.zero;
        int branchCount = Mathf.RoundToInt(Mathf.Lerp(4f, 7f, WorldNoise.Hash01(roadSeed, 11.7f, roadSeed)));
        for (int i = 0; i < branchCount; i++)
        {
            float a = (i / (float)branchCount) * Mathf.PI * 2f + (WorldNoise.Hash01(i + 17f, roadSeed * 0.13f, roadSeed) - 0.5f) * 0.9f;
            float len = Mathf.Lerp(60f, 160f, WorldNoise.Hash01(i * 3.3f + 7.9f, roadSeed + 9.4f, roadSeed));
            AddRoad(origin, origin + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len, 1f);
        }
    }

    private void AddRoad(Vector2 from, Vector2 to, float scale)
    {
        roads.Add(new Road { a = from, b = to, width = roadWidth * scale + Mathf.Tan(shoulderAngle * Mathf.Deg2Rad) * (roadWidth * scale) });
    }

    private float DistanceToRoad(Vector2 p, out Vector2 closest)
    {
        float best = float.MaxValue;
        closest = p;
        foreach (Road road in roads)
        {
            Vector2 d = road.b - road.a;
            float lenSq = d.sqrMagnitude;
            float t = lenSq < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(p - road.a, d) / lenSq);
            Vector2 cp = road.a + d * t;
            float dist = (p - cp).magnitude;
            if (dist < best)
            {
                best = dist;
                closest = cp;
            }
        }
        return best == float.MaxValue ? 0f : best;
    }

    private float EffectiveRoadRadius()
    {
        float shoulderRadius = (roadWidth * 0.5f) + Mathf.Tan(shoulderAngle * Mathf.Deg2Rad) * (roadWidth * 0.5f);
        return Mathf.Max(shoulderRadius * 1.35f, terrainCutWidth * 0.9f);
    }

    public float GetRoadInfluence(float worldX, float worldZ)
    {
        if (!enableRoad || terrainSystem == null) return 0f;
        float dist = DistanceToRoad(new Vector2(worldX, worldZ), out _);
        if (dist <= 0f && roads.Count == 0) return 0f;
        return Mathf.Clamp01(1f - Mathf.Clamp01(dist / EffectiveRoadRadius()));
    }

    public float GetRoadSurfaceHeight(float worldX, float worldZ)
    {
        if (!enableRoad || terrainSystem == null) return 0f;
        Vector2 point = new Vector2(worldX, worldZ);
        Vector2 closest;
        DistanceToRoad(point, out closest);
        float terrainHeight = WorldNoise.Perlin2D(closest.x, closest.y, terrainSystem.terrainSeed, 1f / terrainSystem.Scale) * terrainSystem.HeightMultiplier;
        return terrainHeight + roadHeightOffset + shoulderHeightOffset;
    }

    public float GetTerrainSlump(float worldX, float worldZ)
    {
        float influence = GetRoadInfluence(worldX, worldZ);
        return influence <= 0f ? 0f : Mathf.Lerp(0.6f, 5.5f, influence);
    }

    public bool IsPointInTerrainCut(float worldX, float worldZ)
    {
        if (!enableRoad || terrainSystem == null) return false;
        float dist = DistanceToRoad(new Vector2(worldX, worldZ), out _);
        float shoulderRadius = (roadWidth * 0.5f) + Mathf.Tan(shoulderAngle * Mathf.Deg2Rad) * (roadWidth * 0.5f);
        return dist <= Mathf.Max(shoulderRadius, terrainCutWidth * 0.7f);
    }

    public void ProcessRoads(Mesh targetMesh, Vector3[] terrainVertices, Vector2[] terrainUvs, List<int> terrainTriangles)
    {
        if (terrainSystem == null) terrainSystem = GetComponent<InfiniteTerrainSystem>();
        if (!enableRoad || targetMesh == null) return;

        RefreshRoadNetwork();

        int w = terrainSystem.Width;
        int d = terrainSystem.Depth;
        float minX = transform.position.x - (w * terrainSystem.Spacing * 0.5f);
        float maxX = transform.position.x + (w * terrainSystem.Spacing * 0.5f);
        float minZ = transform.position.z - (d * terrainSystem.Spacing * 0.5f);
        float maxZ = transform.position.z + (d * terrainSystem.Spacing * 0.5f);

        List<Vector3> roadVertices = new List<Vector3>();
        List<Vector2> roadUvs = new List<Vector2>();
        List<int> roadTriangles = new List<int>();

        foreach (Road road in roads)
        {
            Vector3 a = new Vector3(road.a.x, 0f, road.a.y);
            Vector3 b = new Vector3(road.b.x, 0f, road.b.y);
            if ((a.x + road.width < minX && b.x + road.width < minX) || (a.x - road.width > maxX && b.x - road.width > maxX) ||
                (a.z + road.width < minZ && b.z + road.width < minZ) || (a.z - road.width > maxZ && b.z - road.width > maxZ)) continue;

            Vector3 tangent = (b - a).sqrMagnitude > 0.0001f ? (b - a).normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            float h = road.width * 0.5f;
            Vector3 aL = a - right * h, aR = a + right * h, bL = b - right * h, bR = b + right * h;
            aL.y = aR.y = GetRoadSurfaceHeight(a.x, a.z);
            bL.y = bR.y = GetRoadSurfaceHeight(b.x, b.z);

            roadVertices.Add(aL - transform.position);
            roadVertices.Add(aR - transform.position);
            roadVertices.Add(bL - transform.position);
            roadVertices.Add(bR - transform.position);

            int start = roadVertices.Count - 4;
            roadTriangles.Add(start); roadTriangles.Add(start + 2); roadTriangles.Add(start + 1);
            roadTriangles.Add(start + 2); roadTriangles.Add(start + 3); roadTriangles.Add(start + 1);
            roadUvs.Add(new Vector2(0f, 0f)); roadUvs.Add(new Vector2(1f, 0f));
            roadUvs.Add(new Vector2(0f, 1f)); roadUvs.Add(new Vector2(1f, 1f));
        }

        if (roadVertices.Count == 0) return;

        List<Vector3> finalVertices = new List<Vector3>(terrainVertices); finalVertices.AddRange(roadVertices);
        List<Vector2> finalUvs = new List<Vector2>(terrainUvs); finalUvs.AddRange(roadUvs);
        List<int> finalTriangles = new List<int>(roadTriangles.Count);
        int offset = terrainVertices.Length;
        for (int i = 0; i < roadTriangles.Count; i++) finalTriangles.Add(roadTriangles[i] + offset);

        targetMesh.Clear();
        targetMesh.subMeshCount = 2;
        targetMesh.SetVertices(finalVertices);
        targetMesh.SetUVs(0, finalUvs);
        targetMesh.SetTriangles(terrainTriangles, 0);
        targetMesh.SetTriangles(finalTriangles, 1);
        targetMesh.RecalculateNormals();
        targetMesh.RecalculateBounds();

        MeshCollider collider = GetComponent<MeshCollider>();
        if (collider != null)
        {
            collider.sharedMesh = null;
            collider.sharedMesh = targetMesh;
        }
    }
}
