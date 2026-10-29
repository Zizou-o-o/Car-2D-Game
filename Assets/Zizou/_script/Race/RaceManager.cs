using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The race manager: knows the checkpoint order, keeps every racer, and reports laps.
///
/// It reads all <see cref="Checkpoint"/>s in the scene on Awake (the one marked
/// "isStart" is the finish line) and every <see cref="LapTracker"/> registers itself
/// here, so the same setup works for the player and for AI cars.
///
/// How to use from a UI script:
///   RaceManager.Instance.Player.CurrentLap
///   RaceManager.Instance.GetStandings()
///   RaceManager.Instance.LapCompleted += (racer) => ...
/// </summary>
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public class RaceManager : MonoBehaviour
{
    public static RaceManager Instance { get; private set; }

    [Header("Race")]
    [Tooltip("Laps needed to finish the race. 0 = endless race.")]
    [Min(0)]
    public int totalLaps = 3;

    [Header("Debug")]
    [Tooltip("Shows the lap count of every racer in the top-left corner while testing.")]
    public bool showDebugOverlay = true;

    readonly List<Checkpoint> checkpoints = new List<Checkpoint>();
    readonly List<LapTracker> racers = new List<LapTracker>();

    /// <summary>Checkpoints in lap order. Index 0 is the start/finish line.</summary>
    public IReadOnlyList<Checkpoint> Checkpoints => checkpoints;

    /// <summary>Every car currently racing.</summary>
    public IReadOnlyList<LapTracker> Racers => racers;

    public int CheckpointCount => checkpoints.Count;

    /// <summary>Fired once for every completed lap, with the car that completed it.</summary>
    public event Action<LapTracker> LapCompleted;

    /// <summary>Fired every time a car passes a checkpoint, with the car and the checkpoint.</summary>
    public event Action<LapTracker, Checkpoint> CheckpointPassed;

    /// <summary>Fired when a car reaches RaceManager.totalLaps.</summary>
    public event Action<LapTracker> RaceFinished;

    /// <summary>The racer marked as the player, or null.</summary>
    public LapTracker Player
    {
        get
        {
            for (int i = 0; i < racers.Count; i++)
            {
                if (racers[i].isPlayer)
                    return racers[i];
            }
            return null;
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Race] There is already a RaceManager in the scene. " +
                             "Deleting the extra one on \"" + name + "\".", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        CollectCheckpoints();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Re-reads the checkpoints from the scene. Awake calls this for you.</summary>
    public void CollectCheckpoints()
    {
        checkpoints.Clear();

        Checkpoint[] found = FindObjectsByType<Checkpoint>(FindObjectsSortMode.None);

        Checkpoint start = null;
        List<Checkpoint> others = new List<Checkpoint>();

        foreach (Checkpoint checkpoint in found)
        {
            if (checkpoint.isStart)
            {
                if (start == null)
                    start = checkpoint;
                else
                    Debug.LogWarning("[Race] More than one checkpoint is marked as Start. " +
                                     "Using \"" + start.name + "\".", checkpoint);
            }
            else
            {
                others.Add(checkpoint);
            }
        }

        // The start line always comes first, then the rest in the order the user set.
        others.Sort((a, b) => a.index.CompareTo(b.index));

        for (int i = 1; i < others.Count; i++)
        {
            if (others[i].index == others[i - 1].index)
            {
                Debug.LogWarning("[Race] \"" + others[i].name + "\" and \"" + others[i - 1].name +
                                 "\" have the same index (" + others[i].index + "). " +
                                 "The order between them is undefined.", others[i]);
            }
        }

        if (start != null)
            checkpoints.Add(start);
        else
            Debug.LogWarning("[Race] No checkpoint is marked as Start, so laps cannot be counted. " +
                             "Tick \"Is Start\" on the start/finish checkpoint.");

        checkpoints.AddRange(others);

        if (checkpoints.Count == 0)
            Debug.LogWarning("[Race] No checkpoints were found in the scene.");
    }

    public void Register(LapTracker racer)
    {
        if (racer != null && !racers.Contains(racer))
            racers.Add(racer);
    }

    public void Unregister(LapTracker racer)
    {
        racers.Remove(racer);
    }

    /// <summary>Called by a LapTracker when it passes a checkpoint.</summary>
    public void NotifyCheckpointPassed(LapTracker racer, Checkpoint checkpoint)
    {
        CheckpointPassed?.Invoke(racer, checkpoint);
    }

    /// <summary>Called by a LapTracker when it completes a lap.</summary>
    public void NotifyLapCompleted(LapTracker racer)
    {
        LapCompleted?.Invoke(racer);

        if (totalLaps > 0 && racer.CompletedLaps == totalLaps)
            RaceFinished?.Invoke(racer);
    }

    /// <summary>Cars sorted by how far they are in the race (first place first).</summary>
    public List<LapTracker> GetStandings()
    {
        List<LapTracker> standings = new List<LapTracker>(racers);

        standings.Sort((a, b) =>
        {
            int laps = b.CompletedLaps.CompareTo(a.CompletedLaps);
            if (laps != 0)
                return laps;

            return b.NextCheckpointIndex.CompareTo(a.NextCheckpointIndex);
        });

        return standings;
    }

    void OnGUI()
    {
        if (!showDebugOverlay || racers.Count == 0)
            return;

        const float width = 220f;
        const float lineHeight = 20f;
        Rect box = new Rect(10f, 10f, width, 8f + lineHeight * (racers.Count + 1));

        GUI.Box(box, GUIContent.none);
        GUI.Label(new Rect(box.x + 8f, box.y + 4f, width, lineHeight), "LAPS");

        for (int i = 0; i < racers.Count; i++)
        {
            LapTracker racer = racers[i];
            string text = (racer.isPlayer ? "> " : "   ") + racer.DisplayName + "   lap " + racer.CurrentLap;

            if (racer.NextCheckpointIndex >= 0)
                text += "   next cp " + racer.NextCheckpointIndex;

            if (racer.IsFinished)
                text += "  (finished)";

            GUI.Label(new Rect(box.x + 8f, box.y + 4f + lineHeight * (i + 1), width, lineHeight), text);
        }
    }
}
