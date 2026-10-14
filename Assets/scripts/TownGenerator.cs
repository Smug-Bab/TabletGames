using UnityEngine;

public class TownGenerator : MonoBehaviour
{
    public GameObject[] buildingPrefabs;

    public void SpawnTown(Vector3 centerPosition)
    {
        // Spawns a cluster of buildings around a road intersection
        for (int i = 0; i < 5; i++)
        {
            Vector3 offset = new Vector3(Random.Range(-30f, 30f), 0, Random.Range(-30f, 30f));
            GameObject prefab = buildingPrefabs[Random.Range(0, buildingPrefabs.Length)];
            Instantiate(prefab, centerPosition + offset, Quaternion.Euler(0, Random.Range(0, 360), 0));
        }
    }
}