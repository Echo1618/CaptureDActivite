
    
  
using System;
using System.Collections.Generic;
using UnityEngine;

public class OrchestraInteractionManager : MonoBehaviour
{
    private readonly HashSet<InstrumentGroupId> selectedGroups = new HashSet<InstrumentGroupId>();
    private readonly Dictionary<InstrumentGroupId, bool> groupMuteStates = new Dictionary<InstrumentGroupId, bool>();

    private MusicPhase m_currentPhase = MusicPhase.Classical;
    private float m_globalVolume = 1f;
    private float m_globalFilter = 1f;

    private const float ValueChangeThreshold = 0.01f;

    public void SetGlobalVolume(float p_value)
    {
        if (float.IsNaN(p_value)) return;
        p_value = Mathf.Clamp01(p_value);
        if (Mathf.Abs(m_globalVolume - p_value) < ValueChangeThreshold) return;

        m_globalVolume = p_value;
        OrchestraEventBus.SendCommand(new OrchestraCommand(
            type: OrchestraCommandType.SetGlobalVolume,
            value: m_globalVolume));
    }

    public void SetGlobalFilter(float p_value)
    {
        if (float.IsNaN(p_value)) return;
        p_value = Mathf.Clamp01(p_value);
        if (Mathf.Abs(m_globalFilter - p_value) < ValueChangeThreshold) return;

        m_globalFilter = p_value;
        OrchestraEventBus.SendCommand(new OrchestraCommand(
            type: OrchestraCommandType.SetGlobalFilter,
            value: m_globalFilter));
    }

    public void ToggleGroupSelection(InstrumentGroupId p_group)
    {
        if (!IsValidGroup(p_group)) return;

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

        OrchestraEventBus.SendSelectionChanged(p_group, isNowSelected);
    }

    public void ClearGroupSelection()
    {
        foreach (InstrumentGroupId group in selectedGroups)
        {
            OrchestraEventBus.SendSelectionChanged(group, false);
        }
        selectedGroups.Clear();
    }

    public void ToggleSelectedGroupsMute()
    {
        if (selectedGroups.Count == 0) return;

        // Si tous les groupes sont muets, on les réactive ; sinon on les coupe tous.
        bool allMuted = true;
        foreach (InstrumentGroupId group in selectedGroups)
        {
            bool muted;
            if (!groupMuteStates.TryGetValue(group, out muted) || !muted)
            {
                allMuted = false;
                break;
            }
        }

        bool shouldMute = !allMuted;
        foreach (InstrumentGroupId group in selectedGroups)
        {
            groupMuteStates[group] = shouldMute;
            OrchestraEventBus.SendCommand(new OrchestraCommand(
                type: OrchestraCommandType.SetGroupMute,
                target: group,
                isMuted: shouldMute));
        }
    }

    public void SetSelectedGroupsVolume(float p_value)
    {
        if (selectedGroups.Count == 0 || float.IsNaN(p_value)) return;
        p_value = Mathf.Clamp01(p_value);

        // Volume absolu : tous les groupes sélectionnés reçoivent la même valeur.
        foreach (InstrumentGroupId group in selectedGroups)
        {
            OrchestraEventBus.SendCommand(new OrchestraCommand(
                type: OrchestraCommandType.SetGroupVolume,
                target: group,
                value: p_value));
        }
    }

    public void ChangePhase(MusicPhase p_phase)
    {
        if (!Enum.IsDefined(typeof(MusicPhase), p_phase) || p_phase == m_currentPhase)
            return;

        m_currentPhase = p_phase;
        OrchestraEventBus.SendCommand(new OrchestraCommand(
            type: OrchestraCommandType.ChangePhase,
            phase: m_currentPhase));
    }

    private static bool IsValidGroup(InstrumentGroupId group)
    {
        return group != InstrumentGroupId.None
            && Enum.IsDefined(typeof(InstrumentGroupId), group);
    }
}

