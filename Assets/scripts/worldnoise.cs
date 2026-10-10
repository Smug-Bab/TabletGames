using UnityEngine;

public static class WorldNoise
{
    public static float RandomSeed()
    {
        return Random.Range(1f, 100000f);
    }

    public static float Range(float seed, float offset, float minValue, float maxValue)
    {
        float sample = Mathf.PerlinNoise((seed + offset) * 0.013f, (seed * 1.73f + offset) * 0.017f);
        return Mathf.Lerp(minValue, maxValue, sample);
    }

    public static float Hash01(float x, float z, float seed)
    {
        float sample = Mathf.Sin(x * 12.9898f + z * 78.233f + seed * 37.719f);
        return Mathf.Repeat(Mathf.Abs(sample), 1f);
    }

    public static float Perlin2D(float x, float z, float seed, float scale = 0.01f)
    {
        return Mathf.PerlinNoise((x + seed) * scale, (z + seed * 1.37f) * scale);
    }
}
