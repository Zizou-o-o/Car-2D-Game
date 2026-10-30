using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Works out who is first, last and everything in between, using how far each car has
/// travelled along the <see cref="RacePath"/> racing line.
///
/// Checkpoints alone are too coarse for that: a car that is 3 checkpoints ahead looks
/// identical to a car half a corner ahead of it. The racing line gives the exact order,
/// so a pass is noticed the moment it happens instead of at the next checkpoint.
///
/// Put this on an empty GameObject (Tools > Car2D > Race > Create Position Manager).
/// It finds the RaceManager and the RacePath by itself.
///
/// Read it from your UI:
///   RacePositionManager.Instance.GetPosition(lapTracker)   // 1 = first
///   RacePositionManager.Instance.Leader / .Last
///   RacePositionManager.Instance.Standings                 // P1 ... Pn
///   RacePositionManager.Instance.PositionChanged += (racer, oldPos, newPos) => ...;
/// </summary>
[DefaultExecutionOrder(100)] // after LapTracker (0), so laps are up to date when we read them
[DisallowMultipleComponent]
public class RacePositionManager : MonoBehaviour
{
    public static RacePositionManager Instance { get; private set; }

    [Header("Race")]
    [Tooltip("Racing line used to measure progress. Found automatically when left empty.")]
    public RacePath path;

    [Header("Debug")]
    [Tooltip("Shows the live standings while testing.")]
    public bool showDebugOverlay = true;

    [Tooltip("Screen position of the debug standings box.")]
    public Vector2 overlayOffset = new Vector2(240f, 10f);

    [Tooltip("Show how far each car is behind the leader, in world units.")]
    public bool showGaps = true;

    [Header("Robustness")]
    [Tooltip("Arc jumps larger than this fraction of the track are treated as a teleport " +
             "(respawn, car added mid-race) and do not count as progress.")]
    [Range(0.05f, 0.5f)]
    public float teleportThreshold = 0.25f;

    /// <summary>One row of the standings.</summary>
    public struct Standing
    {
        public LapTracker racer;
        public int position;        // 1 = first
        public int lap;             // the lap the car is on (starts at 1)
        public float progress;      // distance covered along the racing line, in world units
        public float gapToLeader;   // world units behind the leader
        public bool finished;
        public int finishOrder;     // 0 = still racing
    }

    sealed class Tracker
    {
        public int hint = -1;
        public float arc;
        public float progress;
        public bool started;
        public int position;
        public int finishOrder;
    }

    readonly Dictionary<LapTracker, Tracker> trackers = new Dictionary<LapTracker, Tracker>();
    readonly List<Standing> standings = new List<Standing>();
    readonly List<LapTracker> racers = new List<LapTracker>();
    readonly HashSet<LapTracker> racerSet = new HashSet<LapTracker>();
    readonly List<LapTracker> stale = new List<LapTracker>();
    readonly List<LapTracker> foundRacers = new List<LapTracker>();

    RaceManager manager;
    float trackLength;
    float startArc;
    bool trackReady;
    float pathRetryTimer;
    float racerRefreshTimer;
    int finishedCount;

    /// <summary>P1 first, last place at the end.</summary>
    public IReadOnlyList<Standing> Standings => standings;

    public int RacerCount => standings.Count;

    /// <summary>The car in first place, or null.</summary>
    public LapTracker Leader => standings.Count > 0 ? standings[0].racer : null;

    /// <summary>The car in last place, or null.</summary>
    public LapTracker Last => standings.Count > 0 ? standings[standings.Count - 1].racer : null;

    /// <summary>Fired when a car moves up or down: (car, previous position, new position).</summary>
    public event Action<LapTracker, int, int> PositionChanged;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Positions] There is already a RacePositionManager in the scene on \"" +
                             name + "\". Deleting this extra one.", this);
            Destroy(this);
            return;
        }

        Instance = this;

        manager = RaceManager.Instance != null ? RaceManager.Instance : FindFirstObjectByType<RaceManager>();

        if (path == null)
            path = FindFirstObjectByType<RacePath>();

        RebuildTrack();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (path == null)
        {
            // The path might be created while the game is running.
            pathRetryTimer -= Time.deltaTime;

            if (pathRetryTimer <= 0f)
            {
                pathRetryTimer = 1f;
                path = FindFirstObjectByType<RacePath>();

                if (path != null)
                    RebuildTrack();
            }
        }

        if (manager == null)
        {
            manager = RaceManager.Instance;

            if (manager != null)
                RebuildTrack();
        }

        if (trackReady && path != null && path.TotalLength > 0.01f &&
            !Mathf.Approximately(trackLength, path.TotalLength))
        {
            // The line was edited while playing.
            RebuildTrack();
        }

        RefreshRacers();
        UpdateStandings();
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>1-based position of a car, or 0 if it is not racing.</summary>
    public int GetPosition(LapTracker racer)
    {
        for (int i = 0; i < standings.Count; i++)
        {
            if (standings[i].racer == racer)
                return standings[i].position;
        }

        return 0;
    }

    public bool TryGetStanding(LapTracker racer, out Standing standing)
    {
        for (int i = 0; i < standings.Count; i++)
        {
            if (standings[i].racer == racer)
            {
                standing = standings[i];
                return true;
            }
        }

        standing = default;
        return false;
    }

    /// <summary>"P2" for a car, or "--" when it is not racing.</summary>
    public string GetPositionLabel(LapTracker racer)
    {
        int position = GetPosition(racer);
        return position > 0 ? "P" + position : "--";
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    void RebuildTrack()
    {
        trackReady = false;
        trackLength = 0f;
        startArc = 0f;

        if (path == null)
            return;

        trackLength = path.TotalLength;

        if (trackLength <= 0.01f)
            return;

        // The lap is measured from the start/finish checkpoint, not from wherever the
        // first point of the line happens to be.
        if (manager != null && manager.CheckpointCount > 0)
            startArc = path.ArcAt(manager.Checkpoints[0].WorldCenter, -1);
        else
            startArc = 0f;

        trackReady = true;
    }

    void RefreshRacers()
    {
        racers.Clear();
        racerSet.Clear();

        if (manager != null)
        {
            IReadOnlyList<LapTracker> registered = manager.Racers;

            for (int i = 0; i < registered.Count; i++)
            {
                LapTracker racer = registered[i];

                if (racer == null || !racer.isActiveAndEnabled)
                    continue;

                racers.Add(racer);
                racerSet.Add(racer);
            }
        }
        else
        {
            // No RaceManager: fall back to every LapTracker in the scene. Searching the
            // scene every frame is expensive, so only do it now and then.
            racerRefreshTimer -= Time.deltaTime;

            if (racerRefreshTimer <= 0f)
            {
                racerRefreshTimer = 0.5f;
                foundRacers.Clear();
                foundRacers.AddRange(FindObjectsByType<LapTracker>(FindObjectsSortMode.None));
            }

            for (int i = 0; i < foundRacers.Count; i++)
            {
                LapTracker racer = foundRacers[i];

                if (racer == null || !racer.isActiveAndEnabled)
                    continue;

                racers.Add(racer);
                racerSet.Add(racer);
            }
        }

        // Forget cars that were destroyed or stopped racing.
        stale.Clear();

        foreach (KeyValuePair<LapTracker, Tracker> pair in trackers)
        {
            if (pair.Key == null || !racerSet.Contains(pair.Key))
                stale.Add(pair.Key);
        }

        for (int i = 0; i < stale.Count; i++)
            trackers.Remove(stale[i]);
    }

    void UpdateStandings()
    {
        standings.Clear();

        for (int i = 0; i < racers.Count; i++)
        {
            LapTracker racer = racers[i];

            if (!trackers.TryGetValue(racer, out Tracker tracker))
            {
                tracker = new Tracker();
                trackers[racer] = tracker;
            }

            UpdateProgress(racer, tracker);

            if (racer.IsFinished && tracker.finishOrder == 0)
                tracker.finishOrder = ++finishedCount;

            standings.Add(new Standing
            {
                racer = racer,
                lap = racer.CurrentLap,
                progress = tracker.progress,
                finished = racer.IsFinished,
                finishOrder = tracker.finishOrder
            });
        }

        standings.Sort(CompareStandings);

        float leaderProgress = standings.Count > 0 ? standings[0].progress : 0f;

        for (int i = 0; i < standings.Count; i++)
        {
            Standing standing = standings[i];

            standing.position = i + 1;
            standing.gapToLeader = i == 0 ? 0f : Mathf.Max(0f, leaderProgress - standing.progress);
            standings[i] = standing;

            Tracker tracker = trackers[standing.racer];

            if (tracker.position == standing.position)
                continue;

            int previous = tracker.position;
            tracker.position = standing.position;

            if (previous != 0)
                PositionChanged?.Invoke(standing.racer, previous, standing.position);
        }
    }

    void UpdateProgress(LapTracker racer, Tracker tracker)
    {
        if (!trackReady || path == null)
        {
            // No racing line: fall back to checkpoint order so the list still makes sense.
            tracker.progress = racer.RaceProgress;
            return;
        }

        float arc = path.ArcAt(racer.transform.position, tracker.hint, out int nearestSegment);
        tracker.hint = nearestSegment;

        if (!tracker.started)
        {
            tracker.started = true;
            tracker.arc = arc;
            tracker.progress = WrapSigned(arc - startArc, trackLength);
            return;
        }

        float delta = WrapSigned(arc - tracker.arc, trackLength);

        // A big jump means the car was moved (respawn, teleport), not that it drove there.
        if (Mathf.Abs(delta) > trackLength * teleportThreshold)
            delta = 0f;

        tracker.progress += delta;
        tracker.arc = arc;
    }

    int CompareStandings(Standing a, Standing b)
    {
        // Finished cars always stay ahead of the ones still racing...
        if (a.finished != b.finished)
            return a.finished ? -1 : 1;

        // ...in the order they finished...
        if (a.finished && a.finishOrder != b.finishOrder)
            return a.finishOrder.CompareTo(b.finishOrder);

        // ...then whoever covered more of the line is ahead.
        int byProgress = b.progress.CompareTo(a.progress);

        if (byProgress != 0)
            return byProgress;

        // Stable tie-break so the order does not flicker when two cars are level.
        return a.racer.GetInstanceID().CompareTo(b.racer.GetInstanceID());
    }

    static float WrapSigned(float distance, float length)
    {
        if (length <= 1e-5f)
            return 0f;

        while (distance > length * 0.5f)
            distance -= length;

        while (distance < -length * 0.5f)
            distance += length;

        return distance;
    }

    void OnGUI()
    {
        if (!showDebugOverlay || standings.Count == 0)
            return;

        const float width = 290f;
        const float lineHeight = 20f;
        Rect box = new Rect(overlayOffset.x, overlayOffset.y, width,
            8f + lineHeight * (standings.Count + 1));

        GUI.Box(box, GUIContent.none);
        GUI.Label(new Rect(box.x + 8f, box.y + 4f, width, lineHeight),
            "POSITIONS (" + standings.Count + " cars)");

        for (int i = 0; i < standings.Count; i++)
        {
            Standing standing = standings[i];

            string text = (standing.racer.isPlayer ? "> " : "   ") +
                          "P" + standing.position + "   " + standing.racer.DisplayName +
                          "   lap " + standing.lap;

            if (showGaps && standing.position > 1 && !standing.finished)
                text += "   +" + standing.gapToLeader.ToString("0.0");

            if (standing.finished)
                text += "   (finished)";

            GUI.Label(new Rect(box.x + 8f, box.y + 4f + lineHeight * (i + 1), width, lineHeight), text);
        }
    }
}
