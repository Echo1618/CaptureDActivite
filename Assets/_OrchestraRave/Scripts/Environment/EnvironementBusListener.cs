
using System;
using UnityEngine;
using Maestro.Environment;

public class EnvironmentBusListener : MonoBehaviour
{
    [Serializable]
    public class SectionBinding
    {
        public InstrumentGroupId group;
        public SectionHighlight highlight;
    }

    [Header("Sections")]
    [SerializeField]
    private SectionBinding[] sections;

    private void OnEnable()
    {
        OrchestraEventBus.OnGroupSelectionChanged += OnSelectionChanged;
    }

    private void OnDisable()
    {
        OrchestraEventBus.OnGroupSelectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged(
        InstrumentGroupId group,
        bool selected)
    {
        if (sections == null)
            return;

        foreach (SectionBinding section in sections)
        {
            if (section == null || section.highlight == null)
                continue;

            if (section.group == group)
            {
                section.highlight.SetSectionHighlighted(selected);
            }
        }
    }
}
