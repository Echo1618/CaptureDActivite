using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Maestro.EditorTools
{
    /// <summary>Vérifications du greybox : assets, instances, ancres, visibilité depuis (0, 1.6, 0).</summary>
    public static class MaestroVerify
    {
        [MenuItem("Tools/Maestro/Verify Greybox")]
        public static void VerifyFromMenu()
        {
            var sb = new StringBuilder();
            Run(sb);
            Debug.Log("[Maestro] Vérification\n" + sb);
        }

        public static void Run(StringBuilder sb)
        {
            int problems = 0;
            Scene scene = SceneManager.GetActiveScene();
            sb.AppendLine("Scène active : " + scene.path);
            if (scene.path != MaestroGreybox.ScenePath) { sb.AppendLine("PROBLEME: la scène active n'est pas Art_Sandbox"); problems++; }

            // 1. Assets
            foreach (string n in MaestroGreybox.RootPrefabs) problems += CheckPrefab(sb, MaestroGreybox.PrefabDir + "/" + n + ".prefab", n);
            foreach (string n in MaestroGreybox.SectionPrefabs) problems += CheckPrefab(sb, MaestroGreybox.InstrDir + "/" + n + ".prefab", n);
            foreach (string n in MaestroGreybox.MaterialNames)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(MaestroGreybox.MatDir + "/" + n + ".mat");
                if (m == null) { sb.AppendLine("PROBLEME: matériau manquant " + n); problems++; }
                else if (m.shader == null || m.shader.name != "Universal Render Pipeline/Lit") { sb.AppendLine("PROBLEME: shader de " + n + " = " + (m.shader != null ? m.shader.name : "null")); problems++; }
            }
            sb.AppendLine("Matériaux vérifiés : " + MaestroGreybox.MaterialNames.Length + " (URP/Lit)");
            foreach (string n in new[] { "MusicianController", "SpeakersController" })
            {
                if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(MaestroGreybox.AnimDir + "/" + n + ".controller") == null) { sb.AppendLine("PROBLEME: contrôleur manquant " + n); problems++; }
                else sb.AppendLine("Contrôleur OK : " + n);
            }
            foreach (string n in new[] { "Anim_Idle", "Anim_Play", "Anim_Headbang", "Anim_SpeakersDrop", "Anim_SpeakersHidden" })
            {
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(MaestroGreybox.AnimDir + "/" + n + ".anim");
                if (c == null) { sb.AppendLine("PROBLEME: clip manquant " + n); problems++; }
                else sb.AppendLine("Clip OK : " + n + " (" + c.length.ToString("F2") + " s, loop=" + c.isLooping + ", courbes=" + AnimationUtility.GetCurveBindings(c).Length + ")");
            }

            // 2. Instances dans la scène
            GameObject[] roots = scene.GetRootGameObjects();
            foreach (string n in MaestroGreybox.RootPrefabs) problems += CheckInstance(sb, roots, n, MaestroGreybox.PrefabDir + "/" + n + ".prefab");
            foreach (string n in MaestroGreybox.SectionPrefabs) problems += CheckInstance(sb, roots, n, MaestroGreybox.InstrDir + "/" + n + ".prefab");

            // 3. Ancres et composants attendus par le code
            problems += CheckChild(sb, roots, "Baguette", "Tip");
            problems += CheckChild(sb, roots, "SocleBaguette", "RestPoint");
            problems += CheckChild(sb, roots, "Pupitre", "PanelAnchor");
            GameObject bag = Find(roots, "Baguette");
            if (bag != null)
            {
                var cc = bag.GetComponent<CapsuleCollider>();
                var rb = bag.GetComponent<Rigidbody>();
                if (cc == null) { sb.AppendLine("PROBLEME: Baguette sans CapsuleCollider"); problems++; }
                if (rb == null || !rb.isKinematic || rb.useGravity) { sb.AppendLine("PROBLEME: Rigidbody Baguette absent ou mal réglé"); problems++; }
                if (cc != null && rb != null) sb.AppendLine("Baguette : CapsuleCollider (r=" + cc.radius + ", h=" + cc.height + ", axe " + cc.direction + ") + Rigidbody kinematic sans gravité OK");
            }
            foreach (string n in MaestroGreybox.SectionPrefabs)
            {
                GameObject s = Find(roots, n);
                if (s == null) continue;
                Animator[] anims = s.GetComponentsInChildren<Animator>(true);
                int bad = 0;
                foreach (var a in anims) if (a.runtimeAnimatorController == null || a.runtimeAnimatorController.name != "MusicianController") bad++;
                sb.AppendLine(n + " : " + anims.Length + " musicien(s), Animator MusicianController " + (bad > 0 ? "PROBLEME sur " + bad : "OK"));
                if (bad > 0) problems++;
            }
            GameObject spk = Find(roots, "Speakers");
            if (spk != null)
            {
                var a = spk.GetComponent<Animator>();
                if (a == null || a.runtimeAnimatorController == null || a.runtimeAnimatorController.name != "SpeakersController") { sb.AppendLine("PROBLEME: Animator Speakers absent ou mauvais contrôleur"); problems++; }
                else sb.AppendLine("Speakers : Animator SpeakersController OK, " + spk.transform.childCount + " caissons");
            }
            GameObject sl = Find(roots, "StageLights");
            if (sl != null)
            {
                Light[] ls = sl.GetComponentsInChildren<Light>(true);
                int on = 0;
                foreach (var l in ls) if (l.enabled) on++;
                sb.AppendLine("StageLights : " + ls.Length + " spots, " + on + " allumé(s) (attendu 0)");
                if (ls.Length != 3 || on != 0) problems++;
            }
            GameObject lz = Find(roots, "Lasers");
            if (lz != null)
            {
                int active = 0, total = 0;
                foreach (Transform c in lz.transform) { total++; if (c.gameObject.activeSelf) active++; }
                sb.AppendLine("Lasers : " + total + " faisceaux, " + active + " actif(s) (attendu 0)");
                if (total != 3 || active != 0) problems++;
            }

            // 4. Visibilité depuis (0, 1.6, 0) regard +Z
            problems += CheckVisibility(sb, roots);

            sb.AppendLine(problems == 0 ? "VERIFICATION: OK (0 problème)" : "VERIFICATION: " + problems + " problème(s)");
        }

        static GameObject Find(GameObject[] roots, string name)
        {
            foreach (var r in roots) if (r.name == name) return r;
            return null;
        }

        static int CheckPrefab(StringBuilder sb, string path, string name)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p == null) { sb.AppendLine("PROBLEME: prefab manquant " + path); return 1; }
            if (p.name != name) { sb.AppendLine("PROBLEME: nom du prefab " + path + " = " + p.name); return 1; }
            sb.AppendLine("Prefab OK : " + path);
            return 0;
        }

        static int CheckInstance(StringBuilder sb, GameObject[] roots, string name, string expectedPath)
        {
            var go = Find(roots, name);
            if (go == null) { sb.AppendLine("PROBLEME: aucune instance de " + name + " dans la scène"); return 1; }
            string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (src != expectedPath) { sb.AppendLine("PROBLEME: " + name + " n'est pas une instance de " + expectedPath + " (source : " + src + ")"); return 1; }
            sb.AppendLine("Instance OK : " + name + " @ " + go.transform.position.ToString("F2") + " rotY=" + go.transform.eulerAngles.y.ToString("F0"));
            return 0;
        }

        static int CheckChild(StringBuilder sb, GameObject[] roots, string rootName, string childName)
        {
            var go = Find(roots, rootName);
            if (go == null) return 1;
            var c = go.transform.Find(childName);
            if (c == null) { sb.AppendLine("PROBLEME: " + rootName + "/" + childName + " manquant"); return 1; }
            sb.AppendLine(rootName + "/" + childName + " OK @ " + c.position.ToString("F3") + " rot " + c.eulerAngles.ToString("F0"));
            return 0;
        }

        struct Target
        {
            public string label;
            public Transform t;
            public Transform owner;
            public string alsoIgnoreRoot;
        }

        static int CheckVisibility(StringBuilder sb, GameObject[] roots)
        {
            int problems = 0;
            Vector3 eye = new Vector3(0f, 1.6f, 0f);
            const float fovV = 65f;
            const float aspect = 16f / 9f;
            float tanV = Mathf.Tan(fovV * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * aspect;
            Physics.SyncTransforms();

            var targets = new List<Target>();
            foreach (string n in MaestroGreybox.SectionPrefabs)
            {
                var s = Find(roots, n);
                if (s == null) continue;
                foreach (Transform m in s.transform)
                {
                    var body = m.Find("Body");
                    var head = m.Find("Head");
                    if (body != null) targets.Add(new Target { label = n + "/" + m.name + "/Body", t = body, owner = m });
                    if (head != null) targets.Add(new Target { label = n + "/" + m.name + "/Head", t = head, owner = m });
                }
            }
            var pup = Find(roots, "Pupitre");
            if (pup != null) { var a = pup.transform.Find("PanelAnchor"); if (a != null) targets.Add(new Target { label = "Pupitre/PanelAnchor", t = a, owner = pup.transform, alsoIgnoreRoot = "Baguette" }); }
            var soc = Find(roots, "SocleBaguette");
            if (soc != null) { var a = soc.transform.Find("RestPoint"); if (a != null) targets.Add(new Target { label = "SocleBaguette/RestPoint", t = a, owner = soc.transform, alsoIgnoreRoot = "Baguette" }); }
            var bag = Find(roots, "Baguette");
            if (bag != null) { var a = bag.transform.Find("Tip"); if (a != null) targets.Add(new Target { label = "Baguette/Tip", t = a, owner = bag.transform, alsoIgnoreRoot = "SocleBaguette" }); }

            sb.AppendLine("Visibilité depuis (0, 1.6, 0) regard +Z, FOV vertical " + fovV + "°, 16:9 :");
            foreach (var tg in targets)
            {
                Vector3 p = tg.t.position;
                Vector3 d = p - eye;
                bool inFrustum = d.z > 0.01f && Mathf.Abs(d.x / d.z) <= tanH && Mathf.Abs(d.y / d.z) <= tanV;
                string blocker = null;
                RaycastHit[] hits = Physics.RaycastAll(eye, d.normalized, d.magnitude);
                Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
                foreach (var h in hits)
                {
                    if (h.transform.IsChildOf(tg.owner)) continue;
                    if (tg.alsoIgnoreRoot != null && h.transform.root.name == tg.alsoIgnoreRoot) continue;
                    blocker = h.transform.root.name + "/" + h.transform.name;
                    break;
                }
                sb.AppendLine("  " + tg.label + " @ " + p.ToString("F2") + " : " + (inFrustum ? "dans le champ" : "HORS CHAMP") + (blocker == null ? ", ligne de vue libre" : ", MASQUÉ par " + blocker));
                if (!inFrustum || blocker != null) problems++;
            }
            return problems;
        }
    }

    /// <summary>Capture de la vue depuis (0, 1.6, 0) regard +Z vers Docs/screenshots/greybox.png.</summary>
    public static class MaestroScreenshot
    {
        [MenuItem("Tools/Maestro/Capture Greybox Screenshot")]
        public static void CaptureFromMenu()
        {
            var sb = new StringBuilder();
            Capture(sb);
            Debug.Log("[Maestro] " + sb);
        }

        public static string Capture(StringBuilder sb)
        {
            string outPath = Path.Combine(MaestroRunner.ProjectRoot, "Docs", "screenshots", "greybox.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            var go = new GameObject("Maestro_TempCamera");
            go.hideFlags = HideFlags.HideAndDontSave;
            Camera cam = go.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 1.6f, 0f);
            cam.transform.rotation = Quaternion.identity;
            cam.fieldOfView = 65f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.Skybox;
            const int w = 1600, h = 900;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            Texture2D tex = null;
            try
            {
                bool rendered = false;
                try
                {
                    var req = new UniversalRenderPipeline.SingleCameraRequest();
                    req.destination = rt;
                    if (RenderPipeline.SupportsRenderRequest(cam, req))
                    {
                        RenderPipeline.SubmitRenderRequest(cam, req);
                        rendered = true;
                        sb.AppendLine("Rendu via UniversalRenderPipeline.SingleCameraRequest");
                    }
                }
                catch (Exception e)
                {
                    sb.AppendLine("SingleCameraRequest indisponible (" + e.Message + "), repli sur Camera.Render");
                }
                if (!rendered)
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                    sb.AppendLine("Rendu via Camera.Render");
                }
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                Color32[] px = tex.GetPixels32();
                long sum = 0;
                foreach (var p in px) sum += p.r + p.g + p.b;
                float avg = sum / (3f * px.Length);
                File.WriteAllBytes(outPath, tex.EncodeToPNG());
                sb.AppendLine("Capture écrite : " + outPath + " (" + w + "x" + h + ", luminosité moyenne " + avg.ToString("F1") + "/255)");
            }
            finally
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(go);
            }
            return outPath;
        }
    }
}
