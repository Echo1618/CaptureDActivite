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
    /// Test en Play Mode (v3) :
    ///  1. transition Opera → Rave → Reset : échantillons (rave, OperaShell.y, CastleShell.y, intensité du lustre, fogDensity)
    ///     avant, à 3 s et 7 s après Rave (captures v3-transition.png / v3-rave.png), puis 7 s après Reset ;
    ///  2. chaque bouton du DebugPanel déclenché par onClick.Invoke() avec contrôle de l'effet sur sa cible ;
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
        static bool _captured3s;

        static Animator _env;
        static EnvironmentMood _mood;
        static Transform _opera, _castle, _dome;
        static Light _lustre;
        static Animator _speakers;
        static Transform _spkFirst, _spkLast;
        static readonly List<Animator> _musicians = new List<Animator>();
        static Transform _m1Body, _m1Head;
        static MaestroDebugPanel _panel;
        static readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>();
        static Light[] _spots;
        static Transform _lasers;
        static float _minA, _maxA, _minB, _maxB;
        static int _samples;

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
            _captured3s = false;
            _phaseStart = EditorApplication.timeSinceStartup;
            _autoExit = SessionState.GetBool(AutoExitKey, true);
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            MaestroRunner.PlayLog("PlayTest v3 démarré (autoExit=" + _autoExit + ")");
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

        static string Sample()
        {
            return "rave=" + _mood.rave.ToString("F3") + " opera.y=" + _opera.localPosition.y.ToString("F2") + " castle.y=" + _castle.localPosition.y.ToString("F2")
                + " lustre=" + _lustre.intensity.ToString("F2") + " fog=" + RenderSettings.fogDensity.ToString("F4")
                + " | dôme y=" + _dome.localPosition.y.ToString("F1") + " échelle x=" + _dome.localScale.x.ToString("F1") + ", lune échelle=" + _mood.moonRenderer.transform.localScale.x.ToString("F1") + ", ambiante=" + RenderSettings.ambientLight.ToString("F2") + ", état=" + EnvState();
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

        static void Click(string method)
        {
            Button b;
            if (!_buttons.TryGetValue(method, out b)) { Check(false, "bouton " + method + " introuvable"); return; }
            b.onClick.Invoke();
            _sb.AppendLine("--- Bouton « " + b.GetComponentInChildren<TMPro.TextMeshProUGUI>(true).text + " » (" + method + ") invoqué via onClick");
        }

        static bool Setup()
        {
            GameObject envGo = GameObject.Find("Environment");
            if (envGo == null) { _sb.AppendLine("PROBLEME: Environment introuvable"); return false; }
            _env = envGo.GetComponent<Animator>();
            _mood = envGo.GetComponent<EnvironmentMood>();
            _opera = envGo.transform.Find("OperaShell");
            _castle = envGo.transform.Find("CastleShell");
            _dome = _opera != null ? _opera.Find("Dome") : null;
            Transform lustreLight = _opera != null ? _opera.Find("Lustre/Lustre_Light") : null;
            _lustre = lustreLight != null ? lustreLight.GetComponent<Light>() : null;
            if (_env == null || _mood == null || _opera == null || _castle == null || _dome == null || _lustre == null || _mood.moonRenderer == null)
            {
                _sb.AppendLine("PROBLEME: Environment incomplet (Animator/EnvironmentMood/OperaShell/CastleShell/Dome/Lustre_Light/lune)");
                return false;
            }

            GameObject spk = GameObject.Find("Speakers");
            if (spk == null) { _sb.AppendLine("PROBLEME: Speakers introuvable"); return false; }
            _speakers = spk.GetComponent<Animator>();
            _spkFirst = spk.transform.Find("Speaker_1");
            _spkLast = spk.transform.Find("Speaker_" + spk.transform.childCount);

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
            _panel = dp != null ? dp.GetComponent<MaestroDebugPanel>() : null;
            if (_panel == null || _panel.panel == null) { _sb.AppendLine("PROBLEME: DebugPanel introuvable"); return false; }
            _buttons.Clear();
            foreach (Button b in _panel.panel.GetComponentsInChildren<Button>(true))
                if (b.onClick.GetPersistentEventCount() > 0) _buttons[b.onClick.GetPersistentMethodName(0)] = b;

            GameObject sl = GameObject.Find("StageLights");
            _spots = sl != null ? sl.GetComponentsInChildren<Light>(true) : new Light[0];
            GameObject lz = GameObject.Find("Lasers");
            _lasers = lz != null ? lz.transform : null;
            if (_spots.Length == 0 || _lasers == null) { _sb.AppendLine("PROBLEME: StageLights ou Lasers introuvable"); return false; }

            _sb.AppendLine("Cibles : " + _musicians.Count + " musiciens, " + _buttons.Count + " boutons, " + _spots.Length + " spots, " + _lasers.childCount + " lasers, caissons Hidden=" + _speakers.GetCurrentAnimatorStateInfo(0).IsName("Hidden") + " (Speaker_1.y=" + _spkFirst.localPosition.y.ToString("F1") + "), hand tracking actif=" + _panel.HandTrackingAvailable);
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
                        _sb.AppendLine("=== Transition Opera → Rave → Reset ===");
                        _sb.AppendLine("  Avant : " + Sample());
                        _env.SetTrigger("Rave");
                        NextPhase(1);
                        break;
                    case 1:
                        if (!_captured3s && t >= 3.0)
                        {
                            _captured3s = true;
                            _sb.AppendLine("  À 3 s : " + Sample());
                            MaestroScreenshot.CaptureView(_sb, new Vector3(0f, 1.6f, 0f), Quaternion.identity, 65f, Path.Combine(MaestroScreenshot.Dir, "v3-transition.png"));
                        }
                        if (t >= 7.0)
                        {
                            Expect("À 7 s", 1f, MaestroGreybox.OperaShellRaveY, 0f, 0f, 0.008f);
                            SetSpots(true);
                            MaestroScreenshot.CaptureView(_sb, new Vector3(0f, 1.6f, 0f), Quaternion.identity, 65f, Path.Combine(MaestroScreenshot.Dir, "v3-rave.png"));
                            SetSpots(false);
                            _sb.AppendLine("  Spots allumés pour la capture puis réteints (" + SpotsOn() + " allumé(s))");
                            _env.SetTrigger("Reset");
                            NextPhase(2);
                        }
                        break;
                    case 2:
                        if (t >= 7.0)
                        {
                            Expect("7 s après Reset", 0f, 0f, MaestroGreybox.CastleShellHiddenY, MaestroGreybox.LustreIntensity, 0f);
                            NextPhase(3);
                        }
                        break;
                    case 3:
                        _sb.AppendLine("=== DebugPanel : boutons via onClick ===");
                        _panel.TogglePanel();
                        Check(_panel.IsPanelVisible, "TogglePanel() : panneau visible=" + _panel.IsPanelVisible + " @ " + _panel.panel.transform.position.ToString("F2"));
                        Click("OnRave");
                        NextPhase(4);
                        break;
                    case 4:
                        if (t >= 0.3)
                        {
                            Check(EnvState() == "Rave" && _mood.rave > 0f, "Rave : état Environment=" + EnvState() + ", rave=" + _mood.rave.ToString("F3"));
                            Click("OnReset");
                            NextPhase(5);
                        }
                        break;
                    case 5:
                        if (t >= 0.3)
                        {
                            Check(EnvState() == "ToOpera" || EnvState() == "Opera", "Reset : état Environment=" + EnvState() + ", rave=" + _mood.rave.ToString("F3"));
                            Click("OnDrop");
                            NextPhase(6);
                        }
                        break;
                    case 6:
                        Track(_spkFirst.localPosition.y, _spkLast.localPosition.y);
                        if (t >= MaestroGreybox.SpeakerDropDuration + 0.8)
                        {
                            bool dropped = _speakers.GetCurrentAnimatorStateInfo(0).IsName("Dropped") && Mathf.Abs(_spkFirst.localPosition.y - MaestroGreybox.SpeakerRestY) < 0.01f && Mathf.Abs(_spkLast.localPosition.y - MaestroGreybox.SpeakerRestY) < 0.01f;
                            Check(dropped, "Drop caissons (" + _samples + " éch.) : état Dropped=" + _speakers.GetCurrentAnimatorStateInfo(0).IsName("Dropped") + " | Speaker_1.y " + Range(_minA, _maxA) + " final " + _spkFirst.localPosition.y.ToString("F2") + " | " + _spkLast.name + ".y " + Range(_minB, _maxB) + " final " + _spkLast.localPosition.y.ToString("F2") + " (attendu " + MaestroGreybox.SpeakerHiddenY + " → " + MaestroGreybox.SpeakerRestY + ")");
                            Click("OnMusiciansPlay");
                            NextPhase(7);
                        }
                        break;
                    case 7:
                        Track(_m1Body.localPosition.y, SignedX(_m1Head));
                        if (t >= 1.2)
                        {
                            int n = CountMusiciansIn("Play");
                            Check(n == _musicians.Count && _maxA > 0.62f, "Musiciens : Play : " + n + "/" + _musicians.Count + " en état Play | Strings/Musician_1 body.y " + Range(_minA, _maxA) + " (attendu 0.600 → 0.630) head.rotX " + Range(_minB, _maxB) + " (attendu -8 → +8)");
                            Click("OnMusiciansHeadbang");
                            NextPhase(8);
                        }
                        break;
                    case 8:
                        Track(_m1Body.localPosition.y, SignedX(_m1Head));
                        if (t >= 1.2)
                        {
                            int n = CountMusiciansIn("Headbang");
                            Check(n == _musicians.Count && _minB < -25f, "Musiciens : Headbang : " + n + "/" + _musicians.Count + " en état Headbang | body.y " + Range(_minA, _maxA) + " (attendu 0.600 → 0.650) head.rotX " + Range(_minB, _maxB) + " (attendu -30 → +10)");
                            Click("OnMusiciansStop");
                            NextPhase(9);
                        }
                        break;
                    case 9:
                        if (t >= 0.5)
                        {
                            int n = CountMusiciansIn("Idle");
                            Check(n == _musicians.Count, "Musiciens : Stop : " + n + "/" + _musicians.Count + " en état Idle");
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
