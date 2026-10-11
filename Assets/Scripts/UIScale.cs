using UnityEngine;

/// <summary>
/// Makes all the on-screen UI scale with the screen size.
/// Every panel is laid out for a 1600 x 900 reference screen; UIScale.Apply() at the top of an
/// OnGUI scales that up or down to fit the real screen (keeping proportions).
/// Inside OnGUI use UIScale.Width / UIScale.Height instead of Screen.width / Screen.height,
/// and convert mouse / world positions with MouseToGui / WorldToGui.
/// </summary>
public static class UIScale
{
    public const float ReferenceWidth = 1600f;
    public const float ReferenceHeight = 900f;

    /// <summary>How much the reference layout is scaled to fit the current screen.</summary>
    public static float Factor => Mathf.Max(0.1f, Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight));

    /// <summary>Screen size in UI units (use these instead of Screen.width / Screen.height in OnGUI).</summary>
    public static float Width => Screen.width / Factor;
    public static float Height => Screen.height / Factor;

    /// <summary>Call first thing in every OnGUI.</summary>
    public static void Apply()
    {
        float s = Factor;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
    }

    /// <summary>Mouse position from the Input System (pixels, y up) to UI units (y down).</summary>
    public static Vector2 MouseToGui(Vector2 screenPixels) =>
        new Vector2(screenPixels.x / Factor, (Screen.height - screenPixels.y) / Factor);

    /// <summary>A world position to UI units. Returns false if it's behind the camera.</summary>
    public static bool WorldToGui(Camera cam, Vector3 world, out Vector2 gui)
    {
        Vector3 p = cam.WorldToScreenPoint(world);
        gui = new Vector2(p.x / Factor, (Screen.height - p.y) / Factor);
        return p.z > 0f;
    }
}
