using UnityEngine;

public class BoulderTrap : MonoBehaviour
{
    public float triggerRadius = 2.2f;
    public float resetDelay = 6f;
    public float dropSpeed = 25f;
    public float hideRadius = 2.5f;

    Rigidbody rb;
    Renderer rend;
    Transform player;
    Vector3 home;
    bool dropped;
    float dropTime;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rend = GetComponent<Renderer>();
        home = transform.position;
        rb.useGravity = false;
        rb.isKinematic = true;
    }

    Transform FindPlayer()
    {
        if (player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null)
            {
                player = p.transform;
            }
        }
        return player;
    }

    void Update()
    {
        if (rend == null)
        {
            return;
        }

        // A falling boulder is always visible
        if (dropped)
        {
            rend.enabled = true;
            return;
        }

        // A hovering boulder hides itself while it is between the camera and the player
        Camera cam = Camera.main;
        Transform p = FindPlayer();
        if (cam == null || p == null)
        {
            rend.enabled = true;
            return;
        }
        float d = DistToSegment(home, cam.transform.position, p.position);
        rend.enabled = d > hideRadius;
    }

    static float DistToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
        return Vector3.Distance(point, a + ab * t);
    }

    void FixedUpdate()
    {
        Transform p = FindPlayer();
        if (p == null)
        {
            return;
        }

        if (!dropped)
        {
            Vector3 d = p.position - home;
            d.y = 0f;
            if (d.magnitude < triggerRadius)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.linearVelocity = Vector3.down * dropSpeed;
                dropped = true;
                dropTime = Time.time;
            }
        }
        else if (Time.time - dropTime > resetDelay)
        {
            ResetNow();
        }
    }

    // Puts the boulder back up in the air (also called on game restart)
    public void ResetNow()
    {
        if (rb == null)
        {
            return;
        }
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;
        transform.position = home;
        dropped = false;
        if (rend != null)
        {
            rend.enabled = true;
        }
    }

    void OnCollisionEnter(Collision c)
    {
        if (!dropped)
        {
            return;
        }
        if (c.collider.CompareTag("Player") && c.relativeVelocity.magnitude > 1.5f)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.LoseLife("boulder");
            }
        }
    }
}