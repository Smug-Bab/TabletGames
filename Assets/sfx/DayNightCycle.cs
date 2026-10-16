using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Day/night cycle. Add to any GameObject (e.g. the one holding WorldController).
/// - Rotates one directional light across the sky; at night the same light becomes a dim moon.
/// - Fades the sun colour, light intensity, ambient light, fog colour and skybox exposure.
/// - Optionally switches lights on at night (headlights, street lamps).
/// Works with the Built-in and URP pipelines. HDRP drives its sky from volumes and won't pick up the ambient/skybox parts.
/// </summary>
public class DayNightCycle : MonoBehaviour
{
    [Header("Time")]
    [Range(0f, 24f), Tooltip("Hour of the day when the game starts (12 = noon).")]
    public float startHour = 9f;
    [Tooltip("Real-time minutes for one full 24 hour day.")]
    public float dayLengthMinutes = 10f;
    public bool paused = false;

    [Header("Sun & Moon")]
    [Tooltip("The directional light to drive. Leave empty to use the scene's sun, or one is created.")]
    public Light sun;
    [Tooltip("Compass direction the sun rises towards (degrees).")]
    public float sunYaw = 30f;
    [Range(0f, 60f), Tooltip("Tilts the sun's path so it doesn't pass straight overhead at noon.")]
    public float sunTilt = 20f;
    public float sunIntensity = 1.1f;
    public float moonIntensity = 0.15f;
    public Color moonColor = new Color(0.55f, 0.65f, 1f);
    [Tooltip("Sun colour by height in the sky: left = on the horizon, right = straight overhead.")]
    public Gradient sunColorByHeight = DefaultSunGradient();

    [Header("Environment")]
    public bool controlAmbient = true;
    public Color dayAmbient = new Color(0.55f, 0.62f, 0.72f);
    public Color nightAmbient = new Color(0.04f, 0.05f, 0.09f);
    public bool controlFog = true;
    public Color dayFog = new Color(0.72f, 0.80f, 0.90f);
    public Color nightFog = new Color(0.02f, 0.03f, 0.06f);
    [Tooltip("Changes the skybox material's _Exposure (works with the Procedural and Cubemap skyboxes).")]
    public bool controlSkybox = true;
    public float dayExposure = 1.3f;
    public float nightExposure = 0.08f;

    [Header("Night Lights")]
    [Tooltip("Lights switched on at night and off by day (headlights, street lamps...).")]
    public Light[] nightLights;
    [Range(0f, 1f), Tooltip("Lights come on when daylight drops below this.")]
    public float nightLightThreshold = 0.3f;

    [Header("Debug")]
    public bool showClock = false;

    /// <summary>Current time in hours, 0 to 24.</summary>
    public float Hour { get; private set; }
    /// <summary>0 = full night, 1 = full day.</summary>
    public float Daylight { get; private set; }
    public bool IsNight { get; private set; }

    /// <summary>Raised when night begins (true) or ends (false).</summary>
    public event Action<bool> NightChanged;

    private Material skyMaterial;
    private GUIStyle clockStyle;

    private void Awake()
    {
        Hour = Mathf.Repeat(startHour, 24f);

        if (sun == null) sun = RenderSettings.sun;
        if (sun == null) sun = FindDirectionalLight();
        if (sun == null)
        {
            var go = new GameObject("Sun");
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
        }
        RenderSettings.sun = sun;

        // Work on a copy so the skybox asset in your project is never modified
        if (controlSkybox && RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure"))
        {
            skyMaterial = new Material(RenderSettings.skybox);
            RenderSettings.skybox = skyMaterial;
        }

        if (controlAmbient) RenderSettings.ambientMode = AmbientMode.Flat;

        Apply();
        IsNight = Daylight < nightLightThreshold;   // initial state, no event
        SetNightLights(IsNight);
    }

    private void OnDestroy()
    {
        if (skyMaterial != null) Destroy(skyMaterial);
    }

    private void Update()
    {
        if (!paused && dayLengthMinutes > 0f)
            Hour = Mathf.Repeat(Hour + Time.deltaTime * 24f / (dayLengthMinutes * 60f), 24f);

        Apply();
    }

    /// <summary>Jump to a time of day (0-24).</summary>
    public void SetHour(float hour)
    {
        Hour = Mathf.Repeat(hour, 24f);
        Apply();
    }

    // ------------------------------------------------------------------

    private void Apply()
    {
        // 6h = sunrise, 12h = noon, 18h = sunset, 0h = midnight
        float angle = Hour / 24f * 360f - 90f;
        Quaternion sunRot = Quaternion.Euler(0f, sunYaw, 0f) * Quaternion.Euler(0f, 0f, sunTilt) * Quaternion.Euler(angle, 0f, 0f);

        Vector3 lightDir = sunRot * Vector3.forward;    // direction the light travels
        float height = -lightDir.y;                     // > 0 = sun above the horizon

        if (height >= 0f)
        {
            sun.transform.rotation = sunRot;
            sun.color = sunColorByHeight.Evaluate(Mathf.Clamp01(height));
            sun.intensity = sunIntensity * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(height / 0.25f));
        }
        else
        {
            // Below the horizon: the same light becomes the moon, shining from the opposite side.
            // Both intensities are zero at the crossing, so the swap is invisible.
            sun.transform.rotation = Quaternion.LookRotation(-lightDir);
            sun.color = moonColor;
            sun.intensity = moonIntensity * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(-height / 0.25f));
        }

        Daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.1f, 0.3f, height));

        if (controlAmbient) RenderSettings.ambientLight = Color.Lerp(nightAmbient, dayAmbient, Daylight);
        if (controlFog) RenderSettings.fogColor = Color.Lerp(nightFog, dayFog, Daylight);
        if (skyMaterial != null) skyMaterial.SetFloat("_Exposure", Mathf.Lerp(nightExposure, dayExposure, Daylight));

        bool night = Daylight < nightLightThreshold;
        if (night != IsNight)
        {
            IsNight = night;
            SetNightLights(night);
            NightChanged?.Invoke(night);
        }
    }

    private void SetNightLights(bool on)
    {
        if (nightLights == null) return;
        foreach (var l in nightLights)
        {
            if (l != null) l.enabled = on;
        }
    }

    private static Light FindDirectionalLight()
    {
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type == LightType.Directional) return l;
        }
        return null;
    }

    private static Gradient DefaultSunGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.42f, 0.18f), 0.00f),   // sunrise / sunset
                new GradientColorKey(new Color(1.00f, 0.72f, 0.45f), 0.15f),
                new GradientColorKey(new Color(1.00f, 0.95f, 0.85f), 0.50f),
                new GradientColorKey(new Color(1.00f, 0.98f, 0.95f), 1.00f)    // noon
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    private void OnGUI()
    {
        if (!showClock) return;

        if (clockStyle == null)
        {
            clockStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            clockStyle.normal.textColor = Color.white;
        }

        int h = Mathf.FloorToInt(Hour);
        int m = Mathf.FloorToInt((Hour - h) * 60f);
        GUI.Label(new Rect(24f, 16f, 200f, 36f), h.ToString("00") + ":" + m.ToString("00"), clockStyle);
    }
}
