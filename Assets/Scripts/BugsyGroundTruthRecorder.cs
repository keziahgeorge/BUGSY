using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records successful fault injections separately from detector output.
/// </summary>
public static class BugsyGroundTruthRecorder
{
    private const string FileName = "bugsy_ground_truth.csv";
    private const string Header =
        "RunId,TrialId,EpisodeId,EventId,EventType,Component,GameTime," +
        "RealtimeSinceStartup,TimestampUtc," +
        "PositionBeforeX,PositionBeforeY,PositionBeforeZ," +
        "PositionAfterX,PositionAfterY,PositionAfterZ," +
        "VelocityBeforeX,VelocityBeforeY,VelocityBeforeZ," +
        "VelocityAfterX,VelocityAfterY,VelocityAfterZ,Parameters";

    private static string runId;
    private static string trialId;
    private static int episodeId;

    // Shared session context for synchronized detector records.
    public static string RunId { get { EnsureSessionIds(); return runId; } }
    public static string TrialId { get { EnsureSessionIds(); return trialId; } }
    public static int EpisodeId { get { EnsureSessionIds(); return episodeId; } }
    public static double MonotonicSeconds { get { return Time.realtimeSinceStartupAsDouble; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InitializeRuntimeSession()
    {
        runId = Guid.NewGuid().ToString("N");
        trialId = Guid.NewGuid().ToString("N");
        episodeId = 0;
    }

    /// <summary>
    /// Called by GameManager when it begins a fresh game/agent episode.
    /// </summary>
    public static void BeginEpisode()
    {
        EnsureSessionIds();
        episodeId++;
    }

    public static void RecordTeleport(
        Component source,
        Vector3 positionBefore,
        Vector3 positionAfter,
        Vector3 velocityBefore,
        Vector3 velocityAfter,
        float delayBeforeTeleport,
        float teleportDistance,
        Vector3 direction)
    {
        string parameters = string.Format(
            CultureInfo.InvariantCulture,
            "delayBeforeTeleport={0:F3};teleportDistance={1:F3};direction=({2:F3},{3:F3},{4:F3})",
            delayBeforeTeleport,
            teleportDistance,
            direction.x,
            direction.y,
            direction.z);

        RecordEvent(
            "Teleport",
            source,
            positionBefore,
            positionAfter,
            velocityBefore,
            velocityAfter,
            parameters);
    }

    public static void RecordVelocitySpike(
        Component source,
        Vector3 position,
        Vector3 velocityBefore,
        Vector3 velocityAfter,
        float delayBeforeBug,
        float abnormalVelocity,
        Vector3 direction)
    {
        string parameters = string.Format(
            CultureInfo.InvariantCulture,
            "delayBeforeBug={0:F3};abnormalVelocity={1:F3};direction=({2:F3},{3:F3},{4:F3})",
            delayBeforeBug,
            abnormalVelocity,
            direction.x,
            direction.y,
            direction.z);

        RecordEvent(
            "VelocitySpike",
            source,
            position,
            position,
            velocityBefore,
            velocityAfter,
            parameters);
    }

    private static void RecordEvent(
        string eventType,
        Component source,
        Vector3 positionBefore,
        Vector3 positionAfter,
        Vector3 velocityBefore,
        Vector3 velocityAfter,
        string parameters)
    {
        EnsureSessionIds();

        string[] fields =
        {
            runId,
            trialId,
            episodeId.ToString(CultureInfo.InvariantCulture),
            Guid.NewGuid().ToString("N"),
            eventType,
            source != null ? source.GetType().Name : "Unknown",
            Time.time.ToString("F6", CultureInfo.InvariantCulture),
            Time.realtimeSinceStartupAsDouble.ToString("F6", CultureInfo.InvariantCulture),
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Format(positionBefore.x),
            Format(positionBefore.y),
            Format(positionBefore.z),
            Format(positionAfter.x),
            Format(positionAfter.y),
            Format(positionAfter.z),
            Format(velocityBefore.x),
            Format(velocityBefore.y),
            Format(velocityBefore.z),
            Format(velocityAfter.x),
            Format(velocityAfter.y),
            Format(velocityAfter.z),
            parameters
        };

        StringBuilder row = new StringBuilder();
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                row.Append(',');
            }
            row.Append(EscapeCsv(fields[i]));
        }

        try
        {
            string path = Path.Combine(Application.persistentDataPath, FileName);
            Directory.CreateDirectory(Application.persistentDataPath);

            bool needsHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
            string content = (needsHeader ? Header + Environment.NewLine : string.Empty) +
                             row + Environment.NewLine;
            File.AppendAllText(path, content, new UTF8Encoding(false));

            Debug.Log(
                $"BUGSY GROUND TRUTH | Type={eventType} | " +
                $"EventId={fields[3]} | RunId={runId} | TrialId={trialId} | " +
                $"EpisodeId={episodeId} | File={path}");
        }
        catch (Exception e)
        {
            Debug.LogError("BUGSY could not append ground-truth event: " + e.Message);
        }
    }

    private static void EnsureSessionIds()
    {
        if (string.IsNullOrEmpty(runId))
        {
            InitializeRuntimeSession();
        }
    }

    private static string Format(float value)
    {
        return value.ToString("F6", CultureInfo.InvariantCulture);
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
