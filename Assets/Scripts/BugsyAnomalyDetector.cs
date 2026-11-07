using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

[DefaultExecutionOrder(100)]
public class BugsyAnomalyDetector : MonoBehaviour
{
    [Header("References")]
    public PlayerController playerController;

    private Rigidbody rb;
    private Collider playerCollider;

    // Previous physical state
    private Vector3 previousPosition;
    private Vector3 previousVelocity;

    // Softlock tracking
    private float stationaryTime = 0f;

    // Episode information
    private int episodeNumber = 0;

    // CSV logging
    private string logPath;
    private StreamWriter logWriter;
    private StreamWriter structuredWriter;
    private StreamWriter sampleWriter;
    private StreamWriter positionDiagnosticWriter;
    private long evaluationCount;
    private readonly List<double> evaluationMilliseconds = new List<double>();
    private int structuredRowsSinceFlush;
    private int sampleRowsSinceFlush;
    private int positionDiagnosticRowsSinceFlush;

    [Header("Detection Thresholds")]
    [Tooltip("Legacy total speed threshold. Kept for compatibility with existing detector behavior.")]
    public float maxExpectedSpeed = 20f;

    [Tooltip("Horizontal Rigidbody speed above this value is anomalous. Set above the normal move speed.")]
    public float maxHorizontalSpeed = 12f;

    [Tooltip("Vertical Rigidbody speed above this value is anomalous. Set high enough for expected jumps and falls.")]
    public float maxVerticalSpeed = 25f;

    [Tooltip("Position change this large in one physics step is suspicious.")]
    public float maxPositionChange = 2f;

    [Tooltip("How long the player can remain almost stationary while trying to move.")]
    public float softlockTime = 5f;

    [Tooltip("Velocity below this value counts as stationary.")]
    public float stationarySpeed = 0.2f;

    [Header("Evaluation Controls (defaults preserve full detector)")]
    public bool detectTotalVelocity = true;
    public bool detectHorizontalVelocity = true;
    public bool detectVerticalVelocity = true;
    public bool detectPositionDiscontinuity = true;
    public bool detectWallPenetration = true;
    public bool detectSoftlock = true;
    [Tooltip("Records one raw physics sample per FixedUpdate for threshold sensitivity analysis. Adds disk I/O; enable only during evaluation runs.")]
    public bool recordEvaluationSamples = false;
    [Tooltip("Temporary diagnostic trace of position history on every FixedUpdate. Enable before Play Mode to investigate discontinuity sampling; adds disk I/O.")]
    public bool logPositionDiscontinuityDiagnostics = false;

    [Header("Wall Penetration Detection")]
    [Tooltip("Only colliders on these layers are checked. Include solid wall/obstacle layers and exclude Player/trigger layers.")]
    public LayerMask penetrationLayers = ~0;

    [Tooltip("Minimum computed overlap depth before it can count as penetration.")]
    [Min(0f)] public float minimumPenetrationDepth = 0.05f;

    [Tooltip("Penetration must persist this long before it is logged. Brief physics solver overlap is ignored.")]
    [Min(0f)] public float penetrationGracePeriod = 0.15f;

    private readonly Collider[] overlapBuffer = new Collider[64];
    private readonly Dictionary<Collider, float> penetrationDuration = new Dictionary<Collider, float>();
    private readonly HashSet<Collider> loggedPenetrations = new HashSet<Collider>();

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerCollider = GetComponent<Collider>();

        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }

        previousPosition = transform.position;
        previousVelocity = rb != null ? rb.linearVelocity : Vector3.zero;

        CreateLogFile();
        CreateEvaluationFiles();

        Debug.Log("BUGSY Anomaly Detector started.");
        Debug.Log("BUGSY log file: " + logPath);
    }

    private void FixedUpdate()
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            EvaluatePhysicsStep();
        }
        finally
        {
            double elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            evaluationCount++;
            evaluationMilliseconds.Add(elapsedMs);
        }
    }

    private void EvaluatePhysicsStep()
    {
        if (rb == null)
        {
            return;
        }

        Vector3 currentPosition = transform.position;
        Vector3 currentVelocity = rb.linearVelocity;
        Vector3 displacement = currentPosition - previousPosition;
        float positionChange = displacement.magnitude;
        float horizontalSpeed = new Vector2(currentVelocity.x, currentVelocity.z).magnitude;
        float verticalSpeed = Mathf.Abs(currentVelocity.y);
        float speed = currentVelocity.magnitude;

        if (BugsyVelocityBug.ConsumeDetectorDiagnostic())
        {
            bool totalVelocityExceeded = speed > maxExpectedSpeed;
            bool horizontalVelocityExceeded = horizontalSpeed > maxHorizontalSpeed;
            Debug.Log(
                $"BUGSY VELOCITY DETECTOR SAMPLE | RunId={BugsyGroundTruthRecorder.RunId} | " +
                $"TrialId={BugsyGroundTruthRecorder.TrialId} | EpisodeId={BugsyGroundTruthRecorder.EpisodeId} | " +
                $"Observed={currentVelocity} | TotalSpeed={speed:F3} (threshold={maxExpectedSpeed:F3}, exceeded={totalVelocityExceeded}, enabled={detectTotalVelocity}) | " +
                $"HorizontalSpeed={horizontalSpeed:F3} (threshold={maxHorizontalSpeed:F3}, exceeded={horizontalVelocityExceeded}, enabled={detectHorizontalVelocity})");
        }

        // Preserve the legacy overall speed signal.
        if (detectTotalVelocity && speed > maxExpectedSpeed)
        {
            LogAnomaly("Physics", "Abnormally high total velocity", speed, maxExpectedSpeed,
                positionChange, null);
        }

        if (detectHorizontalVelocity && horizontalSpeed > maxHorizontalSpeed)
        {
            LogAnomaly("HorizontalVelocity", "Horizontal velocity exceeded configured limit",
                horizontalSpeed, maxHorizontalSpeed, positionChange, null);
        }

        // A high vertical limit allows ordinary jumping and falling. PlayerController
        // supplies the jump impulse and gravity is not altered by this detector.
        if (detectVerticalVelocity && verticalSpeed > maxVerticalSpeed)
        {
            LogAnomaly("VerticalVelocity", "Vertical velocity exceeded configured limit",
                verticalSpeed, maxVerticalSpeed, positionChange, null);
        }

        bool positionConditionSatisfied = positionChange > maxPositionChange;
        string positionAlertOutcome;
        if (!detectPositionDiscontinuity)
        {
            positionAlertOutcome = "SuppressedRuleDisabled";
        }
        else if (positionConditionSatisfied)
        {
            LogAnomaly("PositionDiscontinuity", "Position changed unusually far in one physics step",
                positionChange, maxPositionChange, positionChange, null);
            positionAlertOutcome = "AlertEmitted";
        }
        else
        {
            positionAlertOutcome = "SuppressedThresholdNotMet";
        }

        WritePositionDiscontinuityDiagnostic(previousPosition, currentPosition, positionChange,
            positionConditionSatisfied, positionAlertOutcome);

        if (detectWallPenetration)
        {
            DetectPersistentPenetration();
        }

        // Preserve existing softlock detection.
        bool tryingToMove = false;
        if (playerController != null)
        {
            tryingToMove =
                Mathf.Abs(playerController.agentMoveF) > 0.01f ||
                Mathf.Abs(playerController.agentMoveS) > 0.01f ||
                playerController.agentJump ||
                playerController.agentSlide ||
                Mathf.Abs(playerController.agentTurn) > 0.01f;
        }

        if (detectSoftlock && tryingToMove && speed < stationarySpeed)
        {
            stationaryTime += Time.fixedDeltaTime;
        }
        else
        {
            stationaryTime = 0f;
        }

        if (detectSoftlock && stationaryTime >= softlockTime)
        {
            LogAnomaly("Softlock", "Agent is trying to act but remains stationary",
                speed, stationarySpeed, positionChange, null);
            stationaryTime = 0f;
        }

        previousPosition = currentPosition;
        previousVelocity = currentVelocity;

        WriteEvaluationSample(currentPosition, currentVelocity, speed, horizontalSpeed,
            verticalSpeed, positionChange, stationaryTime, tryingToMove);
    }

    /// <summary>
    /// Uses the Player collider's world bounds for a broadphase query, then Unity's
    /// collider-specific ComputePenetration test for candidate solid static geometry.
    /// A minimum depth and persistence window prevent ordinary contact and brief solver
    /// overlap from being reported as a confirmed penetration.
    /// </summary>
    private void DetectPersistentPenetration()
    {
        if (playerCollider == null || !playerCollider.enabled || playerCollider.isTrigger)
        {
            return;
        }

        Bounds bounds = playerCollider.bounds;
        int count = Physics.OverlapBoxNonAlloc(
            bounds.center,
            bounds.extents,
            overlapBuffer,
            Quaternion.identity,
            penetrationLayers,
            QueryTriggerInteraction.Ignore);

        HashSet<Collider> overlappingThisStep = new HashSet<Collider>();
        for (int i = 0; i < count; i++)
        {
            Collider other = overlapBuffer[i];
            overlapBuffer[i] = null;

            if (other == null || other == playerCollider || other.transform.IsChildOf(transform) ||
                other.isTrigger || !other.enabled)
            {
                continue;
            }

            // Moving hazards have their own legitimate physics motion. Limit this
            // check to static world geometry to avoid treating their contact as a wall fault.
            if (other.attachedRigidbody != null)
            {
                continue;
            }

            if (Physics.ComputePenetration(
                    playerCollider, playerCollider.transform.position, playerCollider.transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 direction, out float depth) &&
                depth >= minimumPenetrationDepth)
            {
                overlappingThisStep.Add(other);
                float duration = penetrationDuration.TryGetValue(other, out float previousDuration)
                    ? previousDuration + Time.fixedDeltaTime
                    : Time.fixedDeltaTime;
                penetrationDuration[other] = duration;

                if (duration >= penetrationGracePeriod && loggedPenetrations.Add(other))
                {
                    LogAnomaly("WallPenetration",
                        "Persistent collider overlap computed against static geometry; verify in Unity before classifying as a confirmed wall penetration",
                        depth, minimumPenetrationDepth, 0f, other,
                        $"PenetrationDirection=({direction.x:F3},{direction.y:F3},{direction.z:F3}); Duration={duration:F3}s");
                }
            }
        }

        List<Collider> cleared = null;
        foreach (Collider tracked in penetrationDuration.Keys)
        {
            if (tracked == null || !overlappingThisStep.Contains(tracked))
            {
                cleared ??= new List<Collider>();
                cleared.Add(tracked);
            }
        }

        if (cleared != null)
        {
            foreach (Collider tracked in cleared)
            {
                penetrationDuration.Remove(tracked);
                loggedPenetrations.Remove(tracked);
            }
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        Collider other = collision.collider;
        Vector3 relativeVelocity = collision.relativeVelocity;
        string contacts = collision.contactCount > 0
            ? $"Contacts={collision.contactCount}; FirstContactPoint={collision.GetContact(0).point}"
            : "Contacts=0";

        WriteCollision(other, transform.position, relativeVelocity, contacts);
    }

    private void LogAnomaly(
        string anomalyType,
        string description,
        float measuredValue,
        float threshold,
        float positionChange,
        Collider relevantCollider,
        string details = "")
    {
        float timestamp = Time.time;
        string action = GetCurrentAction();
        Vector3 position = transform.position;
        string colliderName = relevantCollider != null ? relevantCollider.name : "";
        string colliderPath = relevantCollider != null ? GetHierarchyPath(relevantCollider.transform) : "";

        string line =
            timestamp.ToString("F3") + "," + episodeNumber + "," +
            EscapeCsv(anomalyType) + "," + EscapeCsv(description) + "," +
            position.x.ToString("F3") + "," + position.y.ToString("F3") + "," + position.z.ToString("F3") + "," +
            measuredValue.ToString("F3") + "," + threshold.ToString("F3") + "," +
            positionChange.ToString("F3") + "," + EscapeCsv(colliderName) + "," +
            EscapeCsv(colliderPath) + "," + EscapeCsv(details) + "," + EscapeCsv(action);

        if (logWriter != null)
        {
            logWriter.WriteLine(line);
            logWriter.Flush();
        }

        WriteStructuredAlert(anomalyType, GetRuleName(anomalyType), description,
            measuredValue, threshold, position, currentVelocityForLog(), relevantCollider, details);

        Debug.Log(
            $"BUGSY ANOMALY | Type={anomalyType} | Description={description} | " +
            $"Measured={measuredValue:F3} | Threshold={threshold:F3} | " +
            $"Position={position} | Collider={(string.IsNullOrEmpty(colliderPath) ? "None" : colliderPath)} | {details}");
    }

    private Vector3 currentVelocityForLog()
    {
        return rb != null ? rb.linearVelocity : Vector3.zero;
    }

    private static string GetRuleName(string anomalyType)
    {
        return anomalyType == "Physics" ? "TotalVelocity" : anomalyType;
    }

    private void CreateEvaluationFiles()
    {
        string root = Application.persistentDataPath;
        structuredWriter = OpenAppendCsv(Path.Combine(root, "bugsy_detector_events.csv"),
            "RunId,TrialId,EpisodeId,AlertId,DetectionRealtimeSinceStartup,GameTime,Category,DetectionRule,Description,MeasuredValue,Threshold,PosX,PosY,PosZ,VelX,VelY,VelZ,Collider,ColliderPath,Details,Action");
        if (recordEvaluationSamples)
        {
            sampleWriter = OpenAppendCsv(Path.Combine(root, "bugsy_detector_samples.csv"),
                "RunId,TrialId,EpisodeId,SampleRealtimeSinceStartup,GameTime,PosX,PosY,PosZ,VelX,VelY,VelZ,TotalSpeed,HorizontalSpeed,VerticalSpeed,PositionChange,StationaryTime,TryingToMove,MaxExpectedSpeed,MaxHorizontalSpeed,MaxVerticalSpeed,MaxPositionChange,TotalVelocityEnabled,HorizontalVelocityEnabled,VerticalVelocityEnabled,PositionDiscontinuityEnabled,WallPenetrationEnabled,SoftlockEnabled");
        }
        if (logPositionDiscontinuityDiagnostics)
        {
            string diagnosticPath = Path.Combine(root, "bugsy_position_diagnostics.csv");
            try
            {
                positionDiagnosticWriter = OpenAppendCsv(diagnosticPath,
                    "RunId,TrialId,EpisodeId,SampleRealtimeSinceStartup,GameTime,PreviousPosX,PreviousPosY,PreviousPosZ,CurrentTransformX,CurrentTransformY,CurrentTransformZ,CurrentRigidbodyX,CurrentRigidbodyY,CurrentRigidbodyZ,Displacement,Threshold,ConditionSatisfied,RuleEnabled,AlertOutcome");
                Debug.Log("BUGSY position diagnostics: " + diagnosticPath);
            }
            catch (Exception e)
            {
                positionDiagnosticWriter = null;
                Debug.LogWarning("BUGSY position diagnostics could not open its output file: " + e.Message);
            }
        }
        Debug.Log("BUGSY structured detector events: " + Path.Combine(root, "bugsy_detector_events.csv"));
        if (recordEvaluationSamples)
        {
            Debug.Log("BUGSY detector samples: " + Path.Combine(root, "bugsy_detector_samples.csv"));
        }
    }

    private static StreamWriter OpenAppendCsv(string path, string header)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        bool needsHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
        StreamWriter writer = new StreamWriter(path, true);
        if (needsHeader)
        {
            writer.WriteLine(header);
            writer.Flush();
        }
        return writer;
    }

    private void WriteStructuredAlert(string category, string rule, string description,
        float measured, float threshold, Vector3 position, Vector3 velocity,
        Collider collider, string details)
    {
        if (structuredWriter == null) return;
        string[] fields = {
            BugsyGroundTruthRecorder.RunId, BugsyGroundTruthRecorder.TrialId,
            BugsyGroundTruthRecorder.EpisodeId.ToString(CultureInfo.InvariantCulture),
            Guid.NewGuid().ToString("N"), F(BugsyGroundTruthRecorder.MonotonicSeconds), F(Time.time),
            category, rule, description, F(measured), F(threshold),
            F(position.x), F(position.y), F(position.z), F(velocity.x), F(velocity.y), F(velocity.z),
            collider != null ? collider.name : "",
            collider != null ? GetHierarchyPath(collider.transform) : "", details, GetCurrentAction()
        };
        structuredWriter.WriteLine(Csv(fields));
        if (++structuredRowsSinceFlush >= 32)
        {
            structuredWriter.Flush();
            structuredRowsSinceFlush = 0;
        }
    }

    private void WriteEvaluationSample(Vector3 position, Vector3 velocity, float total,
        float horizontal, float vertical, float positionChange, float stillTime, bool tryingToMove)
    {
        if (sampleWriter == null) return;
        string[] fields = {
            BugsyGroundTruthRecorder.RunId, BugsyGroundTruthRecorder.TrialId,
            BugsyGroundTruthRecorder.EpisodeId.ToString(CultureInfo.InvariantCulture),
            F(BugsyGroundTruthRecorder.MonotonicSeconds), F(Time.time),
            F(position.x), F(position.y), F(position.z), F(velocity.x), F(velocity.y), F(velocity.z),
            F(total), F(horizontal), F(vertical), F(positionChange), F(stillTime), B(tryingToMove),
            F(maxExpectedSpeed), F(maxHorizontalSpeed), F(maxVerticalSpeed), F(maxPositionChange),
            B(detectTotalVelocity), B(detectHorizontalVelocity), B(detectVerticalVelocity),
            B(detectPositionDiscontinuity), B(detectWallPenetration), B(detectSoftlock)
        };
        sampleWriter.WriteLine(Csv(fields));
        if (++sampleRowsSinceFlush >= 100)
        {
            sampleWriter.Flush();
            sampleRowsSinceFlush = 0;
        }
    }

    private void WritePositionDiscontinuityDiagnostic(Vector3 previous, Vector3 current,
        float displacement, bool conditionSatisfied, string outcome)
    {
        if (positionDiagnosticWriter == null) return;
        Vector3 rigidbodyPosition = rb != null ? rb.position : current;
        string[] fields = {
            BugsyGroundTruthRecorder.RunId, BugsyGroundTruthRecorder.TrialId,
            BugsyGroundTruthRecorder.EpisodeId.ToString(CultureInfo.InvariantCulture),
            F(BugsyGroundTruthRecorder.MonotonicSeconds), F(Time.time),
            F(previous.x), F(previous.y), F(previous.z),
            F(current.x), F(current.y), F(current.z),
            F(rigidbodyPosition.x), F(rigidbodyPosition.y), F(rigidbodyPosition.z),
            F(displacement), F(maxPositionChange), B(conditionSatisfied), B(detectPositionDiscontinuity), outcome
        };
        try
        {
            positionDiagnosticWriter.WriteLine(Csv(fields));
            if (++positionDiagnosticRowsSinceFlush >= 100)
            {
                positionDiagnosticWriter.Flush();
                positionDiagnosticRowsSinceFlush = 0;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("BUGSY position diagnostic write failed; disabling diagnostics: " + e.Message);
            try { positionDiagnosticWriter.Close(); }
            catch { /* Diagnostic output must not interrupt detector behavior. */ }
            positionDiagnosticWriter = null;
        }
    }

    private static string F(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
    private static string B(bool value) { return value ? "1" : "0"; }
    private static string Csv(string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
            fields[i] = "\"" + (fields[i] ?? "").Replace("\"", "\"\"") + "\"";
        return string.Join(",", fields);
    }

    private void WriteCollision(Collider other, Vector3 position, Vector3 velocity, string details)
    {
        if (logWriter == null)
        {
            return;
        }

        string line =
            Time.time.ToString("F3") + "," + episodeNumber + "," +
            "Collision," + EscapeCsv("Collision entered") + "," +
            position.x.ToString("F3") + "," + position.y.ToString("F3") + "," + position.z.ToString("F3") + "," +
            velocity.magnitude.ToString("F3") + "," + "0," + "0," +
            EscapeCsv(other != null ? other.name : "") + "," +
            EscapeCsv(other != null ? GetHierarchyPath(other.transform) : "") + "," +
            EscapeCsv(details) + "," + EscapeCsv(GetCurrentAction());

        logWriter.WriteLine(line);
        logWriter.Flush();
    }

    private static string GetHierarchyPath(Transform item)
    {
        string path = item.name;
        while (item.parent != null)
        {
            item = item.parent;
            path = item.name + "/" + path;
        }
        return path;
    }

    private string GetCurrentAction()
    {
        if (playerController == null)
        {
            return "Unknown";
        }

        return
            "MoveF=" + playerController.agentMoveF.ToString("F1") +
            ";MoveS=" + playerController.agentMoveS.ToString("F1") +
            ";Turn=" + playerController.agentTurn.ToString("F1") +
            ";Jump=" + playerController.agentJump +
            ";Slide=" + playerController.agentSlide;
    }

    private void CreateLogFile()
    {
        // The legacy schema has no RunId column. Keep it separate per runtime run
        // so starting Play Mode never destroys previous diagnostic rows.
        logPath = Path.Combine(Application.persistentDataPath,
            "bugsy_anomalies_" + BugsyGroundTruthRecorder.RunId + ".csv");

        try
        {
            logWriter = OpenAppendCsv(logPath,
                "Timestamp,Episode,Type,Description,PosX,PosY,PosZ,MeasuredValue,Threshold," +
                "PositionChange,Collider,ColliderPath,Details,Action");
        }
        catch (Exception e)
        {
            Debug.LogError("BUGSY could not create anomaly log: " + e.Message);
        }
    }

    private void WritePerformanceSummary()
    {
        string path = Path.Combine(Application.persistentDataPath, "bugsy_detector_performance.csv");
        try
        {
            bool needsHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
            using (StreamWriter writer = new StreamWriter(path, true))
            {
                if (needsHeader) writer.WriteLine("RunId,TrialId,EvaluationCount,TotalMilliseconds,MeanMilliseconds,MedianMilliseconds,P95Milliseconds,MaxMilliseconds");
                evaluationMilliseconds.Sort();
                double total = 0;
                for (int i = 0; i < evaluationMilliseconds.Count; i++) total += evaluationMilliseconds[i];
                double mean = evaluationMilliseconds.Count == 0 ? 0 : total / evaluationMilliseconds.Count;
                double median = Percentile(evaluationMilliseconds, 0.50);
                double p95 = Percentile(evaluationMilliseconds, 0.95);
                double max = evaluationMilliseconds.Count == 0 ? 0 : evaluationMilliseconds[evaluationMilliseconds.Count - 1];
                writer.WriteLine(string.Join(",", BugsyGroundTruthRecorder.RunId, BugsyGroundTruthRecorder.TrialId,
                    evaluationCount.ToString(CultureInfo.InvariantCulture), F(total), F(mean), F(median), F(p95), F(max)));
            }
        }
        catch (Exception e) { Debug.LogError("BUGSY could not write detector performance summary: " + e.Message); }
    }

    private static double Percentile(List<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        int index = Mathf.Clamp(Mathf.CeilToInt((float)(percentile * sorted.Count)) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }

    private string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public void ResetTracking()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        previousPosition = transform.position;
        previousVelocity = rb != null ? rb.linearVelocity : Vector3.zero;
        stationaryTime = 0f;
        penetrationDuration.Clear();
        loggedPenetrations.Clear();
        episodeNumber = BugsyGroundTruthRecorder.EpisodeId;

        Debug.Log("BUGSY detector reset for episode " + episodeNumber);
    }

    private void OnDestroy()
    {
        WritePerformanceSummary();
        if (structuredWriter != null) { structuredWriter.Flush(); structuredWriter.Close(); structuredWriter = null; }
        if (sampleWriter != null) { sampleWriter.Flush(); sampleWriter.Close(); sampleWriter = null; }
        if (positionDiagnosticWriter != null) { positionDiagnosticWriter.Flush(); positionDiagnosticWriter.Close(); positionDiagnosticWriter = null; }
        if (logWriter != null)
        {
            logWriter.Flush();
            logWriter.Close();
            logWriter = null;
        }
    }
}
