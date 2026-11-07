using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class BugsyReachableCells
{
    static Vector3 Center(CoverageTracker ct, int gx, int gz)
    {
        float x = (gx + 0.5f) * ct.gridSize - ct.origin;
        float z = (gz + 0.5f) * ct.gridSize - ct.origin;
        return new Vector3(x, 1f, z);
    }

    static bool Blocked(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float len = dir.magnitude;
        RaycastHit[] hits = Physics.RaycastAll(from, dir.normalized, len, ~0,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.CompareTag("Wall")) return true;
        }
        return false;
    }

    [MenuItem("BUGSY/Count Reachable Cells")]
    static void Count()
    {
        CoverageTracker ct = Object.FindFirstObjectByType<CoverageTracker>();
        if (ct == null)
        {
            Debug.LogError("No CoverageTracker found in this scene.");
            return;
        }
        Physics.SyncTransforms();

        int n = ct.gridCount;
        int sx = Mathf.FloorToInt((ct.transform.position.x + ct.origin) / ct.gridSize);
        int sz = Mathf.FloorToInt((ct.transform.position.z + ct.origin) / ct.gridSize);

        bool[,] seen = new bool[n, n];
        Queue<Vector2Int> q = new Queue<Vector2Int>();
        q.Enqueue(new Vector2Int(sx, sz));
        seen[sx, sz] = true;

        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };
        int count = 0;

        while (q.Count > 0)
        {
            Vector2Int c = q.Dequeue();
            count++;
            Vector3 from = Center(ct, c.x, c.y);
            for (int k = 0; k < 4; k++)
            {
                int nx = c.x + dx[k];
                int nz = c.y + dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (seen[nx, nz]) continue;
                if (Blocked(from, Center(ct, nx, nz))) continue;
                seen[nx, nz] = true;
                q.Enqueue(new Vector2Int(nx, nz));
            }
        }

        Debug.Log("REACHABLE CELLS = " + count + "  (start cell " + sx + "," + sz + ")");
    }
}