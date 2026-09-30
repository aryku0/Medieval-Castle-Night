#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Nightguard > Build Prison and Layout
/// Edits the open castle scene (undoable with Ctrl+Z):
///  - moves Window_Middle into the left room (renamed Window_Left)
///  - adds a prison door in the middle room (x = 1)
///  - builds the cellar below: floor, ceiling, walls, stairs door back up, 4 barred cells,
///    and a copy of your Candle
///  - links the two doors and sets the camera limits for each area
/// Everything uses the same square sprite and scale as your existing walls.
/// </summary>
public static class PrisonLayoutBuilder
{
    // Measurements taken from your castle scene.
    const float PrisonDoorX = 1f;
    const float LeftRoomWindowX = -13.5f;
    const float CellarDrop = 16f;          // how far below the castle floor the cellar floor sits
    const float CellarHalfWidth = 9f;      // inner half-width of the cellar
    const float DoorWidth = 1.4f, DoorHeight = 2.4f;

    [MenuItem("Tools/Nightguard/Build Prison and Layout")]
    static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Dialog("Exit Play mode first."); return; }
        if (GameObject.Find("Cellar")) { Dialog("The cellar is already built in this scene. Use Ctrl+Z to undo it, or delete the 'Cellar' and 'PrisonDoor' objects to rebuild."); return; }

        var ground = GameObject.Find("Ground");
        var groundSr = ground ? ground.GetComponent<SpriteRenderer>() : null;
        var candle = FindCandleTemplate();
        var cameraFollow = Camera.main ? Camera.main.GetComponent<CameraXYFollow>() : null;
        var middleWindow = GameObject.Find("Window_Middle");
        if (!groundSr) { Dialog("Couldn't find the 'Ground' object with a sprite. Is the castle scene open?"); return; }
        if (!candle) { Dialog("Couldn't find a candle to copy (an object with CandleInteraction)."); return; }
        if (!cameraFollow) { Dialog("The Main Camera needs the CameraXYFollow component."); return; }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build prison & layout");

        Sprite sprite = groundSr.sprite;
        Material material = groundSr.sharedMaterial;

        // Castle floor/ceiling heights, measured from the Ground object and your ceiling (y 2.05, thickness 1.35).
        float floorTop = ground.transform.position.y + ground.transform.lossyScale.y * 0.5f;   // about -3.70
        float ceilingBottom = 2.05f - 1.3527f * 0.5f;                                          // about 1.37
        float roomHeight = ceilingBottom - floorTop;                                           // about 5.07

        // 1) Move the middle window into the left room.
        if (middleWindow)
        {
            Undo.RecordObject(middleWindow.transform, "Move window");
            var p = middleWindow.transform.position;
            middleWindow.transform.position = new Vector3(LeftRoomWindowX, p.y, p.z);
            Undo.RecordObject(middleWindow, "Rename window");
            middleWindow.name = "Window_Left";
        }

        // 2) Prison door upstairs, in the middle room.
        var prisonDoor = MakeDoor(sprite, material, "PrisonDoor", new Vector2(PrisonDoorX, floorTop + DoorHeight * 0.5f), "[E] Go down to the cellar");

        // 3) The cellar.
        var cellar = new GameObject("Cellar");
        Undo.RegisterCreatedObjectUndo(cellar, "Create cellar");
        float cFloor = floorTop - CellarDrop;
        float cCeil = cFloor + roomHeight;
        float wallT = 1.3527f;
        float width = CellarHalfWidth * 2f + wallT * 2f;

        Block(sprite, material, cellar, "CellarFloor", new Vector2(0f, cFloor - 0.52f), new Vector2(width, 1.04f), Color.white, 1, solid: true);
        Block(sprite, material, cellar, "CellarCeiling", new Vector2(0f, cCeil + wallT * 0.5f), new Vector2(width, wallT), Color.white, 1, solid: true);
        Block(sprite, material, cellar, "CellarWallLeft", new Vector2(-CellarHalfWidth - wallT * 0.5f, (cFloor + cCeil) * 0.5f), new Vector2(wallT, roomHeight + 2f), Color.white, 1, solid: true);
        Block(sprite, material, cellar, "CellarWallRight", new Vector2(CellarHalfWidth + wallT * 0.5f, (cFloor + cCeil) * 0.5f), new Vector2(wallT, roomHeight + 2f), Color.white, 1, solid: true);

        var stairsDoor = MakeDoor(sprite, material, "StairsDoor", new Vector2(-CellarHalfWidth + 1.5f, cFloor + DoorHeight * 0.5f), "[E] Go upstairs");
        Undo.SetTransformParent(stairsDoor.transform, cellar.transform, "Parent stairs door");

        // Four barred cells along the back wall (prisoners come later).
        float[] cellX = { -3f, 0.3f, 3.6f, 6.9f };
        for (int i = 0; i < cellX.Length; i++)
            MakeCell(sprite, material, cellar, $"Cell {i + 1}", new Vector2(cellX[i], cFloor + 1.6f));

        // A copy of your candle, at the same height above the floor as upstairs.
        float candleAboveFloor = candle.transform.position.y - floorTop;
        var cellarCandle = (GameObject)Object.Instantiate(candle.gameObject);
        Undo.RegisterCreatedObjectUndo(cellarCandle, "Create cellar candle");
        cellarCandle.name = "Candle (Cellar)";
        cellarCandle.transform.SetParent(cellar.transform, true);
        cellarCandle.transform.position = new Vector3(-5.3f, cFloor + candleAboveFloor, candle.transform.position.z);

        // 4) Link the doors and set each area's camera limits.
        var down = prisonDoor.GetComponent<TeleportDoor>();
        var up = stairsDoor.GetComponent<TeleportDoor>();
        float camY = (cFloor + cCeil) * 0.5f;
        down.destination = up;
        down.cameraMin = new Vector2(-1f, camY);
        down.cameraMax = new Vector2(1f, camY);
        up.destination = down;
        up.cameraMin = new Vector2(cameraFollow.minX, cameraFollow.minY);
        up.cameraMax = new Vector2(cameraFollow.maxX, cameraFollow.maxY);
        EditorUtility.SetDirty(down);
        EditorUtility.SetDirty(up);

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(prisonDoor.scene);
        Selection.activeGameObject = cellar;
        Dialog("Done!\n\n• Middle window moved to the left room (now 'Window_Left')\n• Prison door added in the middle room\n• Cellar built below with 4 cells, a stairs door and its own candle\n\nPress Ctrl+S to save the scene. Ctrl+Z undoes all of it.");
    }

    static CandleInteraction FindCandleTemplate()
    {
        var named = GameObject.Find("Candle");
        if (named && named.GetComponent<CandleInteraction>()) return named.GetComponent<CandleInteraction>();
        return Object.FindAnyObjectByType<CandleInteraction>();
    }

    static GameObject MakeDoor(Sprite sprite, Material material, string name, Vector2 pos, string prompt)
    {
        var go = Block(sprite, material, null, name, pos, new Vector2(DoorWidth, DoorHeight), new Color(0.25f, 0.16f, 0.09f), -2, solid: false);
        go.GetComponent<BoxCollider2D>().isTrigger = true; // walk past it; E to use
        var door = go.AddComponent<TeleportDoor>();
        door.prompt = prompt;
        door.interactionRange = 1.2f;
        return go;
    }

    static void MakeCell(Sprite sprite, Material material, GameObject parent, string name, Vector2 pos)
    {
        const float w = 2.6f, h = 3.2f;
        var cell = Block(sprite, material, parent, name, pos, new Vector2(w, h), new Color(0.18f, 0.18f, 0.2f), -3, solid: false, collider: false);
        // Bars: thin black uprights across the front, like your gate.
        for (int b = 0; b < 6; b++)
        {
            float x = pos.x - w * 0.5f + 0.2f + b * (w - 0.4f) / 5f;
            Block(sprite, material, cell, "Bar", new Vector2(x, pos.y), new Vector2(0.1f, h), Color.black, -1, solid: false, collider: false);
        }
    }

    static GameObject Block(Sprite sprite, Material material, GameObject parent, string name, Vector2 pos, Vector2 size,
                            Color color, int order, bool solid, bool collider = true)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        if (parent) go.transform.SetParent(parent.transform, true);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        if (material) sr.sharedMaterial = material;
        sr.color = color;
        sr.sortingOrder = order;
        if (collider)
        {
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = !solid;
        }
        return go;
    }

    static void Dialog(string message) => EditorUtility.DisplayDialog("Nightguard", message, "OK");
}
#endif
