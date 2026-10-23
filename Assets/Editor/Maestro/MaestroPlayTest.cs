using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Maestro.Core;
using Maestro.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Maestro.EditorTools
{
    /// <summary>
    /// Test en Play Mode (v8) :
    ///  1. transition Opera → Rave → Reset : échantillons (rave, OperaShell.y, CastleShell.y, intensité du lustre, fogDensity) avant, à 3 s et 7 s
    ///     après Rave (poursuites et fill à l'intensité 0, capture v8-rave.png), puis 7 s après Reset (poursuites rallumées à leur base) ;
    ///  2. chaque bouton du DebugPanel déclenché par onClick.Invoke() avec contrôle de l'effet : Drop avec, à chaque frame, le point le plus bas de
    ///     chaque caisson comparé au sol détecté par rayon sous l'ancre (capture v8-caissons.png une fois posés), Play / Headbang / Stop sur les
    ///     30 musiciens, Spots, Lasers, Poursuites on/off (enabled basculé et conservé par EnvironmentMood), Fermer ;
    ///  3. détecteur de geste laissé tourner sans main suivie (aucune exception attendue).
    /// Résultat dans Temp/maestro_result.txt et dans la Console. « Play Test Animations » entre et sort du Play Mode tout seul ;
    /// « Arm Play Test » laisse l'entrée/sortie au MCP (Unity_ManageEditor Play/Stop).
    /// </summary>
    public static class MaestroPlayTest
    {
        public const string AutoExitKey = "Maestro.PlayTestAutoExit";

        static string _resultPath;
        static StringBuilder _sb;
        static int _phase;
        static double _phaseStart;
        static bool _autoExit;
        static int _failures;
        static bool _sampled1s, _sampled3s, _sampled5s;
        static float _maxRaveStep, _maxShellStep, _prevRave, _prevShellY, _raveAtToggle;
        static Renderer[] _theatreRenderers;
        static string _pendingLabel;
        static int _pendingFrame;
        static Color _pendingOn;
        static float _pendingCov, _pendingReflKeep;
        static readonly Vector3 Eye = new Vector3(0f, 1.6f, 0f);
        static readonly Quaternion LookBack = Quaternion.Euler(0f, 180f, 0f);
        static float _pulseMin, _pulseMax;
        static readonly MaterialPropertyBlock s_Block = new MaterialPropertyBlock();

        static Animator _env;
        static EnvironmentMood _mood;
        static Transform _opera, _castle, _dome, _lustreModel, _torch1, _anchorSpeakers;
        static Light _lustre;
        static readonly List<Light> _poursuites = new List<Light>();
        static Animator _speakers;
        static Transform _spkFirst, _spkLast;
        static readonly List<Transform> _speakerModels = new List<Transform>();
        static readonly List<Animator> _musicians = new List<Animator>();
        static Transform _m1Body, _m1Head;
        static MaestroArtDebugPanel _panel;
        static readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>();
        static Light[] _spots;
        static Transform _lasers;
        static float _minA, _maxA, _minB, _maxB;
        static int _samples;
        static float _floorY, _dropMinMargin, _dropMinT;
        static string _floorName, _dropMinName;
        static EditorWindow _gameView;
        static bool _gameViewWasMaximized;

        /// <summary>Game View maximisée pendant le test : les stats UnityStats ne comptent alors que la caméra du jeu (pas la Scene View).</summary>
        static void MaximizeGameView(bool maximize)
        {
            try
            {
                if (_gameView == null)
                {
                    Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                    _gameView = EditorWindow.GetWindow(gvType, false, null, false);
                    _gameViewWasMaximized = _gameView.maximized;
                }
                if (maximize) _gameView.maximized = true;
                else _gameView.maximized = _gameViewWasMaximized;
            }
            catch (Exception e)
            {
                _sb.AppendLine("  (Game View non maximisée : " + e.Message + ")");
            }
        }

        [MenuItem("Tools/Maestro/Play Test Animations")]
        public static void PlayTestFromMenu()
        {
            Request(new StringBuilder());
        }

        [MenuItem("Tools/Maestro/Arm Play Test (MCP Play-Stop)")]
        public static void ArmFromMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.isDirty) EditorSceneManager.SaveScene(scene);
            SessionState.SetBool(MaestroRunner.PlayTestPendingKey, true);
            SessionState.SetBool(AutoExitKey, false);
            try { File.Delete(MaestroRunner.ResultPath); } catch (Exception) { }
            Debug.Log("[Maestro] Play test armé : entre en Play Mode (Unity_ManageEditor Play), le résultat sera écrit dans " + MaestroRunner.ResultPath + " ; quitte ensuite le Play Mode toi-même.");
        }

        public static void Request(StringBuilder sb)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != MaestroGreybox.ScenePath) throw new Exception("Scène active ≠ Art_Sandbox, test annulé.");
            if (scene.isDirty) EditorSceneManager.SaveScene(scene);
            SessionState.SetBool(MaestroRunner.PlayTestPendingKey, true);
            SessionState.SetBool(AutoExitKey, true);
            sb.AppendLine("Entrée en Play Mode, résultat écrit à la fin du test.");
            EditorApplication.EnterPlaymode();
        }

        public static void Begin(string resultPath)
        {
            _resultPath = resultPath;
            _sb = new StringBuilder();
            _phase = 0;
            _failures = 0;
            _sampled1s = _sampled3s = _sampled5s = false;
            _pendingLabel = null;
            _phaseStart = EditorApplication.timeSinceStartup;
            _autoExit = SessionState.GetBool(AutoExitKey, true);
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            MaestroRunner.PlayLog("PlayTest v9 démarré (autoExit=" + _autoExit + ")");
        }

        // ------------------------------------------------------------------ helpers

        static void ResetRange()
        {
            _minA = float.MaxValue; _maxA = float.MinValue;
            _minB = float.MaxValue; _maxB = float.MinValue;
            _samples = 0;
        }

        static void Track(float a, float b)
        {
            _minA = Mathf.Min(_minA, a); _maxA = Mathf.Max(_maxA, a);
            _minB = Mathf.Min(_minB, b); _maxB = Mathf.Max(_maxB, b);
            _samples++;
        }

        static string Range(float min, float max) { return min.ToString("F3") + " → " + max.ToString("F3"); }

        static float SignedX(Transform t)
        {
            float x = t.localEulerAngles.x;
            if (x > 180f) x -= 360f;
            return x;
        }

        static void NextPhase(int phase)
        {
            _phase = phase;
            _phaseStart = EditorApplication.timeSinceStartup;
            ResetRange();
        }

        static string EnvState()
        {
            var info = _env.GetCurrentAnimatorStateInfo(0);
            foreach (string s in new[] { "Opera", "Rave", "ToOpera" }) if (info.IsName(s)) return s;
            return "?";
        }

        /// <summary>Statistiques de rendu de la Game View (UnityEditor.UnityStats, par réflexion) : dernière frame rendue.</summary>
        static string RenderStats()
        {
            try
            {
                Type t = typeof(EditorWindow).Assembly.GetType("UnityEditor.UnityStats");
                Func<string, object> get = name => t.GetProperty(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(null, null);
                return "Game View : triangles=" + Convert.ToInt64(get("triangles")).ToString("N0") + " vertices=" + Convert.ToInt64(get("vertices")).ToString("N0") + " batches=" + get("batches") + " drawCalls=" + get("drawCalls") + " setPass=" + get("setPassCalls");
            }
            catch (Exception e)
            {
                return "stats Game View indisponibles (" + e.Message + ")";
            }
        }

        static string Sample()
        {
            return "rave=" + _mood.rave.ToString("F3") + " opera.y=" + _opera.localPosition.y.ToString("F2") + " castle.y=" + _castle.localPosition.y.ToString("F2")
                + " lustre=" + _lustre.intensity.ToString("F2") + " fog=" + RenderSettings.fogDensity.ToString("F4")
                + " | dôme y=" + _dome.localPosition.y.ToString("F1") + " échelle x=" + _dome.localScale.x.ToString("F2") + ", lune échelle=" + _mood.moonRenderer.transform.localScale.x.ToString("F1") + ", ambiante=" + RenderSettings.ambientLight.ToString("F2")
                + ", lustre modèle y=" + (_lustreModel != null ? _lustreModel.position.y.ToString("F1") : "?") + ", Torch_1 y=" + (_torch1 != null ? _torch1.position.y.ToString("F1") : "?") + ", état=" + EnvState();
        }

        /// <summary>État des lumières du groupe Poursuites : « allumées/total, intensité min → max ».</summary>
        static string PoursuitesState()
        {
            int on = 0;
            float min = float.MaxValue, max = float.MinValue;
            foreach (Light l in _poursuites)
            {
                if (l == null) continue;
                if (l.enabled) on++;
                min = Mathf.Min(min, l.intensity);
                max = Mathf.Max(max, l.intensity);
            }
            return on + "/" + _poursuites.Count + " allumées, intensité " + min.ToString("F2") + " → " + max.ToString("F2");
        }

        static int PoursuitesEnabled()
        {
            int on = 0;
            foreach (Light l in _poursuites) if (l != null && l.enabled) on++;
            return on;
        }

        static bool PoursuitesAtBase()
        {
            foreach (Light l in _poursuites)
            {
                if (l == null) return false;
                float expected = l.type == LightType.Spot ? MaestroGreybox.PoursuiteIntensity : MaestroGreybox.FillIntensity;
                if (Mathf.Abs(l.intensity - expected) > 0.05f || !l.enabled) return false;
            }
            return true;
        }

        static bool PoursuitesOff()
        {
            foreach (Light l in _poursuites) if (l != null && (l.intensity > 0.001f || l.enabled)) return false;
            return true;
        }

        static void Expect(string label, float rave, float operaY, float castleY, float lustre, float fog)
        {
            bool ok = Mathf.Abs(_mood.rave - rave) < 0.01f && Mathf.Abs(_opera.localPosition.y - operaY) < 0.05f && Mathf.Abs(_castle.localPosition.y - castleY) < 0.05f
                      && Mathf.Abs(_lustre.intensity - lustre) < 0.05f && Mathf.Abs(RenderSettings.fogDensity - fog) < 0.0002f;
            _sb.AppendLine((ok ? "  OK " : "  PROBLEME ") + label + " : " + Sample() + " (attendu rave=" + rave + " opera.y=" + operaY + " castle.y=" + castleY + " lustre=" + lustre + " fog=" + fog + ")");
            if (!ok) _failures++;
        }

        static void Check(bool ok, string message)
        {
            _sb.AppendLine((ok ? "  OK " : "  PROBLEME ") + message);
            if (!ok) _failures++;
        }

        static int CountMusiciansIn(string state)
        {
            int n = 0;
            foreach (Animator a in _musicians) if (a != null && a.GetCurrentAnimatorStateInfo(0).IsName(state)) n++;
            return n;
        }

        static int SpotsOn()
        {
            int n = 0;
            foreach (Light l in _spots) if (l != null && l.enabled) n++;
            return n;
        }

        static int LasersOn()
        {
            int n = 0;
            foreach (Transform c in _lasers) if (c.gameObject.activeSelf) n++;
            return n;
        }

        static void SetSpots(bool on)
        {
            foreach (Light l in _spots) if (l != null) l.enabled = on;
        }

        static string RaveLabel() { return _panel != null && _panel.raveLabel != null ? _panel.raveLabel.text : "?"; }
        static string HighlightLabel() { return _panel != null && _panel.highlightLabel != null ? _panel.highlightLabel.text : "?"; }

        // ---- v10 : lecture de l'émission effective (MaterialPropertyBlock) sur les renderers des musiciens ----
        static Transform MusicianTransform(string section, int index)
        {
            GameObject s = GameObject.Find(section);
            return s != null ? s.transform.Find("Musician_" + (index + 1)) : null;
        }

        static Renderer MusicianRenderer(string section, int index, string child)
        {
            Transform m = MusicianTransform(section, index);
            Transform c = m != null ? m.Find(child) : null;
            return c != null ? c.GetComponent<Renderer>() : null;
        }

        static Renderer Body(string section, int index) { return MusicianRenderer(section, index, "Body"); }
        static Renderer Head(string section, int index) { return MusicianRenderer(section, index, "Head"); }

        static Renderer Instrument(string section, int index)
        {
            Transform m = MusicianTransform(section, index);
            Transform inst = m != null ? m.Find("Instrument") : null;
            return inst != null ? inst.GetComponentInChildren<Renderer>() : null;
        }

        static Color Emission(Renderer r)
        {
            if (r == null) return Color.black;
            r.GetPropertyBlock(s_Block);
            return s_Block.GetColor("_EmissionColor");
        }

        /// <summary>Intensité effective = composante max de l'émission / composante max de la couleur de section.</summary>
        static float Intensity(Renderer r, string section)
        {
            float b = MaestroGreybox.MusicianBaseColors["M_" + section].maxColorComponent;
            return b > 0f ? Emission(r).maxColorComponent / b : 0f;
        }

        static void CheckEmission(Renderer r, string section, float minI, float maxI, string what)
        {
            Color baseC = MaestroGreybox.MusicianBaseColors["M_" + section];
            Color e = Emission(r);
            float i = Intensity(r, section);
            bool hue = i > 0f;
            if (hue)
            {
                Color n = e / i;
                hue = Mathf.Abs(n.r - baseC.r) < 0.03f && Mathf.Abs(n.g - baseC.g) < 0.03f && Mathf.Abs(n.b - baseC.b) < 0.03f;
            }
            Check(r != null && i >= minI - 0.05f && i <= maxI + 0.05f && hue, what + " : émission " + e.ToString("F2") + " = couleur " + section + " × " + i.ToString("F2") + " (attendu × " + (Mathf.Abs(minI - maxI) < 1e-4f ? minI.ToString("F1") : minI.ToString("F1") + " à " + maxI.ToString("F1")) + ", teinte de la section)");
        }

        static void CheckOff(Renderer r, string what)
        {
            Color e = Emission(r);
            Check(r != null && e.maxColorComponent < 0.01f, what + " : émission " + e.ToString("F2") + " (attendu 0)");
        }

        static void StartContinuity()
        {
            _maxRaveStep = 0f;
            _maxShellStep = 0f;
            _prevRave = _mood.rave;
            _prevShellY = _opera.localPosition.y;
            _samples = 0;
        }

        /// <summary>v9 : saut maximal de rave et d'OperaShell.y entre deux échantillons (détecte une reprise de transition qui repartirait du début).</summary>
        static void TrackContinuity()
        {
            float r = _mood.rave, y = _opera.localPosition.y;
            _maxRaveStep = Mathf.Max(_maxRaveStep, Mathf.Abs(r - _prevRave));
            _maxShellStep = Mathf.Max(_maxShellStep, Mathf.Abs(y - _prevShellY));
            _prevRave = r;
            _prevShellY = y;
            _samples++;
        }

        static string Rgb(Color c) { return c.r.ToString("F0") + ", " + c.g.ToString("F0") + ", " + c.b.ToString("F0"); }

        /// <summary>v9 : capture depuis le podium vers −Z (galeries) + couleur moyenne des pixels du théâtre (rendu de masque). La frame suivante est rendue
        /// sans réflexions d'environnement (FinishMeasure) pour isoler leur part : elle doit avoir la chromaticité de l'ambiant, pas celle d'une skybox.</summary>
        static void MeasureTheatre(string label, string outPath)
        {
            Color[] means;
            float[] cov;
            MaestroScreenshot.CaptureAndMeasure(_sb, Eye, LookBack, 65f, outPath, new[] { _theatreRenderers }, out means, out cov);
            _pendingLabel = label;
            _pendingOn = means[0];
            _pendingCov = cov[0];
            _pendingFrame = Time.frameCount;
            _pendingReflKeep = RenderSettings.reflectionIntensity;
            RenderSettings.reflectionIntensity = 0f;
        }

        /// <summary>Seconde mesure (réflexions à 0) sur une frame ultérieure, puis contrôle : dominante cyan (G+B)/2 − R du théâtre ≤ max(8, celle de l'ambiant + 8)
        /// et part des réflexions de même chromaticité que l'ambiant (écart ≤ 0,10 par canal) quand elle n'est pas négligeable.</summary>
        static void FinishMeasure()
        {
            if (_pendingLabel == null || Time.frameCount <= _pendingFrame) return;
            Color[] means;
            float[] cov;
            MaestroScreenshot.CaptureAndMeasure(_sb, Eye, LookBack, 65f, null, new[] { _theatreRenderers }, out means, out cov);
            RenderSettings.reflectionIntensity = _pendingReflKeep;
            string label = _pendingLabel;
            _pendingLabel = null;
            Color amb = RenderSettings.ambientLight * 255f;
            Color on = _pendingOn, off = means[0];
            Color d = on - off;
            float ambCyan = (amb.g + amb.b) * 0.5f - amb.r, onCyan = (on.g + on.b) * 0.5f - on.r;
            float dSum = d.r + d.g + d.b, aSum = amb.r + amb.g + amb.b;
            bool reflSignificant = dSum > 6f && aSum > 1f;
            float chromaErr = reflSignificant ? Mathf.Max(Mathf.Abs(d.r / dSum - amb.r / aSum), Mathf.Abs(d.g / dSum - amb.g / aSum), Mathf.Abs(d.b / dSum - amb.b / aSum)) : 0f;
            string info = "Théâtre à " + label + " (vue −Z, " + (_pendingCov * 100f).ToString("F1") + " % de l'image) : RGB moyen (" + Rgb(on) + "), dominante cyan (G+B)/2−R = " + onCyan.ToString("F1") + " (ambiant (" + Rgb(amb) + ") : " + ambCyan.ToString("F1") + ") ; part des réflexions d'environnement (" + Rgb(d) + ")" + (reflSignificant ? ", chromaticité à " + chromaErr.ToString("F2") + " de celle de l'ambiant" : ", négligeable");
            if (_pendingCov < 0.005f) { _sb.AppendLine("  " + info + " → théâtre hors champ, pas de contrôle"); return; }
            Check(onCyan <= Mathf.Max(8f, ambCyan + 8f) && chromaErr <= 0.10f, info + " (attendu : dominante cyan ≤ max(8, ambiant + 8) et réflexions de la chromaticité de l'ambiant à 0,10 près)");
        }

        static void Click(string method)
        {
            Button b;
            if (!_buttons.TryGetValue(method, out b)) { Check(false, "bouton " + method + " introuvable"); return; }
            b.onClick.Invoke();
            _sb.AppendLine("--- Bouton « " + b.GetComponentInChildren<TMPro.TextMeshProUGUI>(true).text + " » (" + method + ") invoqué via onClick");
        }

        /// <summary>Sol sous l'ancre des caissons : premier impact sous (ancre + 1 m), caissons et triggers ignorés (cour à -1.5 en Play Mode, le théâtre n'a pas de colliders).</summary>
        static void DetectFloor()
        {
            _floorY = float.NaN;
            _floorName = "aucun";
            RaycastHit[] hits = Physics.RaycastAll(_anchorSpeakers.position + Vector3.up * 1f, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (h.transform.IsChildOf(_speakers.transform)) continue;
                _floorY = h.point.y;
                _floorName = h.transform.name;
                break;
            }
        }

        static float LowestPoint(Transform model)
        {
            float min = float.MaxValue;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true)) min = Mathf.Min(min, r.bounds.min.y);
            return min;
        }

        static bool Setup()
        {
            GameObject envGo = GameObject.Find("Environment");
            if (envGo == null) { _sb.AppendLine("PROBLEME: Environment introuvable"); return false; }
            _env = envGo.GetComponent<Animator>();
            _mood = envGo.GetComponent<EnvironmentMood>();
            _opera = envGo.transform.Find("OperaShell");
            _castle = envGo.transform.Find("CastleShell");
            _dome = envGo.transform.Find(MaestroGreybox.DomePath);
            _anchorSpeakers = envGo.transform.Find("Anchor_Speakers");
            Transform lustreLight = _opera != null ? _opera.Find("Lustre/Lustre_Light") : null;
            _lustre = lustreLight != null ? lustreLight.GetComponent<Light>() : null;
            if (_env == null || _mood == null || _opera == null || _castle == null || _dome == null || _lustre == null || _mood.moonRenderer == null || _anchorSpeakers == null)
            {
                _sb.AppendLine("PROBLEME: Environment incomplet (Animator/EnvironmentMood/OperaShell/CastleShell/Dome/Lustre_Light/lune/Anchor_Speakers)");
                return false;
            }
            _lustreModel = _opera.Find("Lustre/Model");
            _torch1 = _castle.Find("Torch_1");
            Transform theatre = _opera.Find("Model");
            _theatreRenderers = theatre != null ? theatre.GetComponentsInChildren<Renderer>(false) : new Renderer[0];
            _sb.AppendLine("Théâtre : " + _theatreRenderers.Length + " renderers (mesure de la dominante cyan sur leurs pixels, vue −Z)");
            _poursuites.Clear();
            Transform pours = _opera.Find("Poursuites");
            if (pours != null) _poursuites.AddRange(pours.GetComponentsInChildren<Light>(true));
            if (_poursuites.Count != MaestroGreybox.PoursuiteCount + 1) { _sb.AppendLine("PROBLEME: groupe OperaShell/Poursuites : " + _poursuites.Count + " lumière(s) (attendu " + (MaestroGreybox.PoursuiteCount + 1) + ")"); return false; }

            GameObject spk = GameObject.Find("Speakers");
            if (spk == null) { _sb.AppendLine("PROBLEME: Speakers introuvable"); return false; }
            _speakers = spk.GetComponent<Animator>();
            _spkFirst = spk.transform.Find("Speaker_1");
            _spkLast = spk.transform.Find("Speaker_" + spk.transform.childCount);
            _speakerModels.Clear();
            foreach (Transform s in spk.transform) { Transform m = s.Find("Model"); if (m != null) _speakerModels.Add(m); }
            _sb.AppendLine("Habillage : lustre modèle=" + (_lustreModel != null) + ", Torch_1 modèle=" + (_torch1 != null && _torch1.Find("Model") != null) + ", caissons avec modèle " + _speakerModels.Count + "/" + spk.transform.childCount + ", théâtre=" + (_opera.Find("Model") != null) + ", coupole animée=" + MaestroGreybox.DomePath + ", Anchor_Speakers @ " + _anchorSpeakers.position.ToString("F2"));

            _musicians.Clear();
            foreach (string n in MaestroGreybox.SectionPrefabs)
            {
                GameObject s = GameObject.Find(n);
                if (s == null) { _sb.AppendLine("PROBLEME: section " + n + " introuvable"); return false; }
                _musicians.AddRange(s.GetComponentsInChildren<Animator>(true));
            }
            Transform m1 = GameObject.Find("Strings").transform.Find("Musician_1");
            _m1Body = m1 != null ? m1.Find("Body") : null;
            _m1Head = m1 != null ? m1.Find("Head") : null;
            if (_m1Body == null || _m1Head == null) { _sb.AppendLine("PROBLEME: Strings/Musician_1 incomplet"); return false; }

            GameObject dp = GameObject.Find("DebugPanel");
            _panel = dp != null ? dp.GetComponent<MaestroArtDebugPanel>() : null;
            if (_panel == null || _panel.panel == null) { _sb.AppendLine("PROBLEME: DebugPanel introuvable"); return false; }
            _buttons.Clear();
            foreach (Button b in _panel.panel.GetComponentsInChildren<Button>(true))
                if (b.onClick.GetPersistentEventCount() > 0) _buttons[b.onClick.GetPersistentMethodName(0)] = b;

            GameObject sl = GameObject.Find("StageLights");
            _spots = sl != null ? sl.GetComponentsInChildren<Light>(true) : new Light[0];
            GameObject lz = GameObject.Find("Lasers");
            _lasers = lz != null ? lz.transform : null;
            if (_spots.Length == 0 || _lasers == null) { _sb.AppendLine("PROBLEME: StageLights ou Lasers introuvable"); return false; }

            _sb.AppendLine("Cibles : " + _musicians.Count + " musiciens (attendu " + MaestroGreybox.MusicianCount + "), " + _buttons.Count + " boutons, " + _spots.Length + " spots, " + _lasers.childCount + " lasers, " + _poursuites.Count + " lumières de poursuite (" + PoursuitesState() + "), caissons Hidden=" + _speakers.GetCurrentAnimatorStateInfo(0).IsName("Hidden") + " (Speaker_1.y=" + _spkFirst.localPosition.y.ToString("F1") + "), hand tracking actif=" + _panel.HandTrackingAvailable);
            Check(_musicians.Count == MaestroGreybox.MusicianCount, "Effectif : " + _musicians.Count + " Animator de musicien (attendu " + MaestroGreybox.MusicianCount + ")");
            return true;
        }

        // ------------------------------------------------------------------ machine à états

        static void Update()
        {
            try
            {
                if (!EditorApplication.isPlaying) { Finish("FAIL", "Play Mode quitté avant la fin du test"); return; }
                double t = EditorApplication.timeSinceStartup - _phaseStart;
                switch (_phase)
                {
                    case 0:
                        if (t < 0.5) return;
                        if (!Setup()) { Finish("FAIL", "cibles manquantes"); return; }
                        MaximizeGameView(true);
                        NextPhase(20);
                        break;
                    case 20:
                        if (t < 1.0) return; // quelques frames rendues avec la Game View maximisée
                        _sb.AppendLine("=== Transition Opera → Rave → Reset ===");
                        _sb.AppendLine("  Avant : " + Sample());
                        Check(PoursuitesAtBase(), "Poursuites avant Rave : " + PoursuitesState() + " (attendu toutes allumées à " + MaestroGreybox.PoursuiteIntensity + " / fill " + MaestroGreybox.FillIntensity + ")");
                        _sb.AppendLine("  Rendu mode opéra (Game View maximisée, caméra du rig), " + RenderStats());
                        _env.SetTrigger("Rave");
                        NextPhase(1);
                        break;
                    case 1:
                        FinishMeasure();
                        if (!_sampled1s && t >= 1.0)
                        {
                            _sampled1s = true;
                            MeasureTheatre("1 s", Path.Combine(MaestroScreenshot.Dir, "v9-transition-1s.png"));
                        }
                        if (!_sampled3s && t >= 3.0)
                        {
                            _sampled3s = true;
                            _sb.AppendLine("  À 3 s : " + Sample() + " | poursuites : " + PoursuitesState());
                            MeasureTheatre("3 s", Path.Combine(MaestroScreenshot.Dir, "v9-transition-3s.png"));
                        }
                        if (!_sampled5s && t >= 5.0)
                        {
                            _sampled5s = true;
                            MeasureTheatre("5 s", Path.Combine(MaestroRunner.ProjectRoot, "Temp", "v9-transition-5s.png"));
                        }
                        if (t >= 7.0)
                        {
                            Expect("À 7 s", 1f, MaestroGreybox.OperaShellRaveY, 0f, 0f, 0.008f);
                            Check(PoursuitesOff(), "Poursuites à 7 s : " + PoursuitesState() + " (attendu intensité 0, éteintes par EnvironmentMood)");
                            _sb.AppendLine("  Rendu mode rave (Game View maximisée, caméra du rig), " + RenderStats());
                            Check(_dome.localScale.x < 1e-4f && Mathf.Abs(_dome.localPosition.y - (MaestroGreybox.DomeRestPos.y + MaestroGreybox.DomeRise)) < 0.05f, "Coupole à 7 s : échelle " + _dome.localScale.ToString("F2") + " y local " + _dome.localPosition.y.ToString("F1") + " (attendu 0 / " + (MaestroGreybox.DomeRestPos.y + MaestroGreybox.DomeRise) + ")");
                            if (_lustreModel != null)
                            {
                                float expectedY = MaestroGreybox.LustrePos.y + MaestroGreybox.LustreRise + MaestroGreybox.OperaShellRaveY;
                                Check(Mathf.Abs(_lustreModel.position.y - expectedY) < 0.05f, "Lustre modèle à 7 s : y monde " + _lustreModel.position.y.ToString("F2") + " (attendu " + expectedY + " = accroche " + MaestroGreybox.LustrePos.y + " + 60 - 30)");
                            }
                            if (_torch1 != null)
                                Check(Mathf.Abs(_torch1.position.y - _torch1.localPosition.y) < 0.05f, "Torch_1 à 7 s : y monde " + _torch1.position.y.ToString("F2") + " (remontée avec le château, attendu " + _torch1.localPosition.y.ToString("F2") + ")");
                            MeasureTheatre("7 s (rave)", Path.Combine(MaestroScreenshot.Dir, "v9-rave.png"));
                            _env.SetTrigger("Reset");
                            NextPhase(2);
                        }
                        break;
                    case 2:
                        FinishMeasure();
                        if (t >= 7.0)
                        {
                            Expect("7 s après Reset", 0f, 0f, MaestroGreybox.CastleShellHiddenY, MaestroGreybox.LustreIntensity, 0f);
                            Check((_dome.localScale - MaestroGreybox.DomeRestScale).magnitude < 0.01f && (_dome.localPosition - MaestroGreybox.DomeRestPos).magnitude < 0.05f, "Coupole après Reset : échelle " + _dome.localScale.ToString("F1") + " position " + _dome.localPosition.ToString("F1") + " (attendu " + MaestroGreybox.DomeRestScale.ToString("F1") + " / " + MaestroGreybox.DomeRestPos.ToString("F1") + ")");
                            Check(PoursuitesAtBase(), "Poursuites après Reset : " + PoursuitesState() + " (attendu rallumées à " + MaestroGreybox.PoursuiteIntensity + " / fill " + MaestroGreybox.FillIntensity + ")");
                            NextPhase(3);
                        }
                        break;
                    case 3:
                        _sb.AppendLine("=== DebugPanel : boutons via onClick ===");
                        _panel.TogglePanel();
                        Check(_panel.IsPanelVisible, "TogglePanel() : panneau visible=" + _panel.IsPanelVisible + " @ " + _panel.panel.transform.position.ToString("F2"));
                        Check(RaveLabel() == _panel.raveLabelOpera, "Libellé du bouton Rave à l'opéra : « " + RaveLabel() + " » (attendu « " + _panel.raveLabelOpera + " »)");
                        Click("OnRave");
                        NextPhase(4);
                        break;
                    case 4:
                        if (t >= 0.3)
                        {
                            Check(EnvState() == "Rave" && _mood.rave > 0f && RaveLabel() == _panel.raveLabelRave, "Rave (bascule, 1) : état Environment=" + EnvState() + ", rave=" + _mood.rave.ToString("F3") + ", libellé « " + RaveLabel() + " » (attendu Rave / « " + _panel.raveLabelRave + " »)");
                            StartContinuity();
                            NextPhase(40);
                        }
                        break;
                    case 40:
                        TrackContinuity();
                        if (t >= 2.7) // 3 s après le clic Rave : en pleine transition
                        {
                            _raveAtToggle = _mood.rave;
                            _sb.AppendLine("  À 3 s de transition : rave=" + _mood.rave.ToString("F3") + " opera.y=" + _opera.localPosition.y.ToString("F2") + " ; clic « " + RaveLabel() + " » (bascule → Reset)");
                            Click("OnRave");
                            NextPhase(41);
                        }
                        break;
                    case 41:
                        TrackContinuity();
                        if (t >= 0.3)
                        {
                            Check(EnvState() == "ToOpera" && _mood.rave <= _raveAtToggle + 0.01f && _mood.rave >= _raveAtToggle - 0.2f && RaveLabel() == _panel.raveLabelOpera, "Rave (bascule, 2) à 3 s : état Environment=" + EnvState() + ", rave " + _raveAtToggle.ToString("F3") + " → " + _mood.rave.ToString("F3") + " après 0,3 s, libellé « " + RaveLabel() + " » (attendu ToOpera, rave en baisse continue, « " + _panel.raveLabelOpera + " »)");
                            NextPhase(42);
                        }
                        break;
                    case 42:
                        TrackContinuity();
                        if (t >= 3.8)
                        {
                            Check(EnvState() == "Opera" && _mood.rave < 0.001f && Mathf.Abs(_opera.localPosition.y) < 0.05f && Mathf.Abs(_castle.localPosition.y - MaestroGreybox.CastleShellHiddenY) < 0.05f, "Retour depuis 3 s de transition : état " + EnvState() + ", rave=" + _mood.rave.ToString("F3") + ", opera.y=" + _opera.localPosition.y.ToString("F2") + ", castle.y=" + _castle.localPosition.y.ToString("F1") + " après 4,1 s (attendu Opera, 0, 0, " + MaestroGreybox.CastleShellHiddenY + ")");
                            Check(_maxRaveStep <= 0.05f && _maxShellStep <= 2f, "Continuité (" + _samples + " éch.) : saut max de rave par échantillon " + _maxRaveStep.ToString("F4") + ", d'OperaShell.y " + _maxShellStep.ToString("F3") + " m (attendu ≤ 0,05 et ≤ 2 : reprise à l'instant miroir, sans saut ; un saut vaudrait ≈ 0,5 et ≈ 25 m)");
                            Click("OnReset");
                            NextPhase(5);
                        }
                        break;
                    case 5:
                        if (t >= 0.3)
                        {
                            Check(EnvState() == "Opera" && _mood.rave < 0.001f && !_env.GetBool("Reset"), "Reset à l'opéra (bouton séparé) : état Environment=" + EnvState() + ", rave=" + _mood.rave.ToString("F3") + ", déclencheur Reset en attente=" + _env.GetBool("Reset") + " (attendu Opera, 0, effacé par EnvironmentMood)");
                            DetectFloor();
                            _dropMinMargin = float.MaxValue;
                            _dropMinT = 0f;
                            _dropMinName = "";
                            _sb.AppendLine("  Sol sous Anchor_Speakers " + _anchorSpeakers.position.ToString("F2") + " : " + _floorName + " à y " + _floorY.ToString("F2"));
                            Click("OnDrop");
                            NextPhase(6);
                        }
                        break;
                    case 6:
                        Track(_spkFirst.localPosition.y, _spkLast.localPosition.y);
                        if (!float.IsNaN(_floorY))
                        {
                            foreach (Transform m in _speakerModels)
                            {
                                float margin = LowestPoint(m) - _floorY;
                                if (margin < _dropMinMargin) { _dropMinMargin = margin; _dropMinT = (float)t; _dropMinName = m.parent.name; }
                            }
                        }
                        if (t >= MaestroGreybox.SpeakerDropDuration + 0.8)
                        {
                            int landed = 0, models = 0;
                            foreach (Transform s in _spkFirst.parent)
                            {
                                if (Mathf.Abs(s.localPosition.y - MaestroGreybox.SpeakerRestY) < 0.01f) landed++;
                                if (s.Find("Model") != null) models++;
                            }
                            bool dropped = _speakers.GetCurrentAnimatorStateInfo(0).IsName("Dropped") && landed == _spkFirst.parent.childCount && models == _spkFirst.parent.childCount;
                            Check(dropped, "Drop caissons (" + _samples + " éch.) : état Dropped=" + _speakers.GetCurrentAnimatorStateInfo(0).IsName("Dropped") + " | " + landed + "/" + _spkFirst.parent.childCount + " posés à " + MaestroGreybox.SpeakerRestY + ", " + models + " avec Model | Speaker_1.y " + Range(_minA, _maxA) + " | " + _spkLast.name + ".y " + Range(_minB, _maxB) + " (attendu " + MaestroGreybox.SpeakerHiddenY + " → " + MaestroGreybox.SpeakerRestY + ")");
                            bool floorOk = !float.IsNaN(_floorY) && _dropMinMargin >= -0.005f;
                            Check(floorOk, "Chute frame par frame (" + _samples + " frames) : point le plus bas des caissons au minimum à " + (float.IsNaN(_floorY) ? "?" : _dropMinMargin.ToString("F3")) + " m au-dessus du sol " + _floorName + " (y " + _floorY.ToString("F2") + "), " + _dropMinName + " à t = " + _dropMinT.ToString("F2") + " s (attendu ≥ 0 : jamais sous le sol, ni à l'impact ni au repos)");
                            Vector3 camPos = _anchorSpeakers.position + new Vector3(-6f, 3.5f, -2.5f);
                            MaestroScreenshot.CaptureView(_sb, camPos, Quaternion.LookRotation(_anchorSpeakers.position - camPos, Vector3.up), 65f, Path.Combine(MaestroScreenshot.Dir, "v8-caissons.png"));
                            Click("OnMusiciansPlay");
                            NextPhase(7);
                        }
                        break;
                    case 7:
                        Track(_m1Body.localPosition.y, SignedX(_m1Head));
                        if (t >= 1.2)
                        {
                            int n = CountMusiciansIn("Play");
                            Check(n == _musicians.Count && n == MaestroGreybox.MusicianCount && _maxA > 0.62f, "Musiciens : Play : " + n + "/" + _musicians.Count + " en état Play (attendu " + MaestroGreybox.MusicianCount + ") | Strings/Musician_1 body.y " + Range(_minA, _maxA) + " (attendu 0.600 → 0.630) head.rotX " + Range(_minB, _maxB) + " (attendu -8 → +8)");
                            Click("OnMusiciansHeadbang");
                            NextPhase(8);
                        }
                        break;
                    case 8:
                        Track(_m1Body.localPosition.y, SignedX(_m1Head));
                        if (t >= 1.2)
                        {
                            int n = CountMusiciansIn("Headbang");
                            Check(n == _musicians.Count && n == MaestroGreybox.MusicianCount && _minB < -25f, "Musiciens : Headbang : " + n + "/" + _musicians.Count + " en état Headbang | body.y " + Range(_minA, _maxA) + " (attendu 0.600 → 0.650) head.rotX " + Range(_minB, _maxB) + " (attendu -30 → +10)");
                            Click("OnMusiciansStop");
                            NextPhase(9);
                        }
                        break;
                    case 9:
                        if (t >= 0.5)
                        {
                            int n = CountMusiciansIn("Idle");
                            Check(n == _musicians.Count && n == MaestroGreybox.MusicianCount, "Musiciens : Stop : " + n + "/" + _musicians.Count + " en état Idle");
                            Click("OnToggleSpots");
                            NextPhase(10);
                        }
                        break;
                    case 10:
                        if (t >= 0.1)
                        {
                            Check(SpotsOn() == _spots.Length, "Spots on/off (1) : " + SpotsOn() + "/" + _spots.Length + " allumés (attendu " + _spots.Length + ")");
                            Click("OnToggleSpots");
                            NextPhase(11);
                        }
                        break;
                    case 11:
                        if (t >= 0.1)
                        {
                            Check(SpotsOn() == 0, "Spots on/off (2) : " + SpotsOn() + "/" + _spots.Length + " allumés (attendu 0)");
                            Click("OnToggleLasers");
                            NextPhase(12);
                        }
                        break;
                    case 12:
                        if (t >= 0.1)
                        {
                            Check(LasersOn() == _lasers.childCount, "Lasers on/off (1) : " + LasersOn() + "/" + _lasers.childCount + " actifs (attendu " + _lasers.childCount + ")");
                            Click("OnToggleLasers");
                            NextPhase(13);
                        }
                        break;
                    case 13:
                        if (t >= 0.1)
                        {
                            Check(LasersOn() == 0, "Lasers on/off (2) : " + LasersOn() + "/" + _lasers.childCount + " actifs (attendu 0)");
                            Check(PoursuitesEnabled() == _poursuites.Count, "Poursuites avant le bouton : " + PoursuitesState() + " (attendu toutes allumées)");
                            Click("OnTogglePoursuites");
                            NextPhase(16);
                        }
                        break;
                    case 16:
                        if (t >= 0.5) // plusieurs LateUpdate d'EnvironmentMood : l'état éteint doit être conservé
                        {
                            Check(PoursuitesEnabled() == 0, "Poursuites on/off (1) : " + PoursuitesState() + " après 0.5 s (attendu 0 allumée, état conservé par EnvironmentMood)");
                            Click("OnTogglePoursuites");
                            NextPhase(17);
                        }
                        break;
                    case 17:
                        if (t >= 0.5)
                        {
                            Check(PoursuitesEnabled() == _poursuites.Count && PoursuitesAtBase(), "Poursuites on/off (2) : " + PoursuitesState() + " (attendu toutes rallumées à leur base)");
                            NextPhase(50);
                        }
                        break;
                    case 50:
                        _sb.AppendLine("=== Highlight de section et de musicien (v10 : émission par MaterialPropertyBlock lue sur les renderers) ===");
                        {
                            int withHl = 0;
                            foreach (string n in MaestroGreybox.SectionPrefabs) if (_panel.GetHighlight(n) != null) withHl++;
                            Check(withHl == MaestroGreybox.SectionPrefabs.Length, "SectionHighlight trouvé par le panneau sur " + withHl + "/" + MaestroGreybox.SectionPrefabs.Length + " sections");
                            Check(HighlightLabel() == _panel.highlightLabelIdle, "Libellé initial du bouton : « " + HighlightLabel() + " » (attendu « " + _panel.highlightLabelIdle + " »)");
                        }
                        Click("OnHighlightTest"); // Strings section
                        NextPhase(51);
                        break;
                    case 51:
                        if (t >= 0.6)
                        {
                            CheckEmission(Body("Strings", 0), "Strings", MaestroGreybox.HighlightSoft, MaestroGreybox.HighlightSoft, "Strings section : Strings/Musician_1 Body doux");
                            CheckEmission(Body("Strings", 2), "Strings", MaestroGreybox.HighlightSoft, MaestroGreybox.HighlightSoft, "Strings section : Strings/Musician_3 Body doux");
                            CheckOff(Body("Vent", 0), "Strings section : Vent/Musician_1 Body éteint (non concerné)");
                            Check(HighlightLabel() == "Highlight : Strings section", "Libellé « " + HighlightLabel() + " » (attendu « Highlight : Strings section »)");
                            Click("OnHighlightTest"); // Strings musicien 3
                            _pulseMin = float.MaxValue;
                            _pulseMax = float.MinValue;
                            NextPhase(52);
                        }
                        break;
                    case 52:
                        if (t >= 0.3)
                        {
                            float e = Intensity(Body("Strings", 2), "Strings");
                            _pulseMin = Mathf.Min(_pulseMin, e);
                            _pulseMax = Mathf.Max(_pulseMax, e);
                        }
                        if (t >= 1.5)
                        {
                            float lo = MaestroGreybox.HighlightStrong - MaestroGreybox.HighlightPulseAmplitude, hi = MaestroGreybox.HighlightStrong + MaestroGreybox.HighlightPulseAmplitude;
                            CheckEmission(Body("Strings", 2), "Strings", lo, hi, "Strings musicien 3 : Strings/Musician_3 Body fort");
                            Check(_pulseMax - _pulseMin >= 0.5f && _pulseMin >= lo - 0.05f && _pulseMax <= hi + 0.05f, "Pulsation du musicien fort sur 1,2 s : intensité " + _pulseMin.ToString("F2") + " → " + _pulseMax.ToString("F2") + " (attendu entre " + lo + " et " + hi + ", amplitude ≥ 0,5 à " + MaestroGreybox.HighlightPulseHz + " Hz)");
                            CheckEmission(Body("Strings", 0), "Strings", MaestroGreybox.HighlightSoft, MaestroGreybox.HighlightSoft, "Strings musicien 3 : Strings/Musician_1 Body doux (section impliquée)");
                            CheckOff(Body("Vent", 0), "Strings musicien 3 : Vent/Musician_1 Body éteint (non concerné)");
                            Check(_panel.GetHighlight("Strings").HighlightedMusician == 2 && _panel.GetHighlight("Strings").SectionHighlighted, "API Strings : HighlightedMusician=" + _panel.GetHighlight("Strings").HighlightedMusician + ", SectionHighlighted=" + _panel.GetHighlight("Strings").SectionHighlighted + " (attendu 2 / True)");
                            Click("OnHighlightTest"); // Vent musicien 1
                            NextPhase(53);
                        }
                        break;
                    case 53:
                        if (t >= 0.6)
                        {
                            float lo = MaestroGreybox.HighlightStrong - MaestroGreybox.HighlightPulseAmplitude, hi = MaestroGreybox.HighlightStrong + MaestroGreybox.HighlightPulseAmplitude;
                            CheckEmission(Body("Vent", 0), "Vent", lo, hi, "Vent musicien 1 : Vent/Musician_1 Body fort");
                            CheckEmission(Head("Vent", 0), "Vent", lo, hi, "Vent musicien 1 : Vent/Musician_1 Head forte aussi");
                            CheckEmission(Body("Vent", 1), "Vent", MaestroGreybox.HighlightSoft, MaestroGreybox.HighlightSoft, "Vent musicien 1 : Vent/Musician_2 Body doux (section impliquée)");
                            CheckOff(Body("Strings", 2), "Vent musicien 1 : Strings/Musician_3 Body éteint (étape précédente effacée)");
                            CheckOff(Head("Strings", 0), "Vent musicien 1 : Strings/Musician_1 Head éteinte");
                            CheckOff(Instrument("Vent", 0), "Vent musicien 1 : modèle d'instrument de Vent/Musician_1 non affecté");
                            Check(HighlightLabel() == "Highlight : Vent musicien 1", "Libellé « " + HighlightLabel() + " » (attendu « Highlight : Vent musicien 1 »)");
                            MaestroScreenshot.CaptureView(_sb, Eye, Quaternion.identity, 65f, Path.Combine(MaestroScreenshot.Dir, "v10-highlight.png"));
                            Click("OnRave"); // Rave avec un highlight actif
                            NextPhase(54);
                        }
                        break;
                    case 54:
                        if (t >= 0.5)
                        {
                            float lo = MaestroGreybox.HighlightStrong - MaestroGreybox.HighlightPulseAmplitude, hi = MaestroGreybox.HighlightStrong + MaestroGreybox.HighlightPulseAmplitude;
                            Check(EnvState() == "Rave" && _mood.rave > 0f, "Rave pendant le highlight : état Environment=" + EnvState() + ", rave=" + _mood.rave.ToString("F3"));
                            CheckEmission(Body("Vent", 0), "Vent", lo, hi, "Rave pendant le highlight : Vent/Musician_1 Body toujours fort");
                            Click("OnRave"); // bascule → Reset
                            NextPhase(55);
                        }
                        break;
                    case 55:
                        if (t >= 2.0)
                        {
                            float lo = MaestroGreybox.HighlightStrong - MaestroGreybox.HighlightPulseAmplitude, hi = MaestroGreybox.HighlightStrong + MaestroGreybox.HighlightPulseAmplitude;
                            Check(EnvState() == "Opera" && _mood.rave < 0.001f, "Reset pendant le highlight : état Environment=" + EnvState() + ", rave=" + _mood.rave.ToString("F3") + " après 2 s (attendu Opera, 0)");
                            Check(_speakers.GetCurrentAnimatorStateInfo(0).IsName("Dropped"), "Drop non affecté : caissons en état Dropped=" + _speakers.GetCurrentAnimatorStateInfo(0).IsName("Dropped"));
                            CheckEmission(Body("Vent", 0), "Vent", lo, hi, "Après Rave/Reset : Vent/Musician_1 Body toujours fort");
                            Click("OnHighlightTest"); // Brass section
                            NextPhase(56);
                        }
                        break;
                    case 56:
                        if (t >= 0.6)
                        {
                            CheckEmission(Body("Brass", 0), "Brass", MaestroGreybox.HighlightSoft, MaestroGreybox.HighlightSoft, "Brass section : Brass/Musician_1 Body doux");
                            CheckOff(Body("Vent", 0), "Brass section : Vent/Musician_1 Body éteint");
                            Click("OnHighlightTest"); // Percution musicien 2
                            NextPhase(57);
                        }
                        break;
                    case 57:
                        if (t >= 0.6)
                        {
                            float lo = MaestroGreybox.HighlightStrong - MaestroGreybox.HighlightPulseAmplitude, hi = MaestroGreybox.HighlightStrong + MaestroGreybox.HighlightPulseAmplitude;
                            CheckEmission(Body("Percution", 1), "Percution", lo, hi, "Percution musicien 2 : Percution/Musician_2 Body fort");
                            CheckEmission(Body("Percution", 0), "Percution", MaestroGreybox.HighlightSoft, MaestroGreybox.HighlightSoft, "Percution musicien 2 : Percution/Musician_1 Body doux");
                            CheckOff(Body("Brass", 0), "Percution musicien 2 : Brass/Musician_1 Body éteint");
                            Click("OnHighlightTest"); // ClearAll
                            NextPhase(58);
                        }
                        break;
                    case 58:
                        if (t >= 0.6)
                        {
                            int lit = 0, total = 0;
                            foreach (string n in MaestroGreybox.SectionPrefabs)
                            {
                                GameObject s = GameObject.Find(n);
                                if (s == null) continue;
                                foreach (Transform m in s.transform)
                                {
                                    if (!m.name.StartsWith("Musician_")) continue;
                                    foreach (Renderer r in m.GetComponentsInChildren<Renderer>()) { total++; if (Emission(r).maxColorComponent > 0.01f) lit++; }
                                }
                            }
                            Check(lit == 0 && total >= MaestroGreybox.MusicianCount * 2, "ClearAll : " + lit + "/" + total + " renderers de musiciens encore émissifs (attendu 0)");
                            Check(HighlightLabel() == "Highlight : ClearAll", "Libellé « " + HighlightLabel() + " » (attendu « Highlight : ClearAll »)");
                            _panel.GetHighlight("Strings").SetMusicianHighlighted(2, true); // API directe : un seul musicien fort par section
                            NextPhase(59);
                        }
                        break;
                    case 59:
                        if (t >= 0.6)
                        {
                            float lo = MaestroGreybox.HighlightStrong - MaestroGreybox.HighlightPulseAmplitude, hi = MaestroGreybox.HighlightStrong + MaestroGreybox.HighlightPulseAmplitude;
                            CheckEmission(Body("Strings", 2), "Strings", lo, hi, "API SetMusicianHighlighted(2, true) : Strings/Musician_3 Body fort");
                            CheckEmission(Body("Strings", 0), "Strings", MaestroGreybox.HighlightSoft, MaestroGreybox.HighlightSoft, "API : Strings/Musician_1 Body doux (section impliquée)");
                            _panel.GetHighlight("Strings").SetMusicianHighlighted(5, true);
                            NextPhase(60);
                        }
                        break;
                    case 60:
                        if (t >= 0.6)
                        {
                            float lo = MaestroGreybox.HighlightStrong - MaestroGreybox.HighlightPulseAmplitude, hi = MaestroGreybox.HighlightStrong + MaestroGreybox.HighlightPulseAmplitude;
                            CheckEmission(Body("Strings", 5), "Strings", lo, hi, "API SetMusicianHighlighted(5, true) : Strings/Musician_6 Body fort");
                            CheckEmission(Body("Strings", 2), "Strings", MaestroGreybox.HighlightSoft, MaestroGreybox.HighlightSoft, "API : Strings/Musician_3 Body redescendu en doux (un seul fort par section)");
                            Check(_panel.GetHighlight("Strings").HighlightedMusician == 5, "API : HighlightedMusician=" + _panel.GetHighlight("Strings").HighlightedMusician + " (attendu 5)");
                            _panel.GetHighlight("Strings").ClearAll();
                            NextPhase(61);
                        }
                        break;
                    case 61:
                        if (t >= 0.6)
                        {
                            CheckOff(Body("Strings", 5), "API ClearAll : Strings/Musician_6 Body éteint");
                            CheckOff(Body("Strings", 0), "API ClearAll : Strings/Musician_1 Body éteint");
                            Check(!_panel.GetHighlight("Strings").SectionHighlighted && _panel.GetHighlight("Strings").HighlightedMusician == -1, "API ClearAll : SectionHighlighted=" + _panel.GetHighlight("Strings").SectionHighlighted + ", HighlightedMusician=" + _panel.GetHighlight("Strings").HighlightedMusician + " (attendu False / -1)");
                            Click("OnClose");
                            NextPhase(14);
                        }
                        break;
                    case 14:
                        if (t >= 0.1)
                        {
                            Check(!_panel.IsPanelVisible, "Fermer : panneau visible=" + _panel.IsPanelVisible);
                            NextPhase(15);
                        }
                        break;
                    case 15:
                        if (t >= 1.5)
                        {
                            Check(_panel.HoldProgress == 0f, "Détecteur de geste : hand tracking actif=" + _panel.HandTrackingAvailable + ", progression=" + _panel.HoldProgress + ", état final Environment=" + EnvState() + " (" + Sample() + ")");
                            Finish(_failures == 0 ? "OK" : "FAIL", _failures == 0 ? null : _failures + " contrôle(s) en échec");
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                Finish("FAIL", "EXCEPTION: " + e);
            }
        }

        static void Finish(string status, string note)
        {
            EditorApplication.update -= Update;
            if (_gameView != null) MaximizeGameView(false);
            if (note != null) _sb.AppendLine(note);
            _sb.AppendLine(MaestroConsole.Summary());
            Debug.Log("[Maestro] PlayTest " + status + "\n" + _sb);
            MaestroRunner.PlayLog("PlayTest " + status);
            try { File.WriteAllText(_resultPath, "STATUS: " + status + "\nCOMMAND: playtest\n" + _sb); }
            catch (Exception e) { Debug.LogError("[Maestro] " + e.Message); }
            if (_autoExit && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }
    }
}
