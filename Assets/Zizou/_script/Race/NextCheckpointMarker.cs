using UnityEngine;

/// <summary>
/// Draws a red square on the checkpoint the player has to reach next, and moves it
/// to the following checkpoint as soon as the player passes the current one.
///
/// It follows <see cref="LapTracker.NextCheckpoint"/>, so it always matches the same
/// rule the lap counting uses (checkpoints in order, start line last).
///
/// The square is generated in code, so there is nothing to import and it works in a build.
/// </summary>
[DisallowMultipleComponent]
public class NextCheckpointMarker : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Car to follow. Leave empty to use the player from the RaceManager.")]
    public LapTracker racer;

    [Header("Look")]
    [Tooltip("Colour of the square.")]
    public Color color = new Color(1f, 0.1f, 0.1f, 0.9f);

    [Tooltip("Size of the square in world units.")]
    [Min(0.1f)]
    public float size = 2f;

    [Tooltip("How opaque the inside of the square is (the border is always solid).")]
    [Range(0f, 1f)]
    public float fillAlpha = 0.18f;

    [Header("Feedback")]
    [Tooltip("How much the square pulses. 1 = no pulse.")]
    [Min(1f)]
    public float pulseScale = 1.08f;

    public float pulseSpeed = 3f;

    [Tooltip("How much the square pops when it moves to the next checkpoint.")]
    [Min(1f)]
    public float popScale = 1.4f;

    public float popDuration = 0.25f;

    SpriteRenderer square;
    Sprite generatedSprite;
    Checkpoint currentCheckpoint;
    float popTimer;
    float retryTimer;

    void Awake()
    {
        CreateSquare();
    }

    void OnDestroy()
    {
        if (generatedSprite == null)
            return;

        Texture2D texture = generatedSprite.texture;
        Destroy(generatedSprite);
        Destroy(texture);
    }

    void Update()
    {
        if (racer == null)
        {
            // The LapTracker registers itself in Start, so the player might not exist yet.
            retryTimer -= Time.deltaTime;
            if (retryTimer <= 0f)
            {
                retryTimer = 0.5f;
                racer = FindPlayer();
            }

            if (racer == null)
            {
                SetVisible(false);
                return;
            }
        }

        Checkpoint next = racer.NextCheckpoint;

        if (next == null || racer.IsFinished)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        if (next != currentCheckpoint)
        {
            currentCheckpoint = next;
            popTimer = popDuration;
        }

        transform.position = next.WorldCenter;
        transform.rotation = next.transform.rotation;

        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * (pulseScale - 1f);
        float pop = 1f;

        if (popTimer > 0f)
        {
            pop = Mathf.Lerp(popScale, 1f, 1f - popTimer / popDuration);
            popTimer -= Time.deltaTime;
        }

        transform.localScale = Vector3.one * (size * pulse * pop);
    }

    static LapTracker FindPlayer()
    {
        if (RaceManager.Instance != null)
        {
            LapTracker player = RaceManager.Instance.Player;
            if (player != null)
                return player;
        }

        foreach (LapTracker tracker in FindObjectsByType<LapTracker>(FindObjectsSortMode.None))
        {
            if (tracker.isPlayer)
                return tracker;
        }

        return null;
    }

    void SetVisible(bool visible)
    {
        if (square == null || square.enabled == visible)
            return;

        square.enabled = visible;

        if (!visible)
            currentCheckpoint = null;
    }

    void CreateSquare()
    {
        GameObject squareObject = new GameObject("Square");
        squareObject.transform.SetParent(transform, false);

        square = squareObject.AddComponent<SpriteRenderer>();
        square.sortingLayerName = "Default";
        square.sortingOrder = 50;
        square.enabled = false;

        generatedSprite = BuildSquareSprite(color, fillAlpha);
        square.sprite = generatedSprite;
    }

    // Builds a 1x1 unit sprite: a solid border with a see-through middle.
    static Sprite BuildSquareSprite(Color color, float fillAlpha, int resolution = 64, int border = 5)
    {
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.name = "NextCheckpointSquare";

        Color edge = new Color(color.r, color.g, color.b, color.a);
        Color fill = new Color(color.r, color.g, color.b, color.a * fillAlpha);

        Color[] pixels = new Color[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                bool isBorder = x < border || y < border || x >= resolution - border || y >= resolution - border;
                pixels[y * resolution + x] = isBorder ? edge : fill;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return Sprite.Create(texture, new Rect(0f, 0f, resolution, resolution), new Vector2(0.5f, 0.5f), resolution);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = color;
        Gizmos.DrawWireCube(transform.position, Vector3.one * size);
    }
#endif
}
