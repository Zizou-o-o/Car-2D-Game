using UnityEngine;

/// <summary>
/// Drift, off-road and collision cosmetics for one car:
///   - dark skid lines under the rear wheels while the car is sliding,
///   - white tyre smoke while it drifts,
///   - brown dust behind the rear wheels while it is off the road,
///   - yellow sparks at the contact point when two cars bump into each other,
///   - a small camera shake when the player is part of the hit.
///
/// Everything is built at runtime and the car finds its own wheels, so adding this
/// component to a car is enough. Use
/// Tools > Car2D > FX > Add Drift + Smoke + Dirt To Cars to add it to every car.
///
/// The drift test uses the car's real sideways velocity / slip angle, so the player and
/// the AI cars get exactly the same effect.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TopDown2DCarController))]
public class CarEffects : MonoBehaviour
{
    [Header("Drift detection")]
    [Tooltip("The car has to move at least this fast before it can drift (units/second).")]
    public float minDriftSpeed = 3.5f;

    [Tooltip("Sideways speed that counts as a drift (units/second).")]
    public float driftLateralSpeed = 3.2f;

    [Tooltip("Slip angle that counts as a drift, in degrees.")]
    public float driftSlipAngle = 24f;

    [Header("Skid lines")]
    [Tooltip("How long a skid line stays on the road, in seconds.")]
    public float skidTime = 1.5f;

    [Tooltip("Width of the skid line. The car is about 0.3 units wide.")]
    public float skidWidth = 0.07f;

    public Color skidColor = new Color(0.07f, 0.07f, 0.07f, 0.8f);

    [Header("White tyre smoke")]
    public Color smokeColor = new Color(1f, 1f, 1f, 0.5f);

    [Tooltip("Particles per second, per rear wheel, at full drift.")]
    public float smokeRate = 26f;

    public float smokeSize = 0.2f;
    public float smokeLifetime = 0.7f;

    [Header("Off-road dust")]
    public Color dustColor = new Color(0.55f, 0.44f, 0.3f, 0.9f);

    [Tooltip("Particles per second, per rear wheel, while off the road.")]
    public float dustRate = 34f;

    [Tooltip("The car has to move at least this fast to kick up dust (units/second).")]
    public float minDustSpeed = 1f;

    public float dustSize = 0.16f;
    public float dustLifetime = 0.45f;

    [Header("Collision sparks")]
    [Tooltip("Sparks only fly when the cars hit each other at least this fast (units/second).")]
    public float minSparkImpact = 1.5f;

    [Tooltip("Base number of sparks thrown by a hit. Harder hits throw more.")]
    public int sparkCount = 14;

    [Tooltip("Base speed of a spark (units/second).")]
    public float sparkSpeed = 2.4f;

    [Tooltip("Length of a spark. The car is about 0.64 units long.")]
    public float sparkSize = 0.12f;

    [Tooltip("How long a spark lives, in seconds.")]
    public float sparkLifetime = 0.32f;

    [Tooltip("Minimum time between two spark bursts on the same car, in seconds.")]
    public float sparkCooldown = 0.07f;

    [Tooltip("Hot colour of the sparks. Some of them are pushed towards white.")]
    public Color sparkColor = new Color(1f, 0.72f, 0.18f, 1f);

    [Header("Impact camera shake")]
    [Tooltip("Largest camera shake (world units) when the player hits another car. 0 turns it off.")]
    public float maxHitShake = 0.06f;

    [Tooltip("How long the camera shake lasts, in seconds.")]
    public float hitShakeLength = 0.16f;

    /// <summary>True while the car is sliding on the road (wheels screeching).</summary>
    public bool IsDrifting { get; private set; }

    /// <summary>0..1, how hard the car is drifting. Drives the smoke density.</summary>
    public float DriftAmount { get; private set; }

    TopDown2DCarController car;
    Rigidbody2D body;
    OffRoadSlowdown offRoad;
    SpriteRenderer sprite;

    Transform[] rearWheels;
    ParticleSystem smoke;
    ParticleSystem dust;
    ParticleSystem sparks;

    Vector2 currentVelocity;
    float currentSpeed;
    float smokeEmission;
    float dustEmission;
    float nextSparkTime;
    bool dusting;

    static Material lineMaterial;
    static Material particleMaterial;
    static Material sparkMaterial;
    static Texture2D softDot;
    static Texture2D sparkStreak;

    // Safety net: if the scene was not updated with the menu tool, every car still gets
    // its cosmetics when Play is pressed. Adding the component to the scene is cleaner
    // because the settings are then visible in the inspector.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AddToCarsAutomatically()
    {
        TopDown2DCarController[] cars =
            Object.FindObjectsByType<TopDown2DCarController>(FindObjectsSortMode.None);

        for (int i = 0; i < cars.Length; i++)
        {
            if (cars[i].GetComponent<CarEffects>() == null)
                cars[i].gameObject.AddComponent<CarEffects>();
        }
    }

    void Awake()
    {
        car = GetComponent<TopDown2DCarController>();
        body = GetComponent<Rigidbody2D>();
        offRoad = GetComponent<OffRoadSlowdown>();
        sprite = GetComponentInChildren<SpriteRenderer>();

        CollectRearWheels();
        ConfigureSkidLines();

        if (ParticleMaterial() != null)
        {
            smoke = CreateParticles("Tyre Smoke", 2, false);
            dust = CreateParticles("Off-road Dust", 3, true);

            if (SparkMaterial() != null)
                sparks = CreateSparkParticles();
        }
        else
        {
            Debug.LogWarning("[FX] No sprite shader was found, so the smoke, dust and sparks " +
                             "were not created on \"" + name + "\".", this);
        }
    }

    void OnDisable()
    {
        IsDrifting = false;
        DriftAmount = 0f;
        dusting = false;
        smokeEmission = 0f;
        dustEmission = 0f;
        nextSparkTime = 0f;

        foreach (TrailRenderer trail in GetComponentsInChildren<TrailRenderer>(true))
            trail.emitting = false;

        if (smoke != null)
            smoke.Clear();

        if (dust != null)
            dust.Clear();

        if (sparks != null)
            sparks.Clear();
    }

    void Update()
    {
        UpdateDriftState();

        if (smoke != null)
            EmitSmoke(Time.deltaTime);

        if (dust != null)
            EmitDust(Time.deltaTime);
    }

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------

    void UpdateDriftState()
    {
        currentVelocity = body != null ? body.linearVelocity : Vector2.zero;
        currentSpeed = currentVelocity.magnitude;

        float forwardSpeed = Vector2.Dot(transform.up, currentVelocity);
        float lateralSpeed = Vector2.Dot(transform.right, currentVelocity);
        float slipAngle = Mathf.Atan2(Mathf.Abs(lateralSpeed), Mathf.Max(0.01f, Mathf.Abs(forwardSpeed)))
                          * Mathf.Rad2Deg;

        bool sliding = currentSpeed >= minDriftSpeed &&
                       (Mathf.Abs(lateralSpeed) >= driftLateralSpeed || slipAngle >= driftSlipAngle);

        bool onRoad = offRoad == null || offRoad.IsOnRoad;

        // Off the road the car kicks up dust instead of leaving black lines and smoke.
        IsDrifting = sliding && onRoad;
        dusting = !onRoad && currentSpeed >= minDustSpeed;

        float target = IsDrifting ? Mathf.Clamp01(0.45f + Mathf.Abs(lateralSpeed) / 8f) : 0f;
        DriftAmount = Mathf.MoveTowards(DriftAmount, target, Time.deltaTime * 4f);
    }

    // ------------------------------------------------------------------
    // Particles
    // ------------------------------------------------------------------

    void EmitSmoke(float deltaTime)
    {
        if (!IsDrifting || DriftAmount <= 0.02f)
        {
            smokeEmission = 0f;
            return;
        }

        smokeEmission += smokeRate * DriftAmount * rearWheels.Length * deltaTime;

        int count = Mathf.Min(6, Mathf.FloorToInt(smokeEmission));
        smokeEmission -= count;

        Vector2 backwards = currentVelocity.sqrMagnitude > 0.01f
            ? -currentVelocity.normalized
            : Vector2.down;

        for (int i = 0; i < count; i++)
        {
            Color colour = smokeColor;
            colour.a *= Random.Range(0.7f, 1f);

            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
            {
                position = WheelPosition() + (Vector3)(Random.insideUnitCircle * 0.05f),
                velocity = backwards * Random.Range(0.3f, 1.1f) + Random.insideUnitCircle * 0.8f,
                startColor = colour,
                startSize = smokeSize * Random.Range(0.7f, 1.25f),
                startLifetime = smokeLifetime * Random.Range(0.75f, 1.35f),
                rotation = Random.Range(0f, 360f),
                angularVelocity = Random.Range(-60f, 60f)
            };

            smoke.Emit(emit, 1);
        }
    }

    void EmitDust(float deltaTime)
    {
        if (!dusting)
        {
            dustEmission = 0f;
            return;
        }

        float intensity = Mathf.Clamp01(currentSpeed / 8f);
        dustEmission += dustRate * (0.35f + 0.65f * intensity) * rearWheels.Length * deltaTime;

        int count = Mathf.Min(6, Mathf.FloorToInt(dustEmission));
        dustEmission -= count;

        Vector2 backwards = currentVelocity.sqrMagnitude > 0.01f
            ? -currentVelocity.normalized
            : Vector2.down;

        for (int i = 0; i < count; i++)
        {
            Color colour = dustColor;
            colour.a *= Random.Range(0.65f, 1f);

            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
            {
                position = WheelPosition() + (Vector3)(Random.insideUnitCircle * 0.05f),
                velocity = backwards * Random.Range(1.2f, 2.6f) + Random.insideUnitCircle * 1.1f,
                startColor = colour,
                startSize = dustSize * Random.Range(0.6f, 1.3f),
                startLifetime = dustLifetime * Random.Range(0.7f, 1.3f),
                rotation = Random.Range(0f, 360f),
                angularVelocity = Random.Range(-90f, 90f)
            };

            dust.Emit(emit, 1);
        }
    }

    Vector3 WheelPosition()
    {
        return rearWheels[Random.Range(0, rearWheels.Length)].position;
    }

    // ------------------------------------------------------------------
    // Collision sparks
    // ------------------------------------------------------------------

    void OnCollisionEnter2D(Collision2D collision)
    {
        SprinkleSparks(collision, 1f, 1f, sparkCooldown);
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        // Two cars rubbing along each other while cornering: now and then a few
        // sparks, but only if they are really scraping (higher threshold, lower rate).
        SprinkleSparks(collision, 0.5f, 2f, sparkCooldown + 0.09f);
    }

    void SprinkleSparks(Collision2D collision, float intensity, float thresholdScale, float cooldown)
    {
        if (sparks == null || Time.time < nextSparkTime)
            return;

        CarEffects other = collision.collider != null
            ? collision.collider.GetComponentInParent<CarEffects>()
            : null;

        // Only car-on-car hits throw sparks. Walls and the road stay quiet.
        if (other == null)
        {
            Rigidbody2D otherBody = collision.rigidbody;

            if (otherBody == null || otherBody.GetComponent<TopDown2DCarController>() == null)
                return;
        }

        // Both cars receive the callback for the same contact, so let only the car
        // with the lower id emit the burst. That keeps one hit from doubling the sparks.
        if (other != null && other.GetInstanceID() < GetInstanceID())
            return;

        Vector2 relativeVelocity = collision.relativeVelocity;
        float impact = relativeVelocity.magnitude;

        if (impact < minSparkImpact * thresholdScale)
            return;

        // A small camera kick when the player is one of the two cars. Only on the first
        // contact (not while they keep scraping), so pushing a car does not rumble forever.
        if (intensity >= 0.9f && maxHitShake > 0f &&
            (IsPlayerCar(this) || (other != null && IsPlayerCar(other))))
        {
            float strength = Mathf.Clamp01(impact / 6f);
            CameraLogic.ShakeCamera(Mathf.Lerp(0.015f, maxHitShake, strength), hitShakeLength);
        }

        Vector2 point = transform.position;
        Vector2 normal = relativeVelocity.sqrMagnitude > 0.001f ? -relativeVelocity.normalized : Vector2.up;

        if (collision.contactCount > 0)
        {
            ContactPoint2D contact = collision.GetContact(0);
            point = contact.point;
            normal = contact.normal;
        }
        else if (collision.rigidbody != null)
        {
            // No contact data: drop the burst halfway between the two cars.
            point = ((Vector2)transform.position + collision.rigidbody.position) * 0.5f;
        }

        nextSparkTime = Time.time + cooldown;
        EmitSparks(point, normal, relativeVelocity, impact, intensity);
    }

    void EmitSparks(Vector2 point, Vector2 normal, Vector2 relativeVelocity, float impact,
                    float intensity)
    {
        Vector2 travel = body != null ? body.linearVelocity : Vector2.zero;

        // Sparks are squeezed out sideways from the contact: mostly backwards along the
        // impact, pushed a little away from the other car.
        Vector2 direction = relativeVelocity.sqrMagnitude > 0.001f
            ? Vector2.Lerp(-relativeVelocity.normalized, normal, 0.35f)
            : normal;

        if (direction.sqrMagnitude < 0.001f)
            direction = normal.sqrMagnitude > 0.001f ? normal : Vector2.up;

        direction.Normalize();

        float strength = Mathf.Clamp01(impact / 6f);
        int count = Mathf.Clamp(
            Mathf.RoundToInt(sparkCount * intensity * Mathf.Lerp(0.7f, 1.5f, strength)), 2, 40);

        for (int i = 0; i < count; i++)
        {
            Vector2 spray = Rotate(direction, Random.Range(-65f, 65f));

            Color colour = Color.Lerp(sparkColor, Color.white, Random.value * 0.6f);
            colour.a *= Random.Range(0.75f, 1f);

            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
            {
                position = (Vector3)point + (Vector3)(Random.insideUnitCircle * 0.03f),
                velocity = spray * (sparkSpeed * Mathf.Lerp(0.65f, 1.5f, strength) *
                                    Random.Range(0.55f, 1.25f)) + travel * 0.25f,
                startColor = colour,
                startSize = sparkSize * Random.Range(0.6f, 1.4f),
                startLifetime = sparkLifetime * Random.Range(0.6f, 1.5f),
                rotation = Mathf.Atan2(spray.y, spray.x) * Mathf.Rad2Deg,
                angularVelocity = 0f
            };

            sparks.Emit(emit, 1);
        }

        // A short, hot flash right where the two bumpers meet.
        if (intensity >= 0.9f)
        {
            ParticleSystem.EmitParams flash = new ParticleSystem.EmitParams
            {
                position = point,
                velocity = direction * 0.4f + travel * 0.2f,
                startColor = new Color(1f, 0.96f, 0.82f, 0.85f),
                startSize = sparkSize * 2.8f,
                startLifetime = 0.09f,
                rotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg,
                angularVelocity = 0f
            };

            sparks.Emit(flash, 1);
        }
    }

    static Vector2 Rotate(Vector2 vector, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);

        return new Vector2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos);
    }

    // The player is the car with CarInput, or the car the camera is following.
    static bool IsPlayerCar(CarEffects effects)
    {
        if (effects == null)
            return false;

        if (effects.GetComponent<CarInput>() != null)
            return true;

        CameraLogic camera = CameraLogic.Instance;

        return camera != null && camera.IsFollowing(effects.body);
    }

    ParticleSystem CreateParticles(string name, int sortingOffset, bool dustStyle)
    {
        ParticleSystem particles = CreateParticleHolder(name, sortingOffset, ParticleMaterial());

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;

        // Smoke puffs grow as they drift away, dust shrinks as it settles.
        size.size = new ParticleSystem.MinMaxCurve(1f, dustStyle
            ? new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f))
            : new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 2f)));

        ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
        colour.enabled = true;

        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.6f, 0.3f),
                new GradientAlphaKey(0f, 1f)
            });
        colour.color = new ParticleSystem.MinMaxGradient(fade);

        return particles;
    }

    ParticleSystem CreateSparkParticles()
    {
        ParticleSystem particles = CreateParticleHolder("Collision Sparks", 4, SparkMaterial());

        ParticleSystem.MainModule main = particles.main;
        main.gravityModifier = 0.7f;
        main.maxParticles = 220;

        // Sparks shrink and fall away almost immediately.
        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.4f, 0.6f),
            new Keyframe(1f, 0.05f)));

        // White-hot, then orange, then out.
        ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
        colour.enabled = true;

        Gradient heat = new Gradient();
        heat.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, 0.82f, 0.4f), 0.35f),
                new GradientColorKey(new Color(1f, 0.32f, 0.04f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.8f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });
        colour.color = new ParticleSystem.MinMaxGradient(heat);

        return particles;
    }

    // Shared setup for every particle system on the car: world space, emitter driven
    // by hand (Emit calls), sorting just above the car sprite.
    ParticleSystem CreateParticleHolder(string name, int sortingOffset, Material material)
    {
        GameObject holder = new GameObject(name);
        holder.transform.SetParent(transform, false);

        ParticleSystem particles = holder.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startLifetime = 1f;
        main.startSize = 1f;
        main.startColor = Color.white;
        main.gravityModifier = 0f;
        main.maxParticles = 300;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = false;

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.sortingLayerID = sprite != null ? sprite.sortingLayerID : 0;
        renderer.sortingOrder = sprite != null ? sprite.sortingOrder + sortingOffset : sortingOffset;

        particles.Play();
        return particles;
    }

    // ------------------------------------------------------------------
    // Wheels, trails and materials
    // ------------------------------------------------------------------

    // The cars have a TrailRenderer on each rear wheel, so those transforms are the
    // exact emission points. If a car has none, two points behind the collider are used.
    void CollectRearWheels()
    {
        TrailRenderer[] trails = GetComponentsInChildren<TrailRenderer>(true);

        if (trails.Length > 0)
        {
            rearWheels = new Transform[trails.Length];

            for (int i = 0; i < trails.Length; i++)
                rearWheels[i] = trails[i].transform;

            return;
        }

        Vector2 halfSize = new Vector2(0.15f, 0.32f);
        Vector2 centre = Vector2.zero;

        BoxCollider2D box = GetComponent<BoxCollider2D>();

        if (box != null)
        {
            halfSize = box.size * 0.5f;
            centre = box.offset;
        }
        else if (sprite != null && sprite.sprite != null)
        {
            halfSize = sprite.sprite.bounds.extents;
        }

        rearWheels = new Transform[2];

        for (int i = 0; i < rearWheels.Length; i++)
        {
            GameObject wheel = new GameObject("Rear Wheel FX " + (i + 1));
            wheel.transform.SetParent(transform, false);
            wheel.transform.localPosition = centre + new Vector2(
                i == 0 ? -halfSize.x * 0.55f : halfSize.x * 0.55f,
                -halfSize.y * 0.8f);

            rearWheels[i] = wheel.transform;
        }
    }

    void ConfigureSkidLines()
    {
        Material line = LineMaterial();

        foreach (TrailRenderer trail in GetComponentsInChildren<TrailRenderer>(true))
        {
            trail.time = skidTime;
            trail.widthMultiplier = skidWidth;
            trail.minVertexDistance = 0.04f;
            trail.numCapVertices = 2;
            trail.numCornerVertices = 2;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;

            if (line != null)
                trail.sharedMaterial = line;

            trail.startColor = skidColor;
            trail.endColor = new Color(skidColor.r, skidColor.g, skidColor.b, 0f);

            // Every wheel trail should follow CarEffects.IsDrifting. The cars that come
            // with the scene already have this component; new wheels get it here.
            if (trail.GetComponent<WeelTrailRanderer>() == null)
                trail.gameObject.AddComponent<WeelTrailRanderer>();
        }
    }

    static Material LineMaterial()
    {
        if (lineMaterial == null)
            lineMaterial = CreateMaterial(Texture2D.whiteTexture);

        return lineMaterial;
    }

    static Material ParticleMaterial()
    {
        if (particleMaterial == null)
            particleMaterial = CreateMaterial(SoftDot());

        return particleMaterial;
    }

    static Material SparkMaterial()
    {
        if (sparkMaterial == null)
            sparkMaterial = CreateMaterial(SparkStreak());

        return sparkMaterial;
    }

    // Sprites/Default is the most portable choice here: it is a simple transparent,
    // vertex-coloured shader that renders in URP 2D (as SRPDefaultUnlit) and works for
    // trail meshes and billboard particles alike. The URP 2D sprite shader is the
    // fallback when it is missing.
    static Material CreateMaterial(Texture texture)
    {
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");

        if (shader == null)
            return null;

        Material material = new Material(shader);
        material.hideFlags = HideFlags.DontSave;
        material.mainTexture = texture;
        material.color = Color.white;
        return material;
    }

    static Texture2D SoftDot()
    {
        if (softDot != null)
            return softDot;

        const int size = 64;
        softDot = new Texture2D(size, size, TextureFormat.RGBA32, false);
        softDot.hideFlags = HideFlags.DontSave;
        softDot.wrapMode = TextureWrapMode.Clamp;
        softDot.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        Vector2 centre = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), centre) / radius;
                float alpha = Mathf.Clamp01(1f - distance);
                alpha = alpha * alpha * (3f - 2f * alpha);

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        softDot.SetPixels(pixels);
        softDot.Apply();
        return softDot;
    }

    // A thin horizontal streak, so a spark reads as a flying ember instead of a dot.
    // The particle rotation is set to the spray direction when it is emitted.
    static Texture2D SparkStreak()
    {
        if (sparkStreak != null)
            return sparkStreak;

        const int size = 64;
        sparkStreak = new Texture2D(size, size, TextureFormat.RGBA32, false);
        sparkStreak.hideFlags = HideFlags.DontSave;
        sparkStreak.wrapMode = TextureWrapMode.Clamp;
        sparkStreak.filterMode = FilterMode.Bilinear;

        float halfLength = size * 0.30f;
        float radius = size * 0.12f;

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - (size - 1) * 0.5f;
                float dy = y - (size - 1) * 0.5f;

                // Distance to the horizontal segment in the middle of the texture.
                float closest = Mathf.Clamp(dx, -halfLength, halfLength);
                float distance = new Vector2(dx - closest, dy).magnitude / radius;

                float alpha = Mathf.Clamp01(1f - distance);
                alpha = alpha * alpha * (3f - 2f * alpha);

                // Hotter, thinner core along the middle of the streak.
                float core = Mathf.Clamp01(1f - Mathf.Abs(dy) / (radius * 0.6f));
                core *= core;
                alpha = Mathf.Max(alpha, core * 0.95f);

                // Round the two tips off so the spark does not look like a bar.
                alpha *= Mathf.Clamp01(1f - Mathf.Pow(
                    Mathf.Abs(dx) / (halfLength + radius), 8f));

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        sparkStreak.SetPixels(pixels);
        sparkStreak.Apply();
        return sparkStreak;
    }
}
