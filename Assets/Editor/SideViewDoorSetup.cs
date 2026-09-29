#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Explicit, undoable setup for a selected test door. Never runs automatically.</summary>
public static class SideViewDoorSetup
{
    public const string MaterialPath = "Assets/Doors/SideViewDoor.mat";
    public const string PrefabPath = "Assets/Doors/SideViewDoor.prefab";

    [MenuItem("Tools/Nightguard/Configure Selected Side-on Door")]
    private static void ConfigureSelected()
    {
        if (!Selection.activeGameObject || Selection.activeGameObject.name != "DoorRoot")
            throw new InvalidOperationException("Select the existing DoorRoot first.");
        Configure(Selection.activeGameObject);
    }

    public static SideViewDoor Configure(GameObject root)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode before setting up a door.");
        if (root.GetComponent<SideViewDoor>())
            throw new InvalidOperationException("This DoorRoot is already configured; tune its Inspector instead.");
        Transform visual = root.transform.Find("DoorVisual");
        if (!visual || !visual.GetComponent<MeshFilter>() || !visual.GetComponent<MeshRenderer>())
            throw new InvalidOperationException("DoorRoot must have a cube DoorVisual child.");
        if (root.transform.rotation != Quaternion.identity || root.transform.lossyScale != Vector3.one)
            throw new InvalidOperationException("DoorRoot needs identity rotation and unit scale for initial setup.");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Configure side-on push door");
        Undo.RegisterFullObjectHierarchyUndo(root, "Configure side-on push door");
        Vector3 centre = visual.position;
        Vector2 size = new Vector2(Mathf.Abs(visual.lossyScale.x), Mathf.Abs(visual.lossyScale.y));
        root.transform.position = centre - Vector3.right * size.x * 0.5f;

        // Remove only obsolete door components, never anything outside this hierarchy.
        foreach (var old in root.GetComponentsInChildren<SideViewDoor>(true)) Undo.DestroyObjectImmediate(old);
        foreach (var old in root.GetComponentsInChildren<Collider>(true)) Undo.DestroyObjectImmediate(old);
        foreach (var old in root.GetComponentsInChildren<Rigidbody>(true)) Undo.DestroyObjectImmediate(old);
        foreach (var old in root.GetComponentsInChildren<Joint>(true)) Undo.DestroyObjectImmediate(old);
        foreach (var old in root.GetComponentsInChildren<Collider2D>(true)) Undo.DestroyObjectImmediate(old);
        foreach (var old in root.GetComponentsInChildren<Joint2D>(true)) Undo.DestroyObjectImmediate(old);

        var blocker = MakeBox(root, "DoorBlocker");
        var sensor = MakeBox(root, "DoorSensor");
        root.layer = visual.gameObject.layer = blocker.gameObject.layer = sensor.gameObject.layer = 0;
        var door = Undo.AddComponent<SideViewDoor>(root);
        var so = new SerializedObject(door);
        so.FindProperty("doorVisual").objectReferenceValue = visual;
        so.FindProperty("doorBlocker").objectReferenceValue = blocker;
        so.FindProperty("doorSensor").objectReferenceValue = sensor;
        so.FindProperty("doorwaySize").vector2Value = size;
        so.FindProperty("sensorSize").vector2Value = size + new Vector2(1f, 0.1f);
        so.ApplyModifiedProperties();

        if (!AssetDatabase.IsValidFolder("Assets/Doors")) AssetDatabase.CreateFolder("Assets", "Doors");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!material)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader) throw new InvalidOperationException("URP Unlit shader is unavailable.");
            material = new Material(shader) { name = "SideViewDoor" };
            material.SetColor("_BaseColor", new Color(0.34f, 0.20f, 0.09f, 1f));
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        var renderer = visual.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 1;
        EditorUtility.SetDirty(door);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Undo.CollapseUndoOperations(group);
        var copy = UnityEngine.Object.Instantiate(root);
        copy.name = "DoorRoot";
        copy.transform.position = Vector3.zero;
        try { PrefabUtility.SaveAsPrefabAsset(copy, PrefabPath); }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
        AssetDatabase.SaveAssets();
        return door;
    }

    private static BoxCollider2D MakeBox(GameObject root, string name)
    {
        var child = root.transform.Find(name);
        if (!child)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(root.transform, false);
            child = go.transform;
        }
        return Undo.AddComponent<BoxCollider2D>(child.gameObject);
    }
}
#endif
