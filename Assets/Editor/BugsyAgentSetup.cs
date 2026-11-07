using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public static class BugsyAgentSetup
{
    [MenuItem("BUGSY/Add Agent To Player")]
    public static void AddAgent()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            Debug.LogError("No Player found. Run BUGSY > Build Maze Level first.");
            return;
        }

        BugsyAgent agent = player.GetComponent<BugsyAgent>();
        if (agent == null)
        {
            agent = player.AddComponent<BugsyAgent>();
        }
        agent.MaxStep = 2000;

        BehaviorParameters bp = player.GetComponent<BehaviorParameters>();
        bp.BehaviorName = "BugsyAgent";
        bp.BrainParameters.VectorObservationSize = 21;
        bp.BrainParameters.NumStackedVectorObservations = 1;
        bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(new int[] { 5, 2, 2, 3 });
        bp.BehaviorType = BehaviorType.HeuristicOnly;

        DecisionRequester dr = player.GetComponent<DecisionRequester>();
        if (dr == null)
        {
            dr = player.AddComponent<DecisionRequester>();
        }
        dr.DecisionPeriod = 5;
        dr.TakeActionsBetweenDecisions = true;

        RayPerceptionSensorComponent3D ray = player.GetComponent<RayPerceptionSensorComponent3D>();
        if (ray == null)
        {
            ray = player.AddComponent<RayPerceptionSensorComponent3D>();
        }
        ray.SensorName = "RaySensor";
        ray.DetectableTags = new List<string> { "Wall", "Obstacle", "Platform" };
        ray.RaysPerDirection = 5;
        ray.MaxRayDegrees = 90f;
        ray.SphereCastRadius = 0.5f;
        ray.RayLength = 15f;
        ray.StartVerticalOffset = 0f;
        ray.EndVerticalOffset = 0f;

        EditorUtility.SetDirty(player);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("BugsyAgent added to Player (Heuristic mode). Press Ctrl+S to save.");
    }
}