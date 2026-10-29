using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view editing for the racing line: drag a point to move it, click the small dot
/// between two points to insert one, press Delete near a point to remove it.
/// </summary>
[CustomEditor(typeof(RacePath))]
public class RacePathEditor : Editor
{
    const float PointHandleSize = 0.28f;
    const float InsertHandleSize = 0.09f;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Scene view: drag a point to move it. Click the small dot between two points to " +
            "insert a point there. Put the mouse near a point and press Delete to remove it.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Validate On Road"))
                RacePathTools.ValidatePath((RacePath)target);

            if (GUILayout.Button("Clear Points"))
            {
                RacePath path = (RacePath)target;
                Undo.RecordObject(path, "Clear Race Path");
                path.SetPoints(null);
                EditorUtility.SetDirty(path);
            }
        }
    }

    void OnSceneGUI()
    {
        RacePath path = (RacePath)target;

        if (path == null || path.PointCount == 0)
            return;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        DrawPointHandles(path);
        DrawInsertHandles(path);
        HandleDelete(path);
    }

    static void DrawPointHandles(RacePath path)
    {
        Handles.color = path.lineColor;

        for (int i = 0; i < path.PointCount; i++)
        {
            Vector3 point = path.Points[i];
            Vector3 moved = Handles.FreeMoveHandle(point, PointHandleSize, Vector3.zero, Handles.SphereHandleCap);

            if ((moved - point).sqrMagnitude < 0.0000001f)
                continue;

            Undo.RecordObject(path, "Move Race Path Point");
            path.SetPoint(i, moved);
            EditorUtility.SetDirty(path);
        }
    }

    static void DrawInsertHandles(RacePath path)
    {
        int segments = path.Closed ? path.PointCount : path.PointCount - 1;

        Handles.color = new Color(1f, 1f, 1f, 0.55f);

        for (int i = 0; i < segments; i++)
        {
            Vector3 a = path.Points[i];
            Vector3 b = path.Points[(i + 1) % path.PointCount];
            Vector3 middle = (a + b) * 0.5f;

            if (!Handles.Button(middle, Quaternion.identity, InsertHandleSize, InsertHandleSize * 2f,
                    Handles.DotHandleCap))
                continue;

            Undo.RecordObject(path, "Insert Race Path Point");
            path.InsertPoint(i + 1, middle);
            EditorUtility.SetDirty(path);

            // The list changed, so the loop indices are stale.
            return;
        }
    }

    static void HandleDelete(RacePath path)
    {
        Event current = Event.current;

        if (current.type != EventType.KeyDown ||
            (current.keyCode != KeyCode.Delete && current.keyCode != KeyCode.Backspace))
            return;

        int nearest = NearestPointToMouse(path, out float distance);

        if (nearest < 0 || distance > 1.5f)
            return;

        Undo.RecordObject(path, "Remove Race Path Point");
        path.RemovePointAt(nearest);
        EditorUtility.SetDirty(path);
        current.Use();
    }

    static int NearestPointToMouse(RacePath path, out float nearestDistance)
    {
        nearestDistance = float.MaxValue;
        int nearest = -1;

        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

        if (Mathf.Abs(ray.direction.z) < 0.0001f)
            return -1;

        float distanceToPlane = -ray.origin.z / ray.direction.z;

        if (distanceToPlane < 0f)
            return -1;

        Vector3 mouse = ray.GetPoint(distanceToPlane);

        for (int i = 0; i < path.PointCount; i++)
        {
            float distance = Vector3.Distance(mouse, path.Points[i]);

            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearest = i;
        }

        return nearest;
    }
}
