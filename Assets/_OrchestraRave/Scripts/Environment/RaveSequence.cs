using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Maestro.Environment
{
    /// <summary>
    /// Séquence « passage en rave » (v11), sur la racine Environment. Une seule timeline (coroutine) :
    ///  - Countdown (countdownSeconds) : CountdownPanel (enfant de VFX) placé à countdownDistance devant la tête du joueur, chiffres « 3 », « 2 », « 1 »
    ///    (apparition à l'échelle digitScaleFrom puis 1 en digitScaleSeconds), flash blanc (Light Flash, flashIntensity pendant flashSeconds) et
    ///    EnvironmentMood.operaDim += operaDimPerDigit à chaque chiffre ; puis « DROP » pendant dropLabelSeconds et le panneau disparaît ;
    ///  - Drop (dropSeconds) : trigger Drop sur Speakers, caissons normaux ;
    ///  - Transform : transformDelay après l'impact, trigger Rave, attente de EnvironmentMood.rave = 1 (raveTimeout) ;
    ///  - Party : dans la même frame, lasers, StageLights + StageLightSweep, EnvironmentMood.party = 1, Headbang sur tous les musiciens, SpeakerPulse,
    ///    particules de notes. IsRunning reste vrai jusqu'à AbortSequence() ou un Reset (trigger) : tout est alors éteint, Stop sur les musiciens,
    ///    caissons remis en Hidden, compte à rebours caché.
    /// v12 : références vides (prefab posé dans MainScene, où le build ne câble rien) retrouvées par nom de racine au démarrage et à chaque lancement.
    /// SetParty(bool) allume / éteint le bloc Party seul, hors timeline (utilisé par MaestroOrchestraApi.SetParty). Les triggers des musiciens
    /// ne sont envoyés que si l'Animator n'est pas déjà dans l'état visé, pour ne jamais laisser un Stop ou un Headbang en attente.
    /// Contrat devs : StartSequence() (ignoré si déjà en cours), AbortSequence(), IsRunning, Phase, OnPhaseChanged(string) avec les phases
    /// « Countdown », « Drop », « Transform », « Party », « Idle ». Les triggers Drop / Rave / Reset / Play / Stop / Headbang restent utilisables tels quels.
    /// </summary>
    [DisallowMultipleComponent]
    public class RaveSequence : MonoBehaviour
    {
        public const string PhaseIdle = "Idle";
        public const string PhaseCountdown = "Countdown";
        public const string PhaseDrop = "Drop";
        public const string PhaseTransform = "Transform";
        public const string PhaseParty = "Party";

        [Header("Références (remplies par le build)")]
        public Animator environmentAnimator;
        public EnvironmentMood mood;
        public Animator speakersAnimator;
        public GameObject[] sections = new GameObject[0];
        public GameObject stageLights;
        public GameObject lasers;
        public GameObject vfx;
        public GameObject countdownPanel;
        public TextMeshProUGUI countdownText;
        public Light flashLight;

        [Header("Timings (secondes)")]
        public float countdownSeconds = 3f;
        public float digitScaleFrom = 1.4f;
        public float digitScaleSeconds = 0.25f;
        public float flashIntensity = 2f;
        public float flashSeconds = 0.08f;
        [Tooltip("À chaque chiffre, les lumières d'opéra chutent de cette fraction (EnvironmentMood.operaDim).")]
        public float operaDimPerDigit = 0.25f;
        public float dropLabelSeconds = 0.4f;
        public float dropSeconds = 2.5f;
        public float transformDelay = 1f;
        public float raveTimeout = 8f;
        public float countdownDistance = 2f;

        [Header("Noms (triggers et états)")]
        public string dropTrigger = "Drop";
        public string raveTrigger = "Rave";
        public string resetTrigger = "Reset";
        public string headbangTrigger = "Headbang";
        public string stopTrigger = "Stop";
        public string raveState = "Rave";
        public string hiddenState = "Hidden";
        public string musicianIdleState = "Idle";
        public string musicianHeadbangState = "Headbang";

        /// <summary>Appelé à chaque changement de phase (Countdown, Drop, Transform, Party, Idle).</summary>
        public event Action<string> OnPhaseChanged;

        public bool IsRunning { get { return _running; } }
        public string Phase { get { return _phase; } }
        public bool PartyStarted { get { return _partyStarted; } }

        const string Tag = "[RaveSequence] ";
        bool _running, _partyStarted, _raveRequested;
        float _raveRequestTime;
        string _phase = PhaseIdle;
        Coroutine _co;
        float _digitStart;
        readonly List<Animator> _musicians = new List<Animator>();
        readonly List<SpeakerPulse> _pulses = new List<SpeakerPulse>();
        readonly List<ParticleSystem> _notes = new List<ParticleSystem>();
        Light[] _spots = new Light[0];
        StageLightSweep _sweep;

        void Awake()
        {
            Collect();
            if (countdownPanel != null) countdownPanel.SetActive(false);
            if (flashLight != null) { flashLight.intensity = 0f; flashLight.enabled = false; }
        }

        void OnDisable()
        {
            if (_running) StopAll();
        }

        // ------------------------------------------------------------------ API

        public void StartSequence()
        {
            if (_running) { Debug.Log(Tag + "séquence déjà en cours : StartSequence() ignoré."); return; }
            AutoWire();
            if (environmentAnimator == null || speakersAnimator == null || mood == null)
            {
                Debug.LogWarning(Tag + "références manquantes (Environment / Speakers / EnvironmentMood) : séquence annulée.");
                return;
            }
            Collect();
            _running = true;
            _partyStarted = false;
            _raveRequested = false;
            // Les caissons tombent pendant la séquence : déjà posés par un Drop précédent, ils repassent en Hidden sans animation.
            if (!speakersAnimator.GetCurrentAnimatorStateInfo(0).IsName(hiddenState))
            {
                speakersAnimator.ResetTrigger(dropTrigger);
                speakersAnimator.Play(hiddenState, 0, 0f);
            }
            _co = StartCoroutine(Run());
        }

        public void AbortSequence()
        {
            bool was = _running;
            StopAll();
            if (environmentAnimator != null)
            {
                environmentAnimator.ResetTrigger(raveTrigger);
                environmentAnimator.SetTrigger(resetTrigger);
            }
            if (was) Debug.Log(Tag + "séquence interrompue (AbortSequence).");
        }

        /// <summary>v12 : bloc Party seul (lasers, spots + balayage, EnvironmentMood.party, Headbang / Stop, SpeakerPulse, notes), sans toucher à la timeline
        /// ni à IsRunning. EnvironmentMood remet party à 0 hors de l'état Rave : les néons ne s'allument qu'en rave.</summary>
        public void SetParty(bool on)
        {
            Collect();
            if (on) StartParty(); else StopParty();
        }

        // ------------------------------------------------------------------ timeline

        IEnumerator Run()
        {
            SetPhase(PhaseCountdown);
            PlaceCountdown();
            if (countdownPanel != null) countdownPanel.SetActive(true);
            int digits = Mathf.Max(1, Mathf.RoundToInt(countdownSeconds));
            float perDigit = countdownSeconds / digits;
            for (int d = digits; d >= 1; d--)
            {
                ShowDigit(d.ToString());
                yield return HoldDigit(perDigit);
                if (!_running) yield break;
            }
            ShowDigit("DROP");
            yield return HoldDigit(dropLabelSeconds);
            if (!_running) yield break;
            if (countdownPanel != null) countdownPanel.SetActive(false);

            SetPhase(PhaseDrop);
            speakersAnimator.ResetTrigger(dropTrigger);
            speakersAnimator.SetTrigger(dropTrigger);
            yield return Wait(dropSeconds);
            if (!_running) yield break;

            SetPhase(PhaseTransform);
            yield return Wait(transformDelay);
            if (!_running) yield break;
            _raveRequested = true;
            _raveRequestTime = Time.time;
            environmentAnimator.ResetTrigger(resetTrigger);
            environmentAnimator.SetTrigger(raveTrigger);
            float t0 = Time.time;
            while (mood.rave < 0.999f)
            {
                if (Time.time - t0 > raveTimeout)
                {
                    Debug.LogWarning(Tag + "rave n'a pas atteint 1 en " + raveTimeout + " s : séquence interrompue.");
                    StopAll();
                    yield break;
                }
                yield return null;
                if (!_running) yield break;
            }

            StartParty();
            SetPhase(PhaseParty);
            _co = null;
        }

        IEnumerator HoldDigit(float seconds)
        {
            if (mood != null) mood.operaDim = Mathf.Clamp01(mood.operaDim + operaDimPerDigit);
            StartCoroutine(Flash());
            float t0 = Time.time;
            while (Time.time - t0 < seconds)
            {
                AnimateDigit(Time.time - _digitStart);
                yield return null;
                if (!_running) yield break;
            }
        }

        IEnumerator Wait(float seconds)
        {
            float t0 = Time.time;
            while (Time.time - t0 < seconds)
            {
                yield return null;
                if (!_running) yield break;
            }
        }

        IEnumerator Flash()
        {
            if (flashLight == null) yield break;
            flashLight.intensity = flashIntensity;
            flashLight.enabled = true;
            float t0 = Time.time;
            while (Time.time - t0 < flashSeconds) yield return null;
            flashLight.intensity = 0f;
            flashLight.enabled = false;
        }

        void PlaceCountdown()
        {
            if (countdownPanel == null) return;
            Camera cam = Camera.main;
            if (cam == null) { countdownPanel.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, countdownDistance), Quaternion.identity); return; }
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 pos = cam.transform.position + fwd * countdownDistance;
            countdownPanel.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - cam.transform.position, Vector3.up));
        }

        void ShowDigit(string s)
        {
            _digitStart = Time.time;
            if (countdownText == null) return;
            countdownText.text = s;
            countdownText.rectTransform.localScale = Vector3.one * digitScaleFrom;
        }

        void AnimateDigit(float elapsed)
        {
            if (countdownText == null) return;
            float k = digitScaleSeconds > 0f ? Mathf.Clamp01(elapsed / digitScaleSeconds) : 1f;
            countdownText.rectTransform.localScale = Vector3.one * Mathf.Lerp(digitScaleFrom, 1f, k);
        }

        void StartParty()
        {
            if (lasers != null) foreach (Transform c in lasers.transform) c.gameObject.SetActive(true);
            foreach (Light l in _spots) if (l != null) l.enabled = true;
            if (_sweep != null) _sweep.enabled = true;
            if (mood != null) mood.party = 1f;
            foreach (Animator a in _musicians) if (a != null) { a.ResetTrigger(stopTrigger); FireUnlessIn(a, headbangTrigger, musicianHeadbangState); }
            foreach (SpeakerPulse p in _pulses) if (p != null) p.enabled = true;
            foreach (ParticleSystem ps in _notes) if (ps != null) { ps.Clear(true); ps.Play(true); }
            _partyStarted = true;
        }

        void StopParty()
        {
            if (lasers != null) foreach (Transform c in lasers.transform) c.gameObject.SetActive(false);
            if (_sweep != null) _sweep.enabled = false;
            foreach (Light l in _spots) if (l != null) l.enabled = false;
            if (mood != null) mood.party = 0f;
            foreach (Animator a in _musicians) if (a != null) { a.ResetTrigger(headbangTrigger); FireUnlessIn(a, stopTrigger, musicianIdleState); }
            foreach (SpeakerPulse p in _pulses) if (p != null) p.enabled = false;
            foreach (ParticleSystem ps in _notes) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _partyStarted = false;
        }

        /// <summary>v12 : trigger envoyé seulement si l'Animator n'est pas déjà dans l'état visé (ou en transition vers lui) ; sinon il resterait en attente
        /// (aucune transition d'un état vers lui-même) et partirait plus tard au mauvais moment.</summary>
        static void FireUnlessIn(Animator a, string trigger, string state)
        {
            if (!a.isActiveAndEnabled || a.runtimeAnimatorController == null) return;
            bool inState = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0).IsName(state) : a.GetCurrentAnimatorStateInfo(0).IsName(state);
            if (inState) a.ResetTrigger(trigger); else a.SetTrigger(trigger);
        }

        /// <summary>Arrêt complet : coroutine, compte à rebours, flash, party, caissons remis en Hidden.</summary>
        void StopAll()
        {
            if (_co != null) { StopCoroutine(_co); _co = null; }
            _running = false;
            _raveRequested = false;
            if (countdownPanel != null) countdownPanel.SetActive(false);
            if (flashLight != null) { flashLight.intensity = 0f; flashLight.enabled = false; }
            if (mood != null) { mood.operaDim = 0f; mood.party = 0f; }
            StopParty();
            if (speakersAnimator != null)
            {
                speakersAnimator.ResetTrigger(dropTrigger);
                speakersAnimator.Play(hiddenState, 0, 0f);
            }
            SetPhase(PhaseIdle);
        }

        void Update()
        {
            if (!_running || environmentAnimator == null || !_raveRequested) return;
            if (Time.time - _raveRequestTime < 0.5f) return; // laisser le trigger Rave être consommé
            AnimatorStateInfo st = environmentAnimator.GetCurrentAnimatorStateInfo(0);
            bool inRave = st.IsName(raveState) || (environmentAnimator.IsInTransition(0) && environmentAnimator.GetNextAnimatorStateInfo(0).IsName(raveState));
            if (!inRave)
            {
                Debug.Log(Tag + "Reset détecté (état " + (st.IsName("Opera") ? "Opera" : "ToOpera") + ") : séquence terminée, tout éteint.");
                StopAll();
            }
        }

        void SetPhase(string phase)
        {
            if (_phase == phase) return;
            _phase = phase;
            if (OnPhaseChanged != null) OnPhaseChanged(phase);
        }

        static readonly string[] SectionRootNames = { "Strings", "Vent", "Brass", "Percution" };

        /// <summary>v12 : références nulles retrouvées par nom de racine dans la scène de l'Environment (prefabs posés dans une autre scène que
        /// Art_Sandbox, où le build ne les a pas câblées : Speakers, Strings / Vent / Brass / Percution, StageLights, Lasers, VFX avec CountdownPanel
        /// et Flash). Une référence déjà remplie n'est jamais remplacée.</summary>
        void AutoWire()
        {
            if (environmentAnimator == null) environmentAnimator = GetComponent<Animator>();
            if (mood == null) mood = GetComponent<EnvironmentMood>();
            bool sectionsMissing = sections == null || sections.Length == 0 || Array.Exists(sections, s => s == null);
            if (speakersAnimator != null && !sectionsMissing && stageLights != null && lasers != null && vfx != null
                && countdownPanel != null && countdownText != null && flashLight != null) return;
            GameObject[] roots = gameObject.scene.GetRootGameObjects();
            if (speakersAnimator == null) { GameObject g = Root(roots, "Speakers"); if (g != null) speakersAnimator = g.GetComponent<Animator>(); }
            if (sectionsMissing)
            {
                var found = new List<GameObject>();
                foreach (string n in SectionRootNames) { GameObject g = Root(roots, n); if (g != null) found.Add(g); }
                if (found.Count > 0) sections = found.ToArray();
            }
            if (stageLights == null) stageLights = Root(roots, "StageLights");
            if (lasers == null) lasers = Root(roots, "Lasers");
            if (vfx == null) vfx = Root(roots, "VFX");
            if (vfx == null) return;
            if (countdownPanel == null) { Transform t = vfx.transform.Find("CountdownPanel"); if (t != null) countdownPanel = t.gameObject; }
            if (countdownText == null && countdownPanel != null) countdownText = countdownPanel.GetComponentInChildren<TextMeshProUGUI>(true);
            if (flashLight == null) { Transform t = vfx.transform.Find("Flash"); if (t != null) flashLight = t.GetComponent<Light>(); }
        }

        static GameObject Root(GameObject[] roots, string name)
        {
            foreach (GameObject r in roots) if (r != null && r.name == name) return r;
            return null;
        }

        void Collect()
        {
            AutoWire();
            _musicians.Clear();
            foreach (GameObject s in sections) if (s != null) _musicians.AddRange(s.GetComponentsInChildren<Animator>(true));
            _pulses.Clear();
            _notes.Clear();
            if (speakersAnimator != null)
            {
                _pulses.AddRange(speakersAnimator.GetComponentsInChildren<SpeakerPulse>(true));
                _notes.AddRange(speakersAnimator.GetComponentsInChildren<ParticleSystem>(true));
            }
            _spots = stageLights != null ? stageLights.GetComponentsInChildren<Light>(true) : new Light[0];
            _sweep = stageLights != null ? stageLights.GetComponent<StageLightSweep>() : null;
        }
    }
}
