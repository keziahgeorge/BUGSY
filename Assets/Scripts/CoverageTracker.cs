using System.IO;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;

public class CoverageTracker : MonoBehaviour
{
    public float gridSize = 3f;
    public int gridCount = 30;
    public float origin = 12f;          // world offset so negative positions fit the grid
    public float newCellReward = 0.1f;

    [Header("Standard-PPO baseline: reward only for reaching the finish")]
    public bool taskRewardMode = false; // ticked = no novelty reward, visited map hidden from the agent
    public bool hideVisitedMap = false; // ablation: novelty reward stays, visited map is hidden
    [HideInInspector] public bool wonThisEpisode = false;

    [Header("Baseline measurement (leave Csv Label empty for normal use)")]
    public string csvLabel = "";        // e.g. "random" or "v8". Empty = no logging, no speed-up
    public bool randomActions = false;  // true = random agent (needs Behavior Type = Heuristic Only)
    public int targetEpisodes = 100;    // Play stops by itself after this many episodes
    public float speedUp = 10f;         // game speed during measurement (lower to 5 if it lags)

    bool[,] visited;
    bool[,] everVisited;
    int visitedCount;
    int everCount;
    int episodeIndex;
    string csvPath;
    BugsyAgent agent;

    public int EpisodeCells { get { return visitedCount; } }

    void Awake()
    {
        visited = new bool[gridCount, gridCount];
        everVisited = new bool[gridCount, gridCount];
        agent = GetComponent<BugsyAgent>();

        if (!string.IsNullOrEmpty(csvLabel))
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            csvPath = Path.Combine(root, "baseline_" + csvLabel + ".csv");
            File.WriteAllText(csvPath,
                "episode,cells_this_episode,cumulative_unique_cells,finished\n");
        }
    }

    void Start()
    {
        if (!string.IsNullOrEmpty(csvLabel))
        {
            Time.timeScale = speedUp;
            Debug.Log("BASELINE '" + csvLabel + "' started. Writing to " + csvPath
                + ". Will stop after " + targetEpisodes + " episodes.");
        }
    }

    int CellX()
    {
        return Mathf.FloorToInt((transform.position.x + origin) / gridSize);
    }

    int CellZ()
    {
        return Mathf.FloorToInt((transform.position.z + origin) / gridSize);
    }

    void FixedUpdate()
    {
        int gx = CellX();
        int gz = CellZ();
        if (gx < 0 || gz < 0 || gx >= gridCount || gz >= gridCount)
        {
            return;
        }
        if (!visited[gx, gz])
        {
            visited[gx, gz] = true;
            visitedCount++;
            if (!everVisited[gx, gz])
            {
                everVisited[gx, gz] = true;
                everCount++;
            }
            if (agent != null && !taskRewardMode)
            {
                agent.AddReward(newCellReward);
            }
        }
    }

    // 25 numbers: 1 = already visited (or outside the grid), 0 = not visited yet
    public void AddVisitedObservations(VectorSensor sensor)
    {
        if (taskRewardMode || hideVisitedMap)
        {
            for (int i = 0; i < 25; i++)
            {
                sensor.AddObservation(0f);
            }
            return;
        }

        int cx = CellX();
        int cz = CellZ();
        for (int dz = 2; dz >= -2; dz--)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = cx + dx;
                int z = cz + dz;
                bool inside = x >= 0 && z >= 0 && x < gridCount && z < gridCount;
                sensor.AddObservation((inside && !visited[x, z]) ? 0f : 1f);
            }
        }
    }

    public void NewEpisode()
    {
        if (episodeIndex > 0)
        {
            Academy.Instance.StatsRecorder.Add("Bugsy/CellsVisited", visitedCount);
            Academy.Instance.StatsRecorder.Add("Bugsy/Finished", wonThisEpisode ? 1f : 0f);

            if (!string.IsNullOrEmpty(csvPath))
            {
                File.AppendAllText(csvPath,
                    episodeIndex + "," + visitedCount + "," + everCount + ","
                    + (wonThisEpisode ? 1 : 0) + "\n");

                if (episodeIndex >= targetEpisodes)
                {
                    Debug.Log("BASELINE '" + csvLabel + "' finished. " + episodeIndex
                        + " episodes saved to " + csvPath);
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#endif
                }
            }
        }
        episodeIndex++;
        visited = new bool[gridCount, gridCount];
        visitedCount = 0;
        wonThisEpisode = false;
    }
}