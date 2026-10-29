using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A checkpoint the cars have to pass, drawn as a red square in the Scene view.
///
/// Place several of them around the track and give them the order you want the cars
/// to pass them in (<see cref="index"/> 1, 2, 3...). Mark exactly one of them as
/// <see cref="isStart"/>: that is the start/finish line used to count laps.
///
/// The square is the BoxCollider2D on this object. Drag the object to move it, and
/// resize it with the BoxCollider2D's green handles in the Scene view.
///
/// IMPORTANT: keep checkpoints OFF the "Road" layer. That layer is used by
/// OffRoadSlowdown, so a checkpoint there would count as drivable road.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
[DisallowMultipleComponent]
public class Checkpoint : MonoBehaviour
{
    [Tooltip("Order the cars must pass this checkpoint in a lap. The start/finish line is 0.")]
    [Min(0)]
    public int index;

    [Tooltip("Tick this on ONE checkpoint: the start/finish line used to count laps.")]
    public bool isStart;

    [Tooltip("Colour of the square in the Scene view.")]
    public Color gizmoColor = new Color(1f, 0.15f, 0.15f, 0.3f);

    BoxCollider2D box;

    /// <summary>Centre of the checkpoint in world space (used by the next-checkpoint marker).</summary>
    public Vector3 WorldCenter
    {
        get
        {
            if (box == null)
                box = GetComponent<BoxCollider2D>();

            if (box == null)
                return transform.position;

            return transform.TransformPoint((Vector3)box.offset);
        }
    }

    void Reset()
    {
        box = GetComponent<BoxCollider2D>();
        box.isTrigger = true;
        // Wide enough to cover a road, thin enough to be a line the car has to cross.
        // Drag the green BoxCollider2D handles (or the size fields) to fit your track.
        box.size = new Vector2(6f, 2f);
    }

    void OnValidate()
    {
        // A checkpoint must never push the car around, so it is always a trigger.
        box = GetComponent<BoxCollider2D>();
        if (box != null)
            box.isTrigger = true;
    }

    void Awake()
    {
        box = GetComponent<BoxCollider2D>();
        box.isTrigger = true;

        if (gameObject.layer == LayerMask.NameToLayer("Road"))
        {
            Debug.LogWarning("[Race] Checkpoint \"" + name + "\" is on the Road layer. " +
                             "OffRoadSlowdown will treat it as road - move it to another layer.", this);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        // The collider that entered might be a child of the car, so look upwards.
        LapTracker racer = other.GetComponentInParent<LapTracker>();
        if (racer != null)
            racer.PassCheckpoint(this);
    }

#if UNITY_EDITOR
    static GUIStyle labelStyle;

    void OnDrawGizmos()
    {
        BoxCollider2D boxCollider = GetComponent<BoxCollider2D>();
        if (boxCollider == null)
            return;

        Color fill = isStart ? new Color(1f, 0.85f, 0.1f, 0.3f) : gizmoColor;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = fill;
        Gizmos.DrawCube(boxCollider.offset, boxCollider.size);
        Gizmos.color = new Color(fill.r, fill.g, fill.b, 1f);
        Gizmos.DrawWireCube(boxCollider.offset, boxCollider.size);
        Gizmos.matrix = Matrix4x4.identity;

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            labelStyle.normal.textColor = Color.white;
        }

        Vector3 labelPosition = transform.TransformPoint(
            (Vector3)boxCollider.offset + new Vector3(0f, boxCollider.size.y * 0.5f + 0.4f, 0f));

        Handles.Label(labelPosition, isStart ? "START / FINISH" : "CP " + index, labelStyle);
    }
#endif
}
