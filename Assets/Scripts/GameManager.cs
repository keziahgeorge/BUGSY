using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public int maxLives = 3;
    public float invulnTime = 1.5f;

    public enum State { Playing, GameOver, Won }
    public State state = State.Playing;
    public int lives;
    public bool trainingMode = false;
    [Header("Bug Injection Settings")]
    public bool enableTeleportBugOnRestart = false;
    public bool enableVelocityBugOnRestart = false;


    float invulnUntil;
    float startTime;
    float endTime;
    string lastHit = "";
    float lastHitTime = -10f;

    Vector3 spawnPos;
    Quaternion spawnRot;
    Renderer playerRenderer;
    Texture2D white;
    GUIStyle big;
    GUIStyle mid;
    GUIStyle small;
    GUIStyle btn;

    void Awake()
    {
        Instance = this;
        if (!trainingMode)
        {
            Time.timeScale = 1f;
        }
        Time.fixedDeltaTime = 0.01f; // 100 physics steps per second for snappier controls
        lives = maxLives;
        state = State.Playing;
        startTime = Time.time;

        white = new Texture2D(1, 1);
        white.SetPixel(0, 0, Color.white);
        white.Apply();
    }

    void Start()
    {
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            playerRenderer = p.GetComponent<Renderer>();
            spawnPos = p.transform.position;
            spawnRot = p.transform.rotation;
        }
    }

    public bool IsPlaying()
    {
        return state == State.Playing;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartManually();
        }

        // Blink while invulnerable
        if (playerRenderer != null && state == State.Playing)
        {
            bool invuln = Time.time < invulnUntil;
            playerRenderer.enabled = !invuln || ((int)(Time.time * 12f) % 2 == 0);
        }
    }

    public void LoseLife(string reason)
    {
        if (state != State.Playing)
        {
            return;
        }
        if (Time.time < invulnUntil)
        {
            return;
        }

        lives--;
        lastHit = reason;
        lastHitTime = Time.time;
        invulnUntil = Time.time + invulnTime;
        Debug.Log("Lost a life (" + reason + "). Lives left: " + lives);

        if (lives <= 0)
        {
            lives = 0;
            EndGame(State.GameOver);
        }
    }

    public void Win()
    {
        if (state != State.Playing)
        {
            return;
        }
        EndGame(State.Won);
        if (!trainingMode)
        {
            SpawnConfetti();
        }
        Debug.Log("YOU WIN! Time: " + (endTime - startTime).ToString("F1") + " s");
    }

    void EndGame(State s)
    {
        state = s;
        endTime = Time.time;
        if (trainingMode)
        {
            return; // during training: no freezing, the agent ends the episode itself
        }

        GameObject p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            MonoBehaviour[] scripts = p.GetComponents<MonoBehaviour>();
            for (int i = 0; i < scripts.Length; i++)
            {
                scripts[i].enabled = false;
            }
            Rigidbody rb = p.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
            }
            Renderer r = p.GetComponent<Renderer>();
            if (r != null)
            {
                r.enabled = true;
            }
        }

        Time.timeScale = 0f;
    }

    // Resets everything in place (does not reload the scene)
    public void Restart()
    {
        if (!trainingMode)
        {
            Time.timeScale = 1f;
        }
        state = State.Playing;
        lives = maxLives;
        startTime = Time.time;
        invulnUntil = 0f;
        lastHitTime = -10f;

        GameObject conf = GameObject.Find("Confetti");
        if (conf != null)
        {
            Destroy(conf);
        }

        GameObject p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            Rigidbody rb = p.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = spawnPos;
                rb.rotation = spawnRot;
            }
            p.transform.position = spawnPos;
            p.transform.rotation = spawnRot;

            PlayerController pc = p.GetComponent<PlayerController>();
            if (pc != null)
            {
                pc.ResetState();
            }

            MonoBehaviour[] scripts = p.GetComponents<MonoBehaviour>();
            for (int i = 0; i < scripts.Length; i++)
            {
                scripts[i].enabled = true;
            }

            BugsyTeleportBug teleportBug = p.GetComponent<BugsyTeleportBug>();
            if (teleportBug != null)
            {
                teleportBug.enabled = enableTeleportBugOnRestart;
            }

            BugsyVelocityBug velocityBug = p.GetComponent<BugsyVelocityBug>();
            if (velocityBug != null)
            {
                velocityBug.enabled = enableVelocityBugOnRestart;
            }

            if (playerRenderer != null)
            {
                playerRenderer.enabled = true;
            }
        }

        BoulderTrap[] traps = FindObjectsByType<BoulderTrap>(FindObjectsSortMode.None);
        for (int i = 0; i < traps.Length; i++)
        {
            traps[i].ResetNow();
        }

        CameraFollow cf = FindFirstObjectByType<CameraFollow>();
        if (cf != null)
        {
            cf.ResetView();
        }

        BugsyGroundTruthRecorder.BeginEpisode();
    }

    private void RestartManually()
    {
        Restart();

        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            BugsyAnomalyDetector detector = player.GetComponent<BugsyAnomalyDetector>();
            if (detector != null)
            {
                detector.ResetTracking();
            }
        }
    }

    void SpawnConfetti()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        GameObject go = new GameObject("Confetti");
        go.transform.position = cam.transform.position
            + cam.transform.forward * 12f
            + cam.transform.up * 7f;
        go.transform.rotation = Quaternion.LookRotation(Vector3.down, cam.transform.forward);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.duration = 5f;
        main.useUnscaledTime = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.gravityModifier = 0.5f;
        main.maxParticles = 1500;

        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1f, 0.2f, 0.2f), 0f),
                new GradientColorKey(new Color(1f, 0.9f, 0.2f), 0.2f),
                new GradientColorKey(new Color(0.3f, 0.9f, 0.3f), 0.4f),
                new GradientColorKey(new Color(0.2f, 0.9f, 0.9f), 0.6f),
                new GradientColorKey(new Color(0.3f, 0.4f, 1f), 0.8f),
                new GradientColorKey(new Color(1f, 0.3f, 0.9f), 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            });
        ParticleSystem.MinMaxGradient mm = new ParticleSystem.MinMaxGradient(grad);
        mm.mode = ParticleSystemGradientMode.RandomColor;
        main.startColor = mm;

        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 120f;
        em.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0f, (short)250)
        });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(26f, 14f, 1f);
        shape.randomDirectionAmount = 0.35f;

        ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);

        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null)
        {
            sh = Shader.Find("Sprites/Default");
        }
        if (sh == null)
        {
            sh = Shader.Find("Particles/Standard Unlit");
        }
        pr.material = new Material(sh);
        pr.sortingOrder = 10;

        ps.Play();
    }

    void OnGUI()
    {
        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label);
            big.fontSize = 72;
            big.fontStyle = FontStyle.Bold;
            big.alignment = TextAnchor.MiddleCenter;

            mid = new GUIStyle(GUI.skin.label);
            mid.fontSize = 30;
            mid.alignment = TextAnchor.MiddleCenter;

            small = new GUIStyle(GUI.skin.label);
            small.fontSize = 18;
            small.fontStyle = FontStyle.Bold;

            btn = new GUIStyle(GUI.skin.button);
            btn.fontSize = 28;
        }

        // ---- Lives ----
        GUI.color = Color.white;
        GUI.Label(new Rect(16, 12, 100, 30), "LIVES", small);
        for (int i = 0; i < maxLives; i++)
        {
            GUI.color = i < lives
                ? new Color(0.9f, 0.1f, 0.15f)
                : new Color(0.3f, 0.3f, 0.3f, 0.8f);
            GUI.DrawTexture(new Rect(92 + i * 38, 12, 30, 30), white);
        }
        GUI.color = Color.white;

        float t = (state == State.Playing ? Time.time : endTime) - startTime;
        GUI.Label(new Rect(16, 48, 250, 30), "TIME  " + t.ToString("F1") + " s", small);
        GUI.Label(new Rect(16, Screen.height - 34, 700, 30),
            "WASD move   Space jump   Shift slide   Right mouse = camera   R restart", small);

        // ---- Hit message ----
        if (state == State.Playing && Time.time - lastHitTime < 2f)
        {
            GUI.color = new Color(1f, 0.3f, 0.3f);
            GUI.Label(new Rect(0, 70, Screen.width, 50), "-1 LIFE  (" + lastHit + ")", mid);
            GUI.color = Color.white;
        }

        // ---- End screens ----
        if (state != State.Playing)
        {
            GUI.color = new Color(0f, 0f, 0f, state == State.GameOver ? 0.65f : 0.25f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);
            GUI.color = Color.white;

            float cx = Screen.width / 2f;
            float cy = Screen.height / 2f;

            if (state == State.GameOver)
            {
                GUI.color = new Color(1f, 0.25f, 0.25f);
                GUI.Label(new Rect(cx - 400, cy - 120, 800, 100), "GAME OVER", big);
                GUI.color = Color.white;
                GUI.Label(new Rect(cx - 400, cy - 30, 800, 50), "You ran out of lives", mid);
            }
            else
            {
                GUI.color = new Color(1f, 0.9f, 0.2f);
                GUI.Label(new Rect(cx - 400, cy - 120, 800, 100), "YOU WIN!", big);
                GUI.color = Color.white;
                GUI.Label(new Rect(cx - 400, cy - 30, 800, 50),
                    "Time: " + (endTime - startTime).ToString("F1") + " s   Lives left: " + lives, mid);
            }

            if (GUI.Button(new Rect(cx - 150, cy + 50, 300, 60), "Play Again (R)", btn))
            {
                RestartManually();
            }
        }
    }
}
