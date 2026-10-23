using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Maestro.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maestro.EditorTools
{
    /// <summary>
    /// Test Play Mode de MaestroOrchestraApi (v12) : chaque membre public est appelé au moins une fois, dans un ordre qui fait sens, et son effet est
    /// confirmé par lecture d'état sur les objets de la scène (retrouvés par nom, indépendamment de l'API). Puis tests négatifs : racine Lasers
    /// désactivée (warning nommant Lasers, aucune exception) et section inconnue (warning listant les sections valides).
    /// « Arm API Play Test » prépare le test ; l'entrée et la sortie du Play Mode sont laissées au MCP (Unity_ManageEditor Play / Stop).
    /// Résultat : Temp/maestro_api_result.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class MaestroApiPlayTest
    {
        public const string PendingKey = "Maestro.ApiPlayTestPending";
        public static string ResultPath { get { return Path.Combine(MaestroRunner.ProjectRoot, "Temp", "maestro_api_result.txt"); } }

        class Step
        {
            public string label;
            public Action act;
            public float wait;
            public Func<bool> until;
            public float timeout;
            public Func<string> check;
        }

        static readonly List<Step> _steps = new List<Step>();
        static int _index;
        static bool _acted;
        static double _stepStart;
        static StringBuilder _sb;
        static int _failures, _checks;
        static readonly List<string> _logs = new List<string>();
        static int _exceptions;
        static int _captureFrom = -1;
        static readonly List<string> _phases = new List<string>();
        static double _seqStart;
        static string _diag, _diagWhileDisabled;
        static bool _readyWhileDisabled, _resolvedBeforeSubscribe;
        static readonly Dictionary<SpeakerPulse, int> _beatBase = new Dictionary<SpeakerPulse, int>();
        static readonly List<bool> _externalBefore = new List<bool>();

        // Objets de la scène, lus directement pour confirmer les effets.
        static GameObject _lasersRoot;
        static Animator _env, _speakers;
        static EnvironmentMood _mood;
        static RaveSequence _seq;
        static readonly List<SpeakerPulse> _pulses = new List<SpeakerPulse>();
        static readonly List<ParticleSystem> _notes = new List<ParticleSystem>();
        static readonly Dictionary<string, SectionHighlight> _hl = new Dictionary<string, SectionHighlight>();
        static readonly Dictionary<string, List<Animator>> _musicians = new Dictionary<string, List<Animator>>();
        static readonly Dictionary<string, Renderer> _body1 = new Dictionary<string, Renderer>();
        static readonly List<Light> _spots = new List<Light>();
        static StageLightSweep _sweep;
        static readonly List<GameObject> _beams = new List<GameObject>();
        static readonly List<Light> _poursuites = new List<Light>();
        static readonly MaterialPropertyBlock s_Block = new MaterialPropertyBlock();
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        static MaestroApiPlayTest()
        {
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        [MenuItem("Tools/Maestro/Arm API Play Test (MCP Play-Stop)")]
        public static void Arm()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.isDirty) EditorSceneManager.SaveScene(scene);
            SessionState.SetBool(PendingKey, true);
            try { File.Delete(ResultPath); } catch (Exception) { }
            Debug.Log("[Maestro] Test API armé : entre en Play Mode (Unity_ManageEditor Play) ; résultat dans " + ResultPath + ".");
        }

        static void Poll()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || !SessionState.GetBool(PendingKey, false)) return;
            if (Time.timeSinceLevelLoad < 1.5f) return; // laisser passer Awake / Start (panneau de debug, RaveSequence)
            SessionState.SetBool(PendingKey, false);
            Begin();
        }

        // ------------------------------------------------------------------ outils

        static string R(bool ok, string detail)
        {
            _checks++;
            if (!ok) _failures++;
            return (ok ? "OK     " : "ÉCHEC  ") + detail;
        }

        static bool InState(Animator a, string state)
        {
            return !a.IsInTransition(0) && a.GetCurrentAnimatorStateInfo(0).IsName(state);
        }

        static int CountIn(IEnumerable<Animator> list, string state)
        {
            int n = 0;
            foreach (Animator a in list) if (a != null && InState(a, state)) n++;
            return n;
        }

        static List<Animator> AllMusicians()
        {
            var all = new List<Animator>();
            foreach (List<Animator> l in _musicians.Values) all.AddRange(l);
            return all;
        }

        static int PendingMusicianTriggers()
        {
            int n = 0;
            foreach (Animator a in AllMusicians())
                foreach (string t in new[] { "Play", "Stop", "Headbang" }) if (a.GetBool(t)) n++;
            return n;
        }

        static int LasersOn() { int n = 0; foreach (GameObject g in _beams) if (g.activeSelf) n++; return n; }
        static int SpotsOn() { int n = 0; foreach (Light l in _spots) if (l.enabled) n++; return n; }
        static int PoursuitesOn() { int n = 0; foreach (Light l in _poursuites) if (l.enabled) n++; return n; }
        static int PulsesOn() { int n = 0; foreach (SpeakerPulse p in _pulses) if (p.enabled) n++; return n; }
        static int NotesPlaying() { int n = 0; foreach (ParticleSystem p in _notes) if (p.isPlaying) n++; return n; }
        static int HighlightsOn() { int n = 0; foreach (SectionHighlight h in _hl.Values) if (h.SectionHighlighted) n++; return n; }

        static float Emission(string section)
        {
            Renderer r;
            if (!_body1.TryGetValue(section, out r) || r == null) return -1f;
            r.GetPropertyBlock(s_Block);
            Color c = s_Block.GetColor(EmissionId);
            return Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        }

        static string SpeakersState()
        {
            AnimatorStateInfo st = _speakers.GetCurrentAnimatorStateInfo(0);
            return st.IsName("Hidden") ? "Hidden" : st.IsName("Dropped") ? "Dropped (t=" + st.normalizedTime.ToString("F2") + ")" : "?";
        }

        static string EnvState()
        {
            AnimatorStateInfo st = _env.GetCurrentAnimatorStateInfo(0);
            string s = st.IsName("Opera") ? "Opera" : st.IsName("Rave") ? "Rave" : st.IsName("ToOpera") ? "ToOpera" : "?";
            return s + " (rave=" + _mood.rave.ToString("F3") + ")";
        }

        static string LogsSince(int from, string filter)
        {
            var found = new List<string>();
            for (int i = Mathf.Max(0, from); i < _logs.Count; i++) if (filter == null || _logs[i].Contains(filter)) found.Add(_logs[i]);
            return string.Join(" | ", found.ToArray());
        }

        static void Add(string label, Action act, float wait, Func<string> check, Func<bool> until = null, float timeout = 0f)
        {
            _steps.Add(new Step { label = label, act = act, wait = wait, check = check, until = until, timeout = timeout });
        }

        // ------------------------------------------------------------------ préparation

        static bool Setup()
        {
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            Func<string, GameObject> root = n => { foreach (GameObject g in roots) if (g.name == n) return g; return null; };
            GameObject env = root("Environment"), spk = root("Speakers"), sl = root("StageLights");
            _lasersRoot = root("Lasers");
            if (env == null || spk == null || sl == null || _lasersRoot == null) { _sb.AppendLine("ÉCHEC  racines introuvables, test annulé"); _failures++; return false; }
            _env = env.GetComponent<Animator>();
            _mood = env.GetComponent<EnvironmentMood>();
            _seq = env.GetComponent<RaveSequence>();
            _poursuites.Clear();
            foreach (Transform t in env.GetComponentsInChildren<Transform>(true)) if (t.name == "Poursuites") { _poursuites.AddRange(t.GetComponentsInChildren<Light>(true)); break; }
            _speakers = spk.GetComponent<Animator>();
            _pulses.Clear();
            _pulses.AddRange(spk.GetComponentsInChildren<SpeakerPulse>(true));
            _notes.Clear();
            _notes.AddRange(spk.GetComponentsInChildren<ParticleSystem>(true));
            _hl.Clear();
            _musicians.Clear();
            _body1.Clear();
            foreach (string s in MaestroGreybox.SectionPrefabs)
            {
                GameObject g = root(s);
                if (g == null) continue;
                _hl[s] = g.GetComponent<SectionHighlight>();
                var list = new List<Animator>();
                foreach (Transform c in g.transform) if (c.name.StartsWith("Musician_")) list.Add(c.GetComponentInChildren<Animator>(true));
                _musicians[s] = list;
                Transform m1 = g.transform.Find("Musician_1");
                Renderer body = null;
                if (m1 != null) foreach (Renderer r in m1.GetComponentsInChildren<Renderer>(true)) if (r.name == "Body") { body = r; break; }
                _body1[s] = body;
            }
            _spots.Clear();
            _spots.AddRange(sl.GetComponentsInChildren<Light>(true));
            _sweep = sl.GetComponent<StageLightSweep>();
            _beams.Clear();
            foreach (Transform c in _lasersRoot.transform) _beams.Add(c.gameObject);

            // Simulation de MainScene (Play Mode seulement, rien n'est sauvegardé) : le build ne câble les références inter-prefabs de RaveSequence
            // que dans Art_Sandbox ; vidées ici, elles doivent être retrouvées par nom de racine au premier appel qui en a besoin.
            if (_seq != null)
            {
                _seq.speakersAnimator = null;
                _seq.sections = new GameObject[0];
                _seq.stageLights = null;
                _seq.lasers = null;
                _seq.vfx = null;
                _seq.countdownPanel = null;
                _seq.countdownText = null;
                _seq.flashLight = null;
                _sb.AppendLine("Simulation MainScene : références inter-prefabs de RaveSequence vidées (Speakers, sections, StageLights, Lasers, VFX, CountdownPanel, texte, Flash).");
            }
            return true;
        }

        static void Begin()
        {
            _sb = new StringBuilder();
            _failures = _checks = _exceptions = 0;
            _logs.Clear();
            _phases.Clear();
            _steps.Clear();
            _index = 0;
            _acted = false;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            _sb.AppendLine("Test Play Mode de MaestroOrchestraApi — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ", scène " + SceneManager.GetActiveScene().name);
            if (!Setup()) { Finish(); return; }
            BuildSteps();
            _stepStart = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception) _exceptions++;
            if (type == LogType.Log && !condition.StartsWith("[MaestroOrchestraApi]") && !condition.StartsWith("[RaveSequence]")) return;
            _logs.Add("[" + type + "] " + condition.Split('\n')[0]);
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog; return; }
            if (_index >= _steps.Count) { Finish(); return; }
            Step s = _steps[_index];
            double now = EditorApplication.timeSinceStartup;
            if (!_acted)
            {
                _acted = true;
                _stepStart = now;
                try { if (s.act != null) s.act(); }
                catch (Exception e) { _exceptions++; _failures++; _sb.AppendLine("ÉCHEC  " + s.label + " : exception " + e.GetType().Name + " : " + e.Message); }
                return;
            }
            double elapsed = now - _stepStart;
            if (elapsed < s.wait) return;
            if (s.until != null && elapsed < s.wait + s.timeout)
            {
                bool done;
                try { done = s.until(); } catch (Exception) { done = true; }
                if (!done) return;
            }
            string line;
            try { line = s.check != null ? s.check() : null; }
            catch (Exception e) { _failures++; line = "ÉCHEC  exception pendant la vérification : " + e.Message; }
            if (line != null) _sb.AppendLine("[" + (now - _seqStartOrZero()).ToString("F1").PadLeft(5) + " s] " + s.label + " → " + line);
            _index++;
            _acted = false;
        }

        static double _t0;
        static double _seqStartOrZero() { if (_t0 <= 0) _t0 = EditorApplication.timeSinceStartup; return _t0; }

        // ------------------------------------------------------------------ scénario

        static void BuildSteps()
        {
            _t0 = 0;
            string[] sections = MaestroGreybox.SectionPrefabs;
            const BindingFlags PS = BindingFlags.NonPublic | BindingFlags.Static;

            // 0. Abonnement à l'event avant toute résolution : cache de l'API vidé par réflexion (le panneau de debug l'a déjà rempli au Start).
            Add("OnRavePhaseChanged (abonnement avant résolution)", () =>
            {
                typeof(MaestroOrchestraApi).GetMethod("ResetCache", PS).Invoke(null, null);
                _resolvedBeforeSubscribe = (bool)typeof(MaestroOrchestraApi).GetField("_resolved", PS).GetValue(null);
                MaestroOrchestraApi.OnRavePhaseChanged += p => _phases.Add(p + " @ " + (EditorApplication.timeSinceStartup - _seqStart).ToString("F2") + " s");
            }, 0f, () =>
            {
                object hooked = typeof(MaestroOrchestraApi).GetField("_hooked", PS).GetValue(null);
                return R(!_resolvedBeforeSubscribe && ReferenceEquals(hooked, _seq), "cache vide avant l'abonnement (résolu=" + _resolvedBeforeSubscribe + "), relais branché sur la RaveSequence de la scène après l'abonnement : " + ReferenceEquals(hooked, _seq));
            });

            // 1. Diagnostic et propriétés de lecture.
            Add("Diagnose()", () => { _diag = MaestroOrchestraApi.Diagnose(); }, 0f, () =>
                R(_diag != null && !_diag.Contains("MANQUANT") && _diag.Contains("IsReady = true"), _diag != null ? _diag.Split('\n')[0].Replace("[MaestroOrchestraApi] ", "") : "null"));
            Add("IsReady", null, 0f, () => R(MaestroOrchestraApi.IsReady, "IsReady = " + MaestroOrchestraApi.IsReady));
            Add("Sections", null, 0f, () => R(string.Join(",", MaestroOrchestraApi.Sections) == "Strings,Vent,Brass,Percution", "Sections = " + string.Join(", ", MaestroOrchestraApi.Sections)));
            Add("IsRaveSequenceRunning / RavePhase au repos", null, 0f, () =>
                R(!MaestroOrchestraApi.IsRaveSequenceRunning && MaestroOrchestraApi.RavePhase == "Idle", "IsRaveSequenceRunning = " + MaestroOrchestraApi.IsRaveSequenceRunning + ", RavePhase = " + MaestroOrchestraApi.RavePhase));

            // 2. Surbrillances : chaque section (une en minuscules pour l'insensibilité à la casse), puis ClearHighlights.
            for (int i = 0; i < sections.Length; i++)
            {
                string s = sections[i];
                string arg = i == 3 ? s.ToLowerInvariant() : s;
                int expected = i + 1;
                Add("HighlightSection(\"" + arg + "\", true)", () => MaestroOrchestraApi.HighlightSection(arg, true), 0.3f, () =>
                    R(_hl[s].SectionHighlighted && HighlightsOn() == expected && Emission(s) > 0.1f, s + ".SectionHighlighted = " + _hl[s].SectionHighlighted + ", sections allumées " + HighlightsOn() + "/4, émission Musician_1/Body " + Emission(s).ToString("F2")));
            }
            Add("ClearHighlights()", () => MaestroOrchestraApi.ClearHighlights(), 0.4f, () =>
            {
                float maxE = 0f;
                foreach (string s in sections) maxE = Mathf.Max(maxE, Emission(s));
                return R(HighlightsOn() == 0 && maxE < 0.01f, "sections allumées " + HighlightsOn() + "/4, émission max des Musician_1/Body " + maxE.ToString("F3"));
            });

            // 3. Musiciens section par section, puis toutes (null).
            foreach (string s in sections)
            {
                string sec = s;
                Add("MusiciansPlay(\"" + sec + "\")", () => MaestroOrchestraApi.MusiciansPlay(sec), 0.6f, () =>
                    R(CountIn(_musicians[sec], "Play") == _musicians[sec].Count && CountIn(AllMusicians(), "Play") == _musicians[sec].Count, sec + " en Play " + CountIn(_musicians[sec], "Play") + "/" + _musicians[sec].Count + ", autres sections en Play " + (CountIn(AllMusicians(), "Play") - CountIn(_musicians[sec], "Play"))));
                Add("MusiciansHeadbang(\"" + sec + "\")", () => MaestroOrchestraApi.MusiciansHeadbang(sec), 0.6f, () =>
                    R(CountIn(_musicians[sec], "Headbang") == _musicians[sec].Count, sec + " en Headbang " + CountIn(_musicians[sec], "Headbang") + "/" + _musicians[sec].Count));
                Add("MusiciansStop(\"" + sec + "\")", () => MaestroOrchestraApi.MusiciansStop(sec), 0.6f, () =>
                    R(CountIn(_musicians[sec], "Idle") == _musicians[sec].Count && PendingMusicianTriggers() == 0, sec + " au repos (Idle) " + CountIn(_musicians[sec], "Idle") + "/" + _musicians[sec].Count + ", triggers en attente " + PendingMusicianTriggers()));
            }
            Add("MusiciansPlay()", () => { MaestroOrchestraApi.MusiciansPlay(); MaestroOrchestraApi.MusiciansPlay(null); }, 0.6f, () =>
                R(CountIn(AllMusicians(), "Play") == AllMusicians().Count && PendingMusicianTriggers() == 0, "toutes les sections en Play " + CountIn(AllMusicians(), "Play") + "/" + AllMusicians().Count + " (appel doublé), triggers en attente " + PendingMusicianTriggers()));
            Add("MusiciansHeadbang()", () => MaestroOrchestraApi.MusiciansHeadbang(), 0.6f, () =>
                R(CountIn(AllMusicians(), "Headbang") == AllMusicians().Count, "toutes les sections en Headbang " + CountIn(AllMusicians(), "Headbang") + "/" + AllMusicians().Count));
            Add("MusiciansStop()", () => { MaestroOrchestraApi.MusiciansStop(); MaestroOrchestraApi.MusiciansStop(); }, 0.6f, () =>
                R(CountIn(AllMusicians(), "Idle") == AllMusicians().Count && PendingMusicianTriggers() == 0, "toutes les sections au repos " + CountIn(AllMusicians(), "Idle") + "/" + AllMusicians().Count + " (appel doublé), triggers en attente " + PendingMusicianTriggers()));

            // 4. Caissons puis transformation, bloc Party en rave, retour à l'opéra.
            Add("DropSpeakers()", () => MaestroOrchestraApi.DropSpeakers(), 0.2f, () =>
                R(_speakers.GetCurrentAnimatorStateInfo(0).IsName("Dropped") && _speakers.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f, "Speakers : " + SpeakersState() + " (chute de 2,5 s terminée)"),
                () => _speakers.GetCurrentAnimatorStateInfo(0).IsName("Dropped") && _speakers.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f, 5f);
            Add("DropSpeakers() déjà tombés", () => MaestroOrchestraApi.DropSpeakers(), 0.2f, () =>
                R(_speakers.GetCurrentAnimatorStateInfo(0).IsName("Dropped") && !_speakers.GetBool("Drop"), "Speakers : " + SpeakersState() + ", trigger Drop en attente : " + _speakers.GetBool("Drop")));
            Add("TransformToRave()", () => MaestroOrchestraApi.TransformToRave(), 0.2f, () =>
                R(_env.GetCurrentAnimatorStateInfo(0).IsName("Rave") && _mood.rave >= 0.999f, "Environment : " + EnvState()),
                () => _mood.rave >= 0.999f, 9f);
            Add("SetParty(true)", () => MaestroOrchestraApi.SetParty(true), 1.5f, () =>
                R(LasersOn() == _beams.Count && SpotsOn() == _spots.Count && _sweep.enabled && PulsesOn() == _pulses.Count && NotesPlaying() == _notes.Count
                  && CountIn(AllMusicians(), "Headbang") == AllMusicians().Count && _mood.party >= 0.999f && _mood.PartyLevel >= 0.999f && _mood.NeonsActive == _mood.neons.Length,
                  "lasers " + LasersOn() + "/" + _beams.Count + ", spots " + SpotsOn() + "/" + _spots.Count + ", balayage " + _sweep.enabled + ", pulsation " + PulsesOn() + "/" + _pulses.Count
                  + ", notes " + NotesPlaying() + "/" + _notes.Count + ", Headbang " + CountIn(AllMusicians(), "Headbang") + "/" + AllMusicians().Count + ", party " + _mood.party.ToString("F2") + " (niveau " + _mood.PartyLevel.ToString("F2") + "), néons " + _mood.NeonsActive + "/" + _mood.neons.Length + " (cascade)"),
                () => _mood.NeonsActive == _mood.neons.Length, 6f);
            Add("SetParty(false)", () => MaestroOrchestraApi.SetParty(false), 1.2f, () =>
                R(LasersOn() == 0 && SpotsOn() == 0 && !_sweep.enabled && PulsesOn() == 0 && NotesPlaying() == 0 && CountIn(AllMusicians(), "Idle") == AllMusicians().Count
                  && _mood.party < 0.001f && _mood.NeonsActive == 0 && PendingMusicianTriggers() == 0,
                  "lasers " + LasersOn() + ", spots " + SpotsOn() + ", balayage " + _sweep.enabled + ", pulsation " + PulsesOn() + ", notes " + NotesPlaying() + ", au repos " + CountIn(AllMusicians(), "Idle") + "/" + AllMusicians().Count
                  + ", party " + _mood.party.ToString("F2") + ", néons " + _mood.NeonsActive + ", triggers en attente " + PendingMusicianTriggers()));
            Add("ResetToOpera()", () => MaestroOrchestraApi.ResetToOpera(), 0.2f, () =>
                R(_env.GetCurrentAnimatorStateInfo(0).IsName("Opera") && _mood.rave <= 0.001f, "Environment : " + EnvState() + ", Speakers : " + SpeakersState() + " (restent posés, comme documenté)"),
                () => _env.GetCurrentAnimatorStateInfo(0).IsName("Opera") && _mood.rave <= 0.001f, 9f);

            // 5. Lumières une par une.
            Add("SetLasers(true)", () => MaestroOrchestraApi.SetLasers(true), 0.1f, () => R(LasersOn() == _beams.Count, "lasers " + LasersOn() + "/" + _beams.Count));
            Add("SetLasers(false)", () => MaestroOrchestraApi.SetLasers(false), 0.1f, () => R(LasersOn() == 0, "lasers " + LasersOn() + "/" + _beams.Count));
            Add("SetStageLights(true)", () => MaestroOrchestraApi.SetStageLights(true), 0.3f, () => R(SpotsOn() == _spots.Count && _sweep.enabled, "spots " + SpotsOn() + "/" + _spots.Count + ", balayage " + _sweep.enabled));
            Add("SetStageLights(false)", () => MaestroOrchestraApi.SetStageLights(false), 0.1f, () => R(SpotsOn() == 0 && !_sweep.enabled, "spots " + SpotsOn() + "/" + _spots.Count + ", balayage " + _sweep.enabled));
            Add("SetPoursuites(true)", () => MaestroOrchestraApi.SetPoursuites(true), 0.3f, () => R(PoursuitesOn() == _poursuites.Count && _poursuites.Count > 0, "poursuites allumées " + PoursuitesOn() + "/" + _poursuites.Count));
            Add("SetPoursuites(false)", () => MaestroOrchestraApi.SetPoursuites(false), 0.3f, () => R(PoursuitesOn() == 0, "poursuites allumées " + PoursuitesOn() + "/" + _poursuites.Count + " (état conservé par EnvironmentMood 0,3 s après)"));

            // 6. Séquence complète, appels concurrents pendant le compte à rebours, Beat() en Party, Abort.
            Add("StartRaveSequence()", () => { _seqStart = EditorApplication.timeSinceStartup; _phases.Clear(); MaestroOrchestraApi.StartRaveSequence(); }, 0.5f, () =>
            {
                bool wired = _seq.speakersAnimator == _speakers && _seq.sections.Length == 4 && _seq.stageLights != null && _seq.lasers == _lasersRoot
                    && _seq.vfx != null && _seq.countdownPanel != null && _seq.countdownText != null && _seq.flashLight != null;
                bool panel = _seq.countdownPanel != null && _seq.countdownPanel.activeInHierarchy;
                string digit = _seq.countdownText != null ? _seq.countdownText.text : "?";
                return R(MaestroOrchestraApi.IsRaveSequenceRunning && MaestroOrchestraApi.RavePhase == "Countdown" && wired && panel,
                    "IsRaveSequenceRunning = " + MaestroOrchestraApi.IsRaveSequenceRunning + ", RavePhase = " + MaestroOrchestraApi.RavePhase + ", Speakers : " + SpeakersState()
                    + ", références RaveSequence retrouvées par nom : " + wired + ", compte à rebours affiché : " + panel + " (« " + digit + " »)");
            });
            Add("StartRaveSequence() + DropSpeakers() + TransformToRave() pendant Countdown", () =>
            {
                MaestroOrchestraApi.StartRaveSequence();
                MaestroOrchestraApi.DropSpeakers();
                MaestroOrchestraApi.TransformToRave();
            }, 0.5f, () =>
                R(MaestroOrchestraApi.RavePhase == "Countdown" && _speakers.GetCurrentAnimatorStateInfo(0).IsName("Hidden") && !_speakers.GetBool("Drop") && _env.GetCurrentAnimatorStateInfo(0).IsName("Opera") && !_env.GetBool("Rave"),
                  "appels ignorés, séquence intacte : phase " + MaestroOrchestraApi.RavePhase + ", Speakers " + SpeakersState() + ", Environment " + EnvState()));
            Add("attente de Party", null, 0f, () =>
                R(MaestroOrchestraApi.RavePhase == "Party" && _phases.Count >= 4 && _phases[0].StartsWith("Countdown") && _phases[1].StartsWith("Drop") && _phases[2].StartsWith("Transform") && _phases[3].StartsWith("Party"),
                  "journal OnRavePhaseChanged : " + string.Join(" → ", _phases.ToArray())),
                () => MaestroOrchestraApi.RavePhase == "Party", 20f);
            for (int b = 1; b <= 10; b++)
            {
                int beat = b;
                Add("Beat() n°" + beat, () =>
                {
                    if (beat == 1)
                    {
                        _beatBase.Clear();
                        _externalBefore.Clear();
                        foreach (SpeakerPulse p in _pulses) { _beatBase[p] = p.BeatCount; _externalBefore.Add(p.ExternalClock); }
                    }
                    MaestroOrchestraApi.Beat();
                }, 0.469f, beat < 10 ? (Func<string>)null : () =>
                {
                    int external = 0, exact = 0, before = 0;
                    var deltas = new List<string>();
                    foreach (SpeakerPulse p in _pulses)
                    {
                        if (p.ExternalClock) external++;
                        int d = p.BeatCount - _beatBase[p];
                        if (d == 10) exact++;
                        deltas.Add(d.ToString());
                    }
                    foreach (bool e in _externalBefore) if (e) before++;
                    return R(external == _pulses.Count && exact == _pulses.Count && before == 0 && PulsesOn() == _pulses.Count,
                        "ExternalClock " + before + "/" + _pulses.Count + " avant le 1er Beat → " + external + "/" + _pulses.Count + " après, temps comptés par caisson " + string.Join("/", deltas.ToArray()) + " (attendu 10, horloge interne coupée), phase " + MaestroOrchestraApi.RavePhase);
                });
            }
            Add("AbortRaveSequence()", () => MaestroOrchestraApi.AbortRaveSequence(), 0.6f, () =>
                R(!MaestroOrchestraApi.IsRaveSequenceRunning && MaestroOrchestraApi.RavePhase == "Idle" && _phases.Count > 0 && _phases[_phases.Count - 1].StartsWith("Idle")
                  && LasersOn() == 0 && SpotsOn() == 0 && PulsesOn() == 0 && NotesPlaying() == 0 && _mood.party < 0.001f
                  && _speakers.GetCurrentAnimatorStateInfo(0).IsName("Hidden") && _env.GetCurrentAnimatorStateInfo(0).IsName("ToOpera") && CountIn(AllMusicians(), "Idle") == AllMusicians().Count,
                  "IsRaveSequenceRunning = " + MaestroOrchestraApi.IsRaveSequenceRunning + ", RavePhase = " + MaestroOrchestraApi.RavePhase + ", dernier event " + (_phases.Count > 0 ? _phases[_phases.Count - 1] : "aucun")
                  + ", lasers " + LasersOn() + ", spots " + SpotsOn() + ", pulsation " + PulsesOn() + ", notes " + NotesPlaying() + ", party " + _mood.party.ToString("F2") + ", Speakers " + SpeakersState() + ", Environment " + EnvState() + ", musiciens au repos " + CountIn(AllMusicians(), "Idle") + "/" + AllMusicians().Count));
            Add("AbortRaveSequence() 2e appel", () => MaestroOrchestraApi.AbortRaveSequence(), 0.3f, () =>
                R(!MaestroOrchestraApi.IsRaveSequenceRunning && !_env.GetBool("Reset") && PendingMusicianTriggers() == 0, "toujours arrêtée, trigger Reset en attente : " + _env.GetBool("Reset") + ", triggers musiciens en attente " + PendingMusicianTriggers()));

            // 7. Tests négatifs.
            Add("SetLasers(true) avec « Lasers » désactivé", () =>
            {
                _lasersRoot.SetActive(false);
                _captureFrom = _logs.Count;
                _readyWhileDisabled = MaestroOrchestraApi.IsReady;
                MaestroOrchestraApi.SetLasers(true);
                _diagWhileDisabled = MaestroOrchestraApi.Diagnose();
            }, 0.1f, () =>
            {
                string warn = LogsSince(_captureFrom, "SetLasers");
                int beams = LasersOn();
                _lasersRoot.SetActive(true);
                bool diagOk = _diagWhileDisabled != null && _diagWhileDisabled.Contains("MANQUANT  Lasers");
                return R(warn.Contains("[Warning]") && warn.Contains("Lasers") && beams == 0 && !_readyWhileDisabled && diagOk && _exceptions == 0,
                    "warning : " + warn + " ; faisceaux allumés " + beams + ", IsReady = " + _readyWhileDisabled + ", Diagnose signale Lasers : " + diagOk + ", exceptions " + _exceptions + " ; Lasers réactivé");
            });
            Add("IsReady après réactivation", null, 0.1f, () => R(MaestroOrchestraApi.IsReady && LasersOn() == 0, "IsReady = " + MaestroOrchestraApi.IsReady + ", faisceaux allumés " + LasersOn()));
            Add("section inconnue", () =>
            {
                _captureFrom = _logs.Count;
                MaestroOrchestraApi.HighlightSection("Cuivres", true);
                MaestroOrchestraApi.MusiciansPlay("Guitares");
            }, 0.4f, () =>
            {
                string warn = LogsSince(_captureFrom, "inconnue");
                int warnings = 0;
                for (int i = _captureFrom; i < _logs.Count; i++) if (_logs[i].StartsWith("[Warning]") && _logs[i].Contains("Strings, Vent, Brass, Percution")) warnings++;
                return R(warnings == 2 && HighlightsOn() == 0 && CountIn(AllMusicians(), "Play") == 0, warnings + " warning(s) listant les sections valides, aucune surbrillance ni Play déclenché : " + warn);
            });
        }

        // ------------------------------------------------------------------ fin

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            _sb.AppendLine();
            _sb.AppendLine("Diagnose() au début :");
            _sb.AppendLine(_diag ?? "(non appelé)");
            _sb.AppendLine();
            _sb.AppendLine("Messages de l'API et de RaveSequence pendant le test :");
            foreach (string l in _logs) _sb.AppendLine("  " + l);
            _sb.AppendLine();
            _sb.AppendLine("Exceptions : " + _exceptions);
            string status = _failures == 0 && _exceptions == 0 ? "OK" : "FAIL";
            _sb.AppendLine("RESULTAT API: " + status + " (" + (_checks - _failures) + "/" + _checks + " vérifications réussies)");
            try { File.WriteAllText(ResultPath, _sb.ToString()); } catch (Exception e) { Debug.LogWarning("[Maestro] écriture du résultat impossible : " + e.Message); }
            Debug.Log("[Maestro] Test API terminé : " + status + " (" + (_checks - _failures) + "/" + _checks + "), détail dans " + ResultPath);
        }
    }
}
