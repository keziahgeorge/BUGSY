using UnityEngine;

public class MinimapFit : MonoBehaviour
{
    public float size = 0.26f;    // minimap height as a fraction of the screen height
    public float margin = 0.015f; // gap from the screen edge

    Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();
    }

    void Update()
    {
        if (cam == null)
        {
            return;
        }
        float aspect = (float)Screen.height / (float)Screen.width;
        float h = size;
        float w = size * aspect;
        cam.rect = new Rect(1f - w - margin * aspect, 1f - h - margin, w, h);
    }
}