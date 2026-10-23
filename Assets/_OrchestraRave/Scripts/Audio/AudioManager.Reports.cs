
using UnityEngine;

public partial class AudioManager
{
    public FamiliesAudioReport GetFamiliesAudioReport()
    {
        FamiliesAudioReport report = new FamiliesAudioReport();

        report.globalVolume = m_RequestedGlobalVolume;

        if (m_AudioMixer != null &&
            !string.IsNullOrWhiteSpace(m_GlobalVolumeParameter) &&
            m_AudioMixer.GetFloat(
                m_GlobalVolumeParameter, out float globalDb))
        {
            report.appliedGlobalVolumeDb = globalDb;
        }
        else
        {
            report.appliedGlobalVolumeDb = float.NaN;
        }

        foreach (FamilyEntry family in m_Families)
        {
            if (family == null) continue;

            FamilyAudioState state = new FamilyAudioState
            {
                family = family.family,
                requestedVolume = family.requestedVolume
            };

            float familyDb = 0f;

            state.available =
                m_AudioMixer != null &&
                !string.IsNullOrWhiteSpace(family.mixerVolumeParameter) &&
                m_AudioMixer.GetFloat(
                    family.mixerVolumeParameter,
                    out familyDb);

            state.appliedVolume = state.available
                ? DbToNormalized(familyDb)
                : float.NaN;

            bool found = false;
            bool allMuted = true;

            foreach (InstrumentEntry instrument in m_Instruments)
            {
                if (instrument == null ||
                    instrument.family != family.family)
                    continue;

                found = true;

                InstrumentAudioState instrumentState =
                    BuildInstrumentState(instrument);

                state.instruments.Add(instrumentState);

                if (!instrumentState.actuallyMuted)
                    allMuted = false;
            }

            state.isMuted = found && allMuted;

            report.families.Add(state);
        }

        return report;
    }

    private InstrumentAudioState BuildInstrumentState(
        InstrumentEntry instrument)
    {
        InstrumentAudioState state = new InstrumentAudioState
        {
            instrumentId = instrument.instrumentId,
            family = instrument.family,
            requestedVolume = instrument.requestedVolume,
            userMuted = instrument.userMuted,
            available = instrument.audioSource != null
        };

        if (state.available)
        {
            state.appliedSourceVolume =
                instrument.audioSource.volume;

            state.actuallyMuted =
                instrument.audioSource.mute;

            state.isPlaying =
                instrument.audioSource.isPlaying;
        }
        else
        {
            state.appliedSourceVolume = float.NaN;
            state.actuallyMuted = false;
            state.isPlaying = false;
        }

        return state;
    }

    public string GetAudioPhaseInformation()
    {
        return
            $"Phase audio : {m_ActivePhase} | " +
            $"Multiplicateur électro : " +
            $"{m_ElectroPhaseMultiplier:F2} | " +
            $"Transition en cours : " +
            $"{m_ElectroFadeCoroutine != null}";
    }

    public AudioPlaybackState GetPlaybackState()
    {
        bool hasSource = false;
        bool hasPlaying = false;

        foreach (InstrumentEntry instrument in m_Instruments)
        {
            if (instrument == null ||
                instrument.audioSource == null)
                continue;

            hasSource = true;

            if (instrument.audioSource.isPlaying)
                hasPlaying = true;
        }

        if (hasPlaying)
            return AudioPlaybackState.Playing;

        return hasSource
            ? AudioPlaybackState.Stopped
            : AudioPlaybackState.Unavailable;
    }

    public string GetPlaybackInformation()
    {
        switch (GetPlaybackState())
        {
            case AudioPlaybackState.Playing:
                return "Lecture active";

            case AudioPlaybackState.Stopped:
                return "Lecture arrêtée";

            default:
                return "Lecture indisponible";
        }
    }
}
