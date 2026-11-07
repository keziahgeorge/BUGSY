using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.MLAgents;
using Unity.MLAgents.Policies;

public static class BugsyTrainingMode
{
    [MenuItem("BUGSY/Training Mode On")]
    public static void TrainingOn()
    {
        Apply(true);
    }

    [MenuItem("BUGSY/Training Mode Off")]
    public static void TrainingOff()
    {
        Apply(false);
    }

    static void Apply(bool on)
    {
        GameManager gm = Object.FindFirstObjectByType<GameManager>();
        GameObject player = GameObject.FindWithTag("Player");
        if (gm == null || player == null)
        {
            Debug.LogError("GameManager or Player not found. Open MazeBugFree first.");
            return;
        }

        BugsyAgent agent = player.GetComponent<BugsyAgent>();
        BehaviorParameters bp = player.GetComponent<BehaviorParameters>();
        DecisionRequester dr = player.GetComponent<DecisionRequester>();
        if (agent == null || bp == null || dr == null)
        {
            Debug.LogError("Agent not found. Run BUGSY > Add Agent To Player first.");
            return;
        }

        Undo.RecordObject(gm, "Training mode");
        gm.trainingMode = on;
        EditorUtility.SetDirty(gm);

        bp.BehaviorType = on ? BehaviorType.Default : BehaviorType.HeuristicOnly;
        dr.DecisionPeriod = on ? 10 : 5;
        agent.MaxStep = on ? 10000 : 2000;

        EditorUtility.SetDirty(player);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Training mode " + (on ? "ON" : "OFF")
            + ": Behavior Type = " + bp.BehaviorType
            + ", Decision Period = " + dr.DecisionPeriod
            + ", Max Step = " + agent.MaxStep + ". Press Ctrl+S to save.");
    }
}