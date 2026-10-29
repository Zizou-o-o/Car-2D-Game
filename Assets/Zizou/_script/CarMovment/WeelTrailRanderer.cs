using UnityEngine;

public class WeelTrailRanderer : MonoBehaviour
{
    TopDown2DCarController topDown2DCarController;
    TrailRenderer trailRenderer;

    private void Awake()
    {
        topDown2DCarController = GetComponentInParent<TopDown2DCarController>();

        trailRenderer = GetComponent<TrailRenderer>();

        trailRenderer.emitting = false;
    }
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (topDown2DCarController.IsWeelScreeching(out float lateralVelocity, out bool isBreaking))
            trailRenderer.emitting = true;
        else trailRenderer.emitting = false;
    }
}
