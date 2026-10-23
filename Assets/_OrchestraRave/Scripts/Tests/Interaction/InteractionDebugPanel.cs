
using System;
using System.Collections.Generic;
using UnityEngine;

public class InteractionDebugPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private OrchestraInteractionManager interactionManager;

    [SerializeField]
    private InstrumentSelector instrumentSelector;

    [Header("Display")]
    [SerializeField]
    private bool visible = true;

    [SerializeField]
    private int maxLogEntries = 12;

    [SerializeField]
    private float panelWidth = 440f;

    private readonly List<string> eventLog =
        new List<string>();

    private Vector2 scrollPosition;

    public bool IsVisible => visible;

    // --------------------------------------------------
    // EVENTS
    // --------------------------------------------------

    private void OnEnable()
    {
        OrchestraEventBus.OnCommand += HandleCommand;
        OrchestraEventBus.OnGroupSelectionChanged +=
            HandleSelectionChanged;
    }

    private void OnDisable()
    {
        OrchestraEventBus.OnCommand -= HandleCommand;
        OrchestraEventBus.OnGroupSelectionChanged -=
            HandleSelectionChanged;
    }

    public void ToggleVisible()
    {
        visible = !visible;
    }

    public void SetVisible(bool value)
    {
        visible = value;
    }

    private void HandleCommand(OrchestraCommand command)
    {
        string message;

        switch (command.Type)
        {
            case OrchestraCommandType.SetGlobalVolume:
            case OrchestraCommandType.SetGlobalFilter:
                message =
                    $"{command.Type}: {command.Value:F2}";
                break;

            case OrchestraCommandType.SetGroupVolume:
                message =
                    $"{command.Type}: {command.Target} " +
                    $"= {command.Value:F2}";
                break;

            case OrchestraCommandType.SetGroupMute:
                message =
                    $"{command.Type}: {command.Target} " +
                    $"= {command.IsMuted}";
                break;

            case OrchestraCommandType.ChangePhase:
                message =
                    $"ChangePhase: {command.Phase}";
                break;

            default:
                message = command.Type.ToString();
                break;
        }

        AddLog(message);
    }

    private void HandleSelectionChanged(
        InstrumentGroupId group,
        bool selected)
    {
        AddLog(
            $"Selection: {group} = {selected}"
        );
    }

    private void AddLog(string message)
    {
        string time = DateTime.Now.ToString("HH:mm:ss");

        eventLog.Insert(0, $"[{time}] {message}");

        int limit = Mathf.Max(1, maxLogEntries);

        if (eventLog.Count > limit)
        {
            eventLog.RemoveRange(
                limit,
                eventLog.Count - limit
            );
        }
    }

    // --------------------------------------------------
    // DEBUG UI
    // --------------------------------------------------

    private void OnGUI()
    {
        if (!visible)
            return;

        float width = Mathf.Min(
            panelWidth,
            Screen.width - 20f
        );

        float height = Mathf.Min(
            650f,
            Screen.height - 20f
        );

        if (width <= 0f || height <= 0f)
            return;

        GUILayout.BeginArea(
            new Rect(10f, 10f, width, height),
            GUI.skin.box
        );

        scrollPosition = GUILayout.BeginScrollView(
            scrollPosition
        );

        DrawHeader();
        DrawGlobalState();
        DrawPointingState();
        DrawGroups();
        DrawEventLog();
        DrawControls();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawHeader()
    {
        GUILayout.Label("ORCHESTRA RAVE — DEBUG");
        GUILayout.Label("Interaction / EventBus");
        GUILayout.Space(10f);
    }

    private void DrawGlobalState()
    {
        GUILayout.Label("=== GLOBAL STATE ===");

        if (interactionManager == null)
        {
            GUILayout.Label(
                "InteractionManager : MISSING"
            );
            return;
        }

        GUILayout.Label(
            $"Phase : {interactionManager.CurrentPhase}"
        );

        GUILayout.Label(
            $"Global volume : " +
            $"{interactionManager.GlobalVolume:F2}"
        );

        GUILayout.Label(
            $"Global filter : " +
            $"{interactionManager.GlobalFilter:F2}"
        );

        GUILayout.Space(10f);
    }

    private void DrawPointingState()
    {
        GUILayout.Label("=== POINTING ===");

        if (instrumentSelector == null)
        {
            GUILayout.Label(
                "InstrumentSelector : MISSING"
            );
            return;
        }

        GUILayout.Label(
            $"Hovered : {instrumentSelector.HoveredGroup}"
        );

        GUILayout.Label(
            $"Candidate : {instrumentSelector.CandidateGroup}"
        );

        GUILayout.Label(
            $"Armed : {instrumentSelector.SelectionArmed}"
        );

        GUILayout.Label(
            $"Interruption : " +
            $"{instrumentSelector.InterruptionTime:F2}s"
        );

        float progress =
            instrumentSelector.ConfirmationProgress01;

        GUILayout.Label(
            $"Confirmation : {progress * 100f:F0}%"
        );

        Rect progressRect =
            GUILayoutUtility.GetRect(100f, 16f);

        GUI.Box(progressRect, GUIContent.none);

        Rect fillRect = new Rect(
            progressRect.x,
            progressRect.y,
            progressRect.width * progress,
            progressRect.height
        );

        GUI.DrawTexture(
            fillRect,
            Texture2D.whiteTexture
        );

        GUILayout.Space(10f);
    }

    private void DrawGroups()
    {
        GUILayout.Label("=== INSTRUMENT GROUPS ===");

        if (interactionManager == null)
            return;

        foreach (InstrumentGroupId group
                 in Enum.GetValues(typeof(InstrumentGroupId)))
        {
            if (group == InstrumentGroupId.None)
                continue;

            bool selected =
                interactionManager.IsGroupSelected(group);

            bool muted =
                interactionManager.IsGroupMuted(group);

            float volume =
                interactionManager.GetGroupVolume(group);

            string selection = selected ? "[X]" : "[ ]";
            string mute = muted ? "MUTED" : "ACTIVE";

            GUILayout.Label(
                $"{selection} {group}"
            );

            GUILayout.Label(
                $"     Volume: {volume:F2} | {mute}"
            );
        }

        GUILayout.Space(10f);
    }

    private void DrawEventLog()
    {
        GUILayout.Label("=== EVENT LOG ===");

        if (eventLog.Count == 0)
        {
            GUILayout.Label("No events yet.");
        }
        else
        {
            foreach (string entry in eventLog)
            {
                GUILayout.Label(entry);
            }
        }

        GUILayout.Space(10f);
    }

    private void DrawControls()
    {
        GUILayout.Label("=== CONTROLS ===");

        GUILayout.Label("Mouse : point at instrument");
        GUILayout.Label("C : clear selection");
        GUILayout.Label("M : mute / unmute");
        GUILayout.Label("Up / Down : global volume");
        GUILayout.Label("Left / Right : global filter");
        GUILayout.Label("Page Up / Down : group volume");
        GUILayout.Label("1 / 2 / 3 : music phase");
        GUILayout.Label("F2 : show / hide debug");
    }
}
