using UnityEngine;

public class LaserGrid : MonoBehaviour
{
    public float onTime = 2f;
    public float offTime = 2f;
    public float warnTime = 0.6f;
    public float phase = 0f;
    public Vector3 damageHalfExtents = new Vector3(2.8f, 1.3f, 0.4f);

    Renderer[] rends;
    int state = -1; // 0 = off, 1 = warning (visible, harmless), 2 = on (costs a life)

    void Start()
    {
        rends = GetComponentsInChildren<Renderer>();
        SetState(0);
    }

    void Update()
    {
        float cycle = onTime + offTime;
        float t = (Time.time + phase) % cycle;

        int want;
        if (t < onTime)
        {
            want = 2;
        }
        else if (t > cycle - warnTime)
        {
            want = 1;
        }
        else
        {
            want = 0;
        }

        if (want != state)
        {
            SetState(want);
        }

        if (state == 2 && GameManager.Instance != null && PlayerInside())
        {
            GameManager.Instance.LoseLife("laser");
        }
    }

    bool PlayerInside()
    {
        Collider[] hits = Physics.OverlapBox(transform.position, damageHalfExtents, Quaternion.identity);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].CompareTag("Player"))
            {
                return true;
            }
        }
        return false;
    }

    void SetState(int s)
    {
        state = s;
        for (int i = 0; i < rends.Length; i++)
        {
            rends[i].enabled = (s != 0);
        }
    }
}