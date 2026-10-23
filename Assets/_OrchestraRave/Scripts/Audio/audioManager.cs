
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public partial class AudioManager : MonoBehaviour
{
    [Serializable]
    public class InstrumentEntry
    {
        public string instrumentId;
        public InstrumentGroupId family;
        public AudioSource audioSource;

        [Range(0f, 1f)]
        public float initialVolume = 1f;

        public bool initiallyMuted = false;

        [NonSerialized] public float requestedVolume;
        [NonSerialized] public bool userMuted;
    }

    [Serializable]
    public class FamilyEntry
    {
        public InstrumentGroupId family;
        public string mixerVolumeParameter;

        [Range(0f, 1f)]
        public float initialVolume = 1f;

        [NonSerialized] public float requestedVolume;
    }

    public enum AudioPlaybackState
    {
        Unavailable,
        Stopped,
        Playing
    }

    [Serializable]
    public class InstrumentAudioState
    {
        public string instrumentId;
        public InstrumentGroupId family;
        public float requestedVolume;
        public float appliedSourceVolume;
        public bool userMuted;
        public bool actuallyMuted;
        public bool available;
        public bool isPlaying;
    }

    [Serializable]
    public class FamilyAudioState
    {
        public InstrumentGroupId family;
        public float requestedVolume;
        public float appliedVolume;
        public bool isMuted;
        public bool available;

        public List<InstrumentAudioState> instruments =
            new List<InstrumentAudioState>();
    }

    [Serializable]
    public class FamiliesAudioReport
    {
        public float globalVolume;
        public float appliedGlobalVolumeDb;

        public List<FamilyAudioState> families =
            new List<FamilyAudioState>();
    }

    [Header("Audio Mixer")]
    [SerializeField]
    private AudioMixer m_AudioMixer;

    [SerializeField]
    private string m_GlobalVolumeParameter = "GlobalVolume";

    [Header("Familles instrumentales")]
    [SerializeField]
    private List<FamilyEntry> m_Families =
        new List<FamilyEntry>();

    [Header("Instruments")]
    [SerializeField]
    private List<InstrumentEntry> m_Instruments =
        new List<InstrumentEntry>();

    [Header("Conversion des volumes")]
    [SerializeField]
    private float m_MinVolumeDb = -80f;

    [SerializeField]
    private float m_MaxVolumeDb = 0f;

    [Range(0f, 1f)]
    [SerializeField]
    private float m_InitialGlobalVolume = 1f;

    private float m_RequestedGlobalVolume = 1f;

    [Header("Phases musicales")]
    [Range(0f, 1f)]
    [SerializeField]
    private float m_TransitionElectroVolume = 0.35f;

    [Range(0f, 1f)]
    [SerializeField]
    private float m_TechnoElectroVolume = 1f;

    [Min(0f)]
    [SerializeField]
    private float m_PhaseTransitionDuration = 3f;

    [SerializeField]
    private bool m_StartAutomatically = true;

    private MusicPhase m_ActivePhase = MusicPhase.Classical;
    private float m_ElectroPhaseMultiplier;

    private void Awake()
    {
        m_RequestedGlobalVolume =
            Mathf.Clamp01(m_InitialGlobalVolume);

        foreach (FamilyEntry family in m_Families)
        {
            if (family == null) continue;

            family.requestedVolume =
                Mathf.Clamp01(family.initialVolume);
        }

        foreach (InstrumentEntry instrument in m_Instruments)
        {
            if (instrument == null) continue;

            instrument.requestedVolume =
                Mathf.Clamp01(instrument.initialVolume);

            instrument.userMuted = instrument.initiallyMuted;

            if (instrument.audioSource != null)
            {
                instrument.audioSource.playOnAwake = false;
            }
        }

        m_ActivePhase = MusicPhase.Classical;
        m_ElectroPhaseMultiplier = 0f;

        ApplyGlobalVolume();

        foreach (FamilyEntry family in m_Families)
        {
            if (family != null)
                ApplyFamilyVolume(family.family);
        }

        SetGlobalFilter(1f);
        ApplyAllInstrumentStates();
    }

    private void Start()
    {
        if (m_StartAutomatically)
            StartSynchronizedPlayback();
    }

    private void OnDisable()
    {
        StopPhaseFade();
    }

    private FamilyEntry FindFamily(InstrumentGroupId familyId)
    {
        return m_Families.Find(
            f => f != null && f.family == familyId);
    }

    private InstrumentEntry FindInstrument(string instrumentId)
    {
        return m_Instruments.Find(
            i => i != null && i.instrumentId == instrumentId);
    }

    private float NormalizedToDb(float volume)
    {
        volume = Mathf.Clamp01(volume);

        if (volume <= 0f)
            return m_MinVolumeDb;

        return Mathf.Lerp(
            m_MinVolumeDb,
            m_MaxVolumeDb,
            volume);
    }

    private float DbToNormalized(float db)
    {
        if (db <= m_MinVolumeDb)
            return 0f;

        float range = m_MaxVolumeDb - m_MinVolumeDb;

        if (Mathf.Abs(range) < 0.0001f)
            return 0f;

        return Mathf.Clamp01(
            (db - m_MinVolumeDb) / range);
    }
}
