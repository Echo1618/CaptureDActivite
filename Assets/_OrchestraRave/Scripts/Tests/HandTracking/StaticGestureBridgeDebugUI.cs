
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

public class StaticGestureBridgeDebugUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StaticGestureInteractionBridge bridge;
    [SerializeField] private InstrumentSelector instrumentSelector;
    [SerializeField] private OrchestraInteractionManager interactionManager;

    [Header("Display")]
    [SerializeField] private bool showPanel = true;
    [SerializeField] private float preferredWidth = 390f;

    private XRHandSubsystem hands;
    private readonly List<XRHandSubsystem> subsystems =
        new List<XRHandSubsystem>();

    private float nextSearchTime;
    private Vector2 scrollPosition;

    public void ToggleVisible()
    {
        showPanel = !showPanel;
    }

    private void Update()
    {
        if (hands != null && hands.running)
            return;

        if (Time.unscaledTime < nextSearchTime)
            return;

        nextSearchTime = Time.unscaledTime + 1f;

        subsystems.Clear();
        SubsystemManager.GetSubsystems(subsystems);

        hands = null;

        foreach (XRHandSubsystem subsystem in subsystems)
        {
            if (subsystem != null && subsystem.running)
            {
                hands = subsystem;
                break;
            }
        }
    }

    private void OnGUI()
    {
        if (!showPanel)
            return;

        Rect rect = DebugPanelLayout.GetRect(
            DebugPanelLayout.PanelSide.Right,
            preferredWidth
        );

        GUILayout.BeginArea(rect, GUI.skin.box);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        GUILayout.Label("ORCHESTRA RAVE - HAND TRACKING");
        GUILayout.Space(8f);

        DrawTracking();
        DrawGestures();
        DrawPointing();
        DrawAudio();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawTracking()
    {
        GUILayout.Label("=== XR HANDS ===");

        bool running = hands != null && hands.running;

        DrawStatus("Subsystem", running);

        if (running)
        {
            DrawStatus("Left Hand", hands.leftHand.isTracked);
            DrawStatus("Right Hand", hands.rightHand.isTracked);
        }

        GUILayout.Space(10f);
    }

    private void DrawGestures()
    {
        GUILayout.Label("=== STATIC GESTURES ===");

        if (bridge == null)
        {
            GUILayout.Label("Bridge missing");
            return;
        }

        DrawStatus("Right Finger Point", bridge.FingerPointActive);
        DrawStatus("Left Palm Up", bridge.PalmUpActive);
        DrawStatus("Right Palm Up", bridge.RightPalmUpActive);

        DrawStatus("Left Fist - Global Mute", bridge.LeftFistActive);
        DrawStatus("Right Fist - Group Mute", bridge.RightFistActive);
        DrawStatus("Right Thumb Down - Clear", bridge.ThumbDownActive);

        GUILayout.Space(10f);
    }

    private void DrawPointing()
    {
        GUILayout.Label("=== RAYCAST / DWELL ===");

        if (instrumentSelector == null)
        {
            GUILayout.Label("InstrumentSelector missing");
            return;
        }

        GUILayout.Label($"Hovered: {instrumentSelector.HoveredGroup}");
        GUILayout.Label($"Candidate: {instrumentSelector.CandidateGroup}");
        GUILayout.Label($"Armed: {instrumentSelector.SelectionArmed}");

        float progress = instrumentSelector.ConfirmationProgress01;

        GUILayout.Label($"Progress: {progress * 100f:F0}%");
        GUILayout.Label(
            $"Interruption: {instrumentSelector.InterruptionTime:F2}s"
        );

        Rect bar = GUILayoutUtility.GetRect(10f, 14f);
        GUI.Box(bar, GUIContent.none);

        if (Event.current.type == EventType.Repaint)
        {
            GUI.DrawTexture(
                new Rect(
                    bar.x,
                    bar.y,
                    bar.width * progress,
                    bar.height
                ),
                Texture2D.whiteTexture
            );
        }

        GUILayout.Space(10f);
    }

    private void DrawAudio()
    {
        GUILayout.Label("=== AUDIO COMMANDS ===");

        if (interactionManager == null)
        {
            GUILayout.Label("InteractionManager missing");
            return;
        }

        GUILayout.Label(
            $"Global Volume: {interactionManager.GlobalVolume:F2}"
        );

        GUILayout.Label(
            $"Global Mute: {interactionManager.GlobalMuted}"
        );

        GUILayout.Label(
            $"Global Filter: {interactionManager.GlobalFilter:F2}"
        );

        GUILayout.Label(
            $"Phase: {interactionManager.CurrentPhase}"
        );
    }

    private void DrawStatus(string label, bool active)
    {
        Color previousColor = GUI.contentColor;
        GUI.contentColor = active ? Color.green : Color.red;

        GUILayout.Label(
            $"{label}: {(active ? "ACTIVE" : "INACTIVE")}"
        );

        GUI.contentColor = previousColor;
    }
}
