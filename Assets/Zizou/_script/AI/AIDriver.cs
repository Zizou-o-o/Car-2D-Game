using UnityEngine;

/// <summary>
/// Context-steering driver for an AI opponent.
///
/// It uses the exact same car as the player (TopDown2DCarController, OffRoadSlowdown,
/// LapTracker + checkpoints), so an AI races with the same rules: it drives through the
/// checkpoints in order and its laps are counted by the RaceManager.
///
/// How it drives:
///   1. Aim        - a point a little ahead on the <see cref="RacePath"/> (the racing line).
///                   Aiming ahead instead of at the checkpoint is what removes the zig-zag.
///   2. Steering   - context steering, like the reference project: interest towards the aim
///                   point, danger from the Road layer edges and from other cars, pick the
///                   direction with the best score.
///   3. Speed      - the car's own turning circle decides the safe speed for the corner ahead
///                   (radius x yaw rate x grip). If the position it will have in a moment is
///                   already off the road, it slows down further before it slides out.
///   4. Catch up   - when it is far from the player it uses the full grip and never lifts, so
///                   it goes as fast as the track allows, but it still follows the line.
///
/// The line guides, the Road layer is the limit: the AI may drift off the line to overtake,
/// but the road danger keeps it on the track.
/// </summary>
[DefaultExecutionOrder(-50)] // after OffRoadSlowdown (-100), before TopDown2DCarController (0)
[RequireComponent(typeof(TopDown2DCarController))]
[DisallowMultipleComponent]
public class AIDriver : MonoBehaviour
{
    [Header("Racing line")]
    [Tooltip("Line to follow. Found automatically when left empty.")]
    public RacePath path;

    [Tooltip("How far ahead on the line the car aims, per unit of speed.")]
    [Min(0f)]
    public float lookaheadPerSpeed = 0.5f;

    public float minLookahead = 2f;
    public float maxLookahead = 9f;

    [Header("Context steering")]
    [Tooltip("How many directions are evaluated around the car.")]
    [Range(8, 64)]
    public int mapResolution = 16;

    [Tooltip("Higher = reacts faster to the chosen direction. Lower = smoother but lazier.")]
    [Min(0f)]
    public float directionSmoothing = 10f;

    [Tooltip("How strongly the AI wants to drive towards the aim point.")]
    [Range(0f, 2f)]
    public float seekWeight = 1f;

    [Tooltip("How strongly the AI prefers driving forwards over turning around.")]
    [Range(0f, 2f)]
    public float forwardWeight = 0.35f;

    [Header("Stay on the road")]
    [Tooltip("Layers with the road colliders (created by Tools > Car2D > Setup Off-Road Slowdown).")]
    public LayerMask roadLayers;

    public float roadLookMin = 1.5f;
    public float roadLookMax = 6f;

    [Tooltip("How many points are checked along each direction.")]
    [Range(2, 10)]
    public int roadSamples = 5;

    [Range(0f, 1f)]
    public float roadDangerWeight = 1f;

    [Tooltip("Small gaps/notches in the road art that should not count as leaving the road (0 = none).")]
    [Range(0f, 0.5f)]
    public float roadDangerTolerance = 0.15f;

    [Header("Avoid other cars")]
    public float avoidCarDistance = 2.5f;

    [Range(0f, 2f)]
    public float carDangerWeight = 0.8f;

    [Header("Corner speed")]
    [Tooltip("Fraction of the car's own turning limit used when close to the player. Lower = more careful.")]
    [Range(0.1f, 1f)]
    public float cornerSpeedScale = 0.8f;

    [Tooltip("Sideways acceleration assumed for the car. Lower = brakes earlier for corners.")]
    public float maxLateralAcceleration = 22f;

    [Tooltip("How far ahead in time the car predicts its own position to check whether it is about to leave the road.")]
    public float predictTime = 0.35f;

    [Tooltip("Speed factor applied when the predicted position is off the road.")]
    [Range(0.1f, 1f)]
    public float offRoadPredictionSpeedFactor = 0.6f;

    [Tooltip("Speed difference at which the throttle switches between accelerating and coasting.")]
    public float speedDeadband = 0.25f;

    [Header("Steering")]
    [Tooltip("Angle error that uses full steering (degrees).")]
    public float steerFullAngle = 50f;

    [Tooltip("When the AI is this many checkpoints ahead of or behind the player it stops lifting for corners and uses the full grip.")]
    public float catchUpCheckpoints = 2f;

    [Header("Recovery")]
    public float stuckSpeed = 0.6f;
    public float stuckAfter = 2f;
    public float reverseTime = 1f;

    [Header("Debug")]
    public bool drawDebug = false;

    TopDown2DCarController car;
    LapTracker tracker;
    LapTracker player;
    Rigidbody2D body;
    ContextSteering steering;

    Vector2 chosenDirection;
    float stuckTimer;
    float reversingTimer;
    float pathRetryTimer;
    int segmentHint = -1;
    Vector3 aimPoint;

    void Start()
    {
        car = GetComponent<TopDown2DCarController>();
        body = GetComponent<Rigidbody2D>();
        tracker = GetComponent<LapTracker>();

        if (roadLayers.value == 0)
            roadLayers = LayerMask.GetMask("Road");

        steering = new ContextSteering(mapResolution);

        if (body == null)
        {
            Debug.LogWarning("[AI] \"" + name + "\" has no Rigidbody2D, so it cannot drive.", this);
            enabled = false;
            return;
        }

        if (tracker == null)
        {
            Debug.LogWarning("[AI] \"" + name + "\" has no LapTracker, so it has nowhere to drive. " +
                             "Add one, or use Tools > Car2D > Race > Add Race Manager + LapTrackers.", this);
        }

        if (path == null)
            path = FindFirstObjectByType<RacePath>();

        if (path == null)
        {
            Debug.LogWarning("[AI] \"" + name + "\" found no Race Path, so it will drive straight at the " +
                             "next checkpoint instead of following the track. Create one with " +
                             "Tools > Car2D > Race > Create Race Path From Checkpoints.", this);
        }

        if (roadLayers.value != 0 && Physics2D.OverlapCircle(transform.position, 50f, roadLayers) == null)
        {
            Debug.LogWarning("[AI] No road colliders found on the Road layer. The AI will drive straight " +
                             "to the checkpoints instead of following the road. Run " +
                             "Tools > Car2D > Setup Off-Road Slowdown (whole scene) to create them.", this);
        }
    }

    void OnDisable()
    {
        // Do not leave the car capped at a corner speed when the AI is switched off.
        if (car != null)
            car.speedLimit = 0f;
    }

    void FixedUpdate()
    {
        if (car == null || tracker == null)
            return;

        if (tracker.IsFinished)
        {
            car.SetInputVector(Vector2.zero);
            car.speedLimit = 0f;
            return;
        }

        Checkpoint checkpoint = tracker.NextCheckpoint;

        if (path == null)
        {
            // The path might be created while the game is running; do not search every frame.
            pathRetryTimer -= Time.fixedDeltaTime;

            if (pathRetryTimer <= 0f)
            {
                pathRetryTimer = 0.5f;
                path = FindFirstObjectByType<RacePath>();
            }
        }
        if (path == null && checkpoint == null)
        {
            car.SetInputVector(Vector2.zero);
            car.speedLimit = 0f;
            return;
        }

        if (player == null && RaceManager.Instance != null)
            player = RaceManager.Instance.Player;

        // Rebuild if the map resolution was changed in the Inspector.
        if (steering == null || steering.Count != mapResolution)
            steering = new ContextSteering(mapResolution);

        float deltaTime = Time.fixedDeltaTime;
        UpdateStuck(deltaTime);

        Vector2 position = body.position;
        Vector2 forward = transform.up;
        float speed = body.linearVelocity.magnitude;
        float playerPush = PlayerPush();

        // --- Where do we aim? ---
        bool followPath = path != null && path.PointCount >= 2;
        Vector2 aimDirection;
        float curvature = 0f;

        if (followPath)
        {
            float lookahead = Mathf.Clamp(minLookahead + speed * lookaheadPerSpeed, minLookahead, maxLookahead);
            RacePath.PathSample sample = path.Sample(position, lookahead, segmentHint);

            segmentHint = sample.nearestSegment;
            aimPoint = sample.position;
            aimDirection = DirectionTo(position, sample.position, forward);
            curvature = sample.curvature;
        }
        else
        {
            aimPoint = checkpoint.WorldCenter;
            aimDirection = DirectionTo(position, checkpoint.WorldCenter, forward);
        }

        float roadLookAhead = Mathf.Lerp(roadLookMin, roadLookMax,
            Mathf.Clamp01(speed / Mathf.Max(0.01f, car.maxSpeed)));

        // --- Context steering: interest towards the aim point, danger from road and cars ---
        steering.Begin();

        for (int i = 0; i < steering.Count; i++)
        {
            Vector2 direction = steering.Direction(i);

            float seek = Mathf.Clamp01(Vector2.Dot(direction, aimDirection)) * seekWeight;
            float forwardInterest = Mathf.Clamp01(Vector2.Dot(direction, forward)) * forwardWeight;
            steering.AddInterest(i, Mathf.Max(seek, forwardInterest));

            float roadDanger = RoadDanger(position, direction, roadLookAhead) * roadDangerWeight;
            float carDanger = CarDanger(position, direction) * carDangerWeight;
            steering.AddDanger(i, Mathf.Max(roadDanger, carDanger));
        }

        Vector2 desiredDirection = steering.Choose();

        // Smooth the choice a little so the steering does not jitter.
        if (chosenDirection.sqrMagnitude < 0.0001f)
        {
            chosenDirection = desiredDirection;
        }
        else
        {
            Vector2 smoothed = Vector2.Lerp(chosenDirection, desiredDirection,
                1f - Mathf.Exp(-directionSmoothing * deltaTime));

            // Lerping between two opposite directions can cancel out; fall back instead
            // of handing a zero-length direction to SignedAngle.
            chosenDirection = smoothed.sqrMagnitude > 0.0001f ? smoothed.normalized : desiredDirection;
        }

        // --- Convert the chosen direction into car inputs ---
        float signedAngle = Vector2.SignedAngle(forward, chosenDirection);
        float steer = Mathf.Clamp(signedAngle / Mathf.Max(1f, steerFullAngle), -1f, 1f);

        if (Mathf.Abs(signedAngle) < 2f)
            steer = 0f;

        float targetSpeed = TargetSpeed(curvature, followPath, playerPush);

        float throttle;

        if (speed > targetSpeed + 0.75f)
            throttle = 0f;                                  // too fast: coast, the cap bleeds the speed
        else if (speed < targetSpeed - speedDeadband)
            throttle = 1f;                                  // too slow: full throttle
        else
            throttle = 0.5f;                                // about right: hold it

        if (reversingTimer > 0f)
        {
            reversingTimer -= deltaTime;
            throttle = -1f;
            steer = 1f;
            chosenDirection = forward;
            car.speedLimit = 0f;
        }
        else
        {
            // Cap the car at the corner speed so the controller bleeds the extra speed.
            car.speedLimit = targetSpeed < car.maxSpeed - 0.1f ? targetSpeed : 0f;
        }

        // The controller turns clockwise for positive turn input, so steer is inverted.
        car.SetInputVector(new Vector2(-steer, throttle));
    }

    /// <summary>Fastest speed the car can take the corner ahead and stay on the road.</summary>
    float TargetSpeed(float curvature, bool followPath, float playerPush)
    {
        float target = car.maxSpeed;

        if (followPath)
        {
            float radius = 1f / Mathf.Max(0.0001f, curvature);

            // The controller turns turnFacter degrees per physics step at speed, which is this
            // many radians per second. On an arc of radius r that limits the speed to r * yawRate.
            float yawRate = car.turnFacter * Mathf.Deg2Rad / Time.fixedDeltaTime;
            float grip = Mathf.Lerp(cornerSpeedScale, 1f, playerPush);
            float turnLimited = radius * yawRate * grip;

            // The car drifts (driftFactor), so also respect a sideways acceleration limit.
            float lateral = maxLateralAcceleration * Mathf.Lerp(1f, 1.6f, playerPush);
            float gripLimited = Mathf.Sqrt(lateral * radius);

            target = Mathf.Min(target, Mathf.Min(turnLimited, gripLimited));

            // If the car is on the road and the point it is heading to after predictTime is
            // not, it is about to slide off: slow down before it happens.
            if (roadLayers.value != 0 && IsOnRoad(body.position))
            {
                Vector2 predicted = body.position + body.linearVelocity * predictTime;

                if (!IsOnRoad(predicted))
                    target *= offRoadPredictionSpeedFactor;
            }
        }

        return Mathf.Clamp(target, 1f, car.maxSpeed);
    }

    /// <summary>0 = close to the player, 1 = far (ahead or behind): use the full grip.</summary>
    float PlayerPush()
    {
        if (player == null || tracker == null)
            return 0f;

        float gap = Mathf.Abs(player.RaceProgress - tracker.RaceProgress);
        return Mathf.Clamp01(gap / Mathf.Max(0.01f, catchUpCheckpoints));
    }

    static Vector2 DirectionTo(Vector2 position, Vector3 target, Vector2 fallback)
    {
        Vector2 delta = (Vector2)target - position;
        return delta.sqrMagnitude > 0.0001f ? delta.normalized : fallback;
    }

    bool IsOnRoad(Vector2 position)
    {
        return roadLayers.value != 0 && Physics2D.OverlapPoint(position, roadLayers) != null;
    }

    /// <summary>0 = the direction is clear road, 1 = it leads straight off the road.</summary>
    float RoadDanger(Vector2 origin, Vector2 direction, float lookAhead)
    {
        if (roadLayers.value == 0)
            return 0f;

        int onRoad = 0;

        for (int sample = 1; sample <= roadSamples; sample++)
        {
            Vector2 point = origin + direction * (lookAhead * sample / roadSamples);

            if (Physics2D.OverlapPoint(point, roadLayers) != null)
                onRoad++;
        }

        float offRoad = 1f - (float)onRoad / roadSamples;

        // Small notches in the road artwork should not push the AI away, only real edges do.
        float tolerance = Mathf.Clamp(roadDangerTolerance, 0f, 0.49f);
        return Mathf.Clamp01((offRoad - tolerance) / Mathf.Max(0.01f, 1f - tolerance));
    }

    /// <summary>Danger for directions that point at another racer that is close by.</summary>
    float CarDanger(Vector2 position, Vector2 direction)
    {
        if (RaceManager.Instance == null || avoidCarDistance <= 0f)
            return 0f;

        float danger = 0f;

        foreach (LapTracker other in RaceManager.Instance.Racers)
        {
            if (other == null || other == tracker)
                continue;

            Vector2 toOther = (Vector2)other.transform.position - position;
            float distance = toOther.magnitude;

            if (distance > avoidCarDistance || distance < 0.001f)
                continue;

            float alignment = Vector2.Dot(direction, toOther / distance);

            if (alignment <= 0f)
                continue;

            float proximity = 1f - distance / avoidCarDistance;
            danger = Mathf.Max(danger, alignment * proximity);
        }

        return danger;
    }

    void UpdateStuck(float deltaTime)
    {
        if (reversingTimer > 0f)
            return;

        if (body.linearVelocity.magnitude < stuckSpeed)
            stuckTimer += deltaTime;
        else
            stuckTimer = 0f;

        if (stuckTimer >= stuckAfter)
        {
            stuckTimer = 0f;
            reversingTimer = reverseTime;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!drawDebug)
            return;

        Vector3 origin = transform.position;

        if (steering != null)
        {
            for (int i = 0; i < steering.Count; i++)
            {
                Vector2 direction = steering.Direction(i);
                float score = steering.Score(i);

                Gizmos.color = score >= 0f
                    ? Color.Lerp(Color.yellow, Color.green, Mathf.Clamp01(score))
                    : new Color(1f, 0f, 0f, Mathf.Clamp01(-score));

                Gizmos.DrawLine(origin, origin + new Vector3(direction.x, direction.y, 0f) * 2f);
            }

            Gizmos.color = Color.white;
            Gizmos.DrawLine(origin, origin + (Vector3)chosenDirection * 3f);
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, aimPoint);
        Gizmos.DrawWireSphere(aimPoint, 0.35f);
    }
#endif
}
