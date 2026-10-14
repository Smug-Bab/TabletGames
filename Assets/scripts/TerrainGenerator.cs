using System.Collections.Generic;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    public int chunkSize = 60;
    public float heightMultiplier = 15f;
    public float noiseScale = 0.02f;
    public Material terrainMaterial;

    private Dictionary<Vector2Int, GameObject> activeChunks = new Dictionary<Vector2Int, GameObject>();

    public void UpdateChunks(Vector3 playerPosition)
    {
        int playerChunkX = Mathf.FloorToInt(playerPosition.x / chunkSize);
        int playerChunkZ = Mathf.FloorToInt(playerPosition.z / chunkSize);

        // Render a 3x3 grid of chunks around the player
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                Vector2Int chunkCoord = new Vector2Int(playerChunkX + x, playerChunkZ + z);
                if (!activeChunks.ContainsKey(chunkCoord))
                {
                    SpawnChunk(chunkCoord);
                }
            }
        }
    }

    void SpawnChunk(Vector2Int coord)
    {
        GameObject chunkObject = new GameObject($"TerrainChunk_{coord.x}_{coord.y}");
        chunkObject.transform.position = new Vector3(coord.x * chunkSize, 0, coord.y * chunkSize);

        MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();
        meshRenderer.material = terrainMaterial;

        Mesh mesh = GenerateTerrainMesh(chunkObject.transform.position);
        meshFilter.mesh = mesh;

        chunkObject.AddComponent<MeshCollider>();

        activeChunks.Add(coord, chunkObject);
    }

    Mesh GenerateTerrainMesh(Vector3 worldPos)
    {
        Mesh mesh = new Mesh();
        int resolution = 20;
        Vector3[] vertices = new Vector3[(resolution + 1) * (resolution + 1)];
        int[] triangles = new int[resolution * resolution * 6];

        float stepSize = (float)chunkSize / resolution;

        for (int i = 0, z = 0; z <= resolution; z++)
        {
            for (int x = 0; x <= resolution; x++, i++)
            {
                float xCoord = worldPos.x + (x * stepSize);
                float zCoord = worldPos.z + (z * stepSize);
                float yCoord = Mathf.PerlinNoise(xCoord * noiseScale, zCoord * noiseScale) * heightMultiplier;

                vertices[i] = new Vector3(x * stepSize, yCoord, z * stepSize);
            }
        }

        int vert = 0;
        int tris = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                triangles[tris + 0] = vert + 0;
                triangles[tris + 1] = vert + resolution + 1;
                triangles[tris + 2] = vert + 1;

                triangles[tris + 3] = vert + 1;
                triangles[tris + 4] = vert + resolution + 1;
                triangles[tris + 5] = vert + resolution + 2;

                vert++;
                tris += 6;
            }
            vert++;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        return mesh;
    }
}