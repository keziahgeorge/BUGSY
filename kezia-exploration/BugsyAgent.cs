using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class BugsyAgent : Agent
{
    const float MazeSize = 66f;        // 11 cells x 6 m, used to normalise position
    const int StuckLimitSteps = 800;   // 8 s of game time without a new cell ends the episode (training only)

    PlayerController pc;
    Rigidbody rb;
    CameraFollow camFollow;
    CoverageTracker coverage;

    // Previous actions: move(2), jump(2), slide(2), turn(3)
    int prevMove, prevJump, prevSlide, prevTurn;
    int stuckSteps;
    int lastCells;
    int spawnCount;
    int spawnFallbacks;

    public override void Initialize()
    {
        pc = GetComponent<PlayerController>();
        rb = GetComponent<Rigidbody>();
        coverage = GetComponent<CoverageTracker>();
        camFollow = FindFirstObjectByType<CameraFollow>();
        pc.agentControlled = true;
    }

    public override void OnEpisodeBegin()
    {
        if (coverage != null)
        {
            coverage.NewEpisode();
        }

        prevMove = 0;
        prevJump = 0;
        prevSlide = 0;
        prevTurn = 0;
        stuckSteps = 0;
        lastCells = 0;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.Restart();
        }
        RandomSpawn();
    }

    // Picks a random free spot inside the maze and a random facing direction
    void RandomSpawn()
    {
        Vector3 pos = Vector3.zero;
        bool found = false;
        int used = 0;

        for (int tries = 0; tries < 60; tries++)
        {
            used = tries + 1;
            float x = Random.Range(0, 11) * 6f + Random.Range(-1.5f, 1.5f);
            float z = Random.Range(0, 11) * 6f + Random.Range(-1.5f, 1.5f);
            Vector3 cand = new Vector3(x, 1.2f, z);

            Collider[] hits = Physics.OverlapCapsule(
                cand + Vector3.down * 0.3f, cand + Vector3.up * 0.6f, 0.5f,
                ~0, QueryTriggerInteraction.Ignore);

            bool blocked = false;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].gameObject != gameObject)
                {
                    blocked = true;
                    break;
                }
            }

            if (!blocked)
            {
                pos = cand;
                found = true;
                break;
            }
        }

        if (!found)
        {
            pos = new Vector3(Random.Range(0, 11) * 6f, 1.2f, Random.Range(0, 11) * 6f);
            spawnFallbacks++;
            Debug.LogWarning("RandomSpawn: no free spot found, using cell centre " + pos);
        }

        Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = pos;
        rb.rotation = rot;
        transform.position = pos;
        transform.rotation = rot;
        Physics.SyncTransforms();

        spawnCount++;
        if (spawnCount <= 10)
        {
            Debug.Log("SPAWN #" + spawnCount + " at x=" + pos.x.ToString("F1")
                + " z=" + pos.z.ToString("F1") + " (tries: " + used
                + ", fallbacks so far: " + spawnFallbacks + ")");
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 43 numbers in total: 18 body numbers + 25 visited-cell flags
        sensor.AddObservation(transform.position.x / MazeSize);
        sensor.AddObservation(transform.position.z / MazeSize);

        Vector3 v = transform.InverseTransformDirection(rb.linearVelocity);
        sensor.AddObservation(v.x / 10f);
        sensor.AddObservation(v.y / 10f);
        sensor.AddObservation(v.z / 10f);

        float yaw = transform.eulerAngles.y * Mathf.Deg2Rad;
        sensor.AddObservation(Mathf.Sin(yaw));
        sensor.AddObservation(Mathf.Cos(yaw));

        sensor.AddObservation(pc.IsGroundedNow() ? 1f : 0f);
        sensor.AddObservation(pc.IsSlidingNow() ? 1f : 0f);

        sensor.AddOneHotObservation(prevMove, 2);
        sensor.AddOneHotObservation(prevJump, 2);
        sensor.AddOneHotObservation(prevSlide, 2);
        sensor.AddOneHotObservation(prevTurn, 3);

        if (coverage != null)
        {
            coverage.AddVisitedObservations(sensor);
        }
        else
        {
            for (int i = 0; i < 25; i++)
            {
                sensor.AddObservation(0f);
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int move = actions.DiscreteActions[0];  // 0 stand still, 1 forward
        int jump = actions.DiscreteActions[1];  // 0 no, 1 jump
        int slide = actions.DiscreteActions[2]; // 0 no, 1 slide
        int turn = actions.DiscreteActions[3];  // 0 none, 1 left, 2 right

        pc.agentMoveF = move == 1 ? 1f : 0f;
        pc.agentMoveS = 0f;
        pc.agentJump = jump == 1;
        pc.agentSlide = slide == 1;
        pc.agentTurn = turn == 1 ? -1f : (turn == 2 ? 1f : 0f);

        prevMove = move;
        prevJump = jump;
        prevSlide = slide;
        prevTurn = turn;

        // No task reward on purpose: the only reward is the new-cell bonus from CoverageTracker.
    }
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<int> d = actionsOut.DiscreteActions;

        // Random-agent baseline: every decision is a uniformly random action
        if (coverage != null && coverage.randomActions)
        {
            d[0] = Random.Range(0, 2);
            d[1] = Random.Range(0, 2);
            d[2] = Random.Range(0, 2);
            d[3] = Random.Range(0, 3);
            return;
        }

        // Scripted wall-follower baseline (only if the component is on the Player)
        WallFollowerBrain wf = GetComponent<WallFollowerBrain>();
        if (wf != null && wf.enabled)
        {
            wf.Decide(d);
            return;
        }

        int turn = 0;
        if (Input.GetKey(KeyCode.Q)) turn = 1;
        else if (Input.GetKey(KeyCode.E)) turn = 2;

        d[0] = Input.GetKey(KeyCode.W) ? 1 : 0;
        d[1] = Input.GetKey(KeyCode.Space) ? 1 : 0;
        d[2] = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.C)) ? 1 : 0;
        d[3] = turn;
    }

    void Update()
    {
        // Keep the camera behind the player so you can watch the agent
        if (camFollow != null)
        {
            camFollow.yaw = transform.eulerAngles.y;
        }
    }

    void FixedUpdate()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || !gm.trainingMode)
        {
            return;
        }

        // In training mode the game never freezes, so end the episode here
        if (!gm.IsPlaying())
        {
            if (gm.state == GameManager.State.Won && coverage != null)
            {
                coverage.wonThisEpisode = true;
                if (coverage.taskRewardMode)
                {
                    AddReward(1f);
                }
            }
            EndEpisode();
            return;
        }

        // Stuck rule: no new grid cell for 8 s of game time ends the episode
        if (coverage != null)
        {
            if (coverage.EpisodeCells != lastCells)
            {
                lastCells = coverage.EpisodeCells;
                stuckSteps = 0;
            }
            else
            {
                stuckSteps++;
                if (stuckSteps >= StuckLimitSteps)
                {
                    EndEpisode();
                }
            }
        }
    }
}