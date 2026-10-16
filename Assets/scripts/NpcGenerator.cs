using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Populates towns with capsule pedestrians that roam around and can be run over (see NpcController).
/// NPCs spawn on free ground inside each town, never inside buildings, and only once the
/// town's buildings exist. Streamed in/out around the player, like towns and trees.
/// </summary>
[RequireComponent(typeof(TerrainGenerator))]
[RequireComponent(typeof(RoadGenerator))]
[RequireComponent(typeof(TownGenerator))]
public class NpcGenerator : MonoBehaviour
{
    [Header("NPCs")]
    [Tooltip("Optional. Leave empty to use the default primitive material.")]
    public Material npcMaterial;
    public Vector2Int npcsPerTown = new Vector2Int(6, 14);
    [Tooltip("Capsule scale. The primitive is 1 x 2 x 1 m, so (0.7, 0.9, 0.7) gives a 1.8 m tall person.")]
    public Vector3 npcSize = new Vector3(0.7f, 0.9f, 0.7f);
    [Range(0f, 0.3f)] public float sizeVariation = 0.1f;
    public Color[] shirtColors =
    {
        new Color(0.85f, 0.85f, 0.85f), new Color(0.2f, 0.4f, 0.8f), new Color(0.9f, 0.75f, 0.2f),
        new Color(0.3f, 0.65f, 0.35f), new Color(0.75f, 0.35f, 0.6f), new Color(0.95f, 0.5f, 0.2f)
    };

    [Header("Roaming")]
    public float walkSpeed = 1.5f;
    [Tooltip("Seconds an NPC stands still between walks (min, max).")]
    public Vector2 pauseTime = new Vector2(1f, 5f);
    [Tooltip("How far past the town radius NPCs may wander.")]
    public float roamExtra = 6f;
    [Tooltip("Free space required around a spawn point (keeps NPCs out of walls).")]
    public float spawnClearance = 1f;

    [Header("Getting Hit")]
    [Tooltip("Minimum impact speed (m/s) with the vehicle that knocks an NPC down.")]
    public float hitSpeedThreshold = 3f;
    [Tooltip("Seconds a knocked-down NPC stays before shrinking away.")]
    public float removeDelay = 8f;
    public Color hitColor = new Color(0.55f, 0.1f, 0.1f);
    public bool showCounter = true;

    [Header("Streaming (WorldController sets these from viewDistance)")]
    public float spawnRadius = 150f;
    public float despawnRadius = 200f;
    public float updateInterval = 0.5f;
    public int maxTownsPerCall = 1;

    private class TownNpcs
    {
        public Transform root;
        public Vector2 center;
    }

    private TerrainGenerator terrain;
    private RoadGenerator roads;
    private TownGenerator towns;
    private Transform npcRoot;
    private Transform townsRoot;
    private float nextUpdate;
    private MaterialPropertyBlock block;
    private GUIStyle counterStyle;

    private readonly Dictionary<Vector2Int, TownNpcs> active = new Dictionary<Vector2Int, TownNpcs>();
    private readonly List<Vector2Int> toRemove = new List<Vector2Int>();
    private readonly List<KeyValuePair<float, Vector2Int>> pending = new List<KeyValuePair<float, Vector2Int>>();
    private readonly Collider[] overlapBuffer = new Collider[8];

    private void Awake()
    {
        terrain = GetComponent<TerrainGenerator>();
        roads = GetComponent<RoadGenerator>();
        towns = GetComponent<TownGenerator>();
        npcRoot = new GameObject("Npcs").transform;
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>Called every frame by WorldController (does real work every updateInterval seconds).</summary>
    public void UpdateNpcs(Vector3 playerPosition)
    {
        if (Time.time < nextUpdate) return;
        nextUpdate = Time.time + updateInterval;
        Refresh(playerPosition, maxTownsPerCall);
    }

    /// <summary>Populates every town in range immediately (use at startup).</summary>
    public void LoadAllNear(Vector3 position)
    {
        Refresh(position, int.MaxValue);
    }

    // ------------------------------------------------------------------
    // Streaming
    // ------------------------------------------------------------------

    void Refresh(Vector3 playerPosition, int budget)
    {
        Vector2 p = new Vector2(playerPosition.x, playerPosition.z);

        // Remove far towns' NPCs
        toRemove.Clear();
        foreach (var kv in active)
        {
            if (Vector2.Distance(kv.Value.center, p) > despawnRadius)
                toRemove.Add(kv.Key);
        }
        foreach (var key in toRemove)
        {
            Destroy(active[key].root.gameObject);
            active.Remove(key);
        }

        // Find towns that need NPCs
        pending.Clear();
        int range = Mathf.CeilToInt(spawnRadius / roads.roadSpacing) + 1;
        int cx = Mathf.RoundToInt(p.x / roads.roadSpacing);
        int cy = Mathf.RoundToInt(p.y / roads.roadSpacing);

        for (int x = cx - range; x <= cx + range; x++)
        {
            for (int y = cy - range; y <= cy + range; y++)
            {
                var key = new Vector2Int(x, y);
                if (active.ContainsKey(key)) continue;
                if (!towns.IsTown(x, y)) continue;

                float d = Vector2.Distance(roads.GetNodePosition(x, y), p);
                if (d <= spawnRadius && TownBuilt(key))
                    pending.Add(new KeyValuePair<float, Vector2Int>(d, key));
            }
        }

        pending.Sort((a, b) => a.Key.CompareTo(b.Key));
        int count = Mathf.Min(budget, pending.Count);
        for (int i = 0; i < count; i++)
            SpawnTown(pending[i].Value);
    }

    /// <summary>True once TownGenerator has created this town's buildings.</summary>
    bool TownBuilt(Vector2Int key)
    {
        if (townsRoot == null)
        {
            GameObject go = GameObject.Find("Towns");
            if (go != null) townsRoot = go.transform;
        }
        if (townsRoot == null) return true;     // can't tell, so don't block
        return townsRoot.Find("Town_" + key.x + "_" + key.y) != null;
    }

    // ------------------------------------------------------------------
    // Spawning
    // ------------------------------------------------------------------

    void SpawnTown(Vector2Int key)
    {
        int degree = roads.GetNodeDegree(key.x, key.y);
        if (degree < 2) return;

        // Colliders of buildings made this very frame must be visible to the overlap checks
        Physics.SyncTransforms();

        Vector2 center = roads.GetNodePosition(key.x, key.y);
        float radius = Mathf.Min(towns.baseRadius + degree * towns.radiusPerRoad, roads.roadSpacing * 0.5f);

        var rng = new System.Random((int)(HashU(key.x, key.y, 321) & 0x7FFFFFFF));
        int count = rng.Next(npcsPerTown.x, npcsPerTown.y + 1);

        var town = new TownNpcs { center = center };
        town.root = new GameObject("TownNpcs_" + key.x + "_" + key.y).transform;
        town.root.SetParent(npcRoot, false);

        for (int i = 0; i < count; i++)
            TrySpawnNpc(town.root, center, radius, rng);

        active.Add(key, town);
    }

    void TrySpawnNpc(Transform parent, Vector2 center, float radius, System.Random rng)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            // Uniform point inside the town circle
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            float dist = Mathf.Sqrt((float)rng.NextDouble()) * radius;
            Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;

            float groundY = terrain.GetHeightAt(pos.x, pos.y);
            if (!IsFreeSpot(pos, groundY)) continue;

            CreateNpc(parent, center, radius, pos, groundY, rng);
            return;
        }
    }

    /// <summary>Free = no building (box collider) within spawnClearance of the spot.</summary>
    bool IsFreeSpot(Vector2 pos, float groundY)
    {
        Vector3 check = new Vector3(pos.x, groundY + 1.3f, pos.y);
        int n = Physics.OverlapSphereNonAlloc(check, spawnClearance, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (overlapBuffer[i] is BoxCollider) return false;
        }
        return true;
    }

    void CreateNpc(Transform parent, Vector2 center, float radius, Vector2 pos, float groundY, System.Random rng)
    {
        float variation = 1f + ((float)rng.NextDouble() * 2f - 1f) * sizeVariation;
        Vector3 size = npcSize * variation;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "NPC";
        go.transform.SetParent(parent, false);
        go.transform.localScale = size;
        go.transform.SetPositionAndRotation(
            new Vector3(pos.x, groundY + size.y + 0.1f, pos.y),      // capsule is 2 m tall at scale 1
                                            Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));

        Renderer rend = go.GetComponent<Renderer>();
        if (npcMaterial != null) rend.sharedMaterial = npcMaterial;

        Color shirt = shirtColors != null && shirtColors.Length > 0
        ? shirtColors[rng.Next(shirtColors.Length)]
        : Color.HSVToRGB((float)rng.NextDouble(), 0.6f, 0.9f);
        if (block == null) block = new MaterialPropertyBlock();
        block.SetColor("_Color", shirt);        // Built-in pipeline
        block.SetColor("_BaseColor", shirt);    // URP / HDRP
        rend.SetPropertyBlock(block);

        NpcController npc = go.AddComponent<NpcController>();   // also adds the Rigidbody
        npc.Init(center, radius + roamExtra, walkSpeed, pauseTime, hitSpeedThreshold, removeDelay, hitColor);
    }

    // ------------------------------------------------------------------

    private void OnGUI()
    {
        if (!showCounter) return;

        if (counterStyle == null)
        {
            counterStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperRight
            };
            counterStyle.normal.textColor = Color.white;
        }
        GUI.Label(new Rect(Screen.width - 324f, 16f, 300f, 36f), "Run over: " + NpcController.RunOverCount, counterStyle);
    }

    uint HashU(int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393) ^ (uint)(y * 668265263) ^ ((uint)salt * 2246822519u) ^ (uint)(roads.seed * 1274126177);
            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;
            return h;
        }
    }
}
