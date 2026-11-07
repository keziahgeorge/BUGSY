using UnityEngine;

[DefaultExecutionOrder(50)]
public class BugsyVelocityBug : MonoBehaviour
{
    [Header("Velocity Bug Settings")]
    public float delayBeforeBug = 10f;

    public float abnormalVelocity = 40f;

    private bool hasTriggered = false;
    private bool injectionScheduled = false;
    private static bool detectorDiagnosticPending;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeDiagnostics()
    {
        detectorDiagnosticPending = false;
    }

    /// <summary>
    /// Diagnostic handshake only. It does not affect the detector's checks or alerts.
    /// </summary>
    public static bool ConsumeDetectorDiagnostic()
    {
        if (!detectorDiagnosticPending)
        {
            return false;
        }

        detectorDiagnosticPending = false;
        return true;
    }

    private void Start()
    {
        Debug.Log(
            "BUGSY TEST BUG: Velocity bug armed. " +
            "Velocity spike will occur after " +
            delayBeforeBug + " seconds."
        );
    }

    private void Update()
    {
        if (hasTriggered)
        {
            return;
        }

        if (Time.time >= delayBeforeBug)
        {
            injectionScheduled = true;
        }
    }

    // Execution order: PlayerController (-200), this injector (+50),
    // BugsyAnomalyDetector (+100). The controller establishes normal movement
    // first, then this injector sets the test velocity, then the detector samples it.
    private void FixedUpdate()
    {
        if (hasTriggered || !injectionScheduled)
        {
            return;
        }

        injectionScheduled = false;
        hasTriggered = true;
        TriggerVelocityBug();
    }

    private void TriggerVelocityBug()
    {
        Rigidbody rb = GetComponent<Rigidbody>();

        if (rb == null)
        {
            Debug.LogError(
                "BUGSY Velocity Bug: No Rigidbody found on Player."
            );
            return;
        }

        Vector3 direction = transform.forward;

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.01f)
        {
            direction = Vector3.forward;
        }

        direction.Normalize();

        Vector3 position = transform.position;
        Vector3 velocityBefore = rb.linearVelocity;
        Vector3 injectedVelocity = direction * abnormalVelocity;
        rb.linearVelocity = injectedVelocity;
        Vector3 velocityAfter = rb.linearVelocity;
        bool injectionApplied =
            (velocityAfter - injectedVelocity).sqrMagnitude <= 0.000001f;

        if (injectionApplied)
        {
            BugsyGroundTruthRecorder.RecordVelocitySpike(
                this,
                position,
                velocityBefore,
                velocityAfter,
                delayBeforeBug,
                abnormalVelocity,
                direction);
            detectorDiagnosticPending = true;

            Debug.Log(
                $"BUGSY VELOCITY INJECTION | RunId={BugsyGroundTruthRecorder.RunId} | " +
                $"TrialId={BugsyGroundTruthRecorder.TrialId} | EpisodeId={BugsyGroundTruthRecorder.EpisodeId} | " +
                $"Before={velocityBefore} | After={velocityAfter} | " +
                $"GameTime={Time.time:F6} | Realtime={BugsyGroundTruthRecorder.MonotonicSeconds:F6}");
        }
        else
        {
            Debug.LogError("BUGSY Velocity Bug: velocity assignment was not applied; no ground-truth event recorded.");
        }

        if (injectionApplied)
        {
            Debug.Log(
                "BUGSY TEST BUG TRIGGERED! " +
                "Player velocity set to " +
                abnormalVelocity + " m/s."
            );
        }
    }
}
