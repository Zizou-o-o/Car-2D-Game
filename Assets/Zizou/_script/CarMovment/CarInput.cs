using UnityEngine;

public class CarInput : MonoBehaviour
{
    TopDown2DCarController topDown2DCarController;

    private void Awake()
    {
        topDown2DCarController = GetComponent<TopDown2DCarController>();
    }



    // Update is called once per frame
    void Update()
    {
        Vector2 InputVector = Vector2.zero;

        InputVector.x = Input.GetAxis("Horizontal");
        InputVector.y = Input.GetAxis("Vertical");

        topDown2DCarController.SetInputVector (InputVector);
    }
}
