private const float RetryInterval = 5f;

// Per-weapon model config
private sealed class WeaponConfig
{
    public string TargetName;
    public Vector3 LocalPosition;
    public Vector3 LocalScale;
    public bool FlipUV;
    public bool Loaded;
    public float NextAttemptTime;

    public WeaponConfig(string targetName, Vector3 pos, Vector3 scale, bool flipUV)
    {
        TargetName = targetName;
        LocalPosition = pos;
        LocalScale = scale;
        FlipUV = flipUV;
    }
}

private readonly WeaponConfig[] _configs =
{
    new WeaponConfig("Knife(Clone)",          new Vector3( 0f,    -0.65f, 1f),   new Vector3(0.03f,  0.03f,  0.03f), true),
    new WeaponConfig("AWP(Clone)",            new Vector3(-0.35f, -0.4f,  1.2f), new Vector3(0.005f, 0.005f, 0.005f), false),
    // add more here
};

private void Update()
{
    for (int i = 0; i < _configs.Length; i++)
    {
        WeaponConfig cfg = _configs[i];
        if (cfg.Loaded)
            continue;

        if (Time.unscaledTime < cfg.NextAttemptTime)
            continue;

        // Only schedule the next attempt if we actually *tried* (weapon existed).
        GameObject weapon = FindWeapon(cfg.TargetName);
        if (weapon == null)
            continue;

        cfg.NextAttemptTime = Time.unscaledTime + RetryInterval;

        try
        {
            // Swap this for your actual Models API call.
            // Arguments here mirror what the Blades of Chaos plugin does.
            bool ok = LoadModelIntoWeapon(
                weapon,
                modelPath: ResolveModelPath(cfg.TargetName),
                localPosition: cfg.LocalPosition,
                localScale: cfg.LocalScale,
                flipUV: cfg.FlipUV);

            if (ok)
            {
                cfg.Loaded = true;
                Debug.Log($"[ULTRADROID] Model attached: {cfg.TargetName}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ULTRADROID] Model load failed for {cfg.TargetName}: {ex}");
        }
    }
}

private static GameObject FindWeapon(string targetName)
{
    GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
    for (int i = 0; i < all.Length; i++)
    {
        GameObject obj = all[i];
        if (obj != null && obj.activeInHierarchy && obj.name == targetName)
            return obj;
    }
    return null;
}

// Placeholder — replace with your real Models API entry point.
private static bool LoadModelIntoWeapon(GameObject weapon, string modelPath,
    Vector3 localPosition, Vector3 localScale, bool flipUV)
{
    // e.g. return ULTRADROID.Models.Load(weapon, modelPath, localPosition, localScale, flipUV);
    return false;
}
