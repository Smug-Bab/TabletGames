using UnityEngine;

[RequireComponent(typeof(TerrainGenerator))]
[RequireComponent(typeof(RoadGenerator))]
[RequireComponent(typeof(TownGenerator))]
public class WorldController : MonoBehaviour
{
    public static WorldController Instance { get; private set; }

    [Header("Player Settings")]
    [Tooltip("Drag your vehicle/player prefab here.")]
    public GameObject playerPrefab;
    [Tooltip("Optional spawn point transform. If empty, defaults to Vector3.up * 5.")]
    public Transform spawnPoint;
    [HideInInspector]
    public Transform playerTransform;

    [Header("Generator References")]
    [HideInInspector] public TerrainGenerator terrainGenerator;
    [HideInInspector] public RoadGenerator roadGenerator;
    [HideInInspector] public TownGenerator townGenerator;

    [Header("Settings")]
    public float viewDistance = 200f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Automatically cache references via RequireComponent
        terrainGenerator = GetComponent<TerrainGenerator>();
        roadGenerator = GetComponent<RoadGenerator>();
        townGenerator = GetComponent<TownGenerator>();
    }

    private void Start()
    {
        SpawnPlayer();
    }

    void SpawnPlayer()
    {
        if (playerPrefab != null)
        {
            Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : new Vector3(0, 5f, 0);
            Quaternion spawnRot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

            GameObject playerInstance = Instantiate(playerPrefab, spawnPos, spawnRot);
            playerTransform = playerInstance.transform;
        }
        else
        {
            Debug.LogWarning("Player Prefab is not assigned in the WorldController!");
        }
    }

    private void Update()
    {
        if (playerTransform == null) return;

        Vector3 playerPos = playerTransform.position;

        if (terrainGenerator != null)
            terrainGenerator.UpdateChunks(playerPos);

        if (roadGenerator != null)
            roadGenerator.UpdateRoads(playerPos);
    }
}
