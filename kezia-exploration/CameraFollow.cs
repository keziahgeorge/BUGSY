using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float distance = 7f;
    public float height = 7f;
    public float closeDistance = 1.5f;
    public float highHeight = 15f;
    public float lookHeight = 1f;
    public float yaw = 180f;
    public float sensitivity = 3f;

    float blend;     // 0 = normal view, 1 = high close view
    float startYaw;

    void Start()
    {
        startYaw = yaw;
    }

    // Called by GameManager when the game restarts
    public void ResetView()
    {
        yaw = startYaw;
        blend = 0f;
    }

    void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * sensitivity;
        }

        Vector3 focus = target.position + Vector3.up * lookHeight;
        Vector3 back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;

        // Find the least-raised view that has a clear line to the player
        float best = 1f;
        for (int i = 0; i <= 10; i++)
        {
            float s = i / 10f;
            if (!WallBetween(focus, PoseAt(s, back)))
            {
                best = s;
                break;
            }
        }

        if (best > blend)
        {
            blend = best; // snap up immediately when a wall is in the way
        }
        else
        {
            blend = Mathf.Lerp(blend, best, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
        }

        transform.position = PoseAt(blend, back);
        transform.LookAt(focus);
    }

    Vector3 PoseAt(float s, Vector3 back)
    {
        float d = Mathf.Lerp(distance, closeDistance, s);
        float h = Mathf.Lerp(height, highHeight, s);
        return target.position + back * d + Vector3.up * h;
    }

    bool WallBetween(Vector3 a, Vector3 b)
    {
        Vector3 dir = b - a;
        RaycastHit[] hits = Physics.RaycastAll(a, dir, dir.magnitude);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.CompareTag("Wall"))
            {
                return true;
            }
        }
        return false;
    }
}