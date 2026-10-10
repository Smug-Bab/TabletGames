using System.Collections.Generic;
using UnityEngine;

public class TownSystem : MonoBehaviour
{
    [Header("Town Network")]
    public int townCount = 6;
    public float townRadius = 26f;
    public float minTownDistance = 120f;

    [Header("Primitive Buildings")]
    public bool generatePrimitiveBuildings = true;
    public Vector3 buildingSize = new Vector3(4f, 6f, 4f);

    [Header("Material")]
    public Material townMaterial;

    public struct TownArea
    {
        public Vector2 center;
        public float radius;
        public List<Vector2> blocks;
        public List<Vector2> majorRoadPoints;
        public List<Vector2> minorRoadPoints;
    }

    private readonly List<TownArea> towns = new List<TownArea>();
    private readonly List<GameObject> spawnedBuildings = new List<GameObject>();
    private Transform buildingRoot;

    public IReadOnlyList<TownArea> Towns => towns;
    public bool HasTowns => towns.Count > 0;

    private void EnsureBuildingRoot()
    {
        if (buildingRoot != null) return;

        GameObject root = GameObject.Find("TownBuildings");
        if (root == null)
        {
            root = new GameObject("TownBuildings");
            root.transform.SetParent(transform, false);
        }

        buildingRoot = root.transform;
    }

    private void ClearTownBuildings()
    {
        for (int i = spawnedBuildings.Count - 1; i >= 0; i--)
        {
            if (spawnedBuildings[i] == null) continue;
            if (Application.isPlaying)
            {
                Destroy(spawnedBuildings[i]);
            }
            else
            {
                DestroyImmediate(spawnedBuildings[i]);
            }
        }

        spawnedBuildings.Clear();
    }

    public void ClearTowns()
    {
        ClearTownBuildings();
        towns.Clear();
    }

    public void BuildTownStructures()
    {
        if (!generatePrimitiveBuildings) return;

        EnsureBuildingRoot();
        ClearTownBuildings();

        foreach (TownArea town in towns)
        {
            if (town.blocks == null) continue;

            for (int i = 0; i < town.blocks.Count; i++)
            {
                Vector2 block = town.blocks[i];
                GameObject building = GameObject.CreatePrimitive(PrimitiveType.Cube);
                building.name = "TownBuilding";
                building.transform.SetParent(buildingRoot, false);
                building.transform.localScale = buildingSize;
                building.transform.position = new Vector3(block.x, buildingSize.y * 0.5f, block.y);

                Renderer renderer = building.GetComponent<Renderer>();
                if (renderer != null && townMaterial != null)
                {
                    renderer.sharedMaterial = townMaterial;
                }

                spawnedBuildings.Add(building);
            }
        }
    }

    public void GenerateOriginTown(float radius = 28f)
    {
        towns.Clear();

        TownArea town = new TownArea
        {
            center = Vector2.zero,
            radius = radius,
            blocks = new List<Vector2>(),
            majorRoadPoints = new List<Vector2>(),
            minorRoadPoints = new List<Vector2>()
        };

        int blockCount = 8;
        for (int i = 0; i < blockCount; i++)
        {
            float angle = (i / (float)blockCount) * Mathf.PI * 2f;
            float distance = radius * 0.7f;
            town.blocks.Add(town.center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance);
        }

        towns.Add(town);
        BuildTownStructures();
    }

    public void GenerateTowns(Vector2 origin, float halfWidth, float halfDepth, float seed)
    {
        towns.Clear();

        Vector2 boundsMin = origin + new Vector2(-halfWidth, -halfDepth) - new Vector2(120f, 120f);
        Vector2 boundsMax = origin + new Vector2(halfWidth, halfDepth) + new Vector2(120f, 120f);

        int attempts = 0;
        while (towns.Count < townCount && attempts < 500)
        {
            attempts++;

            float x = Mathf.Lerp(boundsMin.x, boundsMax.x, WorldNoise.Hash01(attempts * 2.37f + 1.1f, seed + 3.4f, seed));
            float z = Mathf.Lerp(boundsMin.y, boundsMax.y, WorldNoise.Hash01(attempts * 7.91f + 6.13f, seed + 9.81f, seed));
            Vector2 candidate = new Vector2(x, z);

            bool valid = true;
            for (int i = 0; i < towns.Count; i++)
            {
                if (Vector2.Distance(candidate, towns[i].center) < minTownDistance)
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
            {
                continue;
            }

            TownArea town = new TownArea
            {
                center = candidate,
                radius = Mathf.Lerp(18f, 32f, WorldNoise.Hash01(candidate.x * 0.03f + 11.3f, candidate.y * 0.03f + 4.7f, seed)),
                blocks = new List<Vector2>(),
                majorRoadPoints = new List<Vector2>(),
                minorRoadPoints = new List<Vector2>()
            };

            int blockCount = Mathf.RoundToInt(Mathf.Lerp(6f, 12f, WorldNoise.Hash01(candidate.x * 0.11f + 7.1f, candidate.y * 0.09f + 4.8f, seed)));
            for (int i = 0; i < blockCount; i++)
            {
                float angle = i * (Mathf.PI * 2f / blockCount) + WorldNoise.Hash01(candidate.x + i, candidate.y + i, seed) * 1.2f;
                float distance = Mathf.Lerp(town.radius * 0.25f, town.radius * 0.9f, WorldNoise.Hash01(candidate.x + i * 2.3f, candidate.y + i * 1.7f, seed));
                town.blocks.Add(town.center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance);
            }

            towns.Add(town);
        }

        BuildTownStructures();
    }
}
