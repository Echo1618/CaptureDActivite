
using System;
using System.Collections.Generic;
using UnityEngine;

public class InteractionDebugPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private OrchestraInteractionManager interactionManager;
    [SerializeField] private InstrumentSelector instrumentSelector;

    [Header("Display")]
    [SerializeField] private bool visible = true;
    [SerializeField] private float preferredWidth = 430f;
    [SerializeField] private int maxLogEntries = 12;

    private readonly List<string> eventLog = new List<string>();
    private Vector2 scrollPosition;

    public bool IsVisible => visible;

    public void ToggleVisible()
    {
        visible = !visible;
    }

    public void SetVisible(bool value)
    {
        visible = value;
    }

    private void OnEnable()
    {
        OrchestraEventBus.OnCommand += HandleCommand;
        OrchestraEventBus.OnGroupSelectionChanged += HandleSelectionChanged;
    }

    private void OnDisable()
    {
        OrchestraEventBus.OnCommand -= HandleCommand;
        OrchestraEventBus.OnGroupSelectionChanged -= HandleSelectionChanged;
    }

    private void HandleCommand(OrchestraCommand cmd)
    {
        string message;

        switch (cmd.Type)
        {
            case OrchestraCommandType.SetGlobalVolume:
            case OrchestraCommandType.SetGlobalFilter:
                message = $"{cmd.Type}: {cmd.Value:F2}";
                break;

            case OrchestraCommandType.SetGlobalMute:
                message = $"SetGlobalMute: {cmd.IsMuted}";
                break;

            case OrchestraCommandType.SetGroupVolume:
                message = $"SetGroupVolume: {cmd.Target} = {cmd.Value:F2}";
                break;

            case OrchestraCommandType.SetGroupMute:
                message = $"SetGroupMute: {cmd.Target} = {cmd.IsMuted}";
                break;

            case OrchestraCommandType.ChangePhase:
                message = $"ChangePhase: {cmd.Phase}";
                break;

            default:
                message = cmd.Type.ToString();
                break;
        }

        AddLog(message);
    }

    private void HandleSelectionChanged(
        InstrumentGroupId group,
        bool selected)
    {
        AddLog($"Selection: {group} = {selected}");
    }

    private void AddLog(string message)
    {
        eventLog.Insert(0, $"[{Time.time:F1}] {message}");

        int limit = Mathf.Max(1, maxLogEntries);

        if (eventLog.Count > limit)
            eventLog.RemoveRange(limit, eventLog.Count - limit);
    }

    private void OnGUI()
    {
        if (!visible)
            return;

        Rect rect = DebugPanelLayout.GetRect(
            DebugPanelLayout.PanelSide.Left,
            preferredWidth
        );

        GUILayout.BeginArea(rect, GUI.skin.box);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        GUILayout.Label("ORCHESTRA RAVE - INTERACTION");
        GUILayout.Space(8);

        DrawGlobalState();
        DrawPointingState();
        DrawGroups();
        DrawEvents();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawGlobalState()
    {
        GUILayout.Label("=== GLOBAL ===");

        if (interactionManager == null)
        {
            GUILayout.Label("InteractionManager missing");
            return;
        }

        GUILayout.Label($"Phase: {interactionManager.CurrentPhase}");
        GUILayout.Label($"Volume: {interactionManager.GlobalVolume:F2}");
        GUILayout.Label($"Filter: {interactionManager.GlobalFilter:F2}");
        GUILayout.Label($"Global Mute: {interactionManager.GlobalMuted}");
        GUILayout.Space(10);
    }

    private void DrawPointingState()
    {
        GUILayout.Label("=== SELECTION ===");

        if (instrumentSelector == null)
        {
            GUILayout.Label("InstrumentSelector missing");
            return;
        }

        GUILayout.Label($"Hovered: {instrumentSelector.HoveredGroup}");
        GUILayout.Label($"Candidate: {instrumentSelector.CandidateGroup}");
        GUILayout.Label($"Armed: {instrumentSelector.SelectionArmed}");
        GUILayout.Label(
            $"Interruption: {instrumentSelector.InterruptionTime:F2}s"
        );

        float progress = instrumentSelector.ConfirmationProgress01;
        GUILayout.Label($"Confirmation: {progress * 100f:F0}%");

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

        GUILayout.Space(10);
    }

    private void DrawGroups()
    {
        GUILayout.Label("=== INSTRUMENTS ===");

        if (interactionManager == null)
            return;

        foreach (InstrumentGroupId group
                 in Enum.GetValues(typeof(InstrumentGroupId)))
        {
            if (group == InstrumentGroupId.None)
                continue;

            bool selected = interactionManager.IsGroupSelected(group);
            bool muted = interactionManager.IsGroupMuted(group);
            float volume = interactionManager.GetGroupVolume(group);

            GUILayout.Label(
                $"{(selected ? "[X]" : "[ ]")} {group}"
            );
            GUILayout.Label(
                $"     Vol {volume:F2} | {(muted ? "MUTED" : "ACTIVE")}"
            );
        }

        GUILayout.Space(10);
    }

    private void DrawEvents()
    {
        GUILayout.Label("=== EVENT LOG ===");

        if (eventLog.Count == 0)
            GUILayout.Label("No events");
        else
        {
            foreach (string entry in eventLog)
                GUILayout.Label(entry);
        }
    }
}
