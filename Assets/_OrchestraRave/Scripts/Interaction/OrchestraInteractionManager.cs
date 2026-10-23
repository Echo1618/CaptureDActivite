
using System;
using System.Collections.Generic;
using UnityEngine;

public class OrchestraInteractionManager : MonoBehaviour
{
    private readonly HashSet<InstrumentGroupId> selectedGroups =
        new HashSet<InstrumentGroupId>();

    private readonly Dictionary<InstrumentGroupId, bool> groupMuteStates =
        new Dictionary<InstrumentGroupId, bool>();

    private readonly Dictionary<InstrumentGroupId, float> groupVolumes =
        new Dictionary<InstrumentGroupId, float>();

    private MusicPhase m_currentPhase = MusicPhase.Classical;
    private float m_globalVolume = 1f;
    private float m_globalFilter = 1f;

    private const float ValueChangeThreshold = 0.01f;

    // Lecture des états par les outils de debug
    public MusicPhase CurrentPhase => m_currentPhase;
    public float GlobalVolume => m_globalVolume;
    public float GlobalFilter => m_globalFilter;

    public IReadOnlyCollection<InstrumentGroupId> SelectedGroups =>
        selectedGroups;

    public bool IsGroupSelected(InstrumentGroupId group)
    {
        return selectedGroups.Contains(group);
    }

    public bool IsGroupMuted(InstrumentGroupId group)
    {
        return groupMuteStates.TryGetValue(group, out bool muted)
            && muted;
    }

    public float GetGroupVolume(InstrumentGroupId group)
    {
        return groupVolumes.TryGetValue(group, out float volume)
            ? volume
            : 1f;
    }

    // --------------------------------------------------
    // COMMANDES GLOBALES
    // --------------------------------------------------

    public void SetGlobalVolume(float p_value)
    {
        if (float.IsNaN(p_value) ||
            float.IsInfinity(p_value))
            return;

        p_value = Mathf.Clamp01(p_value);

        if (Mathf.Abs(m_globalVolume - p_value)
            < ValueChangeThreshold)
            return;

        m_globalVolume = p_value;

        OrchestraEventBus.SendCommand(
            new OrchestraCommand(
                type: OrchestraCommandType.SetGlobalVolume,
                value: m_globalVolume
            )
        );
    }

    public void SetGlobalFilter(float p_value)
    {
        if (float.IsNaN(p_value) ||
            float.IsInfinity(p_value))
            return;

        p_value = Mathf.Clamp01(p_value);

        if (Mathf.Abs(m_globalFilter - p_value)
            < ValueChangeThreshold)
            return;

        m_globalFilter = p_value;

        OrchestraEventBus.SendCommand(
            new OrchestraCommand(
                type: OrchestraCommandType.SetGlobalFilter,
                value: m_globalFilter
            )
        );
    }

    // --------------------------------------------------
    // SELECTION MULTIPLE
    // --------------------------------------------------

    public void ToggleGroupSelection(InstrumentGroupId p_group)
    {
        if (!IsValidGroup(p_group))
            return;

        bool isNowSelected;

        if (selectedGroups.Remove(p_group))
        {
            isNowSelected = false;
        }
        else
        {
            selectedGroups.Add(p_group);
            isNowSelected = true;
        }

        OrchestraEventBus.SendSelectionChanged(
            p_group,
            isNowSelected
        );
    }

    public void ClearGroupSelection()
    {
        if (selectedGroups.Count == 0)
            return;

        // Copie pour éviter toute modification de collection
        // pendant la diffusion des événements.
        var groups = new List<InstrumentGroupId>(selectedGroups);

        selectedGroups.Clear();

        foreach (InstrumentGroupId group in groups)
        {
            OrchestraEventBus.SendSelectionChanged(group, false);
        }
    }

    // --------------------------------------------------
    // MUTE DES GROUPES SELECTIONNES
    // --------------------------------------------------

    public void ToggleSelectedGroupsMute()
    {
        if (selectedGroups.Count == 0)
            return;

        bool allMuted = true;

        foreach (InstrumentGroupId group in selectedGroups)
        {
            if (!IsGroupMuted(group))
            {
                allMuted = false;
                break;
            }
        }

        bool shouldMute = !allMuted;

        foreach (InstrumentGroupId group in selectedGroups)
        {
            groupMuteStates[group] = shouldMute;

            OrchestraEventBus.SendCommand(
                new OrchestraCommand(
                    type: OrchestraCommandType.SetGroupMute,
                    target: group,
                    isMuted: shouldMute
                )
            );
        }
    }

    // --------------------------------------------------
    // VOLUME DES GROUPES SELECTIONNES
    // --------------------------------------------------

    public void SetSelectedGroupsVolume(float p_value)
    {
        if (selectedGroups.Count == 0 ||
            float.IsNaN(p_value) ||
            float.IsInfinity(p_value))
            return;

        p_value = Mathf.Clamp01(p_value);

        foreach (InstrumentGroupId group in selectedGroups)
        {
            float previousVolume = GetGroupVolume(group);

            if (Mathf.Abs(previousVolume - p_value)
                < ValueChangeThreshold)
                continue;

            groupVolumes[group] = p_value;

            OrchestraEventBus.SendCommand(
                new OrchestraCommand(
                    type: OrchestraCommandType.SetGroupVolume,
                    target: group,
                    value: p_value
                )
            );
        }
    }

    // --------------------------------------------------
    // PHASES MUSICALES
    // --------------------------------------------------

    public void ChangePhase(MusicPhase p_phase)
    {
        if (!Enum.IsDefined(typeof(MusicPhase), p_phase))
            return;

        if (p_phase == m_currentPhase)
            return;

        m_currentPhase = p_phase;

        OrchestraEventBus.SendCommand(
            new OrchestraCommand(
                type: OrchestraCommandType.ChangePhase,
                phase: m_currentPhase
            )
        );
    }

    // --------------------------------------------------
    // VALIDATION
    // --------------------------------------------------

    private static bool IsValidGroup(InstrumentGroupId group)
    {
        return group != InstrumentGroupId.None &&
            Enum.IsDefined(typeof(InstrumentGroupId), group);
    }
}
