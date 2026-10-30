using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space "sense of speed" for the player car:
///   - pale wind streaks that rush past the car in the world (they are anchored to
///     the track, not to the screen, so they slide along the car's flanks),
///   - a dark border that closes in around the view to show focus.
///
/// Both fade in as the car approaches its top speed and disappear again when it slows
/// down. The border uses its own Screen Space Overlay canvas, so the HUD drawn with
/// OnGUI (the race standings) still renders on top of it.
///
/// The component only has to be dropped on the player car; it is added automatically
/// when Play starts if the scene does not have it yet.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TopDown2DCarController))]
public class SpeedEffects : MonoBehaviour
{
    [Header("When it appears (fraction of the car's top speed)")]
    [Range(0f, 1f)]
    [Tooltip("Speed where the effect starts to show. 0.5 = half of the car's max speed.")]
    public float showAt = 0.5f;

    [Range(0f, 1f)]
    [Tooltip("Speed where the effect is at full strength.")]
    public float fullAt = 0.95f;

    [Tooltip("How quickly the effect follows speed changes.")]
    public float response = 3.5f;

    [Header("Wind around the car")]
    public Color windColor = new Color(0.92f, 0.96f, 1f, 1f);

    [Range(0f, 1f)]
    [Tooltip("Opacity of a wind streak at full speed.")]
    public float windAlpha = 0.5f;

    [Tooltip("Wind streaks spawned per second at full speed.")]
    public float windParticlesPerSecond = 90f;

    [Tooltip("Length of a wind streak. The car is about 0.64 units long.")]
    public float windSize = 0.45f;

    [Tooltip("How long a wind streak lives, in seconds.")]
    public float windLifetime = 0.32f;

    [Header("Focus border")]
    public Color borderColor = Color.black;

    [Range(0f, 1f)]
    [Tooltip("Opacity of the dark border at full speed.")]
    public float borderAlpha = 0.7f;

    TopDown2DCarController car;
    Rigidbody2D body;
    SpriteRenderer sprite;

    Canvas canvas;
    RawImage borderSoft;
    RawImage borderFocus;

    ParticleSystem wind;
    float strength;
    float windEmission;

    static Texture2D softVignette;
    static Texture2D focusVignette;
    static Texture2D windStreak;
    static Material windMaterial;

    // If the scene was not updated with the menu tool, the player still gets the effect
    // when Play is pressed. Only the car with CarInput is treated as the player.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AddToPlayer()
    {
        CarInput[] players = Object.FindObjectsByType<CarInput>(FindObjectsSortMode.None);

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].GetComponent<SpeedEffects>() == null)
                players[i].gameObject.AddComponent<SpeedEffects>();
        }
    }

    void Awake()
    {
        car = GetComponent<TopDown2DCarController>();
        body = GetComponent<Rigidbody2D>();
        sprite = GetComponentInChildren<SpriteRenderer>();

        if (GetComponent<CarInput>() == null)
        {
            Debug.LogWarning("[FX] SpeedEffects is meant for the player car, and \"" + name +
                             "\" has no CarInput, so the speed overlay is disabled.", this);
            enabled = false;
            return;
        }

        CreateWind();
        BuildBorder();
    }

    void OnEnable()
    {
        if (canvas != null)
            canvas.enabled = true;
    }

    void OnDisable()
    {
        strength = 0f;
        windEmission = 0f;

        if (wind != null)
            wind.Clear();

        if (canvas != null)
            canvas.enabled = false;
    }

    void Update()
    {
        if (car == null)
            return;

        float speed = body != null ? body.linearVelocity.magnitude : 0f;
        float topSpeed = Mathf.Max(0.01f, car.maxSpeed);

        float target = Mathf.InverseLerp(showAt * topSpeed, fullAt * topSpeed, speed);
        strength = Mathf.MoveTowards(strength, target, response * Time.deltaTime);

        EmitWind(speed);
        AnimateBorder();
    }

    // ------------------------------------------------------------------
    // Wind (world-space streaks around the car)
    // ------------------------------------------------------------------

    void CreateWind()
    {
        Material material = WindMaterial();

        if (material == null)
        {
            Debug.LogWarning("[FX] No sprite shader was found, so the speed wind was not " +
                             "created on \"" + name + "\".", this);
            return;
        }

        GameObject holder = new GameObject("Speed Wind");
        holder.transform.SetParent(transform, false);

        wind = holder.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = wind.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startLifetime = 1f;
        main.startSize = 1f;
        main.startColor = Color.white;
        main.gravityModifier = 0f;
        main.maxParticles = 120;

        ParticleSystem.EmissionModule emission = wind.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = wind.shape;
        shape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule size = wind.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(1f, 0.7f)));

        ParticleSystem.ColorOverLifetimeModule colour = wind.colorOverLifetime;
        colour.enabled = true;

        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.18f),
                new GradientAlphaKey(1f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });
        colour.color = new ParticleSystem.MinMaxGradient(fade);

        ParticleSystemRenderer renderer = wind.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.sortingLayerID = sprite != null ? sprite.sortingLayerID : 0;
        renderer.sortingOrder = sprite != null ? sprite.sortingOrder + 3 : 3;

        wind.Play();
    }

    void EmitWind(float speed)
    {
        if (wind == null)
            return;

        if (strength <= 0.03f || speed < 0.5f)
        {
            windEmission = 0f;
            return;
        }

        windEmission += windParticlesPerSecond * strength * Time.deltaTime;

        int count = Mathf.Min(5, Mathf.FloorToInt(windEmission));
        windEmission -= count;

        if (count <= 0)
            return;

        // The streaks move slower than the car, so in world space they are left behind
        // and slide backwards along the car's flanks.
        Vector2 velocity = body != null ? body.linearVelocity : Vector2.zero;
        float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;

        for (int i = 0; i < count; i++)
        {
            float side = Random.value < 0.5f ? -1f : 1f;

            Vector3 local = new Vector3(
                side * Random.Range(0.30f, 1.10f),
                Random.Range(-0.7f, 1.5f),
                0f);

            Color colour = windColor;
            colour.a = windAlpha * Mathf.Lerp(0.45f, 1f, strength) * Random.Range(0.65f, 1f);

            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
            {
                position = transform.TransformPoint(local),
                velocity = velocity * Random.Range(0.55f, 0.85f) + Random.insideUnitCircle * 0.35f,
                startColor = colour,
                startSize = windSize * Random.Range(0.7f, 1.35f),
                startLifetime = windLifetime * Random.Range(0.7f, 1.3f),
                rotation = angle,
                angularVelocity = 0f
            };

            wind.Emit(emit, 1);
        }
    }

    // ------------------------------------------------------------------
    // Focus border (screen space)
    // ------------------------------------------------------------------

    void BuildBorder()
    {
        GameObject holder = new GameObject("Focus Border Overlay");
        holder.transform.SetParent(transform, false);

        canvas = holder.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;

        // A wide, soft vignette plus a tighter one that only shows up at high speed.
        borderSoft = CreateBorder("Focus Border Soft", SoftVignette());
        borderFocus = CreateBorder("Focus Border", FocusVignette());
    }

    RawImage CreateBorder(string layerName, Texture texture)
    {
        GameObject go = new GameObject(layerName, typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);

        RawImage image = go.AddComponent<RawImage>();
        image.texture = texture;
        image.color = new Color(borderColor.r, borderColor.g, borderColor.b, 0f);
        image.raycastTarget = false;
        image.enabled = false;

        RectTransform rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        return image;
    }

    void AnimateBorder()
    {
        if (borderSoft != null)
        {
            float alpha = borderAlpha * 0.5f * strength;
            borderSoft.color = new Color(borderColor.r, borderColor.g, borderColor.b, alpha);
            borderSoft.enabled = alpha > 0.002f;
        }

        if (borderFocus != null)
        {
            // The tight border only comes in for the last half of the speed range.
            float focus = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, strength));
            float alpha = borderAlpha * focus;
            borderFocus.color = new Color(borderColor.r, borderColor.g, borderColor.b, alpha);
            borderFocus.enabled = alpha > 0.002f;
        }
    }

    // ------------------------------------------------------------------
    // Generated textures and material
    // ------------------------------------------------------------------

    static Texture2D NewTexture(int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.hideFlags = HideFlags.DontSave;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        return texture;
    }

    static Material WindMaterial()
    {
        if (windMaterial != null)
            return windMaterial;

        // Same portable sprite shader as the car effects: it renders in URP 2D and works
        // for billboard particles.
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");

        if (shader == null)
            return null;

        windMaterial = new Material(shader);
        windMaterial.hideFlags = HideFlags.DontSave;
        windMaterial.mainTexture = WindStreak();
        windMaterial.color = Color.white;
        return windMaterial;
    }

    // A soft, thin horizontal streak. The particle rotation is set to the car's heading,
    // so the streak lies along the direction of travel.
    static Texture2D WindStreak()
    {
        if (windStreak != null)
            return windStreak;

        const int size = 64;
        windStreak = NewTexture(size, size);

        float halfLength = size * 0.34f;
        float radius = size * 0.06f;
        float c = (size - 1) * 0.5f;

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c;
                float dy = y - c;

                float closest = Mathf.Clamp(dx, -halfLength, halfLength);
                float distance = new Vector2(dx - closest, dy).magnitude / radius;

                float alpha = Mathf.Clamp01(1f - distance);
                alpha *= alpha;

                // Soft, rounded ends instead of a hard bar.
                alpha *= Mathf.Clamp01(1f - Mathf.Pow(
                    Mathf.Abs(dx) / (halfLength + radius), 3f));

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        windStreak.SetPixels(pixels);
        windStreak.Apply();
        return windStreak;
    }

    // Dark border that starts at the edge of the view.
    static Texture2D SoftVignette()
    {
        if (softVignette == null)
            softVignette = MakeVignette(0.60f, 1.45f);

        return softVignette;
    }

    // Dark border that reaches much further in, used at high speed.
    static Texture2D FocusVignette()
    {
        if (focusVignette == null)
            focusVignette = MakeVignette(0.45f, 1.15f);

        return focusVignette;
    }

    static Texture2D MakeVignette(float inner, float outer)
    {
        const int size = 256;
        Texture2D texture = NewTexture(size, size);

        Color[] pixels = new Color[size * size];
        float c = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            float ny = (y - c) / c;

            for (int x = 0; x < size; x++)
            {
                float nx = (x - c) / c;
                float radius = Mathf.Sqrt(nx * nx + ny * ny);

                float alpha = Mathf.SmoothStep(
                    0f, 1f, Mathf.InverseLerp(inner, outer, radius));

                pixels[y * size + x] = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
}
