using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Car2D > Race
///
/// Builds a racing line from the checkpoints (in the same order the RaceManager uses),
/// smooths it where the road allows, and draws it with a LineRenderer. You then drag the
/// points in the Scene view to fix the corners until the line is where you want it.
/// </summary>
public static class RacePathTools
{
    const string MaterialFolder = "Assets/Zizou/Materials";
    const string MaterialPath = MaterialFolder + "/RacePath.mat";
    const int SmoothingSteps = 8;

    [MenuItem("Tools/Car2D/Race/Create Race Path From Checkpoints", false, 25)]
    static void CreateFromCheckpoints()
    {
        List<Checkpoint> checkpoints = new List<Checkpoint>(
            Object.FindObjectsByType<Checkpoint>(FindObjectsSortMode.None));

        if (checkpoints.Count < 2)
        {
            EditorUtility.DisplayDialog(
                "Not enough checkpoints",
                "Create a START/FINISH checkpoint and at least one more checkpoint first, " +
                "so the race path has a route to follow.",
                "OK");
            return;
        }

        RacePath existing = Object.FindFirstObjectByType<RacePath>();

        if (existing != null && !EditorUtility.DisplayDialog(
                "Replace the race path?",
                "A Race Path already exists. Rebuilding it from the checkpoints will discard " +
                "the points you edited. Do you want to rebuild it?",
                "Rebuild", "Cancel"))
        {
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        // Same order as the RaceManager: the start line first, then the others by index.
        Checkpoint start = null;
        List<Checkpoint> others = new List<Checkpoint>();

        foreach (Checkpoint checkpoint in checkpoints)
        {
            if (checkpoint.isStart)
            {
                if (start == null)
                    start = checkpoint;
            }
            else
            {
                others.Add(checkpoint);
            }
        }

        others.Sort((a, b) => a.index.CompareTo(b.index));

        List<Vector3> route = new List<Vector3>();

        if (start != null)
            route.Add(start.WorldCenter);

        foreach (Checkpoint checkpoint in others)
            route.Add(checkpoint.WorldCenter);

        if (route.Count < 2)
        {
            EditorUtility.DisplayDialog(
                "Not enough checkpoints",
                "The race path needs at least two checkpoints in the lap order.",
                "OK");
            return;
        }

        LayerMask roadMask = LayerMask.GetMask("Road");
        bool canValidate = roadMask.value != 0 && HasCollidersOn(roadMask);

        if (!canValidate)
        {
            Debug.LogWarning("[Race] Could not find road colliders on the Road layer, so the race path " +
                             "was built without checking it against the road. Run " +
                             "Tools > Car2D > Setup Off-Road Slowdown (whole scene) to create them.");
        }

        List<Vector3> line = BuildLoop(route, SmoothingSteps, roadMask, canValidate);

        RacePath path = existing;

        if (path == null)
        {
            GameObject pathObject = new GameObject("Race Path");
            Undo.RegisterCreatedObjectUndo(pathObject, "Create Race Path");

            LineRenderer renderer = pathObject.AddComponent<LineRenderer>();
            renderer.sharedMaterial = GetOrCreateMaterial();

            if (renderer.sharedMaterial == null)
            {
                Debug.LogWarning("[Race] Could not find a sprite/line shader for the Race Path, so the " +
                                 "line will not render until you assign a material to its LineRenderer.");
            }

            renderer.textureMode = LineTextureMode.Stretch;
            renderer.numCapVertices = 2;
            renderer.numCornerVertices = 2;
            renderer.alignment = LineAlignment.View;

            path = pathObject.AddComponent<RacePath>();
            path.line = renderer;
        }

        Undo.RecordObject(path, "Rebuild Race Path");
        path.SetPoints(line);
        EditorUtility.SetDirty(path);
        EditorSceneManager.MarkSceneDirty(path.gameObject.scene);

        Selection.activeGameObject = path.gameObject;

        int offRoad = CountOffRoadPoints(path, roadMask);

        Debug.Log("[Race] Race Path built from " + route.Count + " checkpoint(s): " + line.Count +
                  " points, " + (canValidate ? offRoad + " of them off the road" : "road check skipped") +
                  ". Select \"Race Path\" and drag the points in the Scene view to fix the corners.");
    }

    [MenuItem("Tools/Car2D/Race/Validate Race Path", false, 26)]
    static void ValidatePathMenu()
    {
        RacePath path = Object.FindFirstObjectByType<RacePath>();

        if (path == null)
        {
            Debug.Log("[Race] There is no Race Path in the scene. Create one with " +
                      "Tools > Car2D > Race > Create Race Path From Checkpoints.");
            return;
        }

        Selection.activeGameObject = path.gameObject;
        ValidatePath(path);
    }

    internal static void ValidatePath(RacePath path)
    {
        if (path == null)
            return;

        LayerMask roadMask = LayerMask.GetMask("Road");

        if (roadMask.value == 0 || !HasCollidersOn(roadMask))
        {
            Debug.LogWarning("[Race] Cannot validate the race path: there are no colliders on the Road " +
                             "layer. Run Tools > Car2D > Setup Off-Road Slowdown (whole scene).");
            return;
        }

        int offRoad = CountOffRoadPoints(path, roadMask);

        if (offRoad == 0)
        {
            Debug.Log("[Race] Race Path is valid: all " + path.PointCount + " points are on the road.");
            return;
        }

        Debug.LogWarning("[Race] Race Path has " + offRoad + " point(s) that are not on the Road layer. " +
                         "The AI will be slowed down where the line leaves the road, so drag those " +
                         "points back onto the track. Select the Race Path to see them in the Scene view.");
    }

    static int CountOffRoadPoints(RacePath path, LayerMask roadMask)
    {
        if (path == null || roadMask.value == 0)
            return 0;

        int offRoad = 0;
        IReadOnlyList<Vector3> points = path.Points;

        for (int i = 0; i < points.Count; i++)
        {
            if (Physics2D.OverlapPoint(points[i], roadMask) != null)
                continue;

            offRoad++;

            if (offRoad <= 5)
                Debug.LogWarning("[Race] Race Path point " + i + " at " + points[i] + " is off the road.", path);
        }

        return offRoad;
    }

    // Builds the closed loop of points, smoothing each checkbox-to-checkpoint section with
    // Catmull-Rom. A section that would leave the road is kept straight instead.
    static List<Vector3> BuildLoop(List<Vector3> route, int subdivisions, LayerMask roadMask, bool validate)
    {
        int count = route.Count;
        List<Vector3> result = new List<Vector3>(count * subdivisions);
        int straightSections = 0;

        for (int i = 0; i < count; i++)
        {
            Vector3 previous = route[(i - 1 + count) % count];
            Vector3 current = route[i];
            Vector3 next = route[(i + 1) % count];
            Vector3 afterNext = route[(i + 2) % count];

            List<Vector3> section = new List<Vector3>(subdivisions);
            bool staysOnRoad = true;

            for (int step = 0; step < subdivisions; step++)
            {
                float t = step / (float)subdivisions;
                Vector3 point = CatmullRom(previous, current, next, afterNext, t);
                point.z = 0f;
                section.Add(point);

                if (validate && Physics2D.OverlapPoint(point, roadMask) == null)
                    staysOnRoad = false;
            }

            if (!staysOnRoad)
            {
                // Smoothing would cut the corner off the road: keep this section straight.
                section.Clear();

                for (int step = 0; step < subdivisions; step++)
                {
                    float t = step / (float)subdivisions;
                    Vector3 point = Vector3.Lerp(current, next, t);
                    point.z = 0f;
                    section.Add(point);
                }

                straightSections++;
            }

            result.AddRange(section);
        }

        if (straightSections > 0)
        {
            Debug.LogWarning("[Race] " + straightSections + " section(s) of the race path could not be " +
                             "smoothed without leaving the road, so they were kept straight. Drag the " +
                             "path points in the Scene view to round those corners.");
        }

        return result;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * ((2f * p1) +
                       (-p0 + p2) * t +
                       (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                       (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    static bool HasCollidersOn(LayerMask layers)
    {
        foreach (Collider2D collider in Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (((1 << collider.gameObject.layer) & layers.value) != 0)
                return true;
        }

        return false;
    }

    static Material GetOrCreateMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (existing != null)
            return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");

        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader == null)
            return null;

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/Zizou", "Materials");

        Material material = new Material(shader) { name = "RacePath" };
        AssetDatabase.CreateAsset(material, MaterialPath);
        AssetDatabase.SaveAssets();

        return material;
    }
}
