using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Maestro.EditorTools
{
    /// <summary>
    /// Pilotage de l'éditeur sans MCP : lit une commande dans Temp/maestro_request.txt,
    /// l'exécute et écrit le résultat (plus l'état de la Console) dans Temp/maestro_result.txt.
    /// Les mêmes commandes sont disponibles dans le menu Tools/Maestro/.
    /// </summary>
    [InitializeOnLoad]
    public static class MaestroRunner
    {
        public const string PlayTestPendingKey = "Maestro.PlayTestPending";
        public static readonly string ProjectRoot = Directory.GetParent(Application.dataPath).FullName;
        public static string RequestPath { get { return Path.Combine(ProjectRoot, "Temp", "maestro_request.txt"); } }
        public static string ResultPath { get { return Path.Combine(ProjectRoot, "Temp", "maestro_result.txt"); } }

        static double _nextPoll;

        static MaestroRunner()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            // Journal horodaté des transitions Play Mode et des messages XR (diagnostic des gels à la sortie).
            PlayLog("domaine chargé | isPlaying=" + EditorApplication.isPlaying + " willChange=" + EditorApplication.isPlayingOrWillChangePlaymode);
            EditorApplication.playModeStateChanged += s => PlayLog("playModeStateChanged " + s);
            Application.logMessageReceived += (condition, stackTrace, type) =>
            {
                string first = condition.Split('\n')[0];
                if (type != LogType.Log || first.Contains("XR") || first.Contains("OpenXR") || first.Contains("Hand"))
                    PlayLog("[" + type + "] " + first);
            };
            try
            {
                UnityEngine.XR.OpenXR.OpenXRRuntime.wantsToQuit += () => { PlayLog("OpenXRRuntime.wantsToQuit (le runtime OpenXR demande l'arrêt)"); return true; };
                UnityEngine.XR.OpenXR.OpenXRRuntime.wantsToRestart += () => { PlayLog("OpenXRRuntime.wantsToRestart (perte de session OpenXR)"); return true; };
            }
            catch (Exception e) { PlayLog("hooks OpenXR indisponibles : " + e.Message); }
        }

        public static string PlayLogPath { get { return Path.Combine(ProjectRoot, "Temp", "maestro_playlog.txt"); } }

        public static void PlayLog(string msg)
        {
            try { File.AppendAllText(PlayLogPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\n"); }
            catch (Exception) { }
        }

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + 0.5;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            if (EditorApplication.isPlaying && SessionState.GetBool(PlayTestPendingKey, false))
            {
                SessionState.SetBool(PlayTestPendingKey, false);
                MaestroPlayTest.Begin(ResultPath);
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) return;
            if (!File.Exists(RequestPath)) return;

            string cmd;
            try
            {
                cmd = File.ReadAllText(RequestPath).Trim();
                File.Delete(RequestPath);
            }
            catch (Exception) { return; }
            Execute(cmd);
        }

        public static void Execute(string cmd)
        {
            var sb = new StringBuilder();
            var logs = new List<string>();
            Application.LogCallback cb = (condition, stackTrace, type) =>
            {
                if (type == LogType.Log && !condition.StartsWith("[Maestro]")) return;
                string extra = (type == LogType.Exception || type == LogType.Error) ? "\n" + stackTrace : "";
                logs.Add("[" + type + "] " + condition + extra);
            };
            Application.logMessageReceived += cb;
            string status = "OK";
            try
            {
                if (EditorApplication.isPlaying && cmd != "console" && cmd != "stopplay")
                    throw new Exception("Editeur en Play Mode : commande '" + cmd + "' refusée.");
                switch (cmd)
                {
                    case "build": MaestroGreybox.Build(sb); break;
                    case "verify": MaestroVerify.Run(sb); break;
                    case "screenshot": MaestroScreenshot.Capture(sb); break;
                    case "save": MaestroGreybox.SaveAll(sb); break;
                    case "console": break;
                    case "stopplay": EditorApplication.ExitPlaymode(); break;
                    case "playtest":
                        MaestroPlayTest.Request(sb);
                        status = "PENDING";
                        break;
                    default:
                        status = "FAIL";
                        sb.AppendLine("Commande inconnue : " + cmd);
                        break;
                }
            }
            catch (Exception e)
            {
                status = "FAIL";
                sb.AppendLine("EXCEPTION: " + e);
            }
            finally
            {
                Application.logMessageReceived -= cb;
            }

            sb.AppendLine("--- logs captés pendant la commande ---");
            foreach (var l in logs) sb.AppendLine(l);
            sb.AppendLine(MaestroConsole.Summary());
            WriteResult(status, cmd, sb.ToString());
        }

        public static void WriteResult(string status, string cmd, string body)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
                File.WriteAllText(ResultPath, "STATUS: " + status + "\nCOMMAND: " + cmd + "\n" + body);
            }
            catch (Exception e)
            {
                Debug.LogError("[Maestro] Écriture du résultat impossible : " + e.Message);
            }
        }

        [MenuItem("Tools/Maestro/Read Console Summary")]
        public static void ReadConsoleMenu()
        {
            Debug.Log("[Maestro] " + MaestroConsole.Summary());
        }
    }

    /// <summary>Lecture de la Console Unity par réflexion (compteurs + premières erreurs).</summary>
    public static class MaestroConsole
    {
        const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static string Summary()
        {
            try
            {
                Type logEntries = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries");
                MethodInfo counts = logEntries.GetMethod("GetCountsByType", AnyStatic);
                object[] args = { 0, 0, 0 };
                counts.Invoke(null, args);
                int errors = (int)args[0];
                var sb = new StringBuilder();
                sb.AppendLine("CONSOLE: errors=" + errors + " warnings=" + args[1] + " logs=" + args[2]);
                if (errors > 0)
                {
                    try
                    {
                        int count = (int)logEntries.GetMethod("GetCount", AnyStatic).Invoke(null, null);
                        Type entryType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntry");
                        MethodInfo start = logEntries.GetMethod("StartGettingEntries", AnyStatic);
                        MethodInfo end = logEntries.GetMethod("EndGettingEntries", AnyStatic);
                        MethodInfo get = logEntries.GetMethod("GetEntryInternal", AnyStatic);
                        FieldInfo msgField = entryType.GetField("message", AnyInstance);
                        FieldInfo modeField = entryType.GetField("mode", AnyInstance);
                        const int errorMask = 1 | 2 | 16 | 64 | 256 | 2048 | (1 << 17) | (1 << 20) | (1 << 21);
                        start.Invoke(null, null);
                        try
                        {
                            int shown = 0;
                            for (int i = 0; i < count && shown < 30; i++)
                            {
                                object entry = Activator.CreateInstance(entryType);
                                get.Invoke(null, new object[] { i, entry });
                                int mode = (int)modeField.GetValue(entry);
                                if ((mode & errorMask) == 0) continue;
                                shown++;
                                string msg = (string)msgField.GetValue(entry) ?? "";
                                sb.AppendLine("  ERR[" + mode + "] " + msg.Split('\n')[0]);
                            }
                        }
                        finally { end.Invoke(null, null); }
                    }
                    catch (Exception inner)
                    {
                        sb.AppendLine("  (détail des erreurs illisible : " + inner.Message + ")");
                    }
                }
                return sb.ToString().TrimEnd();
            }
            catch (Exception e)
            {
                return "CONSOLE: lecture impossible (" + e.Message + ")";
            }
        }
    }
}
