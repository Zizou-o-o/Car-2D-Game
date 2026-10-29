using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The racing line the AI cars follow, drawn with a LineRenderer.
///
/// It stores the points itself (world space) and uses the LineRenderer only to draw
/// them, because a LineRenderer has no useful sampling API. The AI asks this component
/// questions like "where is the point 5 units ahead of me on the line?" and
/// "how tight is the corner there?", which is what makes the driving smooth.
///
/// Create one with Tools > Car2D > Race > Create Race Path From Checkpoints, then drag
/// the points in the Scene view to shape it (see RacePathEditor).
///
/// The line is only the route. The Road layer colliders stay the hard limit of the
/// track, so a car may leave the line to overtake a rival but not the road.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class RacePath : MonoBehaviour
{
    [Header("Racing line")]
    [Tooltip("World-space points of the line, in driving order. Drag them in the Scene view.")]
    [SerializeField]
    List<Vector3> points = new List<Vector3>();

    [Tooltip("Close the loop: the last point connects back to the first one (a circuit).")]
    public bool isClosed = true;

    [Tooltip("Width of the racing corridor. Only used by the gizmo and for tuning.")]
    [Min(0.1f)]
    public float width = 4f;

    [Header("Visuals")]
    [Tooltip("LineRenderer used to draw the line. Found or created automatically.")]
    public LineRenderer line;

    public Color lineColor = new Color(1f, 0.4f, 0.1f, 1f);

    [Min(0.01f)]
    public float lineWidth = 0.12f;

    public string sortingLayer = "Default";
    public int sortingOrder = 40;

    [Header("Gizmos")]
    public bool showGizmos = true;

    /// <summary>A point on the line, ready to be used for driving.</summary>
    public struct PathSample
    {
        /// <summary>World position of the sampled point.</summary>
        public Vector3 position;

        /// <summary>Direction the line is heading at that point.</summary>
        public Vector3 forward;

        /// <summary>1 / radius of the corner there (1/units). 0 = straight.</summary>
        public float curvature;

        /// <summary>Distance along the line from the first point.</summary>
        public float distance;

        /// <summary>Segment index the sampled point is on.</summary>
        public int segment;

        /// <summary>Segment index closest to the position that was queried.</summary>
        public int nearestSegment;
    }

    float[] cumulative = Array.Empty<float>();
    float totalLength;
    int segmentCount;
    bool cacheDirty = true;

    public IReadOnlyList<Vector3> Points => points;
    public int PointCount => points.Count;
    public bool Closed => isClosed;

    public float TotalLength
    {
        get
        {
            EnsureCache();
            return totalLength;
        }
    }

    /// <summary>How many straight pieces the line is made of.</summary>
    public int SegmentCount
    {
        get
        {
            EnsureCache();
            return segmentCount;
        }
    }

    void OnEnable()
    {
        Refresh();
    }

    void OnValidate()
    {
        Refresh();
    }

    void Reset()
    {
        line = GetComponent<LineRenderer>();
        Refresh();
    }

    // ------------------------------------------------------------------
    // Sampling
    // ------------------------------------------------------------------

    /// <summary>
    /// Point on the line <paramref name="distanceAhead"/> units after the point closest to
    /// <paramref name="worldPosition"/>. Pass the previous result's nearestSegment as
    /// <paramref name="hintSegment"/> so the search stays cheap and does not jump to another
    /// part of the track when the circuit passes close to itself.
    /// </summary>
    public PathSample Sample(Vector3 worldPosition, float distanceAhead, int hintSegment = -1)
    {
        EnsureCache();

        PathSample sample = default;

        if (points.Count == 0)
        {
            sample.position = worldPosition;
            sample.forward = transform.up;
            sample.segment = -1;
            sample.nearestSegment = -1;
            return sample;
        }

        if (segmentCount == 0)
        {
            sample.position = points[0];
            sample.forward = transform.up;
            sample.segment = 0;
            sample.nearestSegment = 0;
            return sample;
        }

        int nearest = FindClosestSegment(worldPosition, hintSegment);
        float arc = ProjectToArc(worldPosition, nearest, out float _);

        sample = SampleAtArc(arc + distanceAhead);
        sample.nearestSegment = nearest;
        return sample;
    }

    /// <summary>Point on the line at a given distance along it.</summary>
    public PathSample SampleAtArc(float arc)
    {
        EnsureCache();

        PathSample sample = default;

        if (segmentCount == 0)
        {
            sample.position = points.Count > 0 ? points[0] : transform.position;
            sample.forward = transform.up;
            return sample;
        }

        sample.distance = WrapArc(arc);
        Locate(sample.distance, out int segment, out float t);

        Vector3 a = PointAt(segment);
        Vector3 b = PointAt(segment + 1);

        sample.segment = segment;
        sample.nearestSegment = segment;
        sample.position = Vector3.Lerp(a, b, t);
        sample.forward = Direction(a, b);
        sample.curvature = CurvatureAt(sample.distance);

        return sample;
    }

    /// <summary>Closest point on the line to a world position.</summary>
    public Vector3 NearestPoint(Vector3 worldPosition, int hintSegment = -1)
    {
        EnsureCache();

        if (points.Count == 0)
            return worldPosition;

        if (segmentCount == 0)
            return points[0];

        int segment = FindClosestSegment(worldPosition, hintSegment);
        float arc = ProjectToArc(worldPosition, segment, out float _);
        Locate(WrapArc(arc), out int located, out float t);

        return Vector3.Lerp(PointAt(located), PointAt(located + 1), t);
    }

    /// <summary>Segment whose middle is closest to a world position.</summary>
    public int FindClosestSegment(Vector3 worldPosition, int hint = -1)
    {
        EnsureCache();

        if (segmentCount == 0)
            return -1;

        int best = 0;
        float bestSqr = float.MaxValue;

        if (hint >= 0 && hint < segmentCount)
        {
            SearchSegments(worldPosition, Mathf.Max(0, hint - 3), Mathf.Min(segmentCount - 1, hint + 3),
                ref best, ref bestSqr);

            // The hint is only useful while the car is near the line. If it is not, the
            // car was moved (respawn, AI spawn behind the line...) so search everything.
            float stale = Mathf.Max(6f, width * 2f);

            if (bestSqr > stale * stale)
            {
                bestSqr = float.MaxValue;
                SearchSegments(worldPosition, 0, segmentCount - 1, ref best, ref bestSqr);
            }
        }
        else
        {
            SearchSegments(worldPosition, 0, segmentCount - 1, ref best, ref bestSqr);
        }

        return best;
    }

    // ------------------------------------------------------------------
    // Editing (used by the editor tools and the custom inspector)
    // ------------------------------------------------------------------

    public void SetPoints(IEnumerable<Vector3> newPoints)
    {
        points.Clear();

        if (newPoints != null)
            points.AddRange(newPoints);

        Refresh();
    }

    public void SetPoint(int index, Vector3 worldPosition)
    {
        if (index < 0 || index >= points.Count)
            return;

        worldPosition.z = 0f;
        points[index] = worldPosition;
        Refresh();
    }

    public void InsertPoint(int index, Vector3 worldPosition)
    {
        worldPosition.z = 0f;
        points.Insert(Mathf.Clamp(index, 0, points.Count), worldPosition);
        Refresh();
    }

    public void RemovePointAt(int index)
    {
        if (index < 0 || index >= points.Count)
            return;

        points.RemoveAt(index);
        Refresh();
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    void Refresh()
    {
        cacheDirty = true;
        EnsureCache();
        RefreshLine();
    }

    void EnsureCache()
    {
        if (!cacheDirty)
            return;

        cacheDirty = false;

        int count = points.Count;
        segmentCount = count < 2 ? 0 : (isClosed ? count : count - 1);

        if (cumulative.Length != segmentCount + 1)
            cumulative = new float[segmentCount + 1];

        float sum = 0f;

        for (int i = 0; i < segmentCount; i++)
        {
            cumulative[i] = sum;
            sum += Vector3.Distance(PointAt(i), PointAt(i + 1));
        }

        cumulative[segmentCount] = sum;
        totalLength = sum;
    }

    void SearchSegments(Vector3 worldPosition, int from, int to, ref int best, ref float bestSqr)
    {
        for (int i = from; i <= to; i++)
        {
            float distance = SqrDistanceToSegment(worldPosition, i);

            if (distance < bestSqr)
            {
                bestSqr = distance;
                best = i;
            }
        }
    }

    float SqrDistanceToSegment(Vector3 worldPosition, int segment)
    {
        Vector3 a = PointAt(segment);
        Vector3 b = PointAt(segment + 1);
        Vector3 ab = b - a;
        float lengthSqr = ab.sqrMagnitude;
        float t = lengthSqr > 1e-8f ? Mathf.Clamp01(Vector3.Dot(worldPosition - a, ab) / lengthSqr) : 0f;

        return (worldPosition - (a + ab * t)).sqrMagnitude;
    }

    float ProjectToArc(Vector3 worldPosition, int segment, out float t)
    {
        Vector3 a = PointAt(segment);
        Vector3 b = PointAt(segment + 1);
        Vector3 ab = b - a;
        float lengthSqr = ab.sqrMagnitude;

        t = lengthSqr > 1e-8f ? Mathf.Clamp01(Vector3.Dot(worldPosition - a, ab) / lengthSqr) : 0f;

        return cumulative[segment] + t * Mathf.Sqrt(lengthSqr);
    }

    void Locate(float arc, out int segment, out float t)
    {
        int low = 0;
        int high = segmentCount - 1;
        segment = 0;

        while (low <= high)
        {
            int middle = (low + high) / 2;

            if (cumulative[middle] <= arc)
            {
                segment = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        float length = cumulative[segment + 1] - cumulative[segment];
        t = length > 1e-6f ? Mathf.Clamp01((arc - cumulative[segment]) / length) : 0f;
    }

    float CurvatureAt(float arc)
    {
        // Compare the direction of the line here and a little further ahead.
        float span = Mathf.Clamp(totalLength * 0.03f, 0.4f, 2f);
        float angle = Vector3.Angle(TangentAtArc(arc), TangentAtArc(arc + span)) * Mathf.Deg2Rad;

        return angle / span;
    }

    Vector3 TangentAtArc(float arc)
    {
        Locate(WrapArc(arc), out int segment, out float _);
        return Direction(PointAt(segment), PointAt(segment + 1));
    }

    float WrapArc(float arc)
    {
        if (totalLength <= 1e-5f)
            return 0f;

        return isClosed ? Mathf.Repeat(arc, totalLength) : Mathf.Clamp(arc, 0f, totalLength);
    }

    Vector3 PointAt(int index)
    {
        int count = points.Count;

        if (count == 0)
            return transform.position;

        if (!isClosed)
            return points[Mathf.Clamp(index, 0, count - 1)];

        int wrapped = ((index % count) + count) % count;
        return points[wrapped];
    }

    static Vector3 Direction(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        return direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.up;
    }

    void RefreshLine()
    {
        if (line == null)
            line = GetComponent<LineRenderer>();

        if (line == null)
            return;

        line.useWorldSpace = true;
        line.loop = isClosed;
        line.positionCount = points.Count;

        for (int i = 0; i < points.Count; i++)
            line.SetPosition(i, points[i]);

        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.startColor = lineColor;
        line.endColor = lineColor;
        line.sortingLayerName = sortingLayer;
        line.sortingOrder = sortingOrder;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showGizmos || cacheDirty)
            return;

        if (segmentCount == 0)
        {
            Gizmos.color = lineColor;
            foreach (Vector3 point in points)
                Gizmos.DrawWireSphere(point, 0.15f);

            return;
        }

        Gizmos.color = new Color(lineColor.r, lineColor.g, lineColor.b, 0.35f);

        for (int i = 0; i < segmentCount; i++)
            Gizmos.DrawLine(PointAt(i), PointAt(i + 1));

        Gizmos.color = lineColor;

        for (int i = 0; i < points.Count; i++)
            Gizmos.DrawWireSphere(points[i], 0.15f);
    }
#endif
}
