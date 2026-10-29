using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Car2D
///
/// Creates the "road area" trigger colliders that OffRoadSlowdown tests against,
/// and puts OffRoadSlowdown on every car.
///
/// The colliders are added as children named "RoadArea" under each road sprite,
/// on the "Road" layer, with IsTrigger enabled, matching the sprite's own outline.
/// That way the drivable area always follows the road art - including corners.
/// </summary>
public static class RoadAreaTools
{
    const string RoadAreaName = "RoadArea";
    const string RoadLayerName = "Road";
    const string RoadTextureName = "Roads Final Strips";

    [MenuItem("Tools/Car2D/Setup Off-Road Slowdown (whole scene)")]
    static void SetupScene()
    {
        List<SpriteRenderer> roads = new List<SpriteRenderer>();
        foreach (SpriteRenderer spriteRenderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (IsRoadSprite(spriteRenderer))
                roads.Add(spriteRenderer);
        }

        if (roads.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "No roads found",
                "No sprites using the texture \"" + RoadTextureName + "\" were found in the open scene.\n\n" +
                "If you use another road tileset, select the road objects and use " +
                "\"Tools > Car2D > Generate Road Areas For Selection\" instead.",
                "OK");
            return;
        }

        int generated = GenerateRoadAreas(roads);
        AddToAllCars(out int carsConfigured);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[Off-Road] Setup finished: " + generated + " road colliders created, " +
                  carsConfigured + " car(s) configured. Save the scene (Ctrl+S), then press Play.");
    }

    [MenuItem("Tools/Car2D/Generate Road Areas For Selection")]
    static void GenerateSelected()
    {
        List<SpriteRenderer> roads = new List<SpriteRenderer>();
        foreach (GameObject selected in Selection.gameObjects)
            roads.AddRange(selected.GetComponentsInChildren<SpriteRenderer>());

        if (roads.Count == 0)
        {
            EditorUtility.DisplayDialog("Nothing selected", "Select the road objects (or their parent) first.", "OK");
            return;
        }

        int generated = GenerateRoadAreas(roads);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Off-Road] " + generated + " road collider(s) created for the selection. Save the scene (Ctrl+S).");
    }

    [MenuItem("Tools/Car2D/Remove Road Areas")]
    static void RemoveRoadAreas()
    {
        int removed = 0;
        foreach (SpriteRenderer spriteRenderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            Transform area = spriteRenderer.transform.Find(RoadAreaName);
            if (area == null)
                continue;

            Undo.DestroyObjectImmediate(area.gameObject);
            removed++;
        }

        if (removed > 0)
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[Off-Road] Removed " + removed + " road area(s).");
    }

    [MenuItem("Tools/Car2D/Add OffRoadSlowdown To All Cars")]
    static void AddToAllCarsMenu()
    {
        AddToAllCars(out int carsConfigured);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Off-Road] " + carsConfigured + " car(s) configured with OffRoadSlowdown.");
    }

    static bool IsRoadSprite(SpriteRenderer spriteRenderer)
    {
        return spriteRenderer.sprite != null
            && spriteRenderer.sprite.texture != null
            && spriteRenderer.sprite.texture.name == RoadTextureName
            && spriteRenderer.transform.Find(RoadAreaName) == null;
    }

    static int GenerateRoadAreas(List<SpriteRenderer> roads)
    {
        int roadLayer = LayerMask.NameToLayer(RoadLayerName);
        if (roadLayer < 0)
        {
            Debug.LogError("[Off-Road] There is no \"" + RoadLayerName + "\" layer. Add it in Project Settings > Tags and Layers.");
            return 0;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Generate Road Areas");
        int undoGroup = Undo.GetCurrentGroup();

        int created = 0;
        foreach (SpriteRenderer spriteRenderer in roads)
        {
            Sprite sprite = spriteRenderer.sprite;
            if (sprite == null || spriteRenderer.transform.Find(RoadAreaName) != null)
                continue;

            GameObject area = new GameObject(RoadAreaName);
            Undo.RegisterCreatedObjectUndo(area, "Create Road Area");
            area.transform.SetParent(spriteRenderer.transform, false);
            area.layer = roadLayer;

            if (!AddCollider(area, sprite))
            {
                Object.DestroyImmediate(area);
                continue;
            }

            created++;
        }

        Undo.CollapseUndoOperations(undoGroup);
        return created;
    }

    // Prefers the sprite's own outline (so curbs/corners are respected) and falls
    // back to the sprite bounds if the texture has no generated physics shape.
    static bool AddCollider(GameObject area, Sprite sprite)
    {
        if (sprite.GetPhysicsShapeCount() > 0)
        {
            PolygonCollider2D polygon = area.AddComponent<PolygonCollider2D>();
            polygon.pathCount = sprite.GetPhysicsShapeCount();

            List<Vector2> points = new List<Vector2>();
            for (int i = 0; i < polygon.pathCount; i++)
            {
                points.Clear();
                sprite.GetPhysicsShape(i, points);

                if (points.Count < 3)
                    return false;

                polygon.SetPath(i, points);
            }

            polygon.isTrigger = true;
            return true;
        }

        BoxCollider2D box = area.AddComponent<BoxCollider2D>();
        box.offset = sprite.bounds.center;
        box.size = sprite.bounds.size;
        box.isTrigger = true;
        return true;
    }

    static void AddToAllCars(out int carsConfigured)
    {
        carsConfigured = 0;

        foreach (TopDown2DCarController car in Object.FindObjectsByType<TopDown2DCarController>(FindObjectsSortMode.None))
        {
            car.surfaceSpeedMultiplier = 1f;

            OffRoadSlowdown slowDown = car.GetComponent<OffRoadSlowdown>();
            if (slowDown == null)
                slowDown = Undo.AddComponent<OffRoadSlowdown>(car.gameObject);

            if (slowDown.roadLayers.value == 0)
                slowDown.roadLayers = LayerMask.GetMask(RoadLayerName);

            carsConfigured++;
        }
    }
}
