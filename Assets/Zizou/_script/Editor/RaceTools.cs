using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Car2D > Race
///
/// Creates checkpoints you can drag around the track and adds the RaceManager +
/// LapTracker components to the scene.
/// </summary>
public static class RaceTools
{
    [MenuItem("Tools/Car2D/Race/Create Checkpoint", false, 20)]
    static void CreateCheckpoint()
    {
        Create(false);
    }

    [MenuItem("Tools/Car2D/Race/Create START / FINISH Checkpoint", false, 21)]
    static void CreateStartCheckpoint()
    {
        Create(true);
    }

    [MenuItem("Tools/Car2D/Race/Add Race Manager + LapTrackers", false, 22)]
    static void AddRaceManager()
    {
        RaceManager manager = EnsureRaceManager();

        int cars = 0;
        foreach (TopDown2DCarController car in Object.FindObjectsByType<TopDown2DCarController>(FindObjectsSortMode.None))
        {
            LapTracker tracker = car.GetComponent<LapTracker>();
            if (tracker == null)
                tracker = Undo.AddComponent<LapTracker>(car.gameObject);

            // Cars driven by CarInput are the player's.
            tracker.isPlayer = car.GetComponent<CarInput>() != null;
            tracker.enabled = true;
            cars++;
        }

        EnsureMarker();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Race] Race Manager ready, " + cars + " car(s) got a LapTracker and the " +
                  "next-checkpoint marker is in the scene. Save the scene (Ctrl+S) and press Play.");
        Selection.activeObject = manager;
    }

    [MenuItem("Tools/Car2D/Race/Create Next Checkpoint Marker", false, 24)]
    static void CreateMarker()
    {
        NextCheckpointMarker existing = Object.FindFirstObjectByType<NextCheckpointMarker>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("[Race] A Next Checkpoint Marker already exists - selected it.");
            return;
        }

        NextCheckpointMarker marker = EnsureMarker();
        Selection.activeGameObject = marker.gameObject;
        EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);
        Debug.Log("[Race] Next Checkpoint Marker created. It follows the player automatically while playing.");
    }

    internal static RaceManager EnsureRaceManager()
    {
        RaceManager manager = Object.FindFirstObjectByType<RaceManager>();
        if (manager != null)
            return manager;

        GameObject managerObject = new GameObject("Race Manager");
        Undo.RegisterCreatedObjectUndo(managerObject, "Create Race Manager");
        return managerObject.AddComponent<RaceManager>();
    }

    static NextCheckpointMarker EnsureMarker()
    {
        NextCheckpointMarker marker = Object.FindFirstObjectByType<NextCheckpointMarker>();
        if (marker != null)
            return marker;

        GameObject markerObject = new GameObject("Next Checkpoint Marker");
        Undo.RegisterCreatedObjectUndo(markerObject, "Create Next Checkpoint Marker");
        return markerObject.AddComponent<NextCheckpointMarker>();
    }

    [MenuItem("Tools/Car2D/Race/Log Checkpoint Order", false, 23)]
    static void LogCheckpointOrder()
    {
        List<Checkpoint> checkpoints = new List<Checkpoint>(
            Object.FindObjectsByType<Checkpoint>(FindObjectsSortMode.None));

        if (checkpoints.Count == 0)
        {
            Debug.Log("[Race] No checkpoints in the scene yet.");
            return;
        }

        Checkpoint start = checkpoints.Find(c => c.isStart);
        checkpoints.Remove(start);
        checkpoints.Sort((a, b) => a.index.CompareTo(b.index));

        string order = "START [" + (start != null ? start.name : "none!") + "]";
        foreach (Checkpoint checkpoint in checkpoints)
            order += " -> [" + checkpoint.name + "] (index " + checkpoint.index + ")";

        Debug.Log("[Race] Lap order: " + order);
    }

    static void Create(bool isStart)
    {
        Vector3 position = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            position = SceneView.lastActiveSceneView.pivot;
        position.z = 0f;

        GameObject checkpointObject = new GameObject(isStart ? "Start Finish Line" : "Checkpoint");
        Undo.RegisterCreatedObjectUndo(checkpointObject, "Create Checkpoint");
        checkpointObject.transform.position = position;
        checkpointObject.layer = 0; // never the Road layer

        // Keep them tidy under one parent object.
        GameObject root = GameObject.Find("Checkpoints");
        if (root == null)
        {
            root = new GameObject("Checkpoints");
            Undo.RegisterCreatedObjectUndo(root, "Create Checkpoints");
        }
        checkpointObject.transform.SetParent(root.transform, true);

        BoxCollider2D box = checkpointObject.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(6f, 2f);

        Checkpoint checkpoint = checkpointObject.AddComponent<Checkpoint>();
        checkpoint.isStart = isStart;
        checkpoint.index = isStart ? 0 : NextIndex();

        Selection.activeGameObject = checkpointObject;
        EditorSceneManager.MarkSceneDirty(checkpointObject.scene);

        Debug.Log("[Race] Created " + (isStart ? "START/FINISH" : "checkpoint " + checkpoint.index) +
                  " at " + position + ". Move/rotate it into place and resize the BoxCollider2D so it " +
                  "covers the whole road - otherwise cars can drive around it and miss the checkpoint.");
    }

    static int NextIndex()
    {
        int max = 0;
        foreach (Checkpoint checkpoint in Object.FindObjectsByType<Checkpoint>(FindObjectsSortMode.None))
        {
            if (!checkpoint.isStart)
                max = Mathf.Max(max, checkpoint.index);
        }
        return max + 1;
    }
}
