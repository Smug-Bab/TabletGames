using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places stop signs and traffic lights at road intersections, deterministically from the seed.
/// Everything is built from primitive cubes for now (swap BuildStopSign / BuildTrafficLight
/// for prefab spawns once you have models). Fixtures are streamed in/out around the player.
///
/// Per intersection (a node with at least minRoads roads):
///   1. trafficLightChance -> traffic lights on every approach
///   2. otherwise stopSignChance -> a stop sign on every approach
///   3. otherwise nothing
/// Assumes right-hand traffic: each fixture stands on the right side of its approaching road.
/// </summary>
[RequireComponent(typeof(TerrainGenerator))]
[RequireComponent(typeof(RoadGenerator))]
public class TrafficControlGenerator : MonoBehaviour
{
    public enum ControlType { None, StopSign, TrafficLight }

    [Header("Placement")]
    public int seed = 4321;
    [Range(0f, 1f), Tooltip("Chance that an intersection gets traffic lights.")]
    public float trafficLightChance = 0.2f;
    [Range(0f, 1f), Tooltip("Chance that an intersection without lights gets stop signs.")]
    public float stopSignChance = 0.6f;
    [Range(3, 4), Tooltip("Roads needed at a node to count as an intersection. 3 = T-junctions and crossings, 4 = crossings only.")]
    public int minRoads = 3;
    [Tooltip("Extra distance along the road, past the round intersection pad, where fixtures stand (m).")]
    public float alongOffset = 1.5f;
    [Tooltip("Distance from the road edge to the pole (m).")]
    public float sideOffset = 1.2f;
    [Tooltip("Give the poles a collider so vehicles can hit them.")]
    public bool solidPoles = true;

    [Header("Stop Sign (placeholder cubes)")]
    public float stopSignHeight = 2.4f;

    [Header("Traffic Light (placeholder cubes)")]
    public float trafficLightHeight = 5f;
    [Tooltip("Seconds each direction stays green.")]
    public float greenTime = 8f;
    [Tooltip("Seconds of yellow before the direction turns red.")]
    public float yellowTime = 2f;

    [Header("Streaming")]
    public float spawnRadius = 160f;
    public float despawnRadius = 260f;
    [Tooltip("Limits intersections built per UpdateControls call to avoid frame spikes.")]
    public int maxPerCall = 2;

    private TerrainGenerator terrain;
    private RoadGenerator roads;
    private Transform controlRoot;

    private class LightHead
    {
        public Renderer red, yellow, green;
        public int group;        // 0 or 1: opposite approaches share a group
        public int state = -1;   // 0 = red, 1 = yellow, 2 = green
    }

    private class ActiveControl
    {
        public GameObject root;
        public float phaseOffset;
        public readonly List<LightHead> heads = new List<LightHead>();
    }

    private readonly Dictionary<Vector2Int, ActiveControl> active = new Dictionary<Vector2Int, ActiveControl>();
    private readonly List<KeyValuePair<float, Vector2Int>> pending = new List<KeyValuePair<float, Vector2Int>>();
    private readonly List<Vector2Int> toRemove = new List<Vector2Int>();
    private readonly List<Vector2> arms = new List<Vector2>();

    // Placeholder materials (created on first use)
    private Shader baseShader;
    private bool materialsReady;
    private Material matPole, matStopRed, matWhite, matHousing;
    private Material redLit, redDim, yellowLit, yellowDim, greenLit, greenDim;

    private void Awake()
    {
        terrain = GetComponent<TerrainGenerator>();
        roads = GetComponent<RoadGenerator>();
        controlRoot = new GameObject("TrafficControls").transform;
    }

    private void OnDestroy()
    {
        foreach (var kv in active)
            if (kv.Value.root != null) Destroy(kv.Value.root);
        active.Clear();
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>Called every frame by WorldController.</summary>
    public void UpdateControls(Vector3 playerPosition)
    {
        UpdateControls(playerPosition, maxPerCall);
    }

    /// <summary>Builds every missing fixture around the position immediately (use at startup).</summary>
    public void LoadAllNear(Vector3 position)
    {
        UpdateControls(position, int.MaxValue);
    }

    /// <summary>What kind of control (if any) a node has. Deterministic from the seed.</summary>
    public ControlType GetControlType(int x, int y)
    {
        if (roads.GetNodeDegree(x, y) < minRoads) return ControlType.None;

        if (Hash01(x, y, 31) < trafficLightChance) return ControlType.TrafficLight;
        if (Hash01(x, y, 32) < stopSignChance) return ControlType.StopSign;
        return ControlType.None;
    }

    // ------------------------------------------------------------------
    // Streaming
    // ------------------------------------------------------------------

    void UpdateControls(Vector3 playerPosition, int budget)
    {
        Vector2 p = new Vector2(playerPosition.x, playerPosition.z);

        // 1. Remove far-away intersections
        toRemove.Clear();
        foreach (var kv in active)
        {
            Vector2 n = roads.GetNodePosition(kv.Key.x, kv.Key.y);
            if (Vector2.Distance(n, p) > despawnRadius)
                toRemove.Add(kv.Key);
        }
        foreach (var key in toRemove)
        {
            if (active[key].root != null) Destroy(active[key].root);
            active.Remove(key);
        }

        // 2. Find missing intersections near the player
        pending.Clear();
        float spacing = roads.roadSpacing;
        int range = Mathf.CeilToInt(spawnRadius / spacing) + 1;
        int cx = Mathf.RoundToInt(p.x / spacing);
        int cy = Mathf.RoundToInt(p.y / spacing);

        for (int x = cx - range; x <= cx + range; x++)
        {
            for (int y = cy - range; y <= cy + range; y++)
            {
                var key = new Vector2Int(x, y);
                if (active.ContainsKey(key)) continue;

                float d = Vector2.Distance(roads.GetNodePosition(x, y), p);
                if (d > spawnRadius) continue;
                if (GetControlType(x, y) == ControlType.None) continue;

                pending.Add(new KeyValuePair<float, Vector2Int>(d, key));
            }
        }
        if (pending.Count == 0) return;

        // 3. Build nearest first
        pending.Sort((a, b) => a.Key.CompareTo(b.Key));
        int count = Mathf.Min(budget, pending.Count);
        for (int i = 0; i < count; i++)
            SpawnControl(pending[i].Value, GetControlType(pending[i].Value.x, pending[i].Value.y));
    }

    // ------------------------------------------------------------------
    // Building
    // ------------------------------------------------------------------

    void SpawnControl(Vector2Int node, ControlType type)
    {
        EnsureMaterials();

        roads.GetNodeArms(node.x, node.y, arms);
        if (arms.Count == 0) return;

        Vector2 center = roads.GetNodePosition(node.x, node.y);
        float nodeY = roads.GetNodeHeight(node.x, node.y);

        var ac = new ActiveControl();
        ac.root = new GameObject($"Control_{node.x}_{node.y}_{type}");
        ac.root.transform.SetParent(controlRoot, false);
        ac.phaseOffset = Hash01(node.x, node.y, 33) * 100f;

        float half = roads.roadWidth * 0.5f;
        float along = half * 1.15f + alongOffset;   // just past the intersection pad
        float lateral = half + sideOffset;          // just off the road edge
        Vector2 firstArm = arms[0];

        for (int i = 0; i < arms.Count; i++)
        {
            Vector2 d = arms[i];                    // leaving the intersection
            // Traffic approaches along -d; its right-hand side is (-d.y, d.x)
            Vector2 right = new Vector2(-d.y, d.x);
            Vector2 pos = center + d * along + right * lateral;

            float groundY = terrain.GetHeightAt(pos.x, pos.y);
            float baseY = Mathf.Min(groundY, nodeY) - 0.15f;
            Quaternion rot = Quaternion.LookRotation(new Vector3(d.x, 0f, d.y)); // +Z faces the driver

            GameObject fixture = new GameObject(type == ControlType.StopSign ? "StopSign" : "TrafficLight");
            fixture.transform.SetParent(ac.root.transform, false);
            fixture.transform.SetPositionAndRotation(new Vector3(pos.x, baseY, pos.y), rot);

            if (type == ControlType.StopSign)
            {
                float poleH = nodeY + stopSignHeight - baseY;
                BuildStopSign(fixture.transform, poleH);
            }
            else
            {
                float poleH = nodeY + trafficLightHeight - baseY;
                var head = BuildTrafficLight(fixture.transform, poleH);
                head.group = Mathf.Abs(Vector2.Dot(d, firstArm)) >= 0.7071f ? 0 : 1;
                ac.heads.Add(head);
            }
        }

        active.Add(node, ac);
    }

    /// <summary>Placeholder stop sign: grey pole, red square plate, white bar. Local +Z faces the driver.</summary>
    void BuildStopSign(Transform root, float poleH)
    {
        const float thick = 0.1f;
        MakeCube("Pole", root, new Vector3(0f, poleH * 0.5f, 0f), new Vector3(thick, poleH, thick), matPole);
        MakeCube("Plate", root, new Vector3(0f, poleH - 0.45f, 0.08f), new Vector3(0.8f, 0.8f, 0.06f), matStopRed);
        MakeCube("Bar", root, new Vector3(0f, poleH - 0.45f, 0.115f), new Vector3(0.6f, 0.14f, 0.01f), matWhite);

        AddPoleCollider(root, poleH, 0.3f);
    }

    /// <summary>Placeholder traffic light: grey pole, dark housing, three small lens cubes.</summary>
    LightHead BuildTrafficLight(Transform root, float poleH)
    {
        const float thick = 0.18f;
        MakeCube("Pole", root, new Vector3(0f, poleH * 0.5f, 0f), new Vector3(thick, poleH, thick), matPole);
        MakeCube("Arm", root, new Vector3(0f, poleH - 0.25f, 0.2f), new Vector3(0.1f, 0.1f, 0.4f), matPole);

        float housingY = poleH - 0.9f;
        MakeCube("Housing", root, new Vector3(0f, housingY, 0.35f), new Vector3(0.5f, 1.4f, 0.4f), matHousing);

        var head = new LightHead();
        float lensZ = 0.35f + 0.2f + 0.02f;
        head.red = MakeCube("Red", root, new Vector3(0f, housingY + 0.45f, lensZ), new Vector3(0.3f, 0.3f, 0.04f), redDim).GetComponent<Renderer>();
        head.yellow = MakeCube("Yellow", root, new Vector3(0f, housingY, lensZ), new Vector3(0.3f, 0.3f, 0.04f), yellowDim).GetComponent<Renderer>();
        head.green = MakeCube("Green", root, new Vector3(0f, housingY - 0.45f, lensZ), new Vector3(0.3f, 0.3f, 0.04f), greenDim).GetComponent<Renderer>();

        AddPoleCollider(root, poleH, 0.35f);
        return head;
    }

    void AddPoleCollider(Transform root, float poleH, float width)
    {
        if (!solidPoles) return;
        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, poleH * 0.5f, 0f);
        box.size = new Vector3(width, poleH, width);
    }

    GameObject MakeCube(string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;

        // Parts are visual only; the fixture root carries the one collider
        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    // ------------------------------------------------------------------
    // Light cycling
    // ------------------------------------------------------------------

    private void Update()
    {
        float phase = greenTime + yellowTime;
        float cycle = 2f * phase;
        if (cycle <= 0.01f) return;

        foreach (var kv in active)
        {
            ActiveControl ac = kv.Value;
            if (ac.heads.Count == 0) continue;

            float t = Time.time + ac.phaseOffset;
            for (int i = 0; i < ac.heads.Count; i++)
            {
                LightHead h = ac.heads[i];
                float local = Mathf.Repeat(t + h.group * phase, cycle);
                int state = local < greenTime ? 2 : (local < phase ? 1 : 0);

                if (h.state == state) continue;
                h.state = state;
                h.red.sharedMaterial = state == 0 ? redLit : redDim;
                h.yellow.sharedMaterial = state == 1 ? yellowLit : yellowDim;
                h.green.sharedMaterial = state == 2 ? greenLit : greenDim;
            }
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    void EnsureMaterials()
    {
        if (materialsReady) return;

        baseShader = Shader.Find("Universal Render Pipeline/Lit");
        if (baseShader == null) baseShader = Shader.Find("Standard");

        matPole = MakeMaterial(new Color(0.55f, 0.55f, 0.58f));
        matStopRed = MakeMaterial(new Color(0.8f, 0.05f, 0.05f));
        matWhite = MakeMaterial(new Color(0.95f, 0.95f, 0.95f));
        matHousing = MakeMaterial(new Color(0.08f, 0.08f, 0.08f));

        Color red = new Color(1f, 0.1f, 0.1f);
        Color yellow = new Color(1f, 0.8f, 0.1f);
        Color green = new Color(0.1f, 1f, 0.2f);

        redLit = MakeMaterial(red, true);
        redDim = MakeMaterial(Dim(red));
        yellowLit = MakeMaterial(yellow, true);
        yellowDim = MakeMaterial(Dim(yellow));
        greenLit = MakeMaterial(green, true);
        greenDim = MakeMaterial(Dim(green));

        materialsReady = true;
    }

    Material MakeMaterial(Color color, bool emissive = false)
    {
        Material m = new Material(baseShader) { color = color };
        if (emissive)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(color.r * 2f, color.g * 2f, color.b * 2f, 1f));
        }
        return m;
    }

    static Color Dim(Color c)
    {
        return new Color(c.r * 0.15f, c.g * 0.15f, c.b * 0.15f, 1f);
    }

    float Hash01(int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791) ^ (uint)(seed * 668265263);
            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
