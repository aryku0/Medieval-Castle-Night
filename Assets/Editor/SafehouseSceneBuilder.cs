#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Nightguard > Create Safehouse Scene
/// Builds a placeholder lodgings room (walls, floor, bills table, chest, apothecary jar, bed,
/// your player and a camera), saves it as Assets/Scenes/Safehouse.unity and adds both the
/// castle scene and the safehouse to the build list so the game can move between them.
/// </summary>
public static class SafehouseSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Safehouse.unity";
    const string SpriteFolder = "Assets/Safehouse";
    const string SpritePath = SpriteFolder + "/WhiteSquare.png";

    [MenuItem("Tools/Nightguard/Create Safehouse Scene")]
    static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Dialog("Exit Play mode first."); return; }

        var playerPrefab = FindPlayerPrefab();
        if (!playerPrefab)
        {
            Dialog("I couldn't find a player prefab.\n\n"
                + "Drag Player1 from the Hierarchy into the Project window (for example into the Assets folder) "
                + "to turn it into a prefab, then run this again.");
            return;
        }

        if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Nightguard", "A Safehouse scene already exists. Replace it?", "Replace", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        string castlePath = SceneManager.GetActiveScene().path;
        if (string.IsNullOrEmpty(castlePath)) { Dialog("Open and save your castle scene first."); return; }

        var sprite = WhiteSprite();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Camera + light (URP 2D sprites need a 2D light or they render black)
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.07f, 0.06f);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        camGo.AddComponent<AudioListener>();

        var lightGo = new GameObject("Global Light 2D");
        var light = lightGo.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;
        light.intensity = 1f;
        light.color = new Color(1f, 0.93f, 0.8f);

        // Room shell
        Block(sprite, "BackWall", new Vector2(0f, 0.5f), new Vector2(18f, 8f), new Color(0.32f, 0.24f, 0.17f), -10, ColliderKind.None);
        Block(sprite, "Window (daylight)", new Vector2(-1.5f, 1.4f), new Vector2(2.2f, 1.6f), new Color(0.75f, 0.85f, 0.95f), -9, ColliderKind.None);
        Block(sprite, "Floor", new Vector2(0f, -3.5f), new Vector2(18f, 1f), new Color(0.22f, 0.16f, 0.1f), 0, ColliderKind.Solid);
        Block(sprite, "WallLeft", new Vector2(-9f, 0.5f), new Vector2(1f, 9f), new Color(0.18f, 0.13f, 0.09f), 0, ColliderKind.Solid);
        Block(sprite, "WallRight", new Vector2(9f, 0.5f), new Vector2(1f, 9f), new Color(0.18f, 0.13f, 0.09f), 0, ColliderKind.Solid);

        // Furniture you can use
        var table = Block(sprite, "BillsTable", new Vector2(-5f, -2.55f), new Vector2(2.4f, 0.9f), new Color(0.45f, 0.3f, 0.16f), 0, ColliderKind.Trigger);
        Use(table, SafehouseManager.Panel.Bills);
        Block(sprite, "Letters (decor)", new Vector2(-5.3f, -2.03f), new Vector2(0.6f, 0.15f), new Color(0.92f, 0.88f, 0.75f), 1, ColliderKind.None);

        var chest = Block(sprite, "UpgradeChest", new Vector2(-1.5f, -2.6f), new Vector2(1.4f, 0.8f), new Color(0.35f, 0.22f, 0.12f), 0, ColliderKind.Trigger);
        Use(chest, SafehouseManager.Panel.Upgrades);

        Block(sprite, "JarStand (decor)", new Vector2(2f, -2.4f), new Vector2(0.8f, 1.2f), new Color(0.3f, 0.2f, 0.12f), 0, ColliderKind.None);
        var jar = Block(sprite, "ApothecaryJar", new Vector2(2f, -1.45f), new Vector2(0.45f, 0.7f), new Color(0.45f, 0.6f, 0.5f), 1, ColliderKind.Trigger);
        Use(jar, SafehouseManager.Panel.Medicine);

        var bed = Block(sprite, "Bed", new Vector2(6f, -2.55f), new Vector2(3f, 0.9f), new Color(0.5f, 0.35f, 0.3f), 0, ColliderKind.Trigger);
        Use(bed, SafehouseManager.Panel.Bed);
        Block(sprite, "Pillow (decor)", new Vector2(7.1f, -2.0f), new Vector2(0.7f, 0.3f), new Color(0.9f, 0.88f, 0.82f), 1, ColliderKind.None);

        // Manager + player
        new GameObject("SafehouseManager").AddComponent<SafehouseManager>();
        var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
        player.transform.position = new Vector3(-7f, -2f, 0f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildList(castlePath, ScenePath);

        if (EditorUtility.DisplayDialog("Nightguard",
                "Safehouse scene created and both scenes added to the build list.\n\nOpen your castle scene again now?",
                "Open castle scene", "Stay in the safehouse"))
            EditorSceneManager.OpenScene(castlePath);
    }

    enum ColliderKind { None, Solid, Trigger }

    static GameObject Block(Sprite sprite, string name, Vector2 pos, Vector2 size, Color color, int order, ColliderKind collider)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        if (collider != ColliderKind.None)
        {
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = collider == ColliderKind.Trigger;
        }
        return go;
    }

    static void Use(GameObject go, SafehouseManager.Panel panel)
    {
        var use = go.AddComponent<SafehouseObject>();
        use.opens = panel;
        use.interactionRange = 1.5f;
    }

    static GameObject FindPlayerPrefab()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab && prefab.GetComponentInChildren<PlayerMovement>(true)) return prefab;
        }
        return null;
    }

    static Sprite WhiteSprite()
    {
        if (!File.Exists(SpritePath))
        {
            Directory.CreateDirectory(SpriteFolder);
            var tex = new Texture2D(4, 4);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            File.WriteAllBytes(SpritePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(SpritePath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 4f; // 4 pixels = 1 unit, so scale = size in units
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
    }

    static void AddToBuildList(params string[] paths)
    {
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var path in paths)
        {
            bool found = false;
            foreach (var s in list)
                if (s.path == path) { s.enabled = true; found = true; }
            if (!found) list.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = list.ToArray();
    }

    static void Dialog(string message) => EditorUtility.DisplayDialog("Nightguard", message, "OK");
}
#endif
