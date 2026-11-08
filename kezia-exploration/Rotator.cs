using UnityEngine;

public class Rotator : MonoBehaviour
{
    public Vector3 axis = Vector3.up;
    public float speed = 60f;

    Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        Quaternion delta = Quaternion.AngleAxis(speed * Time.fixedDeltaTime, axis);
        rb.MoveRotation(rb.rotation * delta);
    }
}