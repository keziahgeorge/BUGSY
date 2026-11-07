using UnityEngine;

public class Pendulum : MonoBehaviour
{
    public Vector3 axis = Vector3.forward;
    public float angle = 50f;
    public float speed = 1.5f;
    public float phase = 0f;

    Rigidbody rb;
    Quaternion startRot;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        startRot = rb.rotation;
    }

    void FixedUpdate()
    {
        float a = angle * Mathf.Sin(Time.time * speed + phase);
        rb.MoveRotation(startRot * Quaternion.AngleAxis(a, axis));
    }
}