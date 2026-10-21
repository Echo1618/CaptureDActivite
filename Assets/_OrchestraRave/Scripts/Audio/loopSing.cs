using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

public class loopSing : MonoBehaviour
{

    [Header("Audio")]
    [SerializeField] private AudioClip m_Loop;
    [SerializeField] private AudioSource m_AudioSourceLoopA;
    [SerializeField] private AudioSource m_AudioSourceLoopB;
    [SerializeField] private AudioMixer m_AudioMixer;

    [Header("Pitch")]
    [SerializeField] private float m_PitchShiftLoopA = 1f;
    [SerializeField] private float m_PitchShiftLoopB = 1f;
    [SerializeField] private float m_OffsetPitchShift = 0f;

    [Header("Looping")]
    [SerializeField] private int m_LoopCount = 4;
    [SerializeField] private float m_LoopBOffset = 0f;
    private int m_LoopAIndex = 0;
    private int m_LoopBIndex = 0;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        StartCoroutine(TimerA());
        StartCoroutine(TimerB(m_Loop.length * m_LoopBOffset));
    }

    IEnumerator TimerA()
    {
        if( m_LoopAIndex == m_LoopCount)
        {
            m_PitchShiftLoopA = 1;
            m_AudioMixer.SetFloat("LoopA", m_PitchShiftLoopA);
            m_LoopAIndex = 0;
        }

        m_PitchShiftLoopA += m_OffsetPitchShift;
        m_AudioMixer.SetFloat("LoopA", m_PitchShiftLoopA);
        m_AudioSourceLoopA.PlayOneShot(m_Loop);

        m_LoopAIndex++;

        yield return new WaitForSeconds(m_Loop.length);
        StartCoroutine(TimerA());
    }

    IEnumerator TimerB( float startTimer)
    {
        if (startTimer != 0) {
            yield return new WaitForSeconds(startTimer);
        }


        if (m_LoopBIndex == m_LoopCount)
        {
            m_PitchShiftLoopB = 1;
            m_AudioMixer.SetFloat("LoopB", m_PitchShiftLoopB);
            m_LoopBIndex = 0;
        }

        m_PitchShiftLoopB += m_OffsetPitchShift;
        m_AudioMixer.SetFloat("LoopB", m_PitchShiftLoopB);
        m_AudioSourceLoopB.PlayOneShot(m_Loop);

        m_LoopBIndex++;

        yield return new WaitForSeconds(m_Loop.length);
        StartCoroutine(TimerB(0));
    }
}
