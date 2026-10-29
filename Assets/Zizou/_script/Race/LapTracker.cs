using UnityEngine;

/// <summary>
/// Counts laps for one car (player or AI).
///
/// The car only counts the checkpoint it is supposed to reach next, so short-cutting
/// the track or driving backwards does not count as a lap. A lap is completed when
/// the car crosses the start/finish checkpoint after passing all the other ones.
///
/// Add this to any car that has a collider - it works for the player and for AI.
/// </summary>
[DisallowMultipleComponent]
public class LapTracker : MonoBehaviour
{
    [Tooltip("Name shown in the Race Manager / UI. Leave empty to use the GameObject name.")]
    public string racerName;

    [Tooltip("Tick this on the player's car.")]
    public bool isPlayer;

    /// <summary>Full laps finished so far.</summary>
    public int CompletedLaps { get; private set; }

    /// <summary>The lap the car is driving right now (starts at 1).</summary>
    public int CurrentLap => CompletedLaps + 1;

    /// <summary>True once the car has driven RaceManager.totalLaps laps.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Position of the checkpoint the car has to reach next, in the RaceManager order.</summary>
    public int NextCheckpointIndex => nextIndex;

    /// <summary>The checkpoint the car has to reach next (useful for AI targets and minimaps).</summary>
    public Checkpoint NextCheckpoint
    {
        get
        {
            if (manager == null || manager.CheckpointCount == 0 || nextIndex < 0)
                return null;

            return manager.Checkpoints[nextIndex];
        }
    }

    /// <summary>
    /// How far this car is in the race, in "checkpoints passed" units.
    /// One full lap = RaceManager.CheckpointCount. Used to compare racers.
    /// </summary>
    public float RaceProgress
    {
        get
        {
            if (manager == null || manager.CheckpointCount == 0)
                return 0f;

            // nextIndex 0 means every other checkpoint is done and the finish line is next.
            int passedThisLap = nextIndex <= 0 ? manager.CheckpointCount - 1 : nextIndex - 1;
            return CompletedLaps * manager.CheckpointCount + passedThisLap;
        }
    }

    public string DisplayName => string.IsNullOrEmpty(racerName) ? name : racerName;

    RaceManager manager;
    int nextIndex = -1;

    void Start()
    {
        manager = RaceManager.Instance;

        if (manager == null || manager.CheckpointCount == 0)
        {
            Debug.LogWarning("[Race] \"" + name + "\" cannot count laps: there is no RaceManager " +
                             "or no checkpoint in the scene.", this);
            enabled = false;
            return;
        }

        manager.Register(this);

        // The car starts next to the start line, so the next thing it must reach is
        // checkpoint 1. The start line only counts once all others are passed.
        nextIndex = manager.CheckpointCount > 1 ? 1 : 0;
    }

    void OnDisable()
    {
        if (manager != null)
            manager.Unregister(this);
    }

    /// <summary>Called by <see cref="Checkpoint"/> when this car drives through it.</summary>
    public void PassCheckpoint(Checkpoint checkpoint)
    {
        if (manager == null || manager.CheckpointCount == 0)
            return;

        if (nextIndex < 0 || nextIndex >= manager.CheckpointCount)
            return;

        // Only the expected checkpoint counts. Passing any other one is ignored.
        if (manager.Checkpoints[nextIndex] != checkpoint)
            return;

        bool crossedStartLine = nextIndex == 0;
        nextIndex = (nextIndex + 1) % manager.CheckpointCount;

        manager.NotifyCheckpointPassed(this, checkpoint);

        if (!crossedStartLine)
            return;

        CompletedLaps++;

        if (manager.totalLaps > 0 && CompletedLaps >= manager.totalLaps)
            IsFinished = true;

        manager.NotifyLapCompleted(this);
    }
}
