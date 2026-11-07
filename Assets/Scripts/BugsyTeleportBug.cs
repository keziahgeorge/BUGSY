using UnityEngine;

public class BugsyTeleportBug : MonoBehaviour
{
    [Header("Teleport Bug Settings")]
    public float delayBeforeTeleport = 10f;

    public float teleportDistance = 8f;

    private bool hasTeleported = false;

    private void Start()
    {
        Debug.Log(
            "BUGSY TEST BUG: Teleport bug armed. " +
            "Teleport will occur after " +
            delayBeforeTeleport + " seconds."
        );
    }

    private void Update()
    {
        if (hasTeleported)
        {
            return;
        }

        if (Time.time >= delayBeforeTeleport)
        {
            TriggerTeleport();
        }
    }

    private void TriggerTeleport()
    {
        hasTeleported = true;

        Vector3 oldPosition = transform.position;

        Vector3 teleportDirection = transform.forward;

        teleportDirection.y = 0f;

        if (teleportDirection.sqrMagnitude < 0.01f)
        {
            teleportDirection = Vector3.forward;
        }

        teleportDirection.Normalize();

        Vector3 newPosition =
            oldPosition + teleportDirection * teleportDistance;

        Vector3 velocityBefore = Vector3.zero;

        transform.position = newPosition;

        Rigidbody rb = GetComponent<Rigidbody>();

        if (rb != null)
        {
            velocityBefore = rb.linearVelocity;
            rb.position = newPosition;
            rb.linearVelocity = Vector3.zero;
        }

        bool positionApplied =
            (transform.position - newPosition).sqrMagnitude <= 0.000001f &&
            (rb == null || (rb.position - newPosition).sqrMagnitude <= 0.000001f);

        if (positionApplied)
        {
            Vector3 velocityAfter = rb != null ? rb.linearVelocity : Vector3.zero;
            BugsyGroundTruthRecorder.RecordTeleport(
                this,
                oldPosition,
                transform.position,
                velocityBefore,
                velocityAfter,
                delayBeforeTeleport,
                teleportDistance,
                teleportDirection);
        }
        else
        {
            Debug.LogError("BUGSY Teleport Bug: teleport position was not applied; no ground-truth event recorded.");
        }

        if (positionApplied)
        {
            Debug.Log(
                "BUGSY TEST BUG TRIGGERED! " +
                "Player teleported " +
                teleportDistance +
                " metres."
            );
        }
    }
}
