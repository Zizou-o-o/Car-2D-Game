using UnityEngine;

public class TopDown2DCarController : MonoBehaviour
{ 
    [Header ("Car settings")]
    public float acceleratioFactor = 30.0f;
    public float turnFacter = 3.5f;
    public float driftFactor = 0.40f;
    public float maxSpeed = 20f;

    [Header ("Surface")]
    [Range (0f, 1f)]
    [Tooltip ("1 = full speed (on road). OffRoadSlowdown lowers this while the car is off the road.")]
    public float surfaceSpeedMultiplier = 1f;

    [Tooltip ("How fast the car sheds speed above the current max (units per second). Higher = the off-road penalty kicks in harder.")]
    public float speedLossRate = 12f;

    [Tooltip ("Extra speed cap in units/second, used by AI drivers to slow down for corners. 0 = no extra cap.")]
    public float speedLimit = 0f;


    float accelerationInput = 0;
    float turnInput = 0;
    float rotationAngle = 0;

    float velocityVsup = 0;

    Rigidbody2D carRigidbody2D;

    private void FixedUpdate()
    {
        ApplyEngineForce();
        KillOrthogonalVelocity();
        ApplySteering();
        LimitToSurfaceSpeed();
    }

    // The max speed the car is allowed to reach on the surface it is currently on.
    public float CurrentMaxSpeed
    {
        get
        {
            float currentMaxSpeed = maxSpeed * surfaceSpeedMultiplier;

            if (speedLimit > 0f)
                currentMaxSpeed = Mathf.Min(currentMaxSpeed, speedLimit);

            return currentMaxSpeed;
        }
    }
   

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        carRigidbody2D = GetComponent<Rigidbody2D>();
    }

    void ApplyEngineForce () 
    {

        velocityVsup = Vector2.Dot(transform.up, carRigidbody2D.linearVelocity);

        float currentMaxSpeed = CurrentMaxSpeed;

        if (velocityVsup > currentMaxSpeed && accelerationInput == 0)
            return;

        if (velocityVsup < -currentMaxSpeed * 0.5f && accelerationInput == 0)
            return;
        if(carRigidbody2D.linearVelocity.sqrMagnitude > currentMaxSpeed * currentMaxSpeed && accelerationInput >0)
            return;

        if (accelerationInput == 0)
            carRigidbody2D.linearDamping = Mathf.Lerp(carRigidbody2D.linearDamping, 3.0f, Time.fixedDeltaTime * 3);
        else carRigidbody2D.linearDamping = 0;


        Vector2 engineForceVector = transform.up * accelerationInput * acceleratioFactor;

        carRigidbody2D.AddForce (engineForceVector, ForceMode2D.Force);
        
    }

    void ApplySteering()
    {

        float minSpeedForSteering = (carRigidbody2D.linearVelocity.magnitude / 8);
        minSpeedForSteering = Mathf.Clamp01(minSpeedForSteering);

        rotationAngle  -= turnInput * turnFacter * minSpeedForSteering ;

        carRigidbody2D.MoveRotation (rotationAngle);
    }
    float GetlateralVelocity()
    {
        return Vector2.Dot(transform.right, carRigidbody2D.linearVelocity);
    }
    

    void KillOrthogonalVelocity()
    {
        Vector2 forwardVelocity = transform.up * Vector2.Dot(carRigidbody2D.linearVelocity, transform.up);
        Vector2 rightVelocity = transform.right * Vector2.Dot(carRigidbody2D.linearVelocity, transform.right);

        carRigidbody2D.linearVelocity = forwardVelocity + rightVelocity * driftFactor ;

    }
    void LimitToSurfaceSpeed ()
    {
        // On the road with no extra cap the car keeps the original handling, so leave it alone.
        if (surfaceSpeedMultiplier >= 0.999f && speedLimit <= 0f)
            return;

        // If the car is going faster than the surface allows (for example when it
        // just drove off the road), bleed the extra speed off smoothly instead of
        // letting the car keep its momentum.
        float currentMaxSpeed = CurrentMaxSpeed;
        Vector2 velocity = carRigidbody2D.linearVelocity;

        if (velocity.sqrMagnitude <= currentMaxSpeed * currentMaxSpeed)
            return;

        float speed = Mathf.MoveTowards(velocity.magnitude, currentMaxSpeed, speedLossRate * Time.fixedDeltaTime);
        carRigidbody2D.linearVelocity = velocity.normalized * speed;
    }

    public void SetInputVector (Vector2 inputVector)
    {
        turnInput = inputVector.x;
        accelerationInput = inputVector.y;
    }
    public bool IsWeelScreeching (out float lateralVelocity ,out bool isBreaking)
    {
        lateralVelocity = GetlateralVelocity();
        isBreaking = false;

        if (accelerationInput<0 && velocityVsup > 0)
        {
            isBreaking = true;
            return true;
        }
        if (Mathf.Abs(GetlateralVelocity()) > 4.0f)
        {
            return true;
        }

        return false;
    }

}
