using UnityEngine;

[RequireComponent(typeof(TerrainGenerator))]
[RequireComponent(typeof(RoadGenerator))]
[RequireComponent(typeof(TownGenerator))]
[RequireComponent(typeof(TreeGenerator))]
[RequireComponent(typeof(NpcGenerator))]
[RequireComponent(typeof(TrafficControlGenerator))]
[RequireComponent(typeof(DayNightCycle))]
public class WorldController : MonoBehaviour
{
    public static WorldController Instance { get; private set; }

    [Header("Player Settings")]
    [Tooltip("Drag your vehicle/player prefab here. It spawns at the town nearest the world origin.")]
    public GameObject playerPrefab;
    [Tooltip("Height above the terrain surface to drop the player from.")]
    public float spawnHeight = 2f;
    [HideInInspector]
    public Transform playerTransform;

    [Header("Generator References")]
    [HideInInspector] public TerrainGenerator terrainGenerator;
    [HideInInspector] public RoadGenerator roadGenerator;
    [HideInInspector] public TownGenerator townGenerator;
    [HideInInspector] public TreeGenerator treeGenerator;
    [HideInInspector] public NpcGenerator npcGenerator;
    [HideInInspector] public TrafficControlGenerator trafficControlGenerator;
    [HideInInspector] public DayNightCycle dayNightCycle;

    [Header("Settings")]
    [Tooltip("Master streaming distance. Drives terrain, road, town, tree and NPC radii.")]
    public float viewDistance = 200f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        terrainGenerator = GetComponent<TerrainGenerator>();
        roadGenerator = GetComponent<RoadGenerator>();
        townGenerator = GetComponent<TownGenerator>();

        // RequireComponent only auto-adds on new components, so cover existing objects too
        treeGenerator = GetComponent<TreeGenerator>();
        if (treeGenerator == null) treeGenerator = gameObject.AddComponent<TreeGenerator>();
        npcGenerator = GetComponent<NpcGenerator>();
        if (npcGenerator == null) npcGenerator = gameObject.AddComponent<NpcGenerator>();
        trafficControlGenerator = GetComponent<TrafficControlGenerator>();
        if (trafficControlGenerator == null) trafficControlGenerator = gameObject.AddComponent<TrafficControlGenerator>();
        dayNightCycle = GetComponent<DayNightCycle>();
        if (dayNightCycle == null) dayNightCycle = gameObject.AddComponent<DayNightCycle>();

        // One knob controls how far everything streams
        terrainGenerator.chunkRadius = Mathf.Max(1, Mathf.CeilToInt(viewDistance / terrainGenerator.chunkSize));
        roadGenerator.generateRadius = viewDistance;
        roadGenerator.despawnRadius = viewDistance * 1.3f;
        townGenerator.spawnRadius = viewDistance;
        townGenerator.despawnRadius = viewDistance * 1.3f;
        treeGenerator.spawnRadius = viewDistance;
        treeGenerator.despawnRadius = viewDistance * 1.3f;

        // Signs and lights spawn a bit inside the road radius so the roads exist before they do
        trafficControlGenerator.spawnRadius = viewDistance * 0.8f;
        trafficControlGenerator.despawnRadius = viewDistance * 1.25f;

        // NPCs spawn inside the streamed-in area so buildings and ground exist before they do
        npcGenerator.spawnRadius = viewDistance * 0.75f;
        npcGenerator.despawnRadius = viewDistance;
    }

    private void Start()
    {
        SpawnPlayer();
    }

    void SpawnPlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogWarning("Player Prefab is not assigned in the WorldController!");
            return;
        }

        FindSpawn(out Vector2 xz, out Quaternion rot);
        Vector3 spawnPos = new Vector3(xz.x, 0f, xz.y);

        // Build the world around the spawn first so there is ground (and colliders) to land on
        PreloadWorld(spawnPos);

        spawnPos.y = terrainGenerator.GetHeightAt(xz.x, xz.y) + spawnHeight;

        GameObject playerInstance = Instantiate(playerPrefab, spawnPos, rot);
        playerTransform = playerInstance.transform;
    }

    /// <summary>Picks the town nearest the origin, facing down one of its roads.</summary>
    void FindSpawn(out Vector2 xz, out Quaternion rot)
    {
        rot = Quaternion.identity;

        if (townGenerator.FindNearestTown(Vector2.zero, 10, out Vector2Int node))
        {
            xz = roadGenerator.GetNodePosition(node.x, node.y);

            if (roadGenerator.TryGetRoadDirection(node.x, node.y, out Vector2 dir))
                rot = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y));
            return;
        }

        Debug.LogWarning("No town found near the origin (is Town Chance too low?). Spawning at the origin.");
        xz = Vector2.zero;
    }

    /// <summary>Builds terrain, roads, towns, trees and NPCs around a position in one go, ignoring per-frame budgets.</summary>
    void PreloadWorld(Vector3 position)
    {
        terrainGenerator.LoadAllNear(position);

        int roadBudget = roadGenerator.maxBuildsPerCall;
        roadGenerator.maxBuildsPerCall = int.MaxValue;
        roadGenerator.UpdateRoads(position);
        roadGenerator.maxBuildsPerCall = roadBudget;

        int townBudget = townGenerator.maxTownsPerCall;
        townGenerator.maxTownsPerCall = int.MaxValue;
        townGenerator.UpdateTowns(position);
        townGenerator.maxTownsPerCall = townBudget;

        int treeBudget = treeGenerator.maxChunksPerCall;
        treeGenerator.maxChunksPerCall = int.MaxValue;
        treeGenerator.UpdateTrees(position);
        treeGenerator.maxChunksPerCall = treeBudget;

        trafficControlGenerator.LoadAllNear(position);

        // Last, so towns and their buildings already exist
        npcGenerator.LoadAllNear(position);
    }

    private void Update()
    {
        if (playerTransform == null) return;

        Vector3 playerPos = playerTransform.position;

        terrainGenerator.UpdateChunks(playerPos);
        roadGenerator.UpdateRoads(playerPos);
        townGenerator.UpdateTowns(playerPos);
        treeGenerator.UpdateTrees(playerPos);
        trafficControlGenerator.UpdateControls(playerPos);
        npcGenerator.UpdateNpcs(playerPos);
    }
}
