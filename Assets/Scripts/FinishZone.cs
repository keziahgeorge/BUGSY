using UnityEngine;

public class FinishZone : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
        {
            return;
        }
        Debug.Log("FINISH reached!");
        if (GameManager.Instance != null)
        {
            GameManager.Instance.Win();
        }
    }
}