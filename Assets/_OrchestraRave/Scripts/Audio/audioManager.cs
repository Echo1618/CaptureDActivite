
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    // =========================================================
    // STRUCTURES
    // =========================================================

    [Serializable]
    public class InstrumentEntry
    {
        [Tooltip("Identifiant unique, par exemple Violon ou Synthé")]
        public string instrumentId;

        public InstrumentGroupId family;

        public AudioSource audioSource;

        [Range(0f, 1f)]
        public float initialVolume = 1f;

        [Tooltip("Mute demandé par l'utilisateur")]
        public bool initiallyMuted = false;

        [NonSerialized]
        public float requestedVolume;

        [NonSerialized]
        public bool userMuted;
    }

    [Serializable]
    public class FamilyEntry
    {
        public InstrumentGroupId family;

        [Tooltip("Nom du paramètre exposé dans l'Audio Mixer")]
        public string mixerVolumeParameter;

        [Range(0f, 1f)]
        public float initialVolume = 1f;

        [NonSerialized]
        public float requestedVolume;
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

    // =========================================================
    // AUDIO MIXER
    // =========================================================

    [Header("Audio Mixer")]

    [SerializeField]
    private AudioMixer m_AudioMixer;

    [Tooltip("Nom du paramètre global exposé dans l'Audio Mixer")]
    [SerializeField]
    private string m_GlobalVolumeParameter = "GlobalVolume";

    [Header("Familles instrumentales")]

    [SerializeField]
    private List<FamilyEntry> m_Families = new List<FamilyEntry>();

    [Header("Instruments")]

    [SerializeField]
    private List<InstrumentEntry> m_Instruments =
        new List<InstrumentEntry>();

    // =========================================================
    // VOLUMES
    // =========================================================

    [Header("Conversion des volumes")]

    [Tooltip("Valeur minimale en dB correspondant au volume zéro")]
    [SerializeField]
    private float m_MinVolumeDb = -80f;

    [Tooltip("Valeur maximale en dB correspondant au volume 1")]
    [SerializeField]
    private float m_MaxVolumeDb = 0f;

    [Range(0f, 1f)]
    [SerializeField]
    private float m_InitialGlobalVolume = 1f;

    private float m_RequestedGlobalVolume = 1f;

    // =========================================================
    // PHASES MUSICALES
    // =========================================================

    [Header("Phases musicales")]

    [Tooltip("Multiplicateur électro en phase Transition")]
    [Range(0f, 1f)]
    [SerializeField]
    private float m_TransitionElectroVolume = 0.35f;

    [Tooltip("Multiplicateur électro en phase Techno")]
    [Range(0f, 1f)]
    [SerializeField]
    private float m_TechnoElectroVolume = 1f;

    [Tooltip("Durée du fondu électro en secondes")]
    [Min(0f)]
    [SerializeField]
    private float m_PhaseTransitionDuration = 3f;

    [Tooltip("Durée du fondu des autres paramètres n'est pas gérée ici")]
    [SerializeField]
    private bool m_StartAutomatically = true;

    private MusicPhase m_ActivePhase = MusicPhase.Classical;

    private float m_ElectroPhaseMultiplier = 0f;

    private Coroutine m_ElectroFadeCoroutine;

    // =========================================================
    // INITIALISATION
    // =========================================================

    private void Awake()
    {
        m_RequestedGlobalVolume =
            Mathf.Clamp01(m_InitialGlobalVolume);

        foreach (FamilyEntry family in m_Families)
        {
            family.requestedVolume =
                Mathf.Clamp01(family.initialVolume);
        }

        foreach (InstrumentEntry instrument in m_Instruments)
        {
            instrument.requestedVolume =
                Mathf.Clamp01(instrument.initialVolume);

            instrument.userMuted = instrument.initiallyMuted;
        }

        ApplyGlobalVolume();

        foreach (FamilyEntry family in m_Families)
        {
            ApplyFamilyVolume(family.family);
        }

        // La phase initiale est Classical.
        m_ActivePhase = MusicPhase.Classical;
        m_ElectroPhaseMultiplier = 0f;

        ApplyAllInstrumentStates();
    }

    private void Start()
    {
        if (m_StartAutomatically)
        {
            // On conserve les réglages initiaux des instruments.
            // La phase Classical coupe uniquement l'électro.
            ChangePhase(MusicPhase.Classical);
        }
    }

    private void OnEnable()
    {
        OrchestraEventBus.OnCommand += HandleCommand;
    }

    private void OnDisable()
    {
        OrchestraEventBus.OnCommand -= HandleCommand;

        if (m_ElectroFadeCoroutine != null)
        {
            StopCoroutine(m_ElectroFadeCoroutine);
            m_ElectroFadeCoroutine = null;
        }
    }

    // =========================================================
    // RÉCEPTION DES COMMANDES
    // =========================================================

    private void HandleCommand(OrchestraCommand command)
    {
        switch (command.Type)
        {
            case OrchestraCommandType.SetGlobalVolume:
                SetGlobalVolume(command.Value);
                break;

            case OrchestraCommandType.SetGroupVolume:
                SetFamilyVolume(command.Target, command.Value);
                break;

            case OrchestraCommandType.SetGroupMute:
                SetFamilyMute(command.Target, command.IsMuted);
                break;

            case OrchestraCommandType.ChangePhase:
                ChangePhase(command.Phase);
                break;

                // SetGlobalFilter est volontairement ignoré.
        }
    }

    // =========================================================
    // VOLUME GLOBAL
    // =========================================================

    public void SetGlobalVolume(float volume)
    {
        m_RequestedGlobalVolume = Mathf.Clamp01(volume);
        ApplyGlobalVolume();
    }

    public float GetGlobalVolume()
    {
        return m_RequestedGlobalVolume;
    }

    private void ApplyGlobalVolume()
    {
        if (m_AudioMixer == null ||
            string.IsNullOrWhiteSpace(m_GlobalVolumeParameter))
        {
            return;
        }

        m_AudioMixer.SetFloat(
            m_GlobalVolumeParameter,
            NormalizedToDb(m_RequestedGlobalVolume)
        );
    }

    // =========================================================
    // VOLUME DES FAMILLES
    // =========================================================

    public void SetFamilyVolume(
        InstrumentGroupId familyId,
        float volume)
    {
        FamilyEntry family = FindFamily(familyId);

        if (family == null)
        {
            Debug.LogWarning(
                $"AudioManager : famille non configurée : {familyId}");
            return;
        }

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

        if (family == null || m_AudioMixer == null ||
            string.IsNullOrWhiteSpace(family.mixerVolumeParameter))
        {
            return;
        }

        m_AudioMixer.SetFloat(
            family.mixerVolumeParameter,
            NormalizedToDb(family.requestedVolume)
        );
    }

    // =========================================================
    // VOLUME INDIVIDUEL DES INSTRUMENTS
    // =========================================================

    public void SetInstrumentVolume(
        string instrumentId,
        float volume)
    {
        InstrumentEntry instrument = FindInstrument(instrumentId);

        if (instrument == null)
        {
            Debug.LogWarning(
                $"AudioManager : instrument introuvable : {instrumentId}");
            return;
        }

        instrument.requestedVolume = Mathf.Clamp01(volume);
        ApplyInstrumentState(instrument);
    }

    public float GetInstrumentVolume(string instrumentId)
    {
        InstrumentEntry instrument = FindInstrument(instrumentId);

        return instrument != null ? instrument.requestedVolume : 0f;
    }

    // =========================================================
    // MUTE / UNMUTE
    // =========================================================

    public void SetInstrumentMute(
        string instrumentId,
        bool muted)
    {
        InstrumentEntry instrument = FindInstrument(instrumentId);

        if (instrument == null)
        {
            Debug.LogWarning(
                $"AudioManager : instrument introuvable : {instrumentId}");
            return;
        }

        instrument.userMuted = muted;
        ApplyInstrumentState(instrument);
    }

    public void ToggleInstrumentMute(string instrumentId)
    {
        InstrumentEntry instrument = FindInstrument(instrumentId);

        if (instrument != null)
        {
            SetInstrumentMute(
                instrumentId,
                !instrument.userMuted
            );
        }
    }

    public void SetFamilyMute(
        InstrumentGroupId familyId,
        bool muted)
    {
        bool found = false;

        foreach (InstrumentEntry instrument in m_Instruments)
        {
            if (instrument.family != familyId)
                continue;

            found = true;
            instrument.userMuted = muted;
            ApplyInstrumentState(instrument);
        }

        if (!found)
        {
            Debug.LogWarning(
                $"AudioManager : aucun instrument pour {familyId}");
        }
    }

    public void ToggleFamilyMute(InstrumentGroupId familyId)
    {
        bool currentlyMuted = IsFamilyMuted(familyId);

        SetFamilyMute(familyId, !currentlyMuted);
    }

    public bool IsInstrumentMuted(string instrumentId)
    {
        InstrumentEntry instrument = FindInstrument(instrumentId);

        if (instrument == null || instrument.audioSource == null)
            return false;

        return instrument.audioSource.mute;
    }

    public bool IsFamilyMuted(InstrumentGroupId familyId)
    {
        bool found = false;

        foreach (InstrumentEntry instrument in m_Instruments)
        {
            if (instrument.family != familyId)
                continue;

            found = true;

            if (instrument.audioSource != null &&
                !instrument.audioSource.mute)
            {
                return false;
            }
        }

        return found;
    }

    // =========================================================
    // APPLICATION DES ÉTATS
    // =========================================================

    private void ApplyAllInstrumentStates()
    {
        foreach (InstrumentEntry instrument in m_Instruments)
        {
            ApplyInstrumentState(instrument);
        }
    }

    private void ApplyInstrumentState(InstrumentEntry instrument)
    {
        if (instrument == null || instrument.audioSource == null)
            return;

        bool isElectro = instrument.family == InstrumentGroupId.Electro;

        // Le volume demandé est conservé séparément du volume
        // effectivement appliqué pendant les transitions.
        float phaseMultiplier = isElectro
            ? m_ElectroPhaseMultiplier
            : 1f;

        instrument.audioSource.volume =
            Mathf.Clamp01(instrument.requestedVolume * phaseMultiplier);

        // Le mute de phase ne doit pas effacer le mute utilisateur.
        bool phaseMuted = isElectro &&
                          m_ElectroPhaseMultiplier <= 0.0001f;

        instrument.audioSource.mute =
            instrument.userMuted || phaseMuted;
    }

    // =========================================================
    // TRANSITIONS MUSICALES
    // =========================================================

    public void ChangePhase(MusicPhase phase)
    {
        m_ActivePhase = phase;

        float targetMultiplier;

        switch (phase)
        {
            case MusicPhase.Classical:
                targetMultiplier = 0f;
                break;

            case MusicPhase.Transition:
                targetMultiplier = m_TransitionElectroVolume;
                break;

            case MusicPhase.Techno:
                targetMultiplier = m_TechnoElectroVolume;
                break;

            default:
                targetMultiplier = 0f;
                break;
        }

        if (m_ElectroFadeCoroutine != null)
        {
            StopCoroutine(m_ElectroFadeCoroutine);
        }

        if (m_PhaseTransitionDuration <= 0f)
        {
            m_ElectroPhaseMultiplier = targetMultiplier;
            ApplyAllInstrumentStates();
            m_ElectroFadeCoroutine = null;
            return;
        }

        m_ElectroFadeCoroutine = StartCoroutine(
            FadeElectro(targetMultiplier)
        );
    }

    private IEnumerator FadeElectro(float targetMultiplier)
    {
        float startMultiplier = m_ElectroPhaseMultiplier;
        float elapsed = 0f;

        // Évite de conserver un état mute pendant le fondu entrant.
        if (targetMultiplier > 0f)
        {
            m_ElectroPhaseMultiplier = Mathf.Max(
                m_ElectroPhaseMultiplier,
                0.0001f
            );

            ApplyAllInstrumentStates();
        }

        while (elapsed < m_PhaseTransitionDuration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(
                elapsed / m_PhaseTransitionDuration
            );

            m_ElectroPhaseMultiplier = Mathf.Lerp(
                startMultiplier,
                targetMultiplier,
                t
            );

            ApplyAllInstrumentStates();

            yield return null;
        }

        m_ElectroPhaseMultiplier = targetMultiplier;
        ApplyAllInstrumentStates();

        m_ElectroFadeCoroutine = null;
    }

    public MusicPhase GetActivePhase()
    {
        return m_ActivePhase;
    }

    public float GetElectroPhaseMultiplier()
    {
        return m_ElectroPhaseMultiplier;
    }

    // =========================================================
    // ÉTAT DES FAMILLES ET DES INSTRUMENTS
    // =========================================================

    public FamiliesAudioReport GetFamiliesAudioReport()
    {
        FamiliesAudioReport report = new FamiliesAudioReport();

        report.globalVolume = m_RequestedGlobalVolume;

        if (m_AudioMixer != null &&
            !string.IsNullOrWhiteSpace(m_GlobalVolumeParameter) &&
            m_AudioMixer.GetFloat(
                m_GlobalVolumeParameter,
                out float globalDb))
        {
            report.appliedGlobalVolumeDb = globalDb;
        }
        else
        {
            report.appliedGlobalVolumeDb = float.NaN;
        }

        foreach (FamilyEntry family in m_Families)
        {
            FamilyAudioState familyState = new FamilyAudioState();

            familyState.family = family.family;
            familyState.requestedVolume = family.requestedVolume;

            float familyDb = 0f;

            familyState.available =
                m_AudioMixer != null &&
                !string.IsNullOrWhiteSpace(family.mixerVolumeParameter) &&
                m_AudioMixer.GetFloat(
                    family.mixerVolumeParameter,
                    out familyDb
                );

            familyState.appliedVolume = familyState.available
                ? DbToNormalized(familyDb)
                : float.NaN;

            bool hasInstruments = false;
            bool allMuted = true;

            foreach (InstrumentEntry instrument in m_Instruments)
            {
                if (instrument.family != family.family)
                    continue;

                hasInstruments = true;

                InstrumentAudioState instrumentState =
                    BuildInstrumentState(instrument);

                familyState.instruments.Add(instrumentState);

                if (!instrumentState.actuallyMuted)
                    allMuted = false;
            }

            familyState.isMuted = hasInstruments && allMuted;

            report.families.Add(familyState);
        }

        return report;
    }

    private InstrumentAudioState BuildInstrumentState(
        InstrumentEntry instrument)
    {
        InstrumentAudioState state = new InstrumentAudioState();

        state.instrumentId = instrument.instrumentId;
        state.family = instrument.family;
        state.requestedVolume = instrument.requestedVolume;

        state.available = instrument.audioSource != null;

        if (state.available)
        {
            state.appliedSourceVolume = instrument.audioSource.volume;
            state.actuallyMuted = instrument.audioSource.mute;
            state.userMuted = instrument.userMuted;
            state.isPlaying = instrument.audioSource.isPlaying;
        }
        else
        {
            state.appliedSourceVolume = float.NaN;
            state.actuallyMuted = false;
            state.userMuted = instrument.userMuted;
            state.isPlaying = false;
        }

        return state;
    }

    // =========================================================
    // ÉTAT DE LA PHASE
    // =========================================================

    public string GetAudioPhaseInformation()
    {
        return
            $"Phase audio : {m_ActivePhase} | " +
            $"Multiplicateur électro : {m_ElectroPhaseMultiplier:F2} | " +
            $"Transition en cours : {m_ElectroFadeCoroutine != null}";
    }

    // =========================================================
    // ÉTAT DE LECTURE
    // =========================================================

    public AudioPlaybackState GetPlaybackState()
    {
        bool hasAvailableSource = false;
        bool hasPlayingSource = false;

        foreach (InstrumentEntry instrument in m_Instruments)
        {
            if (instrument.audioSource == null)
                continue;

            hasAvailableSource = true;

            if (instrument.audioSource.isPlaying)
                hasPlayingSource = true;
        }

        if (hasPlayingSource)
            return AudioPlaybackState.Playing;

        if (hasAvailableSource)
            return AudioPlaybackState.Stopped;

        return AudioPlaybackState.Unavailable;
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

    // =========================================================
    // RECHERCHE DES ÉLÉMENTS
    // =========================================================

    private FamilyEntry FindFamily(InstrumentGroupId familyId)
    {
        return m_Families.Find(f => f.family == familyId);
    }

    private InstrumentEntry FindInstrument(string instrumentId)
    {
        return m_Instruments.Find(
            i => i.instrumentId == instrumentId
        );
    }

    // =========================================================
    // CONVERSION DES VOLUMES
    // =========================================================

    private float NormalizedToDb(float volume)
    {
        volume = Mathf.Clamp01(volume);

        if (volume <= 0f)
            return m_MinVolumeDb;

        return Mathf.Lerp(
            m_MinVolumeDb,
            m_MaxVolumeDb,
            volume
        );
    }

    private float DbToNormalized(float db)
    {
        if (db <= m_MinVolumeDb)
            return 0f;

        return Mathf.Clamp01(
            (db - m_MinVolumeDb) /
            (m_MaxVolumeDb - m_MinVolumeDb)
        );
    }
}
