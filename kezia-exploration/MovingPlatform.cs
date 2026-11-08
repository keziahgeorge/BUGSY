using UnityEngine;

public class MovingPlatform : MonoBehaviour
{
    public Vector3 direction = Vector3.up;
    public float distance = 2f;
    public float speed = 1.5f;

    Vector3 startPos;
    Rigidbody rb;

    void Start()
    {
        startPos = transform.position;
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        float d = Mathf.PingPong(Time.time * speed, distance);
        rb.MovePosition(startPos + direction.normalized * d);
    }
}