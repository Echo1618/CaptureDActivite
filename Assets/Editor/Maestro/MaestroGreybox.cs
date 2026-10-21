using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maestro.EditorTools
{
    /// <summary>
    /// Construit le greybox complet (matériaux, clips, contrôleurs, prefabs, instances)
    /// dans Art_Sandbox.unity. Tout en primitives Unity et URP/Lit, zéro asset importé.
    /// Idempotent : les instances et assets déjà produits par ce script sont recréés.
    /// </summary>
    public static class MaestroGreybox
    {
        public const string ScenePath = "Assets/_OrchestraRave/Scenes/Tests/Art_Sandbox.unity";
        public const string MatDir = "Assets/_OrchestraRave/Art/Materials";
        public const string AnimDir = "Assets/_OrchestraRave/Art/Animations";
        public const string PrefabDir = "Assets/_OrchestraRave/Prefabs";
        public const string InstrDir = "Assets/_OrchestraRave/Prefabs/Instruments";

        public static readonly string[] RootPrefabs = { "Environment", "Pupitre", "Baguette", "SocleBaguette", "Speakers", "StageLights", "Lasers", "VFX" };
        public static readonly string[] SectionPrefabs = { "Strings", "Vent", "Brass", "Percution" };
        public static readonly string[] MaterialNames = { "M_Stone", "M_Wood", "M_MetalDark", "M_Chrome", "M_Torch", "M_EmissiveCyan", "M_EmissiveMagenta", "M_Strings", "M_Vent", "M_Brass", "M_Percution", "M_Skin" };

        static StringBuilder _sb;
        static void Log(string msg)
        {
            if (_sb != null) _sb.AppendLine(msg);
            Debug.Log("[Maestro] " + msg);
        }

        [MenuItem("Tools/Maestro/Build Greybox")]
        public static void BuildFromMenu()
        {
            var sb = new StringBuilder();
            Build(sb);
        }

        public static void Build(StringBuilder sb)
        {
            _sb = sb;
            try
            {
                Scene scene = EnsureScene();
                RemoveOwnInstances(scene);
                EnsureFolder(MatDir);
                EnsureFolder(AnimDir);
                EnsureFolder(PrefabDir);
                EnsureFolder(InstrDir);

                // ----- Matériaux (URP/Lit, couleurs unies) -----
                Material stone   = Mat("M_Stone",           new Color(0.45f, 0.44f, 0.42f), 0f, 0.25f);
                Material wood    = Mat("M_Wood",            new Color(0.40f, 0.26f, 0.13f), 0f, 0.35f);
                Material metal   = Mat("M_MetalDark",       new Color(0.08f, 0.08f, 0.09f), 0.3f, 0.2f);
                Material chrome  = Mat("M_Chrome",          new Color(0.85f, 0.85f, 0.88f), 1f, 0.9f);
                Material torch   = Mat("M_Torch",           new Color(1.00f, 0.55f, 0.15f), 0f, 0.5f, new Color(1.00f, 0.55f, 0.15f), 3f);
                Material cyan    = Mat("M_EmissiveCyan",    new Color(0.00f, 0.90f, 1.00f), 0f, 0.5f, new Color(0.00f, 0.90f, 1.00f), 4f);
                Material magenta = Mat("M_EmissiveMagenta", new Color(1.00f, 0.00f, 0.85f), 0f, 0.5f, new Color(1.00f, 0.00f, 0.85f), 4f);
                Material strings = Mat("M_Strings",         new Color(0.00f, 0.50f, 0.50f), 0f, 0.4f);
                Material vent    = Mat("M_Vent",            new Color(0.50f, 0.20f, 0.80f), 0f, 0.4f);
                Material brass   = Mat("M_Brass",           new Color(1.00f, 0.45f, 0.35f), 0f, 0.4f);
                Material percu   = Mat("M_Percution",       new Color(1.00f, 0.70f, 0.20f), 0f, 0.4f);
                Material skin    = Mat("M_Skin",            new Color(0.90f, 0.78f, 0.62f), 0f, 0.4f);
                Log("12 matériaux URP/Lit créés ou mis à jour dans " + MatDir);

                // ----- Animations -----
                AnimatorController musicianCtrl = BuildMusicianController();
                AnimatorController speakersCtrl = BuildSpeakersController();
                AssetDatabase.SaveAssets();

                // ----- Prefabs + instances -----
                BuildEnvironment(stone, wood, torch);
                BuildPupitre(wood);
                BuildBaguette(chrome, metal, cyan);
                BuildSocleBaguette(metal, cyan);
                BuildSection("Strings",   new[] { -2.5f, 0f, 2.5f }, strings, skin, wood, metal, musicianCtrl, new Vector3(0f, 0.2f, 5f));
                BuildSection("Vent",      new[] { -0.75f, 0.75f },   vent,    skin, wood, metal, musicianCtrl, new Vector3(-2.25f, 1f, 6.5f));
                BuildSection("Brass",     new[] { -0.75f, 0.75f },   brass,   skin, wood, metal, musicianCtrl, new Vector3(2.25f, 1f, 6.5f));
                BuildSection("Percution", new[] { 0f },              percu,   skin, wood, metal, musicianCtrl, new Vector3(0f, 2f, 8f));
                BuildSpeakers(metal, magenta, speakersCtrl);
                BuildStageLights(metal);
                BuildLasers(cyan);
                BuildVfx();

                SetupSceneLightAndCamera(scene);
                SaveAll(sb);
                Log("Greybox terminé.");
            }
            finally
            {
                _sb = null;
            }
        }

        // ------------------------------------------------------------------ scène

        static Scene EnsureScene()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.path == ScenePath) return active;
            if (active.isDirty)
                throw new Exception("La scène active (" + active.path + ") a des modifications non sauvegardées : ouvre Art_Sandbox manuellement, rien n'a été touché.");
            Log("Ouverture de " + ScenePath + " (scène active précédente : " + active.path + ")");
            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        static void RemoveOwnInstances(Scene scene)
        {
            var names = new HashSet<string>(RootPrefabs);
            names.UnionWith(SectionPrefabs);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (!names.Contains(root.name)) continue;
                string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (!string.IsNullOrEmpty(src) && src.StartsWith(PrefabDir))
                {
                    Log("Ancienne instance produite par ce script supprimée pour reconstruction : " + root.name);
                    UnityEngine.Object.DestroyImmediate(root);
                }
                else
                {
                    throw new Exception("Objet préexistant '" + root.name + "' dans la scène (pas une instance de nos prefabs) : arrêt pour ne rien supprimer.");
                }
            }
        }

        static void SetupSceneLightAndCamera(Scene scene)
        {
            Light dir = null;
            foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional && l.gameObject.scene == scene) { dir = l; break; }
            }
            if (dir == null)
            {
                var go = new GameObject("Directional Light");
                go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                dir = go.AddComponent<Light>();
                dir.type = LightType.Directional;
                Log("Aucune Directional Light dans la scène : créée.");
            }
            dir.intensity = 0.4f;
            dir.color = new Color(1.00f, 0.78f, 0.50f);
            dir.shadows = LightShadows.Soft;
            EditorUtility.SetDirty(dir);
            Log("Directional Light : intensité 0.4, couleur ambre, ombres douces.");

            Camera cam = null;
            foreach (Camera c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c.gameObject.scene == scene) { cam = c; break; }
            }
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 1.6f, 0f);
                cam.transform.rotation = Quaternion.identity;
                cam.nearClipPlane = 0.05f;
                EditorUtility.SetDirty(cam);
                Log("Caméra '" + cam.name + "' placée à (0, 1.6, 0) regard +Z.");
            }
        }

        [MenuItem("Tools/Maestro/Save Scene And Assets")]
        public static void SaveFromMenu()
        {
            SaveAll(new StringBuilder());
        }

        public static void SaveAll(StringBuilder sb)
        {
            AssetDatabase.SaveAssets();
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path == ScenePath)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                bool ok = EditorSceneManager.SaveScene(scene);
                if (sb != null) sb.AppendLine("Scène sauvegardée (" + ok + ") : " + scene.path);
            }
            else if (sb != null)
            {
                sb.AppendLine("Scène active ≠ Art_Sandbox, scène non sauvegardée : " + scene.path);
            }
            AssetDatabase.Refresh();
        }

        // ------------------------------------------------------------------ helpers assets

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Material Mat(string name, Color baseColor, float metallic, float smoothness, Color? emission = null, float emissionIntensity = 0f)
        {
            string path = MatDir + "/" + name + ".mat";
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new Exception("Shader 'Universal Render Pipeline/Lit' introuvable.");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(shader);
            else if (mat.shader != shader) mat.shader = shader;
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                // Convention du color picker HDR d'Unity : intensité en EV (facteur 2^n).
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", emission.Value * Mathf.Pow(2f, emissionIntensity));
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                mat.SetColor("_EmissionColor", Color.black);
            }
            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Crée le clip, ou le vide et le réutilise en place s'il existe déjà (GUID stable, et surtout
        /// pas de DeleteAsset + CreateAsset dans la même frame : cela faisait perdre courbes et bouclage).
        /// </summary>
        static AnimationClip NewClip(string name, bool loop)
        {
            string path = AnimDir + "/" + name + ".anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip();
                clip.name = name;
                AssetDatabase.CreateAsset(clip, path);
            }
            else
            {
                foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip)) AnimationUtility.SetEditorCurve(clip, b, null);
                foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip)) AnimationUtility.SetObjectReferenceCurve(clip, b, null);
                clip.ClearCurves();
            }
            clip.frameRate = 60f;
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        static void Curve(AnimationClip clip, string path, string property, AnimationCurve curve)
        {
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
        }

        static AnimationCurve Sampled(Func<float, float> f, float duration, int fps, bool linear)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(duration * fps));
            var keys = new Keyframe[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = duration * i / n;
                keys[i] = new Keyframe(t, f(t));
            }
            var curve = new AnimationCurve(keys);
            for (int i = 0; i <= n; i++)
            {
                if (linear)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                }
                else
                {
                    curve.SmoothTangents(i, 0f);
                }
            }
            return curve;
        }

        /// <summary>Position locale (x,y,z) et rotation locale Euler (x,y,z) d'un enfant, échantillonnées.</summary>
        static void TransformCurves(AnimationClip clip, string path, float duration,
            Func<float, float> px, Func<float, float> py, Func<float, float> pz,
            Func<float, float> rx, Func<float, float> ry, Func<float, float> rz)
        {
            const int fps = 30;
            Curve(clip, path, "localPosition.x", Sampled(px, duration, fps, false));
            Curve(clip, path, "localPosition.y", Sampled(py, duration, fps, false));
            Curve(clip, path, "localPosition.z", Sampled(pz, duration, fps, false));
            Curve(clip, path, "localEulerAnglesRaw.x", Sampled(rx, duration, fps, false));
            Curve(clip, path, "localEulerAnglesRaw.y", Sampled(ry, duration, fps, false));
            Curve(clip, path, "localEulerAnglesRaw.z", Sampled(rz, duration, fps, false));
        }

        /// <summary>Crée le contrôleur, ou le vide (paramètres, états, transitions) et le réutilise en place.</summary>
        static AnimatorController NewController(string name)
        {
            string path = AnimDir + "/" + name + ".controller";
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing == null) return AnimatorController.CreateAnimatorControllerAtPath(path);
            foreach (AnimatorControllerParameter p in existing.parameters) existing.RemoveParameter(p);
            AnimatorStateMachine sm = existing.layers[0].stateMachine;
            foreach (AnimatorStateTransition t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
            foreach (ChildAnimatorState s in sm.states) sm.RemoveState(s.state);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static void Transition(AnimatorState from, AnimatorState to, string trigger, float duration = 0.2f)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = false;
            t.exitTime = 0f;
            t.hasFixedDuration = true;
            t.duration = duration;
            t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }

        // ------------------------------------------------------------------ helpers scène

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localEuler, Vector3 localScale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static GameObject Empty(string name, Transform parent, Vector3 localPos, Vector3 localEuler)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            return go;
        }

        static GameObject SavePrefab(GameObject go, string path)
        {
            bool ok;
            GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.AutomatedAction, out ok);
            if (!ok || prefab == null) throw new Exception("Échec de sauvegarde du prefab " + path);
            Log("Prefab : " + path);
            return prefab;
        }

        // ------------------------------------------------------------------ animations

        static AnimatorController BuildMusicianController()
        {
            AnimationClip idle = NewClip("Anim_Idle", true);
            AnimationClip play = NewClip("Anim_Play", true);
            AnimationClip headbang = NewClip("Anim_Headbang", true);

            // Repos : Body centré à y=0.6 (capsule 1.2 m), Head à y=1.35 (cou à 1.2, rayon 0.15).
            // Idle : balancement du corps ±2° en Z sur 3 s ; la tête suit le haut du corps.
            const float TIdle = 3f;
            Func<float, float> sway = t => 2f * Mathf.Sin(2f * Mathf.PI * t / TIdle);
            TransformCurves(idle, "Body", TIdle,
                t => 0f, t => 0.6f, t => 0f,
                t => 0f, t => 0f, sway);
            TransformCurves(idle, "Head", TIdle,
                t => -0.75f * Mathf.Sin(sway(t) * Mathf.Deg2Rad), t => 0.6f + 0.75f * Mathf.Cos(sway(t) * Mathf.Deg2Rad), t => 0f,
                t => 0f, t => 0f, sway);

            // Play : rebond du corps de 3 cm à 1 Hz, tête qui hoche ±8° en X (pivot au cou).
            const float TPlay = 1f;
            Func<float, float> bounce3 = t => 0.03f * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * t));
            Func<float, float> nod = t => 8f * Mathf.Sin(2f * Mathf.PI * t);
            TransformCurves(play, "Body", TPlay,
                t => 0f, t => 0.6f + bounce3(t), t => 0f,
                t => 0f, t => 0f, t => 0f);
            TransformCurves(play, "Head", TPlay,
                t => 0f, t => 1.2f + bounce3(t) + 0.15f * Mathf.Cos(nod(t) * Mathf.Deg2Rad), t => 0.15f * Mathf.Sin(nod(t) * Mathf.Deg2Rad),
                nod, t => 0f, t => 0f);

            // Headbang : tête de -30° à +10° en X à 2 Hz, corps qui rebondit de 5 cm.
            const float THb = 1f;
            Func<float, float> bounce5 = t => 0.05f * (0.5f - 0.5f * Mathf.Cos(4f * Mathf.PI * t));
            Func<float, float> bang = t => -10f - 20f * Mathf.Cos(4f * Mathf.PI * t);
            TransformCurves(headbang, "Body", THb,
                t => 0f, t => 0.6f + bounce5(t), t => 0f,
                t => 0f, t => 0f, t => 0f);
            TransformCurves(headbang, "Head", THb,
                t => 0f, t => 1.2f + bounce5(t) + 0.15f * Mathf.Cos(bang(t) * Mathf.Deg2Rad), t => 0.15f * Mathf.Sin(bang(t) * Mathf.Deg2Rad),
                bang, t => 0f, t => 0f);

            AnimatorController c = NewController("MusicianController");
            c.AddParameter("Play", AnimatorControllerParameterType.Trigger);
            c.AddParameter("Stop", AnimatorControllerParameterType.Trigger);
            c.AddParameter("Headbang", AnimatorControllerParameterType.Trigger);
            AnimatorStateMachine sm = c.layers[0].stateMachine;
            AnimatorState sIdle = sm.AddState("Idle", new Vector3(0f, 0f, 0f));
            AnimatorState sPlay = sm.AddState("Play", new Vector3(300f, -80f, 0f));
            AnimatorState sHb = sm.AddState("Headbang", new Vector3(300f, 80f, 0f));
            sIdle.motion = idle;
            sPlay.motion = play;
            sHb.motion = headbang;
            sm.defaultState = sIdle;
            Transition(sIdle, sPlay, "Play");
            Transition(sIdle, sHb, "Headbang");
            Transition(sPlay, sIdle, "Stop");
            Transition(sPlay, sHb, "Headbang");
            Transition(sHb, sIdle, "Stop");
            Transition(sHb, sPlay, "Play");
            EditorUtility.SetDirty(c);
            Log("MusicianController : Idle / Play / Headbang, triggers Play / Stop / Headbang, transitions 0.2 s sans exit time.");
            return c;
        }

        static float DropY(float t)
        {
            const float top = 10f, rest = 0.4f, tFall = 0.45f, tB1 = 0.22f, tB2 = 0.12f, h1 = 0.45f, h2 = 0.12f;
            if (t <= 0f) return top;
            if (t < tFall) { float u = t / tFall; return top - (top - rest) * u * u; }
            t -= tFall;
            if (t < tB1) { float u = t / tB1; return rest + h1 * 4f * u * (1f - u); }
            t -= tB1;
            if (t < tB2) { float u = t / tB2; return rest + h2 * 4f * u * (1f - u); }
            return rest;
        }

        static AnimatorController BuildSpeakersController()
        {
            float[] xs = { -2f, 0f, 2f };
            float[] delays = { 0f, 0.08f, 0.16f };
            AnimationClip hidden = NewClip("Anim_SpeakersHidden", false);
            AnimationClip drop = NewClip("Anim_SpeakersDrop", false);
            for (int i = 0; i < 3; i++)
            {
                string path = "Speaker_" + (i + 1);
                float delay = delays[i];
                Curve(hidden, path, "localPosition.x", AnimationCurve.Constant(0f, 1f / 60f, xs[i]));
                Curve(hidden, path, "localPosition.y", AnimationCurve.Constant(0f, 1f / 60f, 10f));
                Curve(hidden, path, "localPosition.z", AnimationCurve.Constant(0f, 1f / 60f, 0f));
                Curve(drop, path, "localPosition.x", AnimationCurve.Constant(0f, 1f, xs[i]));
                Curve(drop, path, "localPosition.y", Sampled(t => DropY(t - delay), 1f, 60, true));
                Curve(drop, path, "localPosition.z", AnimationCurve.Constant(0f, 1f, 0f));
            }

            AnimatorController c = NewController("SpeakersController");
            c.AddParameter("Drop", AnimatorControllerParameterType.Trigger);
            AnimatorStateMachine sm = c.layers[0].stateMachine;
            AnimatorState sHidden = sm.AddState("Hidden", new Vector3(0f, 0f, 0f));
            AnimatorState sDropped = sm.AddState("Dropped", new Vector3(300f, 0f, 0f));
            sHidden.motion = hidden;
            sDropped.motion = drop;
            sm.defaultState = sHidden;
            Transition(sHidden, sDropped, "Drop", 0f);
            EditorUtility.SetDirty(c);
            Log("SpeakersController : Hidden (défaut) → Dropped sur trigger Drop, clip de chute 1 s avec deux rebonds.");
            return c;
        }

        // ------------------------------------------------------------------ prefabs

        static void BuildEnvironment(Material stone, Material wood, Material torch)
        {
            var env = new GameObject("Environment");
            Transform t = env.transform;
            Prim(PrimitiveType.Plane, "Sol", t, new Vector3(0f, 0f, 5f), Vector3.zero, new Vector3(2f, 1f, 2f), stone);
            Prim(PrimitiveType.Cylinder, "Podium", t, new Vector3(0f, 0.05f, 0f), Vector3.zero, new Vector3(1.2f, 0.05f, 1.2f), wood);
            Prim(PrimitiveType.Cube, "Plateforme_1", t, new Vector3(0f, 0.1f, 5f), Vector3.zero, new Vector3(9f, 0.2f, 2f), stone);
            Prim(PrimitiveType.Cube, "Plateforme_2", t, new Vector3(0f, 0.5f, 6.5f), Vector3.zero, new Vector3(9f, 1f, 2f), stone);
            Prim(PrimitiveType.Cube, "Plateforme_3", t, new Vector3(0f, 1f, 8f), Vector3.zero, new Vector3(4f, 2f, 2f), stone);
            Prim(PrimitiveType.Cube, "Mur_Fond", t, new Vector3(0f, 3f, 11f), Vector3.zero, new Vector3(20f, 6f, 0.3f), stone);

            Vector3[] torchPos = { new Vector3(-4f, 2.5f, 2f), new Vector3(4f, 2.5f, 2f), new Vector3(-4f, 2.5f, 7f), new Vector3(4f, 2.5f, 7f) };
            for (int i = 0; i < torchPos.Length; i++)
            {
                GameObject g = Empty("Torch_" + (i + 1), t, torchPos[i], Vector3.zero);
                Prim(PrimitiveType.Cylinder, "Manche", g.transform, Vector3.zero, Vector3.zero, new Vector3(0.05f, 0.15f, 0.05f), wood);
                Prim(PrimitiveType.Sphere, "Flamme", g.transform, new Vector3(0f, 0.2f, 0f), Vector3.zero, Vector3.one * 0.12f, torch);
                GameObject lg = Empty("Torch_Light", g.transform, new Vector3(0f, 0.2f, 0f), Vector3.zero);
                Light l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(255f / 255f, 150f / 255f, 60f / 255f);
                l.intensity = 2f;
                l.range = 7f;
                l.shadows = LightShadows.None;
            }
            foreach (Transform c in env.GetComponentsInChildren<Transform>(true)) c.gameObject.isStatic = true;
            SavePrefab(env, PrefabDir + "/Environment.prefab");
        }

        static void BuildPupitre(Material wood)
        {
            var pup = new GameObject("Pupitre");
            Transform t = pup.transform;
            Prim(PrimitiveType.Cylinder, "Pied", t, new Vector3(0f, 0.5f, 1f), Vector3.zero, new Vector3(0.06f, 0.5f, 0.06f), wood);
            Prim(PrimitiveType.Cylinder, "Socle", t, new Vector3(0f, 0.015f, 1f), Vector3.zero, new Vector3(0.4f, 0.015f, 0.4f), wood);
            Vector3 plateauPos = new Vector3(0f, 1.1f, 1f);
            Vector3 plateauEuler = new Vector3(-30f, 0f, 0f);
            Prim(PrimitiveType.Cube, "Plateau", t, plateauPos, plateauEuler, new Vector3(0.5f, 0.02f, 0.35f), wood);
            // Ancre du canvas : centre du plateau, 1 cm au-dessus de sa surface (épaisseur 2 cm → +0.01 + 0.01), même orientation.
            Vector3 anchorPos = plateauPos + Quaternion.Euler(plateauEuler) * new Vector3(0f, 0.02f, 0f);
            Empty("PanelAnchor", t, anchorPos, plateauEuler);
            SavePrefab(pup, PrefabDir + "/Pupitre.prefab");
        }

        static void BuildBaguette(Material chrome, Material metal, Material cyan)
        {
            var bag = new GameObject("Baguette");
            Transform t = bag.transform;
            // Capsule Unity : rayon 0.5 / hauteur 2 le long de Y → rotation X=90° pour aligner sur +Z.
            GameObject corps = Prim(PrimitiveType.Capsule, "Corps", t, new Vector3(0f, 0f, 0.125f), new Vector3(90f, 0f, 0f), new Vector3(0.012f, 0.125f, 0.012f), chrome);
            GameObject poignee = Prim(PrimitiveType.Cylinder, "Poignee", t, new Vector3(0f, 0f, 0.03f), new Vector3(90f, 0f, 0f), new Vector3(0.02f, 0.03f, 0.02f), metal);
            GameObject pointe = Prim(PrimitiveType.Sphere, "Pointe", t, new Vector3(0f, 0f, 0.25f), Vector3.zero, Vector3.one * 0.015f, cyan);
            UnityEngine.Object.DestroyImmediate(corps.GetComponent<Collider>());
            UnityEngine.Object.DestroyImmediate(poignee.GetComponent<Collider>());
            UnityEngine.Object.DestroyImmediate(pointe.GetComponent<Collider>());
            Empty("Tip", t, new Vector3(0f, 0f, 0.25f), Vector3.zero);

            CapsuleCollider cc = bag.AddComponent<CapsuleCollider>();
            cc.direction = 2; // axe Z
            cc.radius = 0.01f;
            cc.height = 0.27f;
            cc.center = new Vector3(0f, 0f, 0.125f);
            Rigidbody rb = bag.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            SavePrefab(bag, PrefabDir + "/Baguette.prefab");

            // Instance posée sur le RestPoint du socle (socle à (0.3, 1.12, 0.95), RestPoint 1 cm au-dessus, X = -20°).
            t.position = new Vector3(0.3f, 1.15f, 0.95f);
            t.rotation = Quaternion.Euler(-20f, 0f, 0f);
        }

        static void BuildSocleBaguette(Material metal, Material cyan)
        {
            var soc = new GameObject("SocleBaguette");
            Transform t = soc.transform;
            Prim(PrimitiveType.Cylinder, "Base", t, new Vector3(0f, 0.01f, 0f), Vector3.zero, new Vector3(0.06f, 0.01f, 0.06f), metal);
            // Anneau à mi-hauteur de la base : seule la bande extérieure (5 mm) dépasse, aucune face coplanaire (pas de z-fighting).
            Prim(PrimitiveType.Cylinder, "Anneau", t, new Vector3(0f, 0.01f, 0f), Vector3.zero, new Vector3(0.07f, 0.0025f, 0.07f), cyan);
            Empty("RestPoint", t, new Vector3(0f, 0.03f, 0f), new Vector3(-20f, 0f, 0f));
            SavePrefab(soc, PrefabDir + "/SocleBaguette.prefab");
            t.position = new Vector3(0.3f, 1.12f, 0.95f);
        }

        static void BuildSection(string name, float[] xs, Material bodyMat, Material skin, Material wood, Material metal, AnimatorController ctrl, Vector3 instancePos)
        {
            var root = new GameObject(name);
            for (int i = 0; i < xs.Length; i++)
            {
                GameObject m = Empty("Musician_" + (i + 1), root.transform, new Vector3(xs[i], 0f, 0f), Vector3.zero);
                Transform mt = m.transform;
                Prim(PrimitiveType.Capsule, "Body", mt, new Vector3(0f, 0.6f, 0f), Vector3.zero, new Vector3(0.5f, 0.6f, 0.5f), bodyMat);
                Prim(PrimitiveType.Sphere, "Head", mt, new Vector3(0f, 1.35f, 0f), Vector3.zero, Vector3.one * 0.3f, skin);
                switch (name)
                {
                    case "Strings":
                        Prim(PrimitiveType.Cube, "Instrument", mt, new Vector3(0f, 0.9f, 0.3f), new Vector3(0f, 0f, 30f), new Vector3(0.5f, 0.15f, 0.05f), wood);
                        break;
                    case "Vent":
                        Prim(PrimitiveType.Cylinder, "Instrument", mt, new Vector3(0f, 1.0f, 0.3f), new Vector3(0f, 0f, 90f), new Vector3(0.04f, 0.25f, 0.04f), wood);
                        break;
                    case "Brass":
                        Prim(PrimitiveType.Cylinder, "Instrument", mt, new Vector3(0f, 1.0f, 0.42f), new Vector3(90f, 0f, 0f), new Vector3(0.15f, 0.2f, 0.15f), metal);
                        break;
                    default: // Percution : tambour posé devant le corps (z=0.5 pour ne pas traverser la capsule)
                        Prim(PrimitiveType.Cylinder, "Instrument", mt, new Vector3(0f, 0.75f, 0.5f), Vector3.zero, new Vector3(0.5f, 0.15f, 0.5f), wood);
                        break;
                }
                Animator a = m.AddComponent<Animator>();
                a.runtimeAnimatorController = ctrl;
                a.applyRootMotion = false;
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            SavePrefab(root, InstrDir + "/" + name + ".prefab");
            root.transform.position = instancePos;
            root.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        }

        static void BuildSpeakers(Material metal, Material magenta, AnimatorController ctrl)
        {
            var root = new GameObject("Speakers");
            float[] xs = { -2f, 0f, 2f };
            for (int i = 0; i < 3; i++)
            {
                // Racine vide par caisson (animée en Y) pour que le cercle émissif ne subisse pas le scale du cube.
                GameObject s = Empty("Speaker_" + (i + 1), root.transform, new Vector3(xs[i], 0.4f, 0f), Vector3.zero);
                Prim(PrimitiveType.Cube, "Box", s.transform, Vector3.zero, Vector3.zero, new Vector3(0.6f, 0.8f, 0.6f), metal);
                Prim(PrimitiveType.Cylinder, "Cone", s.transform, new Vector3(0f, 0f, -0.31f), new Vector3(90f, 0f, 0f), new Vector3(0.4f, 0.01f, 0.4f), magenta);
            }
            Animator a = root.AddComponent<Animator>();
            a.runtimeAnimatorController = ctrl;
            a.applyRootMotion = false;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            SavePrefab(root, PrefabDir + "/Speakers.prefab");
            root.transform.position = new Vector3(0f, 0f, 3.5f);
        }

        static void BuildStageLights(Material metal)
        {
            var root = new GameObject("StageLights");
            Vector3[] pos = { new Vector3(-3f, 6f, 4f), new Vector3(0f, 6f, 4f), new Vector3(3f, 6f, 4f) };
            Color[] cols = { new Color(1f, 0f, 1f), new Color(0f, 1f, 1f), new Color(1f, 0f, 1f) };
            for (int i = 0; i < 3; i++)
            {
                GameObject g = Empty("Spot_" + (i + 1), root.transform, pos[i], Vector3.zero);
                g.transform.LookAt(new Vector3(0f, 1f, 6f));
                Light l = g.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = cols[i];
                l.intensity = 8f;
                l.spotAngle = 40f;
                l.range = 15f;
                l.shadows = LightShadows.None;
                l.enabled = false;
                Prim(PrimitiveType.Cube, "Projecteur", g.transform, Vector3.zero, Vector3.zero, Vector3.one * 0.2f, metal);
            }
            SavePrefab(root, PrefabDir + "/StageLights.prefab");
        }

        static void BuildLasers(Material cyan)
        {
            var root = new GameObject("Lasers");
            Vector3[] starts = { new Vector3(-3f, 5f, 4f), new Vector3(0f, 5f, 4f), new Vector3(3f, 5f, 4f) };
            Vector3 target = new Vector3(0f, 1.5f, 7f);
            for (int i = 0; i < 3; i++)
            {
                Vector3 dir = (target - starts[i]).normalized;
                GameObject g = Prim(PrimitiveType.Cube, "Laser_" + (i + 1), root.transform, starts[i] + dir * 6f, Vector3.zero, new Vector3(0.02f, 0.02f, 12f), cyan);
                g.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                g.SetActive(false);
            }
            SavePrefab(root, PrefabDir + "/Lasers.prefab");
        }

        static void BuildVfx()
        {
            var root = new GameObject("VFX");
            SavePrefab(root, PrefabDir + "/VFX.prefab");
        }
    }
}
