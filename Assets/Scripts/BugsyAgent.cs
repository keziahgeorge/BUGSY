using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class BugsyAgent : Agent
{
    const float MazeSize = 66f;   // 11 cells x 6 m, used to normalise position

    PlayerController pc;
    Rigidbody rb;
    CameraFollow camFollow;

    // Anomaly detector
    BugsyAnomalyDetector anomalyDetector;

    // Previous actions: move(5), jump(2), slide(2), turn(3)
    int prevMove, prevJump, prevSlide, prevTurn;

    public override void Initialize()
    {
        pc = GetComponent<PlayerController>();
        rb = GetComponent<Rigidbody>();
        camFollow = FindFirstObjectByType<CameraFollow>();

        pc.agentControlled = true;

        anomalyDetector = GetComponent<BugsyAnomalyDetector>();
    }

    public override void OnEpisodeBegin()
    {
        prevMove = 0;
        prevJump = 0;
        prevSlide = 0;
        prevTurn = 0;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.Restart();
        }

        RandomSpawn();

        // Tell the anomaly detector that this is a new episode.
        // This prevents the intentional spawn movement from
        // being detected as a teleport anomaly.
        if (anomalyDetector != null)
        {
            anomalyDetector.ResetTracking();
        }
    }

    // Picks a random free spot inside the maze and a random facing direction
    void RandomSpawn()
    {
        Vector3 pos = new Vector3(5f * 6f, 1.1f, 10f * 6f);

        for (int tries = 0; tries < 40; tries++)
        {
            float x = Random.Range(0, 11) * 6f + Random.Range(-2f, 2f);
            float z = Random.Range(0, 11) * 6f + Random.Range(-2f, 2f);

            Vector3 cand = new Vector3(x, 1.1f, z);

            bool blocked = Physics.CheckCapsule(
                cand + Vector3.down * 0.5f,
                cand + Vector3.up * 0.5f,
                0.6f,
                ~0,
                QueryTriggerInteraction.Ignore
            );

            if (!blocked)
            {
                pos = cand;
                break;
            }
        }

        Quaternion rot =
            Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        rb.position = pos;
        rb.rotation = rot;

        transform.position = pos;
        transform.rotation = rot;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 21 numbers in total

        // Position
        sensor.AddObservation(transform.position.x / MazeSize);
        sensor.AddObservation(transform.position.z / MazeSize);

        // Local velocity
        Vector3 v =
            transform.InverseTransformDirection(rb.linearVelocity);

        sensor.AddObservation(v.x / 10f);
        sensor.AddObservation(v.y / 10f);
        sensor.AddObservation(v.z / 10f);

        // Orientation
        float yaw =
            transform.eulerAngles.y * Mathf.Deg2Rad;

        sensor.AddObservation(Mathf.Sin(yaw));
        sensor.AddObservation(Mathf.Cos(yaw));

        // Grounded / sliding
        sensor.AddObservation(
            pc.IsGroundedNow() ? 1f : 0f
        );

        sensor.AddObservation(
            pc.IsSlidingNow() ? 1f : 0f
        );

        // Previous actions
        sensor.AddOneHotObservation(prevMove, 5);
        sensor.AddOneHotObservation(prevJump, 2);
        sensor.AddOneHotObservation(prevSlide, 2);
        sensor.AddOneHotObservation(prevTurn, 3);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Temporary debugging so we can inspect the trained policy.
        if (Time.frameCount % 100 == 0)
        {
            Debug.Log(
                $"BUGSY ACTION | Move={actions.DiscreteActions[0]} " +
                $"Jump={actions.DiscreteActions[1]} " +
                $"Slide={actions.DiscreteActions[2]} " +
                $"Turn={actions.DiscreteActions[3]}"
            );
        }

        int move = actions.DiscreteActions[0];
        // 0 none, 1 forward, 2 backward, 3 left, 4 right

        int jump = actions.DiscreteActions[1];
        // 0 no, 1 jump

        int slide = actions.DiscreteActions[2];
        // 0 no, 1 slide

        int turn = actions.DiscreteActions[3];
        // 0 none, 1 left, 2 right

        pc.agentMoveF =
            move == 1 ? 1f :
            (move == 2 ? -1f : 0f);

        pc.agentMoveS =
            move == 3 ? -1f :
            (move == 4 ? 1f : 0f);

        pc.agentJump = jump == 1;

        pc.agentSlide = slide == 1;

        pc.agentTurn =
            turn == 1 ? -1f :
            (turn == 2 ? 1f : 0f);

        prevMove = move;
        prevJump = jump;
        prevSlide = slide;
        prevTurn = turn;

        // No extrinsic reward on purpose.
        // Curiosity is added by the trainer.
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<int> d =
            actionsOut.DiscreteActions;

        int move = 0;

        if (Input.GetKey(KeyCode.W))
            move = 1;
        else if (Input.GetKey(KeyCode.S))
            move = 2;
        else if (Input.GetKey(KeyCode.A))
            move = 3;
        else if (Input.GetKey(KeyCode.D))
            move = 4;

        int turn = 0;

        if (Input.GetKey(KeyCode.Q))
            turn = 1;
        else if (Input.GetKey(KeyCode.E))
            turn = 2;

        d[0] = move;
        d[1] =
            Input.GetKey(KeyCode.Space) ? 1 : 0;

        d[2] =
            (Input.GetKey(KeyCode.LeftShift) ||
             Input.GetKey(KeyCode.C))
            ? 1
            : 0;

        d[3] = turn;
    }

    void Update()
    {
        // Keep the camera behind the player
        // so you can watch the agent.

        if (camFollow != null)
        {
            camFollow.yaw =
                transform.eulerAngles.y;
        }
    }

    void FixedUpdate()
    {
        // In training mode the game never freezes,
        // so end the episode here when the game ends.

        if (GameManager.Instance != null
            && GameManager.Instance.trainingMode
            && !GameManager.Instance.IsPlaying())
        {
            EndEpisode();
        }
    }
}