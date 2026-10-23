
using System.Collections;
using UnityEngine;

public partial class AudioManager
{
    [Header("Synchronisation")]
    [SerializeField, Min(0.05f)]
    private float m_ScheduleDelay = 0.3f;

    private Coroutine m_ElectroFadeCoroutine;

    // DEMARRAGE SYNCHRONISE

    public void StartSynchronizedPlayback()
    {
        double startTime =
            AudioSettings.dspTime + m_ScheduleDelay;

        // On arrête tous les stems avant de les relancer.
        foreach (InstrumentEntry instrument in m_Instruments)
        {
            if (instrument == null ||
                instrument.audioSource == null)
                continue;

            AudioSource source = instrument.audioSource;

            source.Stop();

            if (source.clip == null)
                continue;

            source.playOnAwake = false;
            source.time = 0f;
            source.loop = true;

            source.PlayScheduled(startTime);
        }
    }

    public void StopPlayback()
    {
        foreach (InstrumentEntry instrument in m_Instruments)
        {
            if (instrument != null &&
                instrument.audioSource != null)
            {
                instrument.audioSource.Stop();
            }
        }
    }

    // CHANGEMENT DE PHASE

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

        StopPhaseFade();

        if (m_PhaseTransitionDuration <= 0f)
        {
            m_ElectroPhaseMultiplier = targetMultiplier;
            ApplyAllInstrumentStates();
            return;
        }

        m_ElectroFadeCoroutine =
            StartCoroutine(FadeElectro(targetMultiplier));
    }

    private IEnumerator FadeElectro(float targetMultiplier)
    {
        float startMultiplier = m_ElectroPhaseMultiplier;
        float elapsed = 0f;

        while (elapsed < m_PhaseTransitionDuration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(
                elapsed / m_PhaseTransitionDuration);

            m_ElectroPhaseMultiplier = Mathf.Lerp(
                startMultiplier,
                targetMultiplier,
                t);

            ApplyAllInstrumentStates();

            yield return null;
        }

        m_ElectroPhaseMultiplier = targetMultiplier;

        ApplyAllInstrumentStates();

        m_ElectroFadeCoroutine = null;
    }

    private void StopPhaseFade()
    {
        if (m_ElectroFadeCoroutine != null)
        {
            StopCoroutine(m_ElectroFadeCoroutine);
            m_ElectroFadeCoroutine = null;
        }
    }

    public MusicPhase GetActivePhase()
    {
        return m_ActivePhase;
    }

    public float GetElectroPhaseMultiplier()
    {
        return m_ElectroPhaseMultiplier;
    }
}
