using UnityEngine;
using Unity.MLAgents.Actuators;

// Scripted right-hand-rule wall follower (baseline). Only works with Behavior Type = Heuristic Only.
public class WallFollowerBrain : MonoBehaviour
{
    public float openDistance = 7f;      // right ray longer than this = opening on the right
    public float frontStopDistance = 3f; // wall closer than this in front = turn left
    public int advanceDecisions = 3;     // drive on a little before turning into an opening
    public int cooldownDecisions = 10;   // after a turn, ignore right openings for a while
    public bool hop = true;              // keep jumping to clear low hurdles

    int mode;          // 0 = drive, 1 = advance before right turn, 2 = turning in place
    int advanceLeft;
    int cooldown;
    float targetYaw;
    int logCount;

    // Distance to the nearest object tagged "Wall" in this direction
    float Dist(Vector3 dir)
    {
        RaycastHit[] hits = Physics.RaycastAll(transform.position, dir, 20f, ~0,
            QueryTriggerInteraction.Ignore);
        float best = 20f;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.CompareTag("Wall") && hits[i].distance < best)
            {
                best = hits[i].distance;
            }
        }
        return best;
    }

    // 1 = turn left, 2 = turn right, 0 = none, so the heading stays on a maze axis
    int HeadingLock(float yaw)
    {
        float snapped = Mathf.Round(yaw / 90f) * 90f;
        float delta = Mathf.DeltaAngle(yaw, snapped);
        if (delta > 6.5f) return 2;
        if (delta < -6.5f) return 1;
        return 0;
    }

    public void Decide(ActionSegment<int> d)
    {
        d[0] = 0;
        d[1] = hop ? 1 : 0;
        d[2] = 0;
        d[3] = 0;

        float yaw = transform.eulerAngles.y;
        Vector3 f = transform.forward;
        f.y = 0f;
        f.Normalize();
        Vector3 r = new Vector3(f.z, 0f, -f.x);

        if (cooldown > 0) cooldown--;

        // Turning in place towards the target heading
        if (mode == 2)
        {
            float delta = Mathf.DeltaAngle(yaw, targetYaw);
            if (Mathf.Abs(delta) > 6.5f)
            {
                d[3] = delta > 0f ? 2 : 1;
                return;
            }
            mode = 0;
            cooldown = cooldownDecisions;
        }

        // Driving a bit further so the turn happens near the middle of the opening
        if (mode == 1)
        {
            if (advanceLeft > 0)
            {
                advanceLeft--;
                d[0] = 1;
                d[3] = HeadingLock(yaw);
                return;
            }
            targetYaw = Mathf.Round(yaw / 90f) * 90f + 90f;
            mode = 2;
            d[3] = 2;
            return;
        }

        // mode 0: drive
        float front = Dist(f);
        float right = Dist(r);

        if (logCount < 30)
        {
            logCount++;
            Debug.Log("WF front=" + front.ToString("F1") + " right=" + right.ToString("F1")
                + " yaw=" + yaw.ToString("F0"));
        }

        if (cooldown == 0 && right > openDistance)
        {
            mode = 1;
            advanceLeft = advanceDecisions;
            d[0] = 1;
            return;
        }

        if (front < frontStopDistance)
        {
            targetYaw = Mathf.Round(yaw / 90f) * 90f - 90f;
            mode = 2;
            d[3] = 1;
            return;
        }

        d[0] = 1;
        d[3] = HeadingLock(yaw);
    }
}