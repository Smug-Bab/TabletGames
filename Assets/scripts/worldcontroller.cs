using UnityEngine;

public class WorldController : MonoBehaviour
{
    [Header("Runtime References")]
    public InfiniteTerrainSystem terrainSystem;
    public ProceduralRoadSystem roadSystem;
    public TownSystem townSystem;

    private void Reset()
    {
        EnsureComponents();
        BindReferences();
    }

    private void Awake()
    {
        EnsureComponents();
        BindReferences();
    }

    private void EnsureComponents()
    {
        if (terrainSystem == null)
        {
            terrainSystem = GetComponent<InfiniteTerrainSystem>();
            if (terrainSystem == null)
            {
                terrainSystem = gameObject.AddComponent<InfiniteTerrainSystem>();
            }
        }

        if (roadSystem == null)
        {
            roadSystem = GetComponent<ProceduralRoadSystem>();
            if (roadSystem == null)
            {
                roadSystem = gameObject.AddComponent<ProceduralRoadSystem>();
            }
        }

        if (townSystem == null)
        {
            townSystem = GetComponent<TownSystem>();
            if (townSystem == null)
            {
                townSystem = gameObject.AddComponent<TownSystem>();
            }
        }
    }

    private void BindReferences()
    {
        if (terrainSystem != null && terrainSystem.roadSystem == null)
        {
            terrainSystem.roadSystem = roadSystem;
        }

        if (roadSystem != null && roadSystem.townSystem == null)
        {
            roadSystem.townSystem = townSystem;
        }
    }
}
