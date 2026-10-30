using UnityEngine;

/// <summary>
/// Turns the skid trail on a wheel on while the car is drifting.
///
/// The trail look (width, duration, colour) is set up by <see cref="CarEffects"/>,
/// which is also the component that decides what "drifting" means. Without a
/// CarEffects on the car this falls back to TopDown2DCarController.IsWeelScreeching.
/// </summary>
public class WeelTrailRanderer : MonoBehaviour
{
    TopDown2DCarController topDown2DCarController;
    CarEffects carEffects;
    TrailRenderer trailRenderer;

    private void Awake()
    {
        topDown2DCarController = GetComponentInParent<TopDown2DCarController>();
        carEffects = GetComponentInParent<CarEffects>();
        trailRenderer = GetComponent<TrailRenderer>();

        trailRenderer.emitting = false;
    }

    // Update is called once per frame
    void Update()
    {
        if (carEffects != null)
        {
            trailRenderer.emitting = carEffects.IsDrifting;
            return;
        }

        trailRenderer.emitting = topDown2DCarController != null &&
                                topDown2DCarController.IsWeelScreeching(out float _, out bool _);
    }
}
