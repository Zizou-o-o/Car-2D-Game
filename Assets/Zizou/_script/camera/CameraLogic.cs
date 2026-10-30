using UnityEngine;

/// <summary>
/// Follows the player car and can add a small positional shake on impacts.
///
/// The shake is applied on top of the smoothed follow position, so a bump never
/// drags the camera away from the car and it settles back by itself.
/// Other scripts can call <see cref="ShakeCamera"/> without holding a reference.
/// </summary>
public class CameraLogic : MonoBehaviour
{
    public Transform target;
    public float smoothSpeed = 0.2f;
    public Vector3 offset = new Vector3(0, 10, -10) ;
    public float lookaheadDistance = 1.5f;
    public Rigidbody2D carRigidbody2D;

    [Header("Impact shake")]
    [Tooltip("Default length of a shake, in seconds. Shorter = snappier.")]
    public float shakeDuration = 0.16f;

    static CameraLogic instance;

    Vector3 followPosition;
    float shakeAmplitude;
    float shakeTime;
    float shakeLength;

    /// <summary>The camera that follows the player, if there is one.</summary>
    public static CameraLogic Instance => instance;

    void Awake()
    {
        instance = this;
        followPosition = transform.position;
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    /// <summary>True when this camera is following the given car.</summary>
    public bool IsFollowing(Rigidbody2D carBody)
    {
        return carBody != null && carRigidbody2D == carBody;
    }

    /// <summary>Starts a short shake. Amplitude is in world units.</summary>
    public void Shake(float amplitude, float length = -1f)
    {
        if (amplitude <= 0f)
            return;

        if (length <= 0f)
            length = shakeDuration;

        shakeAmplitude = Mathf.Max(shakeAmplitude, amplitude);
        shakeLength = Mathf.Max(shakeLength, length);
        shakeTime = Mathf.Max(shakeTime, length);
    }

    /// <summary>Shakes the player camera, callable from any gameplay script.</summary>
    public static void ShakeCamera(float amplitude, float length = -1f)
    {
        CameraLogic camera = instance;

        if (camera == null)
        {
            Camera main = Camera.main;

            if (main != null)
                camera = main.GetComponent<CameraLogic>();
        }

        if (camera != null)
            camera.Shake(amplitude, length);
    }

    private void LateUpdate()
    {
        if (target == null || carRigidbody2D == null)
            return;

        Vector3 lookahead = carRigidbody2D.linearVelocity.normalized * lookaheadDistance;
        Vector3 desiredPosition = target.position + offset + lookahead;

        // Smoothing and shaking are kept apart: the shake is added after the lerp so it
        // cannot push the follow point around.
        followPosition = Vector3.Lerp(followPosition, desiredPosition, smoothSpeed);

        Vector3 position = followPosition;

        if (shakeTime > 0f)
        {
            shakeTime = Mathf.Max(0f, shakeTime - Time.deltaTime);

            float falloff = shakeLength > 0f ? shakeTime / shakeLength : 0f;
            float amount = shakeAmplitude * falloff * falloff;

            if (amount > 0f)
            {
                Vector2 jitter = Random.insideUnitCircle * amount;
                position += transform.right * jitter.x + transform.up * jitter.y;
            }
            else
            {
                shakeAmplitude = 0f;
                shakeLength = 0f;
            }
        }

        transform.position = position;
    }
}
