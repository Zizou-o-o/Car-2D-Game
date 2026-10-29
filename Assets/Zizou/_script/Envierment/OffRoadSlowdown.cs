using UnityEngine;

/// <summary>
/// Slows a car down while it is off the road.
///
/// "The road" is any trigger collider the car can stand on. Those colliders must be
/// on the layer set in <see cref="roadLayers"/> (the "Road" layer by default).
/// You can put one big collider around the whole track, or let
/// Tools > Car2D > Setup Off-Road Slowdown generate one collider per road sprite.
///
/// Because the check is done by this car itself, the same component works for the
/// player and for AI cars: just add it to any car that has a TopDown2DCarController.
/// </summary>
[DefaultExecutionOrder(-100)] // run before TopDown2DCarController, so it reads the multiplier the same frame
[RequireComponent(typeof(TopDown2DCarController))]
public class OffRoadSlowdown : MonoBehaviour
{
    [Header("Off-road penalty")]
    [Tooltip("Top speed lost when the car is completely off the road. 0.4 = the car keeps 60% of its speed.")]
    [Range(0f, 0.95f)]
    public float offRoadSpeedLoss = 0.4f;

    [Tooltip("How quickly the penalty fades in and out. Higher = snappier, lower = softer.")]
    [Min(0.01f)]
    public float transitionSpeed = 5f;

    [Header("Road detection")]
    [Tooltip("Layers that contain the road colliders.")]
    public LayerMask roadLayers;

    [Tooltip("How much of the car has to be on the road to count as on-road. 0.5 = at least half of it.")]
    [Range(0f, 1f)]
    public float onRoadThreshold = 0.5f;

    [Tooltip("Sample points are pulled inside the car footprint by this amount, so a car has to really leave the road before it slows down.")]
    [Range(0.1f, 1f)]
    public float sampleInset = 0.85f;

    /// <summary>True when enough of the car is on the road.</summary>
    public bool IsOnRoad { get; private set; } = true;

    /// <summary>Current speed multiplier applied to the car (1 = full speed).</summary>
    public float SpeedMultiplier => speedMultiplier;

    TopDown2DCarController car;

    // 5 sample points: the centre of the car plus its four corners.
    readonly Vector3[] samplePoints = new Vector3[5];

    float speedMultiplier = 1f;

    void Reset()
    {
        roadLayers = LayerMask.GetMask("Road");
    }

    void Awake()
    {
        car = GetComponent<TopDown2DCarController>();

        if (roadLayers.value == 0)
            roadLayers = LayerMask.GetMask("Road");

        BuildSamplePoints();
    }

    void OnEnable()
    {
        IsOnRoad = true;
        speedMultiplier = 1f;
    }

    void OnDisable()
    {
        // Never leave the car permanently slowed down when this is turned off.
        if (car != null)
            car.surfaceSpeedMultiplier = 1f;
    }

    void FixedUpdate()
    {
        float onRoad = RoadCoverage();
        IsOnRoad = onRoad >= onRoadThreshold;

        // 0% of the car on the road -> full penalty, 100% -> no penalty.
        // Anything in between (half on the kerb, for example) blends smoothly.
        float target = Mathf.Lerp(1f - offRoadSpeedLoss, 1f, onRoad);
        speedMultiplier = Mathf.MoveTowards(speedMultiplier, target, transitionSpeed * Time.fixedDeltaTime);

        car.surfaceSpeedMultiplier = speedMultiplier;
    }

    /// <summary>Returns how much of the car is on the road: 0 = fully off, 1 = fully on.</summary>
    float RoadCoverage()
    {
        if (roadLayers.value == 0)
            return 1f;

        int pointsOnRoad = 0;
        for (int i = 0; i < samplePoints.Length; i++)
        {
            Vector2 worldPoint = transform.TransformPoint(samplePoints[i]);
            if (Physics2D.OverlapPoint(worldPoint, roadLayers) != null)
                pointsOnRoad++;
        }

        return pointsOnRoad / (float)samplePoints.Length;
    }

    // Works out where to test for the road, in the car's local space.
    // The car's sprite is used when there is one (that is what the player sees),
    // otherwise its BoxCollider2D, otherwise a 1x1 box.
    void BuildSamplePoints()
    {
        Vector2 center;
        Vector2 halfSize;

        SpriteRenderer spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        BoxCollider2D box = GetComponent<BoxCollider2D>();

        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            Bounds bounds = spriteRenderer.sprite.bounds;
            Vector3 worldCenter = spriteRenderer.transform.TransformPoint(bounds.center);
            Vector3 worldSize = Vector3.Scale(bounds.size, spriteRenderer.transform.lossyScale);
            Vector3 localSize = transform.InverseTransformVector(worldSize);

            center = transform.InverseTransformPoint(worldCenter);
            halfSize = new Vector2(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y)) * 0.5f;
        }
        else if (box != null)
        {
            center = box.offset;
            halfSize = box.size * 0.5f;
        }
        else
        {
            center = Vector2.zero;
            halfSize = Vector2.one * 0.5f;
        }

        halfSize *= sampleInset;

        samplePoints[0] = center;
        samplePoints[1] = center + new Vector2(-halfSize.x, -halfSize.y);
        samplePoints[2] = center + new Vector2(-halfSize.x, halfSize.y);
        samplePoints[3] = center + new Vector2(halfSize.x, -halfSize.y);
        samplePoints[4] = center + new Vector2(halfSize.x, halfSize.y);
    }

#if UNITY_EDITOR
    // Draw the tested footprint in the Scene view so the setup is easy to check.
    void OnDrawGizmosSelected()
    {
        if (samplePoints[0] == Vector3.zero && samplePoints[4] == Vector3.zero)
            BuildSamplePoints();

        Gizmos.color = Application.isPlaying && IsOnRoad ? Color.green : Color.yellow;
        for (int i = 0; i < samplePoints.Length; i++)
            Gizmos.DrawWireSphere(transform.TransformPoint(samplePoints[i]), 0.05f);
    }
#endif
}
