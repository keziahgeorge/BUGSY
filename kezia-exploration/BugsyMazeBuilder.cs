using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

public static class BugsyMazeBuilder
{
    // ---------- Settings you can change ----------
    const int Cols = 11;
    const int Rows = 11;
    const float C = 6f;
    const float T = 0.6f;
    const float WallTop = 4.5f;
    const float WallBottom = -2f;
    const int Seed = 21;
    const int ExtraLoops = 2;
    const float AcrossW = 5.6f;
    const int StartCol = 5;          // entrance gap (top edge)
    const int StartRow = Rows - 1;
    const int FinishCol = 6;         // exit gap (bottom edge)
    const int FinishRow = 0;

    static bool[,] hWall;
    static bool[,] vWall;
    static Transform L;
    static Material floorM, wallM, obsM, platM, beamM, spinM, playM, startM, finishM;
    static Material laserM, boulderM, markerM, miniM;
    static int obstacleCount;
    static int obstLayer;
    static int miniLayer;

    [MenuItem("BUGSY/Build Maze Level")]
    public static void Build()
    {
        string[] tags = { "Floor", "Wall", "Obstacle", "Platform" };
        for (int k = 0; k < tags.Length; k++)
        {
            AddTag(tags[k]);
        }
        obstLayer = EnsureLayer("Obstacles");
        miniLayer = EnsureLayer("MinimapOnly");

        DestroyByName("Level");
        DestroyByName("Player");
        DestroyByName("BugsyDecor");
        DestroyByName("MinimapCamera");
        DestroyByName("GameManager");
        L = new GameObject("Level").transform;
        obstacleCount = 0;

        floorM = MakeMat("Mat_Floor", new Color(0.93f, 0.93f, 0.88f));
        wallM = MakeMat("Mat_Wall", new Color(0.10f, 0.28f, 0.55f));
        obsM = MakeMat("Mat_Obstacle", new Color(0.50f, 0.32f, 0.18f));
        platM = MakeMat("Mat_Platform", new Color(0.30f, 0.65f, 0.35f));
        beamM = MakeMat("Mat_Beam", new Color(0.85f, 0.15f, 0.15f));
        spinM = MakeMat("Mat_Spinner", new Color(0.65f, 0.30f, 0.80f));
        playM = MakeMat("Mat_Player", new Color(0.95f, 0.55f, 0.10f));
        startM = MakeMat("Mat_Start", new Color(0.55f, 0.90f, 0.55f));
        finishM = MakeMat("Mat_Finish", new Color(1.00f, 0.84f, 0.00f));
        laserM = MakeMat("Mat_Laser", new Color(1.00f, 0.10f, 0.10f));
        boulderM = MakeMat("Mat_Boulder", new Color(0.35f, 0.35f, 0.38f));
        markerM = MakeMat("Mat_BoulderMarker", new Color(0.55f, 0.10f, 0.10f));
        miniM = MakeMat("Mat_MinimapMarker", new Color(1.00f, 0.90f, 0.10f));
        if (laserM.HasProperty("_EmissionColor"))
        {
            laserM.EnableKeyword("_EMISSION");
            laserM.SetColor("_EmissionColor", new Color(1.5f, 0.1f, 0.1f));
        }

        System.Random rnd = new System.Random(Seed);
        GenerateMaze(rnd);
        BuildFloorAndWalls();
        PlaceObstacles(rnd);
        BuildStartAndFinish();

        GameObject player = MakePlayer();
        SetupCamera(player);
        MakeMinimap();

        GameObject gm = new GameObject("GameManager");
        AddScript(gm, "GameManager");

        int path = ShortestPath();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("BUGSY maze built. Shortest route: " + path
            + " cells, obstacles placed: " + obstacleCount
            + ". Press Ctrl+S to save.");
    }

    // ---------------------------------------------------------------
    // Maze generation (always solvable)
    // ---------------------------------------------------------------
    static void GenerateMaze(System.Random rnd)
    {
        hWall = new bool[Cols, Rows + 1];
        vWall = new bool[Cols + 1, Rows];
        for (int i = 0; i < Cols; i++)
        {
            for (int j = 0; j <= Rows; j++)
            {
                hWall[i, j] = true;
            }
        }
        for (int i = 0; i <= Cols; i++)
        {
            for (int j = 0; j < Rows; j++)
            {
                vWall[i, j] = true;
            }
        }

        bool[,] visited = new bool[Cols, Rows];
        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        Vector2Int[] dirs =
        {
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0)
        };

        visited[StartCol, StartRow] = true;
        stack.Push(new Vector2Int(StartCol, StartRow));

        while (stack.Count > 0)
        {
            Vector2Int cur = stack.Peek();
            List<Vector2Int> options = new List<Vector2Int>();
            for (int d = 0; d < dirs.Length; d++)
            {
                Vector2Int n = cur + dirs[d];
                bool inside = n.x >= 0 && n.x < Cols && n.y >= 0 && n.y < Rows;
                if (inside && !visited[n.x, n.y])
                {
                    options.Add(dirs[d]);
                }
            }
            if (options.Count == 0)
            {
                stack.Pop();
                continue;
            }
            Vector2Int pick = options[rnd.Next(options.Count)];
            Carve(cur, pick);
            Vector2Int next = cur + pick;
            visited[next.x, next.y] = true;
            stack.Push(next);
        }

        // A few extra openings so there is more than one route
        List<int[]> cands = new List<int[]>();
        for (int i = 0; i < Cols; i++)
        {
            for (int j = 1; j < Rows; j++)
            {
                if (hWall[i, j])
                {
                    cands.Add(new int[] { 0, i, j });
                }
            }
        }
        for (int i = 1; i < Cols; i++)
        {
            for (int j = 0; j < Rows; j++)
            {
                if (vWall[i, j])
                {
                    cands.Add(new int[] { 1, i, j });
                }
            }
        }
        for (int k = 0; k < ExtraLoops && cands.Count > 0; k++)
        {
            int idx = rnd.Next(cands.Count);
            int[] c = cands[idx];
            cands.RemoveAt(idx);
            if (c[0] == 0)
            {
                hWall[c[1], c[2]] = false;
            }
            else
            {
                vWall[c[1], c[2]] = false;
            }
        }

        // Entrance (top) and exit (bottom) gaps
        hWall[StartCol, Rows] = false;
        hWall[FinishCol, 0] = false;
    }

    static void Carve(Vector2Int a, Vector2Int d)
    {
        if (d.y == 1)
        {
            hWall[a.x, a.y + 1] = false;
        }
        else if (d.y == -1)
        {
            hWall[a.x, a.y] = false;
        }
        else if (d.x == 1)
        {
            vWall[a.x + 1, a.y] = false;
        }
        else
        {
            vWall[a.x, a.y] = false;
        }
    }

    static bool OpenN(int i, int j) { return !hWall[i, j + 1]; }
    static bool OpenS(int i, int j) { return !hWall[i, j]; }
    static bool OpenE(int i, int j) { return !vWall[i + 1, j]; }
    static bool OpenW(int i, int j) { return !vWall[i, j]; }

    static int ShortestPath()
    {
        int[,] dist = new int[Cols, Rows];
        for (int i = 0; i < Cols; i++)
        {
            for (int j = 0; j < Rows; j++)
            {
                dist[i, j] = -1;
            }
        }

        Vector2Int[] dirs =
        {
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0)
        };

        Queue<Vector2Int> q = new Queue<Vector2Int>();
        dist[StartCol, StartRow] = 0;
        q.Enqueue(new Vector2Int(StartCol, StartRow));

        while (q.Count > 0)
        {
            Vector2Int p = q.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                bool open = false;
                if (d == 0) open = OpenN(p.x, p.y);
                if (d == 1) open = OpenS(p.x, p.y);
                if (d == 2) open = OpenE(p.x, p.y);
                if (d == 3) open = OpenW(p.x, p.y);
                if (!open)
                {
                    continue;
                }

                Vector2Int n = p + dirs[d];
                if (n.x < 0 || n.x >= Cols || n.y < 0 || n.y >= Rows)
                {
                    continue;
                }
                if (dist[n.x, n.y] >= 0)
                {
                    continue;
                }

                dist[n.x, n.y] = dist[p.x, p.y] + 1;
                q.Enqueue(n);
            }
        }
        return dist[FinishCol, FinishRow];
    }

    // ---------------------------------------------------------------
    // Floor, walls, start and finish areas
    // (pieces are inset by a few millimetres so that no two faces
    //  share the same plane, which stops the flickering)
    // ---------------------------------------------------------------
    static void BuildFloorAndWalls()
    {
        float cx = (Cols - 1) * C / 2f;
        float cz = (Rows - 1) * C / 2f;
        Box("Floor", new Vector3(cx, -0.5f, cz),
            new Vector3(Cols * C + T - 0.1f, 1f, Rows * C + T - 0.1f), "Floor", floorM);

        float wallH = WallTop - WallBottom;
        float wallY = (WallTop + WallBottom) / 2f;

        // Horizontal wall runs (full height)
        for (int j = 0; j <= Rows; j++)
        {
            int i = 0;
            while (i < Cols)
            {
                if (!hWall[i, j])
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < Cols && hWall[i, j])
                {
                    i++;
                }
                int len = i - start;
                Box("Wall",
                    new Vector3((start + (len - 1) / 2f) * C, wallY, j * C - C / 2f),
                    new Vector3(len * C + T - 0.02f, wallH, T), "Wall", wallM);
            }
        }

        // Vertical wall runs (slightly lower and shorter)
        for (int i = 0; i <= Cols; i++)
        {
            int j = 0;
            while (j < Rows)
            {
                if (!vWall[i, j])
                {
                    j++;
                    continue;
                }
                int start = j;
                while (j < Rows && vWall[i, j])
                {
                    j++;
                }
                int len = j - start;
                Box("Wall",
                    new Vector3(i * C - C / 2f, wallY - 0.01f, (start + (len - 1) / 2f) * C),
                    new Vector3(T, wallH - 0.02f, len * C + T - 0.02f), "Wall", wallM);
            }
        }

        BuildPorch(StartCol, true);
        BuildPorch(FinishCol, false);
    }

    // Small closed room outside the entrance / exit gap
    static void BuildPorch(int col, bool top)
    {
        float cx = col * C;
        float cz = top ? (Rows - 1) * C + C : -C;
        float wallH = WallTop - WallBottom;
        float wallY = (WallTop + WallBottom) / 2f;

        Box("PorchFloor", new Vector3(cx, -0.51f, cz),
            new Vector3(C + T - 0.1f, 1f, C), "Floor", floorM);
        Box("PorchWall", new Vector3(cx - C / 2f, wallY - 0.02f, cz),
            new Vector3(T - 0.06f, wallH - 0.04f, C + T - 0.02f), "Wall", wallM);
        Box("PorchWall", new Vector3(cx + C / 2f, wallY - 0.02f, cz),
            new Vector3(T - 0.06f, wallH - 0.04f, C + T - 0.02f), "Wall", wallM);
        float endZ = top ? cz + C / 2f : cz - C / 2f;
        Box("PorchWall", new Vector3(cx, wallY - 0.01f, endZ),
            new Vector3(C + T - 0.02f, wallH - 0.02f, T), "Wall", wallM);
    }

    static void BuildStartAndFinish()
    {
        float sz = (Rows - 1) * C + C;
        GameObject sp = Box("StartPad", new Vector3(StartCol * C, 0.03f, sz),
            new Vector3(C - 1f, 0.06f, C - 1f), null, startM);
        NoCollider(sp);

        float fz = -C;
        float fx = FinishCol * C;
        GameObject fp = Box("FinishPad", new Vector3(fx, 0.03f, fz),
            new Vector3(C - 1f, 0.06f, C - 1f), null, finishM);
        NoCollider(fp);

        // Solid gold posts with a banner between them
        float postX = 2.3f;
        Box("FinishPost_L", new Vector3(fx - postX, 1.9f, fz),
            new Vector3(0.6f, 1.9f, 0.6f), "Obstacle", finishM, PrimitiveType.Cylinder);
        Box("FinishPost_R", new Vector3(fx + postX, 1.9f, fz),
            new Vector3(0.6f, 1.9f, 0.6f), "Obstacle", finishM, PrimitiveType.Cylinder);
        Box("FinishBanner", new Vector3(fx, 3.7f, fz),
            new Vector3(postX * 2f + 0.6f, 0.5f, 0.3f), "Obstacle", finishM);

        // Invisible trigger in the middle
        GameObject tz = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tz.name = "FinishTrigger";
        tz.transform.position = new Vector3(fx, 2f, fz);
        tz.transform.localScale = new Vector3(C - 1f, 4f, C - 1f);
        MeshRenderer mr = tz.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            UnityEngine.Object.DestroyImmediate(mr);
        }
        tz.GetComponent<Collider>().isTrigger = true;
        tz.transform.SetParent(L, true);
        AddScript(tz, "FinishZone");
    }

    static GameObject MakePlayer()
    {
        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Player";
        player.tag = "Player";
        player.transform.position = new Vector3(StartCol * C, 1.5f, (Rows - 1) * C + C);
        player.GetComponent<Renderer>().sharedMaterial = playM;
        Rigidbody prb = player.AddComponent<Rigidbody>();
        prb.constraints = RigidbodyConstraints.FreezeRotation;
        prb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        prb.interpolation = RigidbodyInterpolation.Interpolate;
        AddScript(player, "PlayerController");

        // Yellow disc that only the minimap camera can see
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "MinimapMarker";
        marker.transform.SetParent(player.transform, false);
        marker.transform.localPosition = new Vector3(0f, 5f, 0f);
        marker.transform.localScale = new Vector3(4f, 0.05f, 4f);
        marker.GetComponent<Renderer>().sharedMaterial = miniM;
        marker.layer = miniLayer;
        NoCollider(marker);

        return player;
    }

    static void SetupCamera(GameObject player)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }
        cam.transform.SetParent(null);
        cam.cullingMask = ~(1 << miniLayer);

        System.Type type = FindType("CameraFollow");
        if (type == null)
        {
            Debug.LogWarning("CameraFollow script not found. Camera will not follow.");
            return;
        }
        Component comp = cam.gameObject.GetComponent(type);
        if (comp == null)
        {
            comp = cam.gameObject.AddComponent(type);
        }
        SetField(comp, "target", player.transform);
        SetField(comp, "distance", 6f);
        SetField(comp, "height", 9f);
        SetField(comp, "yaw", 180f);
    }

    // Small square top-down camera in the top-right corner, without obstacles
    static void MakeMinimap()
    {
        GameObject mc = new GameObject("MinimapCamera");
        Camera cam = mc.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = (Rows + 2) * C / 2f + 2f;
        cam.transform.position = new Vector3((Cols - 1) * C / 2f, 100f, (Rows - 1) * C / 2f);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.08f, 0.10f);
        cam.cullingMask = ~(1 << obstLayer);
        cam.depth = 5f;
        cam.farClipPlane = 200f;
        AddScript(mc, "MinimapFit");
    }

    // ---------------------------------------------------------------
    // Obstacles
    // ---------------------------------------------------------------
    static void PlaceObstacles(System.Random rnd)
    {
        for (int i = 0; i < Cols; i++)
        {
            for (int j = 0; j < Rows; j++)
            {
                bool isStart = (i == StartCol && j == StartRow);
                bool isFinish = (i == FinishCol && j == FinishRow);
                if (isStart || isFinish)
                {
                    continue;
                }

                bool n = OpenN(i, j);
                bool s = OpenS(i, j);
                bool e = OpenE(i, j);
                bool w = OpenW(i, j);
                bool straightZ = n && s && !e && !w;
                bool straightX = e && w && !n && !s;
                Vector3 c = new Vector3(i * C, 0f, j * C);

                if (straightZ || straightX)
                {
                    if (rnd.NextDouble() > 0.85)
                    {
                        continue;
                    }
                    PlaceStraight(rnd.Next(15), c, straightZ, rnd);
                    obstacleCount++;
                }
                else
                {
                    if (rnd.NextDouble() > 0.70)
                    {
                        continue;
                    }
                    PlaceOpen(rnd.Next(7), c, rnd);
                    obstacleCount++;
                }
            }
        }
    }

    // Obstacles for straight corridors (they span the corridor width)
    static void PlaceStraight(int type, Vector3 c, bool z, System.Random rnd)
    {
        switch (type)
        {
            case 0: // Hurdle: jump over
                Box("Hurdle", P(c, z, 0f, 0.4f, 0f),
                    S(z, 0.8f, 0.8f, AcrossW), "Obstacle", obsM);
                break;

            case 1: // Slide bar: slide under
                Box("SlideBar", P(c, z, 0f, 1.4f, 0f),
                    S(z, 1.5f, 0.4f, AcrossW), "Obstacle", beamM);
                break;

            case 2: // Moving beam along the corridor
                {
                    GameObject b = Box("MovingBeam", P(c, z, -1.5f, 1.4f, 0f),
                        S(z, 1.5f, 0.4f, AcrossW), "Obstacle", beamM);
                    AddMover(b, AlongVec(z), 3f, 1.5f);
                    break;
                }

            case 3: // Gate that rises and falls
                {
                    GameObject g = Box("Gate", P(c, z, 0f, 1.5f, 0f),
                        S(z, 0.6f, 3f, AcrossW), "Obstacle", obsM);
                    AddMover(g, Vector3.up, 2.4f, 1.2f);
                    break;
                }

            case 4: // Block sweeping across the corridor
                {
                    GameObject b = Box("SideSlider", P(c, z, 0f, 1.1f, -1.8f),
                        S(z, 1.2f, 2.2f, 1.6f), "Obstacle", obsM);
                    AddMover(b, AcrossVec(z), 3.6f, 1.5f);
                    break;
                }

            case 5: // Low spinning cylinder arm (jump over)
                MakeSweeper(c, "LowSweeper", 0.45f, 0.5f, 1f, 0.45f,
                    RndSpeed(rnd), false);
                break;

            case 6: // High spinning cylinder arm (slide under)
                MakeSweeper(c, "HighSweeper", 1.4f, 0.4f, 1.6f, 0.3f,
                    RndSpeed(rnd), false);
                break;

            case 7: // Windmill: two low arms
                MakeSweeper(c, "Windmill", 0.45f, 0.5f, 1f, 0.45f,
                    RndSpeed(rnd) * 0.8f, true);
                break;

            case 8: // Slalom: two staggered walls
                Box("Slalom_A", P(c, z, -1.2f, 1.5f, -1.3f),
                    S(z, 0.6f, 3f, 3f), "Obstacle", obsM);
                Box("Slalom_B", P(c, z, 1.2f, 1.5f, 1.3f),
                    S(z, 0.6f, 3f, 3f), "Obstacle", obsM);
                break;

            case 9: // Step block: climb or jump over
                Box("StepBlock", P(c, z, 0f, 0.6f, 0f),
                    S(z, 2f, 1.2f, AcrossW), "Platform", platM);
                break;

            case 10: // Combo: slide bar, then hurdle
                Box("Combo_SlideBar", P(c, z, -1.7f, 1.4f, 0f),
                    S(z, 1.2f, 0.4f, AcrossW), "Obstacle", beamM);
                Box("Combo_Hurdle", P(c, z, 1.7f, 0.4f, 0f),
                    S(z, 0.8f, 0.8f, AcrossW), "Obstacle", obsM);
                break;

            case 11: // Long tunnel: slide the whole way
                Box("Tunnel", P(c, z, 0f, 1.6f, 0f),
                    S(z, 4f, 0.8f, AcrossW), "Obstacle", beamM);
                break;

            case 12: // Pendulum swinging across the corridor
                MakePendulum(c, z, rnd);
                break;

            case 13: // Laser grid that cycles on and off
                MakeLaser(c, z, rnd);
                break;

            default: // Falling boulder
                MakeBoulder(c);
                break;
        }
    }

    // Obstacles for corners, junctions and dead ends
    static void PlaceOpen(int type, Vector3 c, System.Random rnd)
    {
        switch (type)
        {
            case 0:
                MakeSweeper(c, "LowSweeper", 0.45f, 0.5f, 1f, 0.45f,
                    RndSpeed(rnd), false);
                break;

            case 1:
                MakeSweeper(c, "HighSweeper", 1.4f, 0.4f, 1.6f, 0.3f,
                    RndSpeed(rnd), false);
                break;

            case 2:
                MakeSweeper(c, "Windmill", 0.45f, 0.5f, 1f, 0.45f,
                    RndSpeed(rnd) * 0.8f, true);
                break;

            case 3: // Central pillar
                Box("Pillar", new Vector3(c.x, 1.5f, c.z),
                    new Vector3(1.6f, 1.5f, 1.6f), "Obstacle", obsM,
                    PrimitiveType.Cylinder);
                break;

            case 4:
                MakePendulum(c, rnd.Next(2) == 0, rnd);
                break;

            case 5: // Falling boulder
                MakeBoulder(c);
                break;

            default: // Patroller: block that crosses the cell
                {
                    bool alongZ = rnd.Next(2) == 0;
                    Vector3 pos = alongZ
                        ? new Vector3(c.x, 1f, c.z - 1.9f)
                        : new Vector3(c.x - 1.9f, 1f, c.z);
                    GameObject b = Box("Patroller", pos,
                        new Vector3(1.4f, 2f, 1.4f), "Obstacle", obsM);
                    AddMover(b, alongZ ? Vector3.forward : Vector3.right, 3.8f, 1.5f);
                    break;
                }
        }
    }

    static float RndSpeed(System.Random rnd)
    {
        float s = 50f + (float)rnd.NextDouble() * 40f;
        return rnd.Next(2) == 0 ? s : -s;
    }

    // Spinning cylinder arm(s) around a central hub
    static void MakeSweeper(Vector3 c, string name, float armY, float armThick,
        float hubH, float hubR, float speed, bool cross)
    {
        GameObject pivot = new GameObject(name);
        pivot.transform.SetParent(L, false);
        pivot.transform.position = c;
        pivot.tag = "Obstacle";
        pivot.layer = obstLayer;
        Rigidbody rb = pivot.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        AddChild(pivot.transform, PrimitiveType.Cylinder, "Hub",
            new Vector3(0f, hubH / 2f, 0f), Vector3.zero,
            new Vector3(hubR * 2f, hubH / 2f, hubR * 2f), spinM, true);

        AddChild(pivot.transform, PrimitiveType.Cylinder, "Arm",
            new Vector3(0f, armY, 0f), new Vector3(0f, 0f, 90f),
            new Vector3(armThick, 2.5f, armThick), spinM, true);

        if (cross)
        {
            AddChild(pivot.transform, PrimitiveType.Cylinder, "Arm2",
                new Vector3(0f, armY, 0f), new Vector3(90f, 0f, 0f),
                new Vector3(armThick, 2.5f, armThick), spinM, true);
        }

        Component r = AddScript(pivot, "Rotator");
        SetField(r, "speed", speed);
        SetField(r, "axis", Vector3.up);
    }

    // Hanging ball that swings across the corridor (thick rod and big ball)
    static void MakePendulum(Vector3 c, bool alongZ, System.Random rnd)
    {
        GameObject pivot = new GameObject("Pendulum");
        pivot.transform.SetParent(L, false);
        pivot.transform.position = new Vector3(c.x, 3.4f, c.z);
        pivot.tag = "Obstacle";
        pivot.layer = obstLayer;
        Rigidbody rb = pivot.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        AddChild(pivot.transform, PrimitiveType.Cube, "Anchor",
            Vector3.zero, Vector3.zero,
            new Vector3(0.6f, 0.3f, 0.6f), obsM, true);
        AddChild(pivot.transform, PrimitiveType.Cylinder, "Rod",
            new Vector3(0f, -1.3f, 0f), Vector3.zero,
            new Vector3(0.3f, 1.3f, 0.3f), spinM, true);
        AddChild(pivot.transform, PrimitiveType.Sphere, "Bob",
            new Vector3(0f, -2.6f, 0f), Vector3.zero,
            new Vector3(1.4f, 1.4f, 1.4f), spinM, true);

        Component p = AddScript(pivot, "Pendulum");
        SetField(p, "axis", alongZ ? Vector3.forward : Vector3.right);
        SetField(p, "angle", 50f);
        SetField(p, "speed", 1.2f + (float)rnd.NextDouble() * 0.6f);
        SetField(p, "phase", (float)rnd.NextDouble() * 6.28f);
    }

    // 6 thin horizontal laser lines across the corridor.
    // No colliders: you can walk through, but it costs a life while ON.
    static void MakeLaser(Vector3 c, bool z, System.Random rnd)
    {
        GameObject pivot = new GameObject("LaserGrid");
        pivot.transform.SetParent(L, false);
        pivot.transform.position = new Vector3(c.x, 1f, c.z);
        pivot.tag = "Obstacle";
        pivot.layer = obstLayer;

        // Local heights -0.8 .. 0.95 = world heights 0.2 .. 1.95
        float[] ys = { -0.8f, -0.45f, -0.1f, 0.25f, 0.6f, 0.95f };
        for (int k = 0; k < ys.Length; k++)
        {
            AddChild(pivot.transform, PrimitiveType.Cube, "Laser" + k,
                P(Vector3.zero, z, 0f, ys[k], 0f), Vector3.zero,
                S(z, 0.05f, 0.05f, AcrossW), laserM, false);
        }

        Component g = AddScript(pivot, "LaserGrid");
        SetField(g, "onTime", 1.8f + (float)rnd.NextDouble() * 0.8f);
        SetField(g, "offTime", 1.6f + (float)rnd.NextDouble() * 0.8f);
        SetField(g, "phase", (float)rnd.NextDouble() * 5f);
        SetField(g, "damageHalfExtents", S(z, 0.8f, 2.2f, AcrossW) * 0.5f);
    }

    // Heavy ball hanging high above the corridor that drops when you walk under it
    static void MakeBoulder(Vector3 c)
    {
        GameObject marker = Box("BoulderMarker", new Vector3(c.x, 0.02f, c.z),
            new Vector3(3.2f, 0.02f, 3.2f), null, markerM, PrimitiveType.Cylinder);
        NoCollider(marker);
        marker.layer = obstLayer;

        GameObject ball = Box("Boulder", new Vector3(c.x, 12f, c.z),
            new Vector3(1.8f, 1.8f, 1.8f), "Obstacle", boulderM, PrimitiveType.Sphere);
        Rigidbody rb = ball.AddComponent<Rigidbody>();
        rb.mass = 60f;
        rb.useGravity = false;
        rb.isKinematic = true;

        Component t = AddScript(ball, "BoulderTrap");
        SetField(t, "triggerRadius", 2.2f);
        SetField(t, "resetDelay", 6f);
    }

    // ---------------------------------------------------------------
    // "along" = direction of the corridor, "across" = sideways
    // ---------------------------------------------------------------
    static Vector3 P(Vector3 c, bool alongZ, float along, float y, float across)
    {
        if (alongZ)
        {
            return new Vector3(c.x + across, y, c.z + along);
        }
        return new Vector3(c.x + along, y, c.z + across);
    }

    static Vector3 S(bool alongZ, float sAlong, float sy, float sAcross)
    {
        if (alongZ)
        {
            return new Vector3(sAcross, sy, sAlong);
        }
        return new Vector3(sAlong, sy, sAcross);
    }

    static Vector3 AlongVec(bool alongZ)
    {
        return alongZ ? Vector3.forward : Vector3.right;
    }

    static Vector3 AcrossVec(bool alongZ)
    {
        return alongZ ? Vector3.right : Vector3.forward;
    }

    // ---------------------------------------------------------------
    // Object helpers
    // ---------------------------------------------------------------
    static GameObject Box(string name, Vector3 pos, Vector3 scale, string tag,
        Material mat, PrimitiveType type = PrimitiveType.Cube)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = scale;
        if (!string.IsNullOrEmpty(tag))
        {
            go.tag = tag;
            if (tag == "Obstacle" || tag == "Platform")
            {
                go.layer = obstLayer;
            }
        }
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.transform.SetParent(L, true);
        return go;
    }

    static GameObject AddChild(Transform parent, PrimitiveType type, string name,
        Vector3 localPos, Vector3 localEuler, Vector3 localScale,
        Material mat, bool collider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localEulerAngles = localEuler;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.tag = "Obstacle";
        go.layer = obstLayer;
        if (!collider)
        {
            NoCollider(go);
        }
        return go;
    }

    static void NoCollider(GameObject go)
    {
        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            UnityEngine.Object.DestroyImmediate(col);
        }
    }

    static void AddMover(GameObject go, Vector3 dir, float dist, float speed)
    {
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        Component c = AddScript(go, "MovingPlatform");
        SetField(c, "direction", dir);
        SetField(c, "distance", dist);
        SetField(c, "speed", speed);
    }

    static void DestroyByName(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go != null)
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    static Material MakeMat(string name, Color c)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }
        string path = "Assets/Materials/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null)
        {
            m.color = c;
            EditorUtility.SetDirty(m);
            return m;
        }
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null)
        {
            sh = Shader.Find("Standard");
        }
        m = new Material(sh);
        m.color = c;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void AddTag(string tag)
    {
        SerializedObject tm = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tags = tm.FindProperty("tags");
        for (int i = 0; i < tags.arraySize; i++)
        {
            if (tags.GetArrayElementAtIndex(i).stringValue == tag)
            {
                return;
            }
        }
        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
        tm.ApplyModifiedProperties();
    }

    static int EnsureLayer(string name)
    {
        SerializedObject tm = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tm.FindProperty("layers");
        for (int i = 8; i < 32; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue == name)
            {
                return i;
            }
        }
        for (int i = 8; i < 32; i++)
        {
            SerializedProperty sp = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(sp.stringValue))
            {
                sp.stringValue = name;
                tm.ApplyModifiedProperties();
                return i;
            }
        }
        Debug.LogWarning("No free layer slot for " + name);
        return 0;
    }

    static System.Type FindType(string name)
    {
        foreach (System.Reflection.Assembly a in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            System.Type t = a.GetType(name);
            if (t != null)
            {
                return t;
            }
        }
        return null;
    }

    static Component AddScript(GameObject go, string typeName)
    {
        System.Type t = FindType(typeName);
        if (t == null)
        {
            Debug.LogWarning(typeName + " script not found, skipped.");
            return null;
        }
        return go.AddComponent(t);
    }

    static void SetField(Component c, string field, object value)
    {
        if (c == null)
        {
            return;
        }
        System.Reflection.FieldInfo f = c.GetType().GetField(field);
        if (f != null)
        {
            f.SetValue(c, value);
        }
    }
}