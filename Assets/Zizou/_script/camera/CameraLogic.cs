using UnityEngine;

public class CameraLogic : MonoBehaviour
{
    public Transform target;
    public float smoothSpeed = 0.2f;
    public Vector3 offset = new Vector3(0, 10, -10) ;
    public float lookaheadDistance = 1.5f;
    public Rigidbody2D carRigidbody2D;

    private void LateUpdate()
    {
        Vector3 lookahead = carRigidbody2D.linearVelocity.normalized * lookaheadDistance;
        Vector3 desiredPosition= target.position + offset + lookahead;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
    }
}
