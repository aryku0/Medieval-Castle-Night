#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Nightguard > Set Up Prisoners
/// Run once in the castle scene after Build Prison and Layout (undoable with Ctrl+Z):
///  - puts a PrisonCell on Cell 1-4, with a placeholder prisoner (guard-sized) and a cell door
///  - adds a placeholder bread shelf (RationShelf) at the right-hand end of the cellar
/// </summary>
public static class PrisonerSetup
{
    [MenuItem("Tools/Nightguard/Set Up Prisoners")]
    static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Dialog("Exit Play mode first."); return; }
        var cellar = GameObject.Find("Cellar");
        if (!cellar) { Dialog("There's no 'Cellar' in this scene. Run Tools > Nightguard > Build Prison and Layout first."); return; }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Set up prisoners");

        // 1) The four cells.
        int cellsDone = 0;
        Transform lastCell = null;
        for (int i = 1; i <= 4; i++)
        {
            var t = cellar.transform.Find($"Cell {i}");
            if (!t) continue;
            var cell = t.GetComponent<PrisonCell>();
            if (!cell) cell = Undo.AddComponent<PrisonCell>(t.gameObject);
            Undo.RecordObject(cell, "Set up cell");
            cell.cellNumber = i;
            cell.interactionRange = 1.4f;
            foreach (var made in cell.BuildPlaceholders())
                Undo.RegisterCreatedObjectUndo(made, "Create cell placeholder");
            EditorUtility.SetDirty(cell);
            cellsDone++;
            lastCell = t;
        }
        if (cellsDone == 0)
        {
            Undo.RevertAllDownToGroup(group);
            Dialog("Couldn't find 'Cell 1' to 'Cell 4' inside the Cellar.");
            return;
        }

        // 2) The bread shelf, between the last cell and the right-hand wall.
        string shelfNote;
        if (Object.FindAnyObjectByType<RationShelf>()) shelfNote = "Bread shelf already there, left as it is.";
        else
        {
            BuildShelf(cellar, lastCell);
            shelfNote = "Bread shelf added at the right-hand end of the cellar.";
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(cellar.scene);
        Dialog($"Done!\n\n• {cellsDone} cells set up, each with a placeholder prisoner and a cell door\n• {shelfNote}\n\nPress Ctrl+S to save the scene. Ctrl+Z undoes all of it.");
    }

    static void BuildShelf(GameObject cellar, Transform lastCell)
    {
        var floorObj = cellar.transform.Find("CellarFloor");
        var wallObj = cellar.transform.Find("CellarWallRight");
        var cellSr = lastCell.GetComponent<SpriteRenderer>();
        Sprite sprite = cellSr ? cellSr.sprite : null;
        Material material = cellSr ? cellSr.sharedMaterial : null;

        float floorTop = floorObj ? floorObj.position.y + floorObj.lossyScale.y * 0.5f
                                  : lastCell.position.y - lastCell.lossyScale.y * 0.5f;
        float wallLeft = wallObj ? wallObj.position.x - wallObj.lossyScale.x * 0.5f : lastCell.position.x + 2f;
        float cellRight = lastCell.position.x + lastCell.lossyScale.x * 0.5f;

        // 70 x 150 cm shelf in the gap; if there's no gap, just past the last cell.
        const float w = 0.7f, h = 1.5f;
        float x = wallLeft - cellRight >= w + 0.1f ? (cellRight + wallLeft) * 0.5f : cellRight + w * 0.5f + 0.1f;
        float z = lastCell.position.z;

        var shelf = Block("BreadShelf", sprite, material, cellar.transform, new Vector3(x, floorTop + h * 0.5f, z), new Vector2(w, h), new Color(0.42f, 0.28f, 0.14f), -2);
        Undo.RegisterCreatedObjectUndo(shelf, "Create bread shelf");
        var rations = Undo.AddComponent<RationShelf>(shelf);
        rations.interactionRange = 0.9f;

        // Placeholder planks and loaves (no colliders, purely so you can see it).
        var plank = new Color(0.25f, 0.16f, 0.08f);
        var loaf = new Color(0.82f, 0.62f, 0.32f);
        for (int level = 0; level < 2; level++)
        {
            float py = floorTop + 0.55f + level * 0.55f;
            var p = Block("Plank", sprite, material, cellar.transform, new Vector3(x, py, z), new Vector2(w, 0.05f), plank, -1);
            Undo.RegisterCreatedObjectUndo(p, "Create plank");
            Undo.SetTransformParent(p.transform, shelf.transform, "Parent plank");
            for (int b = 0; b < 2; b++)
            {
                var l = Block("Loaf", sprite, material, cellar.transform, new Vector3(x - 0.16f + b * 0.32f, py + 0.1f, z), new Vector2(0.24f, 0.14f), loaf, -1);
                Undo.RegisterCreatedObjectUndo(l, "Create loaf");
                Undo.SetTransformParent(l.transform, shelf.transform, "Parent loaf");
            }
        }
    }

    static GameObject Block(string name, Sprite sprite, Material material, Transform parent, Vector3 pos, Vector2 size, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        if (parent) go.transform.SetParent(parent, true);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        if (material) sr.sharedMaterial = material;
        sr.color = color;
        sr.sortingOrder = order;
        return go;
    }

    static void Dialog(string message) => EditorUtility.DisplayDialog("Nightguard", message, "OK");
}
#endif
