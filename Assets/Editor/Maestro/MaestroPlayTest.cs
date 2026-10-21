using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maestro.EditorTools
{
    /// <summary>
    /// Test en Play Mode : déclenche Play puis Headbang sur Strings/Musician_1 et Drop sur Speakers,
    /// mesure les transforms animés, écrit le résultat puis quitte le Play Mode.
    /// </summary>
    public static class MaestroPlayTest
    {
        static string _resultPath;
        static StringBuilder _sb;
        static int _step;
        static double _stepStart;
        static Animator _musician;
        static Transform _body, _head;
        static Animator _speakers;
        static Transform _spk1, _spk3;
        static float _minA, _maxA, _minB, _maxB;
        static int _samples;

        [MenuItem("Tools/Maestro/Play Test Animations")]
        public static void PlayTestFromMenu()
        {
            var sb = new StringBuilder();
            Request(sb);
        }

        public static void Request(StringBuilder sb)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != MaestroGreybox.ScenePath) throw new Exception("Scène active ≠ Art_Sandbox, test annulé.");
            if (scene.isDirty) EditorSceneManager.SaveScene(scene);
            SessionState.SetBool(MaestroRunner.PlayTestPendingKey, true);
            sb.AppendLine("Entrée en Play Mode, résultat écrit à la fin du test.");
            EditorApplication.EnterPlaymode();
        }

        public static void Begin(string resultPath)
        {
            _resultPath = resultPath;
            _sb = new StringBuilder();
            _step = 0;
            _stepStart = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        static void ResetRange()
        {
            _minA = float.MaxValue; _maxA = float.MinValue;
            _minB = float.MaxValue; _maxB = float.MinValue;
            _samples = 0;
        }

        static float SignedX(Transform t)
        {
            float x = t.localEulerAngles.x;
            if (x > 180f) x -= 360f;
            return x;
        }

        static void Track(float a, float b)
        {
            _minA = Mathf.Min(_minA, a); _maxA = Mathf.Max(_maxA, a);
            _minB = Mathf.Min(_minB, b); _maxB = Mathf.Max(_maxB, b);
            _samples++;
        }

        static string Range(float min, float max) { return min.ToString("F3") + " → " + max.ToString("F3"); }

        static void Update()
        {
            try
            {
                if (!EditorApplication.isPlaying) { Finish("FAIL", "Play Mode quitté avant la fin du test"); return; }
                double t = EditorApplication.timeSinceStartup - _stepStart;
                switch (_step)
                {
                    case 0:
                        if (t < 0.5) return;
                        {
                            GameObject strings = GameObject.Find("Strings");
                            GameObject spk = GameObject.Find("Speakers");
                            if (strings == null || spk == null) { Finish("FAIL", "Strings ou Speakers introuvable en Play Mode"); return; }
                            Transform m1 = strings.transform.Find("Musician_1");
                            _musician = m1.GetComponent<Animator>();
                            _body = m1.Find("Body");
                            _head = m1.Find("Head");
                            _speakers = spk.GetComponent<Animator>();
                            _spk1 = spk.transform.Find("Speaker_1");
                            _spk3 = spk.transform.Find("Speaker_3");
                            bool idle = _musician.GetCurrentAnimatorStateInfo(0).IsName("Idle");
                            bool hidden = _speakers.GetCurrentAnimatorStateInfo(0).IsName("Hidden");
                            _sb.AppendLine("État initial musicien : Idle=" + idle + " | body.y=" + _body.localPosition.y.ToString("F3") + " head.rotX=" + SignedX(_head).ToString("F1") + " head.rotZ=" + _head.localEulerAngles.z.ToString("F1"));
                            _sb.AppendLine("État initial Speakers : Hidden=" + hidden + " | Speaker_1.y=" + _spk1.localPosition.y.ToString("F2") + " (attendu 10)");
                            _musician.SetTrigger("Play");
                            ResetRange();
                            _step = 1;
                            _stepStart = EditorApplication.timeSinceStartup;
                        }
                        break;
                    case 1:
                        Track(_body.localPosition.y, SignedX(_head));
                        if (t > 1.5)
                        {
                            bool inPlay = _musician.GetCurrentAnimatorStateInfo(0).IsName("Play");
                            _sb.AppendLine("Après trigger Play (" + _samples + " échantillons) : état Play=" + inPlay + " | body.y " + Range(_minA, _maxA) + " (attendu 0.600 → 0.630) | head.rotX " + Range(_minB, _maxB) + " (attendu -8 → +8)");
                            _musician.SetTrigger("Headbang");
                            ResetRange();
                            _step = 2;
                            _stepStart = EditorApplication.timeSinceStartup;
                        }
                        break;
                    case 2:
                        Track(_body.localPosition.y, SignedX(_head));
                        if (t > 1.5)
                        {
                            bool inHb = _musician.GetCurrentAnimatorStateInfo(0).IsName("Headbang");
                            _sb.AppendLine("Après trigger Headbang (" + _samples + " échantillons) : état Headbang=" + inHb + " | body.y " + Range(_minA, _maxA) + " (attendu 0.600 → 0.650) | head.rotX " + Range(_minB, _maxB) + " (attendu -30 → +10)");
                            _speakers.SetTrigger("Drop");
                            ResetRange();
                            _step = 3;
                            _stepStart = EditorApplication.timeSinceStartup;
                        }
                        break;
                    case 3:
                        Track(_spk1.localPosition.y, _spk3.localPosition.y);
                        if (t > 1.5)
                        {
                            bool dropped = _speakers.GetCurrentAnimatorStateInfo(0).IsName("Dropped");
                            _sb.AppendLine("Après trigger Drop (" + _samples + " échantillons) : état Dropped=" + dropped + " | Speaker_1.y " + Range(_minA, _maxA) + ", final " + _spk1.localPosition.y.ToString("F2") + " | Speaker_3.y " + Range(_minB, _maxB) + ", final " + _spk3.localPosition.y.ToString("F2") + " (attendu 10 → 0.40)");
                            _musician.SetTrigger("Stop");
                            _step = 4;
                            _stepStart = EditorApplication.timeSinceStartup;
                        }
                        break;
                    case 4:
                        if (t > 0.6)
                        {
                            bool idle = _musician.GetCurrentAnimatorStateInfo(0).IsName("Idle");
                            _sb.AppendLine("Après trigger Stop : état Idle=" + idle);
                            Finish("OK", null);
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
            try { File.WriteAllText(_resultPath, "STATUS: " + status + "\nCOMMAND: playtest\n" + _sb); }
            catch (Exception e) { Debug.LogError("[Maestro] " + e.Message); }
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }
    }
}
