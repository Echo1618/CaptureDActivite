
using UnityEngine;

public partial class AudioManager
{
    [Header("Filtre global")]
    [SerializeField]
    private float m_MinCutoff = 250f;

    [SerializeField]
    private float m_MaxCutoff = 22000f;

    private float m_GlobalFilter = 1f;
    private bool m_GlobalMuted = false;

    public float GetGlobalVolume() => m_RequestedGlobalVolume;
    public float GetGlobalFilter() => m_GlobalFilter;
    public bool IsGlobalMuted() => m_GlobalMuted;

    // VOLUME GLOBAL

    public void SetGlobalVolume(float volume)
    {
        if (float.IsNaN(volume) || float.IsInfinity(volume))
            return;

        m_RequestedGlobalVolume = Mathf.Clamp01(volume);
        ApplyGlobalVolume();
    }

    public void SetGlobalMute(bool muted)
    {
        m_GlobalMuted = muted;
        ApplyGlobalVolume();
    }

    private void ApplyGlobalVolume()
    {
        if (m_AudioMixer == null ||
            string.IsNullOrWhiteSpace(m_GlobalVolumeParameter))
            return;

        float volume = m_GlobalMuted
            ? 0f
            : m_RequestedGlobalVolume;

        m_AudioMixer.SetFloat(
            m_GlobalVolumeParameter,
            NormalizedToDb(volume));
    }

    // VOLUME DES FAMILLES

    public void SetFamilyVolume(
        InstrumentGroupId familyId,
        float volume)
    {
        FamilyEntry family = FindFamily(familyId);

        if (family == null)
        {
            Debug.LogWarning(
                $"AudioManager : famille absente : {familyId}");
            return;
        }

        if (float.IsNaN(volume) || float.IsInfinity(volume))
            return;

        family.requestedVolume = Mathf.Clamp01(volume);
        ApplyFamilyVolume(familyId);
    }

    public float GetFamilyVolume(InstrumentGroupId familyId)
    {
        FamilyEntry family = FindFamily(familyId);
        return family != null ? family.requestedVolume : 0f;
    }

    private void ApplyFamilyVolume(InstrumentGroupId familyId)
    {
        FamilyEntry family = FindFamily(familyId);

        if (family == null ||
            m_AudioMixer == null ||
            string.IsNullOrWhiteSpace(family.mixerVolumeParameter))
            return;

        m_AudioMixer.SetFloat(
            family.mixerVolumeParameter,
            NormalizedToDb(family.requestedVolume));
    }

    // VOLUME INDIVIDUEL

    public void SetInstrumentVolume(
        string instrumentId,
        float volume)
    {
        InstrumentEntry instrument = FindInstrument(instrumentId);

        if (instrument == null)
        {
            Debug.LogWarning(
                $"Instrument introuvable : {instrumentId}");
            return;
        }

        if (float.IsNaN(volume) || float.IsInfinity(volume))
            return;

        instrument.requestedVolume = Mathf.Clamp01(volume);
        ApplyInstrumentState(instrument);
    }

    public float GetInstrumentVolume(string instrumentId)
    {
        InstrumentEntry instrument = FindInstrument(instrumentId);

        return instrument != null
            ? instrument.requestedVolume
            : 0f;
    }

    // MUTE INDIVIDUEL

    public void SetInstrumentMute(
        string instrumentId,
        bool muted)
    {
        InstrumentEntry instrument = FindInstrument(instrumentId);

        if (instrument == null)
        {
            Debug.LogWarning(
                $"Instrument introuvable : {instrumentId}");
            return;
        }

        instrument.userMuted = muted;
        ApplyInstrumentState(instrument);
    }

    public void ToggleInstrumentMute(string instrumentId)
    {
        InstrumentEntry instrument = FindInstrument(instrumentId);

        if (instrument != null)
            SetInstrumentMute(instrumentId, !instrument.userMuted);
    }

    // MUTE PAR FAMILLE

    public void SetFamilyMute(
        InstrumentGroupId familyId,
        bool muted)
    {
        bool found = false;

        foreach (InstrumentEntry instrument in m_Instruments)
        {
            if (instrument == null ||
                instrument.family != familyId)
                continue;

            found = true;
            instrument.userMuted = muted;
            ApplyInstrumentState(instrument);
        }

        if (!found)
        {
            Debug.LogWarning(
                $"Aucun instrument pour {familyId}");
        }
    }

    public void ToggleFamilyMute(InstrumentGroupId familyId)
    {
        SetFamilyMute(familyId, !IsFamilyUserMuted(familyId));
    }

    private bool IsFamilyUserMuted(InstrumentGroupId familyId)
    {
        bool found = false;

        foreach (InstrumentEntry instrument in m_Instruments)
        {
            if (instrument == null ||
                instrument.family != familyId)
                continue;

            found = true;

            if (!instrument.userMuted)
                return false;
        }

        return found;
    }

    public bool IsInstrumentMuted(string instrumentId)
    {
        InstrumentEntry instrument = FindInstrument(instrumentId);

        return instrument != null &&
            instrument.audioSource != null &&
            instrument.audioSource.mute;
    }

    public bool IsFamilyMuted(InstrumentGroupId familyId)
    {
        bool found = false;

        foreach (InstrumentEntry instrument in m_Instruments)
        {
            if (instrument == null ||
                instrument.family != familyId)
                continue;

            found = true;

            if (instrument.audioSource != null &&
                !instrument.audioSource.mute)
                return false;
        }

        return found;
    }

    // FILTRE GLOBAL

    public void SetGlobalFilter(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return;

        m_GlobalFilter = Mathf.Clamp01(value);

        // Interpolation logarithmique : plus naturelle
        // pour le contrôle d'une fréquence audio.
        float min = Mathf.Max(10f, m_MinCutoff);
        float max = Mathf.Max(min + 1f, m_MaxCutoff);

        float cutoff = min * Mathf.Pow(
            max / min,
            m_GlobalFilter);

        foreach (InstrumentEntry instrument in m_Instruments)
        {
            if (instrument == null ||
                instrument.audioSource == null)
                continue;

            AudioLowPassFilter filter =
                instrument.audioSource
                    .GetComponent<AudioLowPassFilter>();

            if (filter != null)
                filter.cutoffFrequency = cutoff;
        }
    }

    // APPLICATION DES ETATS

    private void ApplyAllInstrumentStates()
    {
        foreach (InstrumentEntry instrument in m_Instruments)
            ApplyInstrumentState(instrument);
    }

    private void ApplyInstrumentState(InstrumentEntry instrument)
    {
        if (instrument == null || instrument.audioSource == null)
            return;

        bool isElectro =
            instrument.family == InstrumentGroupId.Electro;

        float phaseMultiplier = isElectro
            ? m_ElectroPhaseMultiplier
            : 1f;

        instrument.audioSource.volume = Mathf.Clamp01(
            instrument.requestedVolume * phaseMultiplier);

        bool phaseMuted = isElectro &&
                          m_ElectroPhaseMultiplier <= 0.0001f;

        instrument.audioSource.mute =
            instrument.userMuted || phaseMuted;
    }
}
