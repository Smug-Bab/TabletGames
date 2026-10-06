using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class NoiseSettings
{
    public float scale = 50f;
    public int octaves = 4;
    [Range(0f, 1f)] public float persistence = 0.5f;
    public float lacunarity = 2f;
    public int seed;
}

public class InfiniteTerrainGenerator : MonoBehaviour
{
    public Transform viewer;
    public Material terrainMaterial;

    [Header("Chunk Settings")]
    public int chunkSize = 63;
    public float maxViewDst = 300f;
    public int depth = 40;

    [Header("Noise Settings")]
    public NoiseSettings noiseSettings;

    private int chunksVisibleInViewDst;
    private Dictionary<Vector2, TerrainChunk> terrainChunkDictionary = new Dictionary<Vector2, TerrainChunk>();
    private List<TerrainChunk> terrainChunksVisibleLastUpdate = new List<TerrainChunk>();

    void Start()
    {
        if (viewer == null)
        {
            viewer = Camera.main.transform;
        }
        chunksVisibleInViewDst = Mathf.RoundToInt(maxViewDst / chunkSize);
        UpdateVisibleChunks();
    }

    void Update()
    {
        UpdateVisibleChunks();
    }

    void UpdateVisibleChunks()
    {
        for (int i = 0; i < terrainChunksVisibleLastUpdate.Count; i++)
        {
            terrainChunksVisibleLastUpdate[i].SetVisible(false);
        }
        terrainChunksVisibleLastUpdate.Clear();

        int currentChunkCoordX = Mathf.RoundToInt(viewer.position.x / chunkSize);
        int currentChunkCoordY = Mathf.RoundToInt(viewer.position.z / chunkSize);

        for (int yOffset = -chunksVisibleInViewDst; yOffset <= chunksVisibleInViewDst; yOffset++)
        {
            for (int xOffset = -chunksVisibleInViewDst; xOffset <= chunksVisibleInViewDst; xOffset++)
            {
                Vector2 viewedChunkCoord = new Vector2(currentChunkCoordX + xOffset, currentChunkCoordY + yOffset);

                if (terrainChunkDictionary.ContainsKey(viewedChunkCoord))
                {
                    terrainChunkDictionary[viewedChunkCoord].UpdateTerrainChunk();
                }
                else
                {
                    terrainChunkDictionary.Add(viewedChunkCoord, new TerrainChunk(viewedChunkCoord, chunkSize, depth, transform, noiseSettings, terrainMaterial));
                }
            }
        }
    }

    public class TerrainChunk
    {
        private GameObject terrainObject;
        private Vector2 position;
        private Bounds bounds;
        private Terrain terrain;
        private TerrainData terrainData;

        public TerrainChunk(Vector2 coord, int size, int heightDepth, Transform parent, NoiseSettings settings, Material mat)
        {
            position = coord * size;
            bounds = new Bounds(position, Vector2.one * size);
            Vector3 positionV3 = new Vector3(position.x, 0, position.y);

            terrainData = new TerrainData();
            terrainData.heightmapResolution = size + 1;
            terrainData.size = new Vector3(size, heightDepth, size);
            terrainData.SetHeights(0, 0, GenerateHeights(size, position, settings));

            terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            terrain = terrainObject.GetComponent<Terrain>();
            if (mat != null) terrain.materialTemplate = mat;

            terrainObject.transform.position = positionV3 - new Vector3(size / 2f, 0, size / 2f);
            terrainObject.transform.SetParent(parent);

            SetVisible(false);
        }

        float[,] GenerateHeights(int size, Vector2 chunkPos, NoiseSettings settings)
        {
            float[,] heights = new float[size + 1, size + 1];
            System.Random prng = new System.Random(settings.seed);
            Vector2[] octaveOffsets = new Vector2[settings.octaves];

            for (int i = 0; i < settings.octaves; i++)
            {
                float offsetX = prng.Next(-100000, 100000);
                float offsetY = prng.Next(-100000, 100000);
                octaveOffsets[i] = new Vector2(offsetX, offsetY);
            }

            float maxLocalNoiseHeight = float.MinValue;
            float minLocalNoiseHeight = float.MaxValue;

            for (int y = 0; y <= size; y++)
            {
                for (int x = 0; x <= size; x++)
                {
                    float amplitude = 1;
                    float frequency = 1;
                    float noiseHeight = 0;

                    for (int i = 0; i < settings.octaves; i++)
                    {
                        float sampleX = (chunkPos.x + x) / settings.scale * frequency + octaveOffsets[i].x;
                        float sampleY = (chunkPos.y + y) / settings.scale * frequency + octaveOffsets[i].y;

                        float perlinValue = Mathf.PerlinNoise(sampleX, sampleY) * 2 - 1;
                        noiseHeight += perlinValue * amplitude;

                        amplitude *= settings.persistence;
                        frequency *= settings.lacunarity;
                    }

                    if (noiseHeight > maxLocalNoiseHeight) maxLocalNoiseHeight = noiseHeight;
                    if (noiseHeight < minLocalNoiseHeight) minLocalNoiseHeight = noiseHeight;
                    heights[x, y] = noiseHeight;
                }
            }

            for (int y = 0; y <= size; y++)
            {
                for (int x = 0; x <= size; x++)
                {
                    heights[x, y] = Mathf.InverseLerp(minLocalNoiseHeight, maxLocalNoiseHeight, heights[x, y]);
                }
            }

            return heights;
        }

        public void UpdateTerrainChunk()
        {
            float viewerDstFromNearestEdge = Mathf.Sqrt(bounds.SqrDistance(new Vector2(terrainObject.transform.position.x, terrainObject.transform.position.z)));
            bool visible = viewerDstFromNearestEdge <= 300f;
            SetVisible(visible);
        }

        public void SetVisible(bool visible)
        {
            terrainObject.SetActive(visible);
        }
    }
}
