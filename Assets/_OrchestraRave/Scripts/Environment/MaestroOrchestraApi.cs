// MaestroOrchestraApi : point d'entrée unique pour piloter la DA (séquence rave, lumières, musiciens, caissons) depuis n'importe quel script.
// Utilisation : appeler directement les méthodes statiques, par exemple MaestroOrchestraApi.StartRaveSequence(). Rien à poser dans la scène.
// Cibles : cherchées par nom de GameObject racine dans la scène active au premier appel : Environment (RaveSequence, EnvironmentMood, Animator,
// groupe enfant Poursuites), Speakers (Animator, SpeakerPulse sur chaque Speaker_N), Strings / Vent / Brass / Percution (SectionHighlight et
// Animator de chaque Musician_N), StageLights, Lasers. Le cache est invalidé au chargement d'une scène ou si une cible a été détruite.
// Cible absente ou désactivée : un Debug.LogWarning la nomme et l'appel ne fait rien, sans jamais lever d'exception.
// Diagnose() liste ce qui est trouvé. Documentation et exemples : Docs/API-DA.md.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Maestro.Environment
{
    /// <summary>
    /// Façade statique de la DA pour les autres équipes (hand tracking, audio). Aucune logique métier : chaque méthode délègue à RaveSequence,
    /// EnvironmentMood, SectionHighlight, SpeakerPulse, aux Animators et aux lumières déjà présents dans les prefabs.
    /// Toutes les méthodes sont idempotentes et sûres à appeler à tout moment, y compris pendant la séquence rave.
    /// </summary>
    public static class MaestroOrchestraApi
    {
        const string Tag = "[MaestroOrchestraApi] ";
        const string EnvironmentName = "Environment";
        const string SpeakersName = "Speakers";
        const string StageLightsName = "StageLights";
        const string LasersName = "Lasers";
        const string PoursuitesName = "Poursuites";
        const string VfxName = "VFX";
        const string SpeakerPrefix = "Speaker_";
        const string MusicianPrefix = "Musician_";
        const string TriggerDrop = "Drop", TriggerRave = "Rave", TriggerReset = "Reset";
        const string TriggerPlay = "Play", TriggerStop = "Stop", TriggerHeadbang = "Headbang";
        const string StateHidden = "Hidden", StateIdle = "Idle", StatePlay = "Play", StateHeadbang = "Headbang";
        const float RetryInterval = 1f;

        static readonly string[] SectionNames = { "Strings", "Vent", "Brass", "Percution" };
        static readonly string[] MusicianTriggers = { TriggerPlay, TriggerStop, TriggerHeadbang };

        class Section
        {
            public string name;
            public GameObject root;
            public SectionHighlight highlight;
            public int musicianChildren;
            public readonly List<Animator> musicians = new List<Animator>();
            public readonly List<string> withoutAnimator = new List<string>();
        }

        struct Entry
        {
            public string label, detail, problem;
        }

        static bool _resolved, _complete;
        static int _sceneHandle;
        static float _nextRetry;
        static GameObject _environment, _poursuites, _speakers, _stageLights, _lasers, _vfx;
        static Animator _environmentAnimator, _speakersAnimator;
        static RaveSequence _sequence, _hooked;
        static EnvironmentMood _mood;
        static StageLightSweep _sweep;
        static int _speakerChildren;
        static readonly List<Light> _poursuiteLights = new List<Light>();
        static readonly List<SpeakerPulse> _pulses = new List<SpeakerPulse>();
        static readonly List<string> _speakersWithoutPulse = new List<string>();
        static readonly List<Light> _spots = new List<Light>();
        static readonly List<GameObject> _beams = new List<GameObject>();
        static readonly Section[] _sections = CreateSections();
        static Action<string> _phaseChanged;

        static MaestroOrchestraApi()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>Remise à zéro au lancement du jeu (utile si le rechargement de domaine est désactivé dans l'éditeur).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _phaseChanged = null;
            ResetCache();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        // ================================================================== Diagnostic

        /// <summary>Vrai si toutes les cibles sont trouvées et actives dans la scène active (Environment et ses composants, Poursuites, Speakers et
        /// ses SpeakerPulse, les quatre sections avec SectionHighlight et Animators, StageLights, Lasers). Ne loggue rien.</summary>
        public static bool IsReady
        {
            get
            {
                Resolve();
                foreach (Entry e in Report()) if (e.problem != null) return false;
                return true;
            }
        }

        /// <summary>Relance la recherche des cibles et renvoie une liste lisible des cibles trouvées et manquantes (à passer à Debug.Log).</summary>
        public static string Diagnose()
        {
            ResetCache();
            Resolve();
            List<Entry> entries = Report();
            int ok = 0;
            foreach (Entry e in entries) if (e.problem == null) ok++;
            var sb = new StringBuilder();
            sb.Append(Tag).Append("diagnostic de la scène « ").Append(SceneName()).Append(" » : ").Append(ok).Append('/').Append(entries.Count)
              .Append(" cibles trouvées, IsReady = ").Append(ok == entries.Count ? "true" : "false");
            foreach (Entry e in entries)
            {
                sb.Append('\n').Append(e.problem == null ? "  OK        " : "  MANQUANT  ").Append(e.label).Append(" : ").Append(e.problem ?? e.detail);
            }
            bool countdown = _vfx != null && _vfx.transform.Find("CountdownPanel") != null && _vfx.transform.Find("Flash") != null;
            sb.Append('\n').Append("  OPTION    ").Append(VfxName).Append(" : ").Append(countdown ? "CountdownPanel et Flash présents (compte à rebours visible)"
                : "absent ou incomplet : la séquence tourne sans compte à rebours visible ni flash (non compté dans IsReady)");
            return sb.ToString();
        }

        // ================================================================== Séquence complète

        /// <summary>Lance la séquence complète : compte à rebours (Countdown) → chute des caissons (Drop) → transformation en château (Transform) → Party.
        /// Déjà en cours : ignoré (RaveSequence le note dans la Console). La séquence reste en Party jusqu'à AbortRaveSequence() ou ResetToOpera().</summary>
        public static void StartRaveSequence()
        {
            const string M = "StartRaveSequence";
            Resolve();
            if (!NeedSequence(M)) return;
            if (!_sequence.isActiveAndEnabled) { Warn(M, "le composant RaveSequence est désactivé sur « " + EnvironmentName + " »"); return; }
            _sequence.StartSequence();
        }

        /// <summary>Interrompt la séquence et revient à l'opéra (trigger Reset), tout éteint : compte à rebours caché, bloc Party coupé (lasers, spots,
        /// néons, pulsation, notes), Stop sur les musiciens, caissons remis en hauteur. Sans séquence en cours : même remise à zéro, sans effet de bord.</summary>
        public static void AbortRaveSequence()
        {
            const string M = "AbortRaveSequence";
            Resolve();
            if (!NeedSequence(M)) return;
            _sequence.AbortSequence();
        }

        /// <summary>Vrai du lancement de la séquence jusqu'à son arrêt (Abort ou Reset), y compris pendant toute la phase Party. Ne loggue rien.</summary>
        public static bool IsRaveSequenceRunning
        {
            get
            {
                Resolve();
                return _sequence != null && _sequence.IsRunning;
            }
        }

        /// <summary>Phase courante : "Idle", "Countdown", "Drop", "Transform" ou "Party" ("Idle" si RaveSequence est introuvable). Ne loggue rien.</summary>
        public static string RavePhase
        {
            get
            {
                Resolve();
                return _sequence != null ? _sequence.Phase : RaveSequence.PhaseIdle;
            }
        }

        /// <summary>Appelé à chaque changement de phase, avec le nom de la nouvelle phase. Relayé depuis RaveSequence ; l'abonnement est valable
        /// même s'il a lieu avant tout autre appel, et survit aux changements de scène. Une exception levée par un abonné est loggée sans casser la séquence.</summary>
        public static event Action<string> OnRavePhaseChanged
        {
            add { _phaseChanged += value; Resolve(); }
            remove { _phaseChanged -= value; }
        }

        // ================================================================== Briques séparées

        /// <summary>Fait tomber les caissons (trigger Drop sur Speakers), sans rien d'autre. Déjà tombés : ignoré (rien ne les remonte sauf
        /// AbortRaveSequence ou StartRaveSequence). Pendant la séquence : ignoré, la séquence fait tomber les caissons elle-même en phase Drop.</summary>
        public static void DropSpeakers()
        {
            const string M = "DropSpeakers";
            Resolve();
            if (!NeedRoot(M, _speakers, SpeakersName) || !NeedComponent(M, _speakersAnimator, "Animator", SpeakersName)) return;
            if (!_speakersAnimator.isActiveAndEnabled) { Warn(M, "l'Animator de « " + SpeakersName + " » est désactivé"); return; }
            if (SequenceRunning()) { Info(M, "séquence en cours (phase " + _sequence.Phase + ") : elle gère la chute des caissons, appel ignoré"); return; }
            if (!InState(_speakersAnimator, StateHidden)) { _speakersAnimator.ResetTrigger(TriggerDrop); return; }
            _speakersAnimator.SetTrigger(TriggerDrop);
        }

        /// <summary>Transforme le théâtre en château de nuit (trigger Rave sur Environment, 6 s). Déjà en rave : sans effet. En plein retour vers l'opéra :
        /// la transition repart vers le rave depuis le point atteint, sans saut. Pendant la séquence : ignoré, la séquence déclenche Rave elle-même en phase Transform.</summary>
        public static void TransformToRave()
        {
            const string M = "TransformToRave";
            Resolve();
            if (!NeedRoot(M, _environment, EnvironmentName) || !NeedComponent(M, _environmentAnimator, "Animator", EnvironmentName)) return;
            if (SequenceRunning()) { Info(M, "séquence en cours (phase " + _sequence.Phase + ") : elle déclenche Rave elle-même, appel ignoré"); return; }
            _environmentAnimator.ResetTrigger(TriggerReset);
            _environmentAnimator.SetTrigger(TriggerRave);
        }

        /// <summary>Revient au théâtre d'opéra (trigger Reset sur Environment, 6 s). Déjà à l'opéra : sans effet. Pendant la séquence, quelle que soit
        /// la phase (Party comprise) : équivaut à AbortRaveSequence(). Hors séquence, les caissons restent posés et un bloc Party allumé par
        /// SetParty(true) reste allumé (sauf l'ambiance néon, qui suit l'état Rave) : appeler SetParty(false) ou AbortRaveSequence() pour tout éteindre.</summary>
        public static void ResetToOpera()
        {
            const string M = "ResetToOpera";
            Resolve();
            if (SequenceRunning()) { Info(M, "séquence en cours (phase " + _sequence.Phase + ") : équivaut à AbortRaveSequence()"); _sequence.AbortSequence(); return; }
            if (!NeedRoot(M, _environment, EnvironmentName) || !NeedComponent(M, _environmentAnimator, "Animator", EnvironmentName)) return;
            _environmentAnimator.ResetTrigger(TriggerRave);
            _environmentAnimator.SetTrigger(TriggerReset);
        }

        /// <summary>Allume ou éteint tout le bloc Party d'un coup : lasers, spots de StageLights avec leur balayage, ambiance et néons, pulsation des
        /// caissons, particules de notes, Headbang (on) ou Stop (off) sur tous les musiciens. Les néons et l'ambiance néon ne s'allument qu'en état Rave
        /// (TransformToRave d'abord). Pendant la séquence : appliqué tout de suite et la séquence continue ; elle rallume le bloc en entrant en Party,
        /// et SetParty(false) en Party éteint le bloc sans arrêter la séquence (IsRaveSequenceRunning reste vrai jusqu'à AbortRaveSequence).</summary>
        public static void SetParty(bool on)
        {
            const string M = "SetParty";
            Resolve();
            if (!NeedSequence(M)) return;
            _sequence.SetParty(on);
        }

        /// <summary>Allume ou éteint les faisceaux laser (enfants de Lasers). Pendant la séquence : appliqué tout de suite ; la séquence les rallume
        /// en entrant en Party et les éteint à l'arrêt.</summary>
        public static void SetLasers(bool on)
        {
            const string M = "SetLasers";
            Resolve();
            if (!NeedRoot(M, _lasers, LasersName)) return;
            if (_beams.Count == 0) { Warn(M, "aucun faisceau (enfant) sous « " + LasersName + " »"); return; }
            foreach (GameObject g in _beams) if (g != null && g.activeSelf != on) g.SetActive(on);
        }

        /// <summary>Allume ou éteint les spots de StageLights avec leur balayage (rotation lente, alternance magenta / cyan). À l'extinction, visées et
        /// couleurs de base sont restaurées. Pendant la séquence : appliqué tout de suite ; la séquence les rallume en Party et les éteint à l'arrêt.</summary>
        public static void SetStageLights(bool on)
        {
            const string M = "SetStageLights";
            Resolve();
            if (!NeedRoot(M, _stageLights, StageLightsName)) return;
            if (_spots.Count == 0) { Warn(M, "aucune Light sous « " + StageLightsName + " »"); return; }
            foreach (Light l in _spots) if (l != null) l.enabled = on;
            if (_sweep != null) _sweep.enabled = on;
        }

        /// <summary>Allume ou éteint les poursuites de scène et le fill (lumières d'opéra du groupe Poursuites). Ce sont des lumières d'opéra :
        /// l'ambiance les éteint en rave et les rallume au retour à l'opéra, SetPoursuites(true) n'a donc d'effet visible qu'à l'opéra.
        /// Pendant le compte à rebours, elles baissent avec les autres lumières d'opéra.</summary>
        public static void SetPoursuites(bool on)
        {
            const string M = "SetPoursuites";
            Resolve();
            if (!NeedRoot(M, _environment, EnvironmentName)) return;
            if (_poursuites == null) { Warn(M, "groupe « " + PoursuitesName + " » introuvable sous « " + EnvironmentName + " »"); return; }
            if (!_poursuites.activeInHierarchy) { Warn(M, "le groupe « " + PoursuitesName + " » est désactivé"); return; }
            if (_poursuiteLights.Count == 0) { Warn(M, "aucune Light sous « " + PoursuitesName + " »"); return; }
            foreach (Light l in _poursuiteLights) if (l != null) l.enabled = on;
        }

        // ================================================================== Musiciens

        /// <summary>Les musiciens jouent (trigger Play). section : "Strings", "Vent", "Brass" ou "Percution", insensible à la casse ; null = toutes.
        /// Déjà en train de jouer : sans effet. Pendant la séquence : appliqué tout de suite ; la séquence passe tout le monde en Headbang en Party et en Stop à l'arrêt.</summary>
        public static void MusiciansPlay(string section = null) { FireMusicians("MusiciansPlay", section, TriggerPlay, StatePlay); }

        /// <summary>Les musiciens s'arrêtent (trigger Stop, retour à la pose de repos). section : nom de section ou null = toutes. Déjà au repos : sans effet.
        /// Pendant la séquence : appliqué tout de suite ; la séquence repasse tout le monde en Headbang en entrant en Party.</summary>
        public static void MusiciansStop(string section = null) { FireMusicians("MusiciansStop", section, TriggerStop, StateIdle); }

        /// <summary>Les musiciens headbanguent (trigger Headbang). section : nom de section ou null = toutes. Déjà en Headbang : sans effet.
        /// Pendant la séquence : appliqué tout de suite ; la séquence met tout le monde en Headbang en Party et en Stop à l'arrêt.</summary>
        public static void MusiciansHeadbang(string section = null) { FireMusicians("MusiciansHeadbang", section, TriggerHeadbang, StateHeadbang); }

        /// <summary>Surbrillance douce (émission à la couleur de la section) sur toute une section, ou extinction. section : nom de section, null = toutes.
        /// Sans lien avec la séquence : appliqué tout de suite dans toutes les phases, jamais modifié par la séquence.</summary>
        public static void HighlightSection(string section, bool on)
        {
            const string M = "HighlightSection";
            Resolve();
            List<Section> targets = Select(M, section);
            if (targets == null) return;
            foreach (Section s in targets)
            {
                if (!NeedRoot(M, s.root, s.name) || !NeedComponent(M, s.highlight, "SectionHighlight", s.name)) continue;
                s.highlight.SetSectionHighlighted(on);
            }
        }

        /// <summary>Éteint toutes les surbrillances (sections et musiciens) des quatre sections. Sans lien avec la séquence.</summary>
        public static void ClearHighlights()
        {
            const string M = "ClearHighlights";
            Resolve();
            foreach (Section s in _sections)
            {
                if (!NeedRoot(M, s.root, s.name) || !NeedComponent(M, s.highlight, "SectionHighlight", s.name)) continue;
                s.highlight.ClearAll();
            }
        }

        /// <summary>Noms des sections acceptés par les méthodes Musicians* et HighlightSection : Strings, Vent, Brass, Percution (copie).</summary>
        public static string[] Sections { get { return (string[])SectionNames.Clone(); } }

        // ================================================================== Horloge audio

        /// <summary>Un temps de l'horloge audio : à appeler à chaque temps pour synchroniser la pulsation des caissons. Dès le premier appel, leur horloge
        /// interne (128 BPM) se coupe. Les caissons ne pulsent qu'en Party : hors Party l'appel est sans effet visible, mais toujours sûr.</summary>
        public static void Beat()
        {
            const string M = "Beat";
            Resolve();
            if (!NeedRoot(M, _speakers, SpeakersName)) return;
            if (_pulses.Count == 0) { Warn(M, "aucun SpeakerPulse sur les Speaker_N de « " + SpeakersName + " »"); return; }
            foreach (SpeakerPulse p in _pulses) if (p != null) p.Beat();
        }

        // ================================================================== interne : résolution

        static Section[] CreateSections()
        {
            var a = new Section[SectionNames.Length];
            for (int i = 0; i < a.Length; i++) a[i] = new Section { name = SectionNames[i] };
            return a;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _resolved = false;
            _nextRetry = 0f;
            if (_phaseChanged != null) Resolve(); // garder le relais de OnRavePhaseChanged branché sur la RaveSequence de la nouvelle scène
        }

        static void ResetCache()
        {
            if (!ReferenceEquals(_hooked, null)) _hooked.OnPhaseChanged -= Relay;
            _hooked = null;
            _resolved = false;
            _complete = false;
            _nextRetry = 0f;
        }

        static bool CacheValid()
        {
            if (!_resolved) return false;
            if (SceneManager.GetActiveScene().handle != _sceneHandle) return false;
            if (!_complete) return Time.realtimeSinceStartup < _nextRetry; // une cible manquait : nouvelle recherche au plus une fois par seconde
            if (_environment == null || _environmentAnimator == null || _sequence == null || _mood == null || _poursuites == null) return false;
            if (_speakers == null || _speakersAnimator == null || _stageLights == null || _lasers == null) return false;
            foreach (Section s in _sections) if (s.root == null || s.highlight == null) return false;
            foreach (Section s in _sections) foreach (Animator a in s.musicians) if (a == null) return false;
            foreach (SpeakerPulse p in _pulses) if (p == null) return false;
            foreach (Light l in _poursuiteLights) if (l == null) return false;
            foreach (Light l in _spots) if (l == null) return false;
            foreach (GameObject g in _beams) if (g == null) return false;
            return true;
        }

        static void Resolve()
        {
            if (CacheValid()) return;
            _environment = _poursuites = _speakers = _stageLights = _lasers = _vfx = null;
            _environmentAnimator = _speakersAnimator = null;
            _sequence = null;
            _mood = null;
            _sweep = null;
            _speakerChildren = 0;
            _poursuiteLights.Clear();
            _pulses.Clear();
            _speakersWithoutPulse.Clear();
            _spots.Clear();
            _beams.Clear();
            foreach (Section s in _sections)
            {
                s.root = null;
                s.highlight = null;
                s.musicianChildren = 0;
                s.musicians.Clear();
                s.withoutAnimator.Clear();
            }

            Scene scene = SceneManager.GetActiveScene();
            _sceneHandle = scene.handle;
            _resolved = true;
            _nextRetry = Time.realtimeSinceStartup + RetryInterval;
            if (scene.IsValid() && scene.isLoaded)
            {
                GameObject[] roots = scene.GetRootGameObjects();

                _environment = FindRoot(roots, EnvironmentName);
                if (_environment != null)
                {
                    _environmentAnimator = _environment.GetComponent<Animator>();
                    _sequence = _environment.GetComponent<RaveSequence>();
                    _mood = _environment.GetComponent<EnvironmentMood>();
                    foreach (Transform t in _environment.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name != PoursuitesName) continue;
                        _poursuites = t.gameObject;
                        _poursuiteLights.AddRange(t.GetComponentsInChildren<Light>(true));
                        break;
                    }
                }

                _speakers = FindRoot(roots, SpeakersName);
                if (_speakers != null)
                {
                    _speakersAnimator = _speakers.GetComponent<Animator>();
                    foreach (Transform t in _speakers.transform)
                    {
                        if (!t.name.StartsWith(SpeakerPrefix)) continue;
                        _speakerChildren++;
                        SpeakerPulse p = t.GetComponent<SpeakerPulse>();
                        if (p != null) _pulses.Add(p); else _speakersWithoutPulse.Add(t.name);
                    }
                }

                foreach (Section s in _sections)
                {
                    s.root = FindRoot(roots, s.name);
                    if (s.root == null) continue;
                    s.highlight = s.root.GetComponent<SectionHighlight>();
                    foreach (Transform t in s.root.transform)
                    {
                        if (!t.name.StartsWith(MusicianPrefix)) continue;
                        s.musicianChildren++;
                        Animator a = t.GetComponentInChildren<Animator>(true);
                        if (a != null) s.musicians.Add(a); else s.withoutAnimator.Add(t.name);
                    }
                }

                _stageLights = FindRoot(roots, StageLightsName);
                if (_stageLights != null)
                {
                    _spots.AddRange(_stageLights.GetComponentsInChildren<Light>(true));
                    _sweep = _stageLights.GetComponent<StageLightSweep>();
                }

                _lasers = FindRoot(roots, LasersName);
                if (_lasers != null) foreach (Transform t in _lasers.transform) _beams.Add(t.gameObject);

                _vfx = FindRoot(roots, VfxName); // facultatif : compte à rebours et flash de la séquence
            }

            _complete = AllFound();
            Hook();
        }

        /// <summary>Toutes les cibles trouvées (actives ou non) : le cache est alors conservé tant que rien n'est détruit.</summary>
        static bool AllFound()
        {
            if (_environment == null || _environmentAnimator == null || _sequence == null || _mood == null || _poursuites == null || _poursuiteLights.Count == 0) return false;
            if (_speakers == null || _speakersAnimator == null || _speakerChildren == 0 || _speakersWithoutPulse.Count > 0) return false;
            foreach (Section s in _sections) if (s.root == null || s.highlight == null || s.musicianChildren == 0 || s.withoutAnimator.Count > 0) return false;
            return _stageLights != null && _spots.Count > 0 && _lasers != null && _beams.Count > 0;
        }

        static void Hook()
        {
            if (ReferenceEquals(_hooked, _sequence)) return;
            if (!ReferenceEquals(_hooked, null)) _hooked.OnPhaseChanged -= Relay;
            _hooked = _sequence;
            if (_hooked != null) _hooked.OnPhaseChanged += Relay;
        }

        static void Relay(string phase)
        {
            Action<string> handlers = _phaseChanged;
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try { ((Action<string>)d)(phase); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        static GameObject FindRoot(GameObject[] roots, string name)
        {
            foreach (GameObject root in roots)
            {
                if (root == null)
                    continue;

                // Cherche d'abord l'objet racine.
                if (root.name == name)
                    return root;

                // Recherche aussi dans les enfants,
                // y compris les GameObjects inactifs.
                foreach (Transform child in
                         root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == name)
                        return child.gameObject;
                }
            }

            return null;
        }

        static string SceneName()
        {
            Scene s = SceneManager.GetActiveScene();
            return s.IsValid() ? s.name : "(aucune)";
        }

        // ================================================================== interne : rapport et contrôles

        static string RootProblem(GameObject go, string name)
        {
            if (go == null) return "racine « " + name + " » introuvable dans la scène « " + SceneName() + " »";
            if (!go.activeInHierarchy) return "racine « " + name + " » désactivée dans la scène « " + SceneName() + " »";
            return null;
        }

        static List<Entry> Report()
        {
            var list = new List<Entry>();

            string p = RootProblem(_environment, EnvironmentName);
            if (p == null)
            {
                var miss = new List<string>();
                if (_environmentAnimator == null) miss.Add("Animator");
                if (_sequence == null) miss.Add("RaveSequence");
                if (_mood == null) miss.Add("EnvironmentMood");
                if (miss.Count > 0) p = "composant(s) " + string.Join(", ", miss.ToArray()) + " absent(s) de « " + EnvironmentName + " »";
            }
            list.Add(new Entry { label = EnvironmentName, problem = p, detail = "Animator, RaveSequence (phase " + (_sequence != null ? _sequence.Phase : "?") + "), EnvironmentMood" });

            p = _environment == null ? "racine « " + EnvironmentName + " » introuvable"
              : _poursuites == null ? "groupe « " + PoursuitesName + " » introuvable sous « " + EnvironmentName + " »"
              : !_poursuites.activeInHierarchy ? "groupe « " + PoursuitesName + " » désactivé"
              : _poursuiteLights.Count == 0 ? "aucune Light sous « " + PoursuitesName + " »" : null;
            list.Add(new Entry { label = EnvironmentName + "/…/" + PoursuitesName, problem = p, detail = _poursuiteLights.Count + " Lights" });

            p = RootProblem(_speakers, SpeakersName);
            if (p == null)
            {
                if (_speakersAnimator == null) p = "Animator absent de « " + SpeakersName + " »";
                else if (_speakerChildren == 0) p = "aucun enfant Speaker_N sous « " + SpeakersName + " »";
                else if (_speakersWithoutPulse.Count > 0) p = "SpeakerPulse absent de « " + string.Join(" », « ", _speakersWithoutPulse.ToArray()) + " »";
            }
            list.Add(new Entry { label = SpeakersName, problem = p, detail = "Animator, " + _pulses.Count + "/" + _speakerChildren + " SpeakerPulse" });

            foreach (Section s in _sections)
            {
                p = RootProblem(s.root, s.name);
                if (p == null)
                {
                    if (s.highlight == null) p = "SectionHighlight absent de « " + s.name + " »";
                    else if (s.musicianChildren == 0) p = "aucun enfant Musician_N sous « " + s.name + " »";
                    else if (s.withoutAnimator.Count > 0) p = "Animator absent de « " + s.name + "/" + string.Join(" », « " + s.name + "/", s.withoutAnimator.ToArray()) + " »";
                }
                list.Add(new Entry { label = s.name, problem = p, detail = "SectionHighlight, " + s.musicians.Count + "/" + s.musicianChildren + " musiciens animés" });
            }

            p = RootProblem(_stageLights, StageLightsName);
            if (p == null && _spots.Count == 0) p = "aucune Light sous « " + StageLightsName + " »";
            list.Add(new Entry { label = StageLightsName, problem = p, detail = _spots.Count + " spots" + (_sweep != null ? ", StageLightSweep" : ", sans StageLightSweep (pas de balayage)") });

            p = RootProblem(_lasers, LasersName);
            if (p == null && _beams.Count == 0) p = "aucun faisceau (enfant) sous « " + LasersName + " »";
            list.Add(new Entry { label = LasersName, problem = p, detail = _beams.Count + " faisceaux" });
            return list;
        }

        static bool NeedRoot(string method, GameObject go, string name)
        {
            string p = RootProblem(go, name);
            if (p == null) return true;
            Warn(method, p);
            return false;
        }

        static bool NeedComponent(string method, Object component, string type, string owner)
        {
            if (component != null) return true;
            Warn(method, "composant " + type + " absent de « " + owner + " »");
            return false;
        }

        static bool NeedSequence(string method)
        {
            return NeedRoot(method, _environment, EnvironmentName) && NeedComponent(method, _sequence, "RaveSequence", EnvironmentName);
        }

        static bool SequenceRunning()
        {
            return _sequence != null && _sequence.IsRunning;
        }

        /// <summary>null = toutes les sections ; nom inconnu : warning listant les sections valides et null renvoyé.</summary>
        static List<Section> Select(string method, string section)
        {
            var list = new List<Section>();
            if (section == null) { list.AddRange(_sections); return list; }
            string wanted = section.Trim();
            foreach (Section s in _sections)
                if (string.Equals(s.name, wanted, StringComparison.OrdinalIgnoreCase)) { list.Add(s); return list; }
            Warn(method, "section « " + section + " » inconnue ; sections valides : " + string.Join(", ", SectionNames) + " (ou null pour toutes)");
            return null;
        }

        static void FireMusicians(string method, string section, string trigger, string state)
        {
            Resolve();
            List<Section> targets = Select(method, section);
            if (targets == null) return;
            foreach (Section s in targets)
            {
                if (!NeedRoot(method, s.root, s.name)) continue;
                if (s.musicians.Count == 0) { Warn(method, "aucun Animator de Musician_N sous « " + s.name + " »"); continue; }
                foreach (Animator a in s.musicians)
                {
                    if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) continue;
                    foreach (string t in MusicianTriggers) if (t != trigger) a.ResetTrigger(t);
                    if (InState(a, state)) a.ResetTrigger(trigger); // aucune transition d'un état vers lui-même : le trigger resterait en attente
                    else a.SetTrigger(trigger);
                }
            }
        }

        /// <summary>Dans l'état visé, ou en transition vers lui.</summary>
        static bool InState(Animator a, string state)
        {
            if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) return false;
            return a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0).IsName(state) : a.GetCurrentAnimatorStateInfo(0).IsName(state);
        }

        static void Warn(string method, string reason)
        {
            Debug.LogWarning(Tag + method + "() ignoré : " + reason + ".");
        }

        static void Info(string method, string message)
        {
            Debug.Log(Tag + method + "() : " + message + ".");
        }
    }
}
