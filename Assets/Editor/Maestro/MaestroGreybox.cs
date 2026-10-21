using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Maestro.Core;
using Maestro.Environment;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Maestro.EditorTools
{
    /// <summary>
    /// Greybox v3 : la scène démarre dans un opéra fermé (OperaShell) ; au trigger Rave de l'Animator d'Environment,
    /// l'opéra se démonte (dôme et lustre s'envolent, coque enfoncée) et le château de nuit (CastleShell) remonte du sol.
    /// Tout en primitives Unity et matériaux unis, zéro asset importé, zéro composant XRI dans les prefabs (sauf le
    /// Tracked Device Graphic Raycaster demandé sur le DebugPanel). Source de vérité de la scène Art_Sandbox : idempotent,
    /// assets réutilisés en place (GUID stables), ne touche ni au rig XR, ni à Main Camera, ni au prefab Baguette, ni à
    /// l'enfant Attach de l'instance Baguette.
    /// </summary>
    public static class MaestroGreybox
    {
        public const string ScenePath = "Assets/_OrchestraRave/Scenes/Tests/Art_Sandbox.unity";
        public const string MatDir = "Assets/_OrchestraRave/Art/Materials";
        public const string AnimDir = "Assets/_OrchestraRave/Art/Animations";
        public const string PrefabDir = "Assets/_OrchestraRave/Prefabs";
        public const string InstrDir = "Assets/_OrchestraRave/Prefabs/Instruments";
        public const string SystemsDir = "Assets/_OrchestraRave/Prefabs/Systems";
        public const string RigName = "XR Origin Hands (XR Rig)";
        public const string MainCameraName = "Main Camera";
        public const string EventSystemName = "EventSystem";
        public const string VolumeProfileName = "VP_Maestro";

        public static readonly string[] RootPrefabs = { "Environment", "Pupitre", "Baguette", "SocleBaguette", "Speakers", "StageLights", "Lasers", "VFX" };
        public static readonly string[] SectionPrefabs = { "Strings", "Vent", "Brass", "Percution" };
        public static readonly string[] SystemPrefabs = { "DebugPanel" };
        public static readonly string[] LitMaterialNames =
        {
            "M_Stone", "M_Wood", "M_MetalDark", "M_Chrome", "M_Torch", "M_EmissiveCyan", "M_EmissiveMagenta", "M_Strings", "M_Vent", "M_Brass",
            "M_Percution", "M_Skin", "M_StoneDark", "M_BannerRed", "M_BannerBlue", "M_Moon", "M_Brazier",
            "M_Velvet", "M_Gold", "M_Marble", "M_Plaster", "M_Chandelier",
        };
        public const string SkyboxMaterialName = "M_NightSky";
        public static readonly string[] AnchorNames = { "Anchor_Strings", "Anchor_Vent", "Anchor_Brass", "Anchor_Percution", "Anchor_Speakers" };
        public static readonly string[] EnvironmentGroups = { "Stage", "OperaShell", "CastleShell", "PostProcess" };
        public static readonly string[] DebugButtons = { "Rave", "Reset", "Drop caissons", "Musiciens : Play", "Musiciens : Stop", "Musiciens : Headbang", "Spots on/off", "Lasers on/off", "Fermer" };
        public static readonly string[] DebugButtonMethods = { "OnRave", "OnReset", "OnDrop", "OnMusiciansPlay", "OnMusiciansStop", "OnMusiciansHeadbang", "OnToggleSpots", "OnToggleLasers", "OnClose" };

        // ----- Contrat devs (inchangé depuis la v2 sauf SpeakerX) -----
        public static readonly Dictionary<string, float[]> SectionX = new Dictionary<string, float[]>
        {
            { "Strings",   new[] { -5f, -3f, -1f, 1f, 3f, 5f } },
            { "Vent",      new[] { -2.25f, -0.75f, 0.75f, 2.25f } },
            { "Brass",     new[] { -2.25f, -0.75f, 0.75f, 2.25f } },
            { "Percution", new[] { -2f, 0f, 2f } },
        };
        public static readonly Dictionary<string, Vector3> AnchorPos = new Dictionary<string, Vector3>
        {
            { "Anchor_Strings",   new Vector3(0f, -0.5f, 9f) },
            { "Anchor_Vent",      new Vector3(-5f, 1.5f, 11f) },
            { "Anchor_Brass",     new Vector3(5f, 1.5f, 11f) },
            { "Anchor_Percution", new Vector3(0f, 3.5f, 14.5f) },
            { "Anchor_Speakers",  new Vector3(0f, -1.5f, 6f) },
        };
        // v3 : caissons écartés (ils tombaient devant les cordes à ±2.5 / ±7).
        public static readonly float[] SpeakerX = { -9f, -6f, 6f, 9f };
        public const float SpeakerRestY = 1.5f;
        public const float SpeakerHiddenY = 40f;
        public const float SpeakerDropDuration = 2.5f;
        public const int SpotCount = 6;
        public const int LaserCount = 4;
        public static readonly Vector3 PupitrePos = new Vector3(0f, 0f, 1f);
        public static readonly Vector3 PlateauLocalPos = new Vector3(0f, 1.1f, 0f);
        public const float PlateauTiltX = -30f;
        // Lune (local à CastleShell = monde une fois remonté) : relevée à y = 150 en v2 pour passer au-dessus du mur du fond.
        public static readonly Vector3 MoonPos = new Vector3(35f, 150f, 180f);
        public const float MoonScale = 40f;
        public static readonly Vector3[] TowerPos = { new Vector3(-24f, 12.5f, 25f), new Vector3(24f, 12.5f, 25f) };
        public static readonly Vector3 StageLightTarget = new Vector3(0f, 1f, 11f);

        // Attache de la baguette (instance de scène uniquement, jamais réécrite si elle existe).
        public static readonly Vector3 BaguetteAttachLocalPos = new Vector3(0f, 0f, 0.11f);
        public static readonly Vector3 BaguetteAttachLocalEuler = Vector3.zero;

        // ----- v3 : coques et transition -----
        public const float OperaShellRaveY = -30f;
        public const float CastleShellHiddenY = -45f;
        public const float TransitionDuration = 6f;
        public const float DomeRise = 80f;
        public const float DomeEndScale = 0.2f;
        public const float LustreRise = 60f;
        public const float WallRadius = 32f;
        public const float WallHeight = 26f;
        public const float BalconyRadius = 29f;
        // Dôme : la spec (centre (0, 24, 0), échelle (66, 22, 66)) met le bas de la sphère à 13 m au-dessus du joueur et
        // enferme le lustre dedans. Pour que la salle soit vraiment sous le dôme (vu de l'intérieur), l'ellipsoïde est
        // centré plus bas et agrandi : il passe exactement par le haut (24.5) et le bas (-1.5) des murs à r = 32, sommet à 35.5 m.
        public static readonly Vector3 DomeCenter = new Vector3(0f, 8f, 0f);
        public static readonly Vector3 DomeScale = new Vector3(80f, 55f, 80f);
        public static readonly Vector3 LustrePos = new Vector3(0f, 19f, -6f);
        public static readonly Vector3 LintelPos = new Vector3(0f, 17f, 7f);
        public const float TorchBase = 2.5f;
        public const float BrazierBase = 3f;
        public const float LustreIntensity = 6f;
        public const float OperaLightIntensity = 2.5f;
        public static readonly Color WarmWhite = new Color(1f, 230f / 255f, 190f / 255f);
        public static readonly float[] OperaLightAngles = { 110f, 138f, 166f, 194f, 222f, 250f };
        public static readonly Color AmbientOpera = new Color(0.40f, 0.32f, 0.26f);
        public static readonly Color FogNight = new Color(0.02f, 0.03f, 0.08f);

        class Mats
        {
            public Material stone, stoneDark, wood, metal, chrome, torch, brazier, cyan, magenta, moon, red, blue;
            public Material strings, vent, brass, percu, skin, velvet, gold, marble, plaster, chandelier, sky;
        }

        static StringBuilder _sb;
        static void Log(string msg)
        {
            if (_sb != null) _sb.AppendLine(msg);
            Debug.Log("[Maestro] " + msg);
        }

        [MenuItem("Tools/Maestro/Build Greybox")]
        public static void BuildFromMenu()
        {
            MaestroRunner.Execute("build");
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
                EnsureFolder(SystemsDir);

                Mats m = BuildMaterials();
                AnimatorController musicianCtrl = BuildMusicianController();
                AnimatorController speakersCtrl = BuildSpeakersController();
                AnimatorController envCtrl = BuildEnvironmentController();
                VolumeProfile profile = BuildVolumeProfile();
                AssetDatabase.SaveAssets();

                Light dir = FindOrCreateDirectionalLight(scene);
                GameObject env = BuildEnvironment(m, envCtrl, profile, dir);
                BuildPupitre(m.wood);
                GameObject socle = BuildSocleBaguette(m.metal, m.cyan);
                BuildBaguette(scene, m.chrome, m.metal, m.cyan, socle.transform.Find("RestPoint"));
                foreach (string section in SectionPrefabs)
                {
                    Material bodyMat = section == "Strings" ? m.strings : section == "Vent" ? m.vent : section == "Brass" ? m.brass : m.percu;
                    BuildSection(section, SectionX[section], bodyMat, m.skin, m.wood, m.metal, musicianCtrl, env.transform.Find("Anchor_" + section));
                }
                BuildSpeakers(m.metal, m.magenta, speakersCtrl, env.transform.Find("Anchor_Speakers"));
                BuildStageLights(m.metal);
                BuildLasers(m.cyan);
                BuildVfx();
                BuildDebugPanel();
                EnsureEventSystem(scene);

                SetupSceneLighting(scene, m.sky, dir);
                SaveAll(sb);
                Log("Greybox v3 terminé.");
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

        public static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == name) return root;
            return null;
        }

        /// <summary>Supprime nos instances pour les reconstruire. Baguette (composants de test), le rig XR, Main Camera et EventSystem ne sont jamais touchés.</summary>
        static void RemoveOwnInstances(Scene scene)
        {
            var names = new HashSet<string>(RootPrefabs);
            names.UnionWith(SectionPrefabs);
            names.UnionWith(SystemPrefabs);
            names.Remove("Baguette");
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (!names.Contains(root.name)) continue;
                string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (!string.IsNullOrEmpty(src) && src.StartsWith(PrefabDir))
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
                else
                {
                    throw new Exception("Objet préexistant '" + root.name + "' dans la scène (pas une instance de nos prefabs) : arrêt pour ne rien supprimer.");
                }
            }
            Log("Anciennes instances de nos prefabs supprimées pour reconstruction (Baguette, " + RigName + ", " + MainCameraName + " et " + EventSystemName + " conservés).");
        }

        static Light FindOrCreateDirectionalLight(Scene scene)
        {
            foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.gameObject.scene == scene) return l;
            var go = new GameObject("Directional Light");
            Light dir = go.AddComponent<Light>();
            dir.type = LightType.Directional;
            Log("Aucune Directional Light dans la scène : créée.");
            return dir;
        }

        /// <summary>Réglages de la scène en mode édition = état opéra (rave = 0) ; EnvironmentMood prend le relais en Play Mode.</summary>
        static void SetupSceneLighting(Scene scene, Material sky, Light dir)
        {
            Vector3 toScene = (new Vector3(0f, 0f, 10f) - MoonPos).normalized;
            dir.transform.rotation = Quaternion.LookRotation(toScene, Vector3.up);
            dir.color = new Color(180f / 255f, 200f / 255f, 255f / 255f);
            dir.intensity = 0f;
            dir.shadows = LightShadows.Soft;
            EditorUtility.SetDirty(dir);
            RenderSettings.sun = dir;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientOpera;
            RenderSettings.fog = false;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0f;
            RenderSettings.fogColor = FogNight;
            RenderSettings.skybox = sky;
            float elev = Mathf.Atan2(MoonPos.y, new Vector2(MoonPos.x, MoonPos.z - 10f).magnitude) * Mathf.Rad2Deg;
            Log("Éclairage mode édition = opéra : Directional orientée lune (élévation " + elev.ToString("F0") + "°) intensité 0, ambiante " + AmbientOpera + ", brouillard off, skybox " + SkyboxMaterialName + ". " + MainCameraName + " non touchée.");
        }

        [MenuItem("Tools/Maestro/Save Scene And Assets")]
        public static void SaveFromMenu()
        {
            MaestroRunner.Execute("save");
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

        static Mats BuildMaterials()
        {
            var m = new Mats();
            m.stone      = Mat("M_Stone",           new Color(0.45f, 0.44f, 0.42f), 0f, 0.25f);
            m.stoneDark  = Mat("M_StoneDark",       new Color(0.22f, 0.22f, 0.25f), 0f, 0.2f);
            m.wood       = Mat("M_Wood",            new Color(0.40f, 0.26f, 0.13f), 0f, 0.35f);
            m.metal      = Mat("M_MetalDark",       new Color(0.08f, 0.08f, 0.09f), 0.3f, 0.2f);
            m.chrome     = Mat("M_Chrome",          new Color(0.85f, 0.85f, 0.88f), 1f, 0.9f);
            m.torch      = Mat("M_Torch",           new Color(1.00f, 0.55f, 0.15f), 0f, 0.5f, new Color(1.00f, 0.55f, 0.15f), 2.5f);
            m.brazier    = Mat("M_Brazier",         new Color(1.00f, 0.45f, 0.10f), 0f, 0.5f, new Color(1.00f, 0.45f, 0.10f), 2f);
            m.cyan       = Mat("M_EmissiveCyan",    new Color(0.00f, 0.90f, 1.00f), 0f, 0.5f, new Color(0.00f, 0.90f, 1.00f), 4f);
            m.magenta    = Mat("M_EmissiveMagenta", new Color(1.00f, 0.00f, 0.85f), 0f, 0.5f, new Color(1.00f, 0.00f, 0.85f), 4f);
            m.moon       = Mat("M_Moon",            new Color(0.85f, 0.90f, 1.00f), 0f, 0.5f, new Color(0.85f, 0.90f, 1.00f), 2f);
            m.red        = Mat("M_BannerRed",       new Color(0.60f, 0.08f, 0.08f), 0f, 0.3f);
            m.blue       = Mat("M_BannerBlue",      new Color(0.08f, 0.15f, 0.55f), 0f, 0.3f);
            m.strings    = Mat("M_Strings",         new Color(0.00f, 0.50f, 0.50f), 0f, 0.4f);
            m.vent       = Mat("M_Vent",            new Color(0.50f, 0.20f, 0.80f), 0f, 0.4f);
            m.brass      = Mat("M_Brass",           new Color(1.00f, 0.45f, 0.35f), 0f, 0.4f);
            m.percu      = Mat("M_Percution",       new Color(1.00f, 0.70f, 0.20f), 0f, 0.4f);
            m.skin       = Mat("M_Skin",            new Color(0.90f, 0.78f, 0.62f), 0f, 0.4f);
            // v3 : opéra
            m.velvet     = Mat("M_Velvet",          new Color(0.35f, 0.03f, 0.06f), 0f, 0.2f);
            m.gold       = Mat("M_Gold",            new Color(1.00f, 0.78f, 0.35f), 0.9f, 0.8f, new Color(1.00f, 0.78f, 0.35f), 0.4f);
            m.marble     = Mat("M_Marble",          new Color(0.92f, 0.90f, 0.85f), 0f, 0.6f);
            m.plaster    = Mat("M_Plaster",         new Color(0.93f, 0.88f, 0.78f), 0f, 0.3f, null, 0f, true);
            m.chandelier = Mat("M_Chandelier",      new Color(1.00f, 0.92f, 0.75f), 0f, 0.5f, new Color(1.00f, 0.92f, 0.75f), 3f);
            m.sky        = SkyboxMaterial();
            Log(LitMaterialNames.Length + " matériaux URP/Lit + " + SkyboxMaterialName + " (Skybox/Procedural) créés ou mis à jour dans " + MatDir + " (M_Plaster : Render Face = Both).");
            return m;
        }

        static Material Mat(string name, Color baseColor, float metallic, float smoothness, Color? emission = null, float emissionIntensity = 0f, bool doubleSided = false)
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
            mat.SetFloat("_Cull", doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            mat.doubleSidedGI = doubleSided;
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

        static Material SkyboxMaterial()
        {
            string path = MatDir + "/" + SkyboxMaterialName + ".mat";
            Shader shader = Shader.Find("Skybox/Procedural");
            if (shader == null) throw new Exception("Shader 'Skybox/Procedural' introuvable.");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(shader);
            else if (mat.shader != shader) mat.shader = shader;
            mat.SetFloat("_Exposure", 0.25f);
            mat.SetFloat("_SunSize", 0.02f);
            mat.SetFloat("_SunSizeConvergence", 5f);
            mat.SetFloat("_AtmosphereThickness", 0.5f);
            mat.SetColor("_SkyTint", new Color(0.10f, 0.14f, 0.30f));
            mat.SetColor("_GroundColor", new Color(0.02f, 0.025f, 0.05f));
            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Profil de post-processing VP_Maestro (Bloom) réutilisé en place.</summary>
        static VolumeProfile BuildVolumeProfile()
        {
            string path = MatDir + "/" + VolumeProfileName + ".asset";
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            Bloom bloom;
            if (!profile.TryGet(out bloom))
            {
                bloom = profile.Add<Bloom>(true);
                bloom.name = "Bloom";
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }
            bloom.active = true;
            bloom.intensity.Override(0.8f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.6f);
            bloom.highQualityFiltering.Override(false);
            EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(profile);
            Log("Profil " + VolumeProfileName + " : Bloom intensité 0.8, seuil 1.0, scatter 0.6, HQ filtering off.");
            return profile;
        }

        /// <summary>Crée le clip, ou le vide et le réutilise en place (GUID stable ; jamais DeleteAsset + CreateAsset dans la même frame).</summary>
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

        /// <summary>Courbe « maintien → ease-in-out → maintien » : toutes les tangentes nulles (Hermite = smoothstep entre t0 et t1).</summary>
        static AnimationCurve HoldEase(float t0, float v0, float t1, float v1, float length)
        {
            var keys = new List<Keyframe>();
            if (t0 > 0f) keys.Add(new Keyframe(0f, v0, 0f, 0f));
            keys.Add(new Keyframe(t0, v0, 0f, 0f));
            keys.Add(new Keyframe(t1, v1, 0f, 0f));
            if (t1 < length) keys.Add(new Keyframe(length, v1, 0f, 0f));
            return new AnimationCurve(keys.ToArray());
        }

        static AnimationCurve Const(float v, float length)
        {
            return AnimationCurve.Constant(0f, length, v);
        }

        static void Vec3Curves(AnimationClip clip, string path, string prop, AnimationCurve x, AnimationCurve y, AnimationCurve z)
        {
            Curve(clip, path, prop + ".x", x);
            Curve(clip, path, prop + ".y", y);
            Curve(clip, path, prop + ".z", z);
        }

        static void MoodCurve(AnimationClip clip, AnimationCurve c)
        {
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(EnvironmentMood), "rave"), c);
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

        static GameObject NoCollider(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.DestroyImmediate(c);
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

        static Light PointLight(string name, Transform parent, Vector3 localPos, Color color, float intensity, float range)
        {
            GameObject lg = Empty(name, parent, localPos, Vector3.zero);
            Light l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        static GameObject SavePrefab(GameObject go, string path)
        {
            bool ok;
            GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.AutomatedAction, out ok);
            if (!ok || prefab == null) throw new Exception("Échec de sauvegarde du prefab " + path);
            Log("Prefab : " + path);
            return prefab;
        }

        static Vector3 Polar(float radius, float angleDeg, float y)
        {
            float r = angleDeg * Mathf.Deg2Rad;
            return new Vector3(radius * Mathf.Sin(r), y, radius * Mathf.Cos(r));
        }

        // ------------------------------------------------------------------ animations

        static AnimatorController BuildMusicianController()
        {
            AnimationClip idle = NewClip("Anim_Idle", true);
            AnimationClip play = NewClip("Anim_Play", true);
            AnimationClip headbang = NewClip("Anim_Headbang", true);

            const float TIdle = 3f;
            Func<float, float> sway = t => 2f * Mathf.Sin(2f * Mathf.PI * t / TIdle);
            TransformCurves(idle, "Body", TIdle, t => 0f, t => 0.6f, t => 0f, t => 0f, t => 0f, sway);
            TransformCurves(idle, "Head", TIdle,
                t => -0.75f * Mathf.Sin(sway(t) * Mathf.Deg2Rad), t => 0.6f + 0.75f * Mathf.Cos(sway(t) * Mathf.Deg2Rad), t => 0f,
                t => 0f, t => 0f, sway);

            const float TPlay = 1f;
            Func<float, float> bounce3 = t => 0.03f * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * t));
            Func<float, float> nod = t => 8f * Mathf.Sin(2f * Mathf.PI * t);
            TransformCurves(play, "Body", TPlay, t => 0f, t => 0.6f + bounce3(t), t => 0f, t => 0f, t => 0f, t => 0f);
            TransformCurves(play, "Head", TPlay,
                t => 0f, t => 1.2f + bounce3(t) + 0.15f * Mathf.Cos(nod(t) * Mathf.Deg2Rad), t => 0.15f * Mathf.Sin(nod(t) * Mathf.Deg2Rad),
                nod, t => 0f, t => 0f);

            const float THb = 1f;
            Func<float, float> bounce5 = t => 0.05f * (0.5f - 0.5f * Mathf.Cos(4f * Mathf.PI * t));
            Func<float, float> bang = t => -10f - 20f * Mathf.Cos(4f * Mathf.PI * t);
            TransformCurves(headbang, "Body", THb, t => 0f, t => 0.6f + bounce5(t), t => 0f, t => 0f, t => 0f, t => 0f);
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

        /// <summary>Chute de 40 m à 1.5 m en ~2 s avec deux rebonds amortis.</summary>
        static float DropY(float t)
        {
            const float tFall = 1.2f, tB1 = 0.5f, tB2 = 0.3f, h1 = 1.2f, h2 = 0.35f;
            if (t <= 0f) return SpeakerHiddenY;
            if (t < tFall) { float u = t / tFall; return SpeakerHiddenY - (SpeakerHiddenY - SpeakerRestY) * u * u; }
            t -= tFall;
            if (t < tB1) { float u = t / tB1; return SpeakerRestY + h1 * 4f * u * (1f - u); }
            t -= tB1;
            if (t < tB2) { float u = t / tB2; return SpeakerRestY + h2 * 4f * u * (1f - u); }
            return SpeakerRestY;
        }

        static AnimatorController BuildSpeakersController()
        {
            AnimationClip hidden = NewClip("Anim_SpeakersHidden", false);
            AnimationClip drop = NewClip("Anim_SpeakersDrop", false);
            for (int i = 0; i < SpeakerX.Length; i++)
            {
                string path = "Speaker_" + (i + 1);
                float delay = 0.15f * i;
                Curve(hidden, path, "localPosition.x", AnimationCurve.Constant(0f, 1f / 60f, SpeakerX[i]));
                Curve(hidden, path, "localPosition.y", AnimationCurve.Constant(0f, 1f / 60f, SpeakerHiddenY));
                Curve(hidden, path, "localPosition.z", AnimationCurve.Constant(0f, 1f / 60f, 0f));
                Curve(drop, path, "localPosition.x", AnimationCurve.Constant(0f, SpeakerDropDuration, SpeakerX[i]));
                Curve(drop, path, "localPosition.y", Sampled(t => DropY(t - delay), SpeakerDropDuration, 60, true));
                Curve(drop, path, "localPosition.z", AnimationCurve.Constant(0f, SpeakerDropDuration, 0f));
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
            Log("SpeakersController : Hidden (y=" + SpeakerHiddenY + ") → Dropped sur trigger Drop, chute " + SpeakerDropDuration + " s, caissons x = " + string.Join("/", Array.ConvertAll(SpeakerX, x => x.ToString("F0"))) + ".");
            return c;
        }

        /// <summary>
        /// EnvironmentController : Opera (défaut, 1 frame) → Rave (Anim_ToRave 6 s) sur trigger Rave ; Rave → ToOpera (Anim_ToOpera 6 s,
        /// inverse) sur trigger Reset, puis retour automatique à Opera. Courbes ease-in-out sur les transforms enfants par chemin.
        /// </summary>
        static AnimatorController BuildEnvironmentController()
        {
            float D = TransitionDuration;
            float f = 1f / 60f;
            AnimationClip opera = NewClip("Anim_Opera", false);
            AnimationClip toRave = NewClip("Anim_ToRave", false);
            AnimationClip toOpera = NewClip("Anim_ToOpera", false);
            Vector3 domeEnd = DomeScale * DomeEndScale;

            // Opera : pose de départ (1 frame)
            MoodCurve(opera, Const(0f, f));
            Vec3Curves(opera, "OperaShell", "localPosition", Const(0f, f), Const(0f, f), Const(0f, f));
            Vec3Curves(opera, "CastleShell", "localPosition", Const(0f, f), Const(CastleShellHiddenY, f), Const(0f, f));
            Vec3Curves(opera, "OperaShell/Dome", "localPosition", Const(DomeCenter.x, f), Const(DomeCenter.y, f), Const(DomeCenter.z, f));
            Vec3Curves(opera, "OperaShell/Dome", "localScale", Const(DomeScale.x, f), Const(DomeScale.y, f), Const(DomeScale.z, f));
            Vec3Curves(opera, "OperaShell/Lustre", "localPosition", Const(LustrePos.x, f), Const(LustrePos.y, f), Const(LustrePos.z, f));

            // ToRave : 0→2 s lustre +60 et dôme +80 / échelle 0.2 ; 0.5→4 s OperaShell → -30 ; 1→5 s CastleShell -45 → 0 ; 0→6 s rave 0 → 1
            MoodCurve(toRave, HoldEase(0f, 0f, D, 1f, D));
            Vec3Curves(toRave, "OperaShell", "localPosition", Const(0f, D), HoldEase(0.5f, 0f, 4f, OperaShellRaveY, D), Const(0f, D));
            Vec3Curves(toRave, "CastleShell", "localPosition", Const(0f, D), HoldEase(1f, CastleShellHiddenY, 5f, 0f, D), Const(0f, D));
            Vec3Curves(toRave, "OperaShell/Dome", "localPosition", Const(DomeCenter.x, D), HoldEase(0f, DomeCenter.y, 2f, DomeCenter.y + DomeRise, D), Const(DomeCenter.z, D));
            Vec3Curves(toRave, "OperaShell/Dome", "localScale", HoldEase(0f, DomeScale.x, 2f, domeEnd.x, D), HoldEase(0f, DomeScale.y, 2f, domeEnd.y, D), HoldEase(0f, DomeScale.z, 2f, domeEnd.z, D));
            Vec3Curves(toRave, "OperaShell/Lustre", "localPosition", Const(LustrePos.x, D), HoldEase(0f, LustrePos.y, 2f, LustrePos.y + LustreRise, D), Const(LustrePos.z, D));

            // ToOpera : miroir temporel de ToRave (valeur(t) = ToRave(D - t))
            MoodCurve(toOpera, HoldEase(0f, 1f, D, 0f, D));
            Vec3Curves(toOpera, "OperaShell", "localPosition", Const(0f, D), HoldEase(D - 4f, OperaShellRaveY, D - 0.5f, 0f, D), Const(0f, D));
            Vec3Curves(toOpera, "CastleShell", "localPosition", Const(0f, D), HoldEase(D - 5f, 0f, D - 1f, CastleShellHiddenY, D), Const(0f, D));
            Vec3Curves(toOpera, "OperaShell/Dome", "localPosition", Const(DomeCenter.x, D), HoldEase(D - 2f, DomeCenter.y + DomeRise, D, DomeCenter.y, D), Const(DomeCenter.z, D));
            Vec3Curves(toOpera, "OperaShell/Dome", "localScale", HoldEase(D - 2f, domeEnd.x, D, DomeScale.x, D), HoldEase(D - 2f, domeEnd.y, D, DomeScale.y, D), HoldEase(D - 2f, domeEnd.z, D, DomeScale.z, D));
            Vec3Curves(toOpera, "OperaShell/Lustre", "localPosition", Const(LustrePos.x, D), HoldEase(D - 2f, LustrePos.y + LustreRise, D, LustrePos.y, D), Const(LustrePos.z, D));

            AnimatorController c = NewController("EnvironmentController");
            c.AddParameter("Rave", AnimatorControllerParameterType.Trigger);
            c.AddParameter("Reset", AnimatorControllerParameterType.Trigger);
            AnimatorStateMachine sm = c.layers[0].stateMachine;
            AnimatorState sOpera = sm.AddState("Opera", new Vector3(0f, 0f, 0f));
            AnimatorState sRave = sm.AddState("Rave", new Vector3(320f, -80f, 0f));
            AnimatorState sToOpera = sm.AddState("ToOpera", new Vector3(320f, 80f, 0f));
            sOpera.motion = opera;
            sRave.motion = toRave;
            sToOpera.motion = toOpera;
            sm.defaultState = sOpera;
            Transition(sOpera, sRave, "Rave", 0f);
            Transition(sRave, sToOpera, "Reset", 0f);
            Transition(sToOpera, sRave, "Rave", 0f);
            AnimatorStateTransition back = sToOpera.AddTransition(sOpera);
            back.hasExitTime = true;
            back.exitTime = 1f;
            back.hasFixedDuration = true;
            back.duration = 0f;
            EditorUtility.SetDirty(c);
            Log("EnvironmentController : Opera (défaut) → Rave sur trigger Rave (Anim_ToRave " + D + " s) ; Rave → ToOpera sur trigger Reset (Anim_ToOpera " + D + " s) → Opera. Pas de trigger Night.");
            return c;
        }

        // ------------------------------------------------------------------ prefabs

        static void BuildTier(Transform parent, string name, float radius, Vector3 size, float topY, Material mat)
        {
            GameObject g = Empty(name, parent, Vector3.zero, Vector3.zero);
            for (int i = 0; i < 5; i++)
            {
                float ang = -40f + 20f * i;
                Prim(PrimitiveType.Cube, name + "_Seg" + (i + 1), g.transform, Polar(radius, ang, topY - size.y * 0.5f), new Vector3(0f, ang, 0f), size, mat);
            }
        }

        static void Torch(Transform parent, string name, Vector3 pos, Material wood, Material torch, List<Light> lights)
        {
            GameObject g = Empty(name, parent, pos, Vector3.zero);
            Prim(PrimitiveType.Cylinder, "Manche", g.transform, Vector3.zero, Vector3.zero, new Vector3(0.05f, 0.15f, 0.05f), wood);
            Prim(PrimitiveType.Sphere, "Flamme", g.transform, new Vector3(0f, 0.2f, 0f), Vector3.zero, Vector3.one * 0.12f, torch);
            lights.Add(PointLight("Torch_Light", g.transform, new Vector3(0f, 0.2f, 0f), new Color(255f / 255f, 150f / 255f, 60f / 255f), TorchBase, 8f));
        }

        static void Brazier(Transform parent, string name, Vector3 pos, Material metal, Material brazier, List<Light> lights)
        {
            GameObject g = Empty(name, parent, pos, Vector3.zero);
            Prim(PrimitiveType.Cylinder, "Vasque", g.transform, new Vector3(0f, 0.5f, 0f), Vector3.zero, new Vector3(0.8f, 0.5f, 0.8f), metal);
            Prim(PrimitiveType.Sphere, "Feu", g.transform, new Vector3(0f, 1.15f, 0f), Vector3.zero, Vector3.one * 0.5f, brazier);
            lights.Add(PointLight("Brazier_Light", g.transform, new Vector3(0f, 1.2f, 0f), new Color(255f / 255f, 150f / 255f, 60f / 255f), BrazierBase, 10f));
        }

        static GameObject BuildEnvironment(Mats m, AnimatorController ctrl, VolumeProfile profile, Light dir)
        {
            var env = new GameObject("Environment");
            Transform t = env.transform;
            var operaLights = new List<Light>();
            var torchLights = new List<Light>();
            var brazierLights = new List<Light>();

            // ----- Stage : ce qui ne bouge jamais -----
            Transform st = Empty("Stage", t, Vector3.zero, Vector3.zero).transform;
            Prim(PrimitiveType.Plane, "Cour", st, new Vector3(0f, -1.5f, 20f), Vector3.zero, new Vector3(8f, 1f, 8f), m.stone);
            Prim(PrimitiveType.Cylinder, "Podium", st, new Vector3(0f, -0.75f, 0f), Vector3.zero, new Vector3(2.4f, 0.75f, 2.4f), m.wood);
            for (int k = 0; k < 3; k++)
                Prim(PrimitiveType.Cube, "Marche_" + (k + 1), st, new Vector3(0f, -0.625f - 0.375f * k, -1.45f - 0.5f * k), Vector3.zero, new Vector3(1.2f, 0.5f, 0.5f), m.stone);
            BuildTier(st, "Gradin_1", 9f, new Vector3(4f, 1f, 3f), -0.5f, m.stone);
            BuildTier(st, "Gradin_2", 12f, new Vector3(5f, 3f, 3f), 1.5f, m.stone);
            BuildTier(st, "Gradin_3", 15f, new Vector3(6f, 5f, 3f), 3.5f, m.stone);
            Brazier(st, "Brasero_1", Polar(9f, -50f, -0.5f), m.metal, m.brazier, brazierLights);
            Brazier(st, "Brasero_2", Polar(9f, 50f, -0.5f), m.metal, m.brazier, brazierLights);
            Brazier(st, "Brasero_3", Polar(15f, -50f, 3.5f), m.metal, m.brazier, brazierLights);
            Brazier(st, "Brasero_4", Polar(15f, 50f, 3.5f), m.metal, m.brazier, brazierLights);
            Torch(st, "Torch_Gradin_1", Polar(12f, -50f, 1.5f), m.wood, m.torch, torchLights);
            Torch(st, "Torch_Gradin_2", Polar(12f, 50f, 1.5f), m.wood, m.torch, torchLights);

            // ----- Ancres (contrat devs, directement sous Environment) -----
            foreach (string anchor in AnchorNames)
                Empty(anchor, t, AnchorPos[anchor], anchor == "Anchor_Speakers" ? Vector3.zero : new Vector3(0f, 180f, 0f));

            // ----- Coques -----
            Transform opera = Empty("OperaShell", t, Vector3.zero, Vector3.zero).transform;
            BuildOperaShell(opera, m, operaLights);
            Transform castle = Empty("CastleShell", t, new Vector3(0f, CastleShellHiddenY, 0f), Vector3.zero).transform;
            Renderer moonRenderer = BuildCastleShell(castle, m, torchLights);

            // ----- Post-process -----
            GameObject pp = Empty("PostProcess", t, Vector3.zero, Vector3.zero);
            Volume vol = pp.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0f;
            vol.sharedProfile = profile;

            // ----- Static : Stage et ancres seulement (les coques bougent) -----
            foreach (Transform c in st.GetComponentsInChildren<Transform>(true)) c.gameObject.isStatic = true;
            foreach (string anchor in AnchorNames) t.Find(anchor).gameObject.isStatic = true;

            // ----- Animator + EnvironmentMood sur la racine -----
            Animator a = env.AddComponent<Animator>();
            a.runtimeAnimatorController = ctrl;
            a.applyRootMotion = false;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EnvironmentMood mood = env.AddComponent<EnvironmentMood>();
            mood.rave = 0f;
            mood.operaLights = operaLights.ToArray();
            var bases = new float[operaLights.Count];
            for (int i = 0; i < bases.Length; i++) bases[i] = operaLights[i].intensity;
            mood.operaLightBases = bases;
            mood.torchLights = torchLights.ToArray();
            mood.brazierLights = brazierLights.ToArray();
            mood.torchBase = TorchBase;
            mood.brazierBase = BrazierBase;
            mood.moonRenderer = moonRenderer;
            mood.moonMaxScale = MoonScale;
            mood.nightSkybox = m.sky;
            mood.ambientOpera = AmbientOpera;
            mood.fogNight = FogNight;

            SavePrefab(env, PrefabDir + "/Environment.prefab");

            // La Directional Light est un objet de scène (hors prefab) : référence posée comme override d'instance.
            EnvironmentMood inst = env.GetComponent<EnvironmentMood>();
            inst.directionalLight = dir;
            PrefabUtility.RecordPrefabInstancePropertyModifications(inst);
            Log("Environment : Stage (static), OperaShell @ (0,0,0), CastleShell @ (0," + CastleShellHiddenY + ",0), PostProcess (Volume global " + VolumeProfileName + "), Animator EnvironmentController, EnvironmentMood (" + operaLights.Count + " lumières opéra, " + torchLights.Count + " torches à " + TorchBase + ", " + brazierLights.Count + " braseros à " + BrazierBase + ", lune échelle 0).");
            return env;
        }

        static void BuildOperaShell(Transform p, Mats m, List<Light> operaLights)
        {
            // Sol d'opéra juste au-dessus de la cour
            Prim(PrimitiveType.Plane, "Sol_Opera", p, new Vector3(0f, -1.49f, 0f), Vector3.zero, new Vector3(7f, 1f, 7f), m.velvet);

            // Murs : anneau de 24 cubes tournés vers le centre, du sol (-1.5) à 24.5
            Transform murs = Empty("Murs", p, Vector3.zero, Vector3.zero).transform;
            for (int i = 0; i < 24; i++)
            {
                float ang = 15f * i;
                Prim(PrimitiveType.Cube, "Mur_" + (i + 1), murs, Polar(WallRadius, ang, -1.5f + WallHeight * 0.5f), new Vector3(0f, ang, 0f), new Vector3(8.5f, WallHeight, 1f), m.plaster);
            }

            // Balcons : trois anneaux de loges sur la moitié arrière (100° → 260°), rambarde dorée en bord intérieur
            Transform balcons = Empty("Balcons", p, Vector3.zero, Vector3.zero).transform;
            float[] balconyY = { 5f, 10f, 15f };
            for (int r = 0; r < balconyY.Length; r++)
            {
                int k = 1;
                for (float ang = 100f; ang <= 260.01f; ang += 10f, k++)
                {
                    Prim(PrimitiveType.Cube, "Loge_" + (r + 1) + "_" + k, balcons, Polar(BalconyRadius, ang, balconyY[r]), new Vector3(0f, ang, 0f), new Vector3(4f, 1.2f, 3f), m.velvet);
                    Prim(PrimitiveType.Cube, "Rambarde_" + (r + 1) + "_" + k, balcons, Polar(BalconyRadius - 1.5f + 0.05f, ang, balconyY[r] + 1.0f), new Vector3(0f, ang, 0f), new Vector3(4f, 0.8f, 0.1f), m.gold);
                }
            }

            // Dôme (vu de l'intérieur : M_Plaster en Render Face Both) et corniche dorée au bord des murs
            NoCollider(Prim(PrimitiveType.Sphere, "Dome", p, DomeCenter, Vector3.zero, DomeScale, m.plaster));
            Transform corniche = Empty("Corniche", p, Vector3.zero, Vector3.zero).transform;
            for (int i = 0; i < 24; i++)
            {
                float ang = 15f * i;
                Prim(PrimitiveType.Cube, "Corniche_" + (i + 1), corniche, Polar(WallRadius, ang, 24f), new Vector3(0f, ang, 0f), new Vector3(8f, 0.6f, 0.6f), m.gold);
            }

            // Lustre : plateau doré, chaîne jusqu'au plafond du dôme, 12 perles émissives + cœur, Point Light blanc chaud
            Transform lustre = Empty("Lustre", p, LustrePos, Vector3.zero).transform;
            Prim(PrimitiveType.Cylinder, "Plateau", lustre, Vector3.zero, Vector3.zero, new Vector3(3f, 0.15f, 3f), m.gold);
            float hr = DomeScale.x * 0.5f, vr = DomeScale.y * 0.5f;
            float ceilingY = DomeCenter.y + vr * Mathf.Sqrt(Mathf.Max(0f, 1f - (LustrePos.z * LustrePos.z) / (hr * hr)));
            float chain = Mathf.Max(5f, ceilingY - LustrePos.y - 0.15f);
            NoCollider(Prim(PrimitiveType.Cylinder, "Chaine", lustre, new Vector3(0f, 0.15f + chain * 0.5f, 0f), Vector3.zero, new Vector3(0.1f, chain * 0.5f, 0.1f), m.gold));
            for (int i = 0; i < 12; i++)
            {
                float ang = 30f * i;
                NoCollider(Prim(PrimitiveType.Sphere, "Perle_" + (i + 1), lustre, Polar(1.5f, ang, -0.5f), Vector3.zero, Vector3.one * 0.6f, m.chandelier));
            }
            NoCollider(Prim(PrimitiveType.Sphere, "Coeur", lustre, new Vector3(0f, -0.9f, 0f), Vector3.zero, Vector3.one * 1.2f, m.chandelier));
            Light lustreLight = PointLight("Lustre_Light", lustre, new Vector3(0f, -0.5f, 0f), WarmWhite, LustreIntensity, 45f);
            lustreLight.shadows = LightShadows.Soft;
            operaLights.Add(lustreLight);

            // Cadre de scène : colonnes de marbre, linteau doré, rideaux de velours
            Transform pros = Empty("Proscenium", p, Vector3.zero, Vector3.zero).transform;
            Prim(PrimitiveType.Cylinder, "Colonne_1", pros, new Vector3(-14f, 7.5f, 7f), Vector3.zero, new Vector3(2.5f, 9f, 2.5f), m.marble);
            Prim(PrimitiveType.Cylinder, "Colonne_2", pros, new Vector3(14f, 7.5f, 7f), Vector3.zero, new Vector3(2.5f, 9f, 2.5f), m.marble);
            Prim(PrimitiveType.Cube, "Linteau", pros, LintelPos, Vector3.zero, new Vector3(32f, 2.5f, 2.5f), m.gold);
            Prim(PrimitiveType.Cube, "Rideau_1", pros, new Vector3(-12f, 7f, 7.4f), Vector3.zero, new Vector3(4f, 16f, 0.6f), m.velvet);
            Prim(PrimitiveType.Cube, "Rideau_2", pros, new Vector3(12f, 7f, 7.4f), Vector3.zero, new Vector3(4f, 16f, 0.6f), m.velvet);

            // Lumière opéra : six Point Lights blanc chaud sur les balcons (y = 11), sans ombres
            Transform lum = Empty("Lumieres_Opera", p, Vector3.zero, Vector3.zero).transform;
            for (int i = 0; i < OperaLightAngles.Length; i++)
                operaLights.Add(PointLight("Opera_Light_" + (i + 1), lum, Polar(BalconyRadius - 3f, OperaLightAngles[i], 11f), WarmWhite, OperaLightIntensity, 20f));

            Log("OperaShell : sol velours, 24 murs r=" + WallRadius + ", 3 anneaux de 17 loges, dôme ellipsoïde centre " + DomeCenter + " échelle " + DomeScale + " (sommet " + (DomeCenter.y + vr).ToString("F1") + " m), corniche, lustre @ " + LustrePos + " (chaîne " + chain.ToString("F1") + " m jusqu'au plafond), proscenium, " + OperaLightAngles.Length + " lumières de balcon.");
        }

        static Renderer BuildCastleShell(Transform p, Mats m, List<Light> torchLights)
        {
            Prim(PrimitiveType.Cube, "Mur_Fond", p, new Vector3(0f, 8.5f, 26f), Vector3.zero, new Vector3(50f, 20f, 1f), m.stoneDark);
            Transform cren = Empty("Creneaux", p, Vector3.zero, Vector3.zero).transform;
            for (int x = -24; x <= 24; x += 2)
                Prim(PrimitiveType.Cube, "Creneau", cren, new Vector3(x, 19f, 26f), Vector3.zero, Vector3.one, m.stoneDark);
            for (int i = 0; i < 2; i++)
            {
                Transform tower = Empty("Tour_" + (i + 1), p, new Vector3(TowerPos[i].x, 0f, TowerPos[i].z), Vector3.zero).transform;
                Prim(PrimitiveType.Cylinder, "Fut", tower, new Vector3(0f, 12.5f, 0f), Vector3.zero, new Vector3(7f, 14f, 7f), m.stoneDark);
                Prim(PrimitiveType.Cylinder, "Toit_1", tower, new Vector3(0f, 27f, 0f), Vector3.zero, new Vector3(6f, 0.5f, 6f), m.stoneDark);
                Prim(PrimitiveType.Cylinder, "Toit_2", tower, new Vector3(0f, 28f, 0f), Vector3.zero, new Vector3(4f, 0.5f, 4f), m.stoneDark);
                Prim(PrimitiveType.Cylinder, "Toit_3", tower, new Vector3(0f, 29f, 0f), Vector3.zero, new Vector3(2f, 0.5f, 2f), m.stoneDark);
            }
            Prim(PrimitiveType.Cube, "Mur_Gauche", p, new Vector3(-25f, 4.5f, 8f), Vector3.zero, new Vector3(1f, 12f, 35f), m.stoneDark);
            Prim(PrimitiveType.Cube, "Mur_Droit", p, new Vector3(25f, 4.5f, 8f), Vector3.zero, new Vector3(1f, 12f, 35f), m.stoneDark);
            float[] bannerX = { -16f, -8f, 8f, 16f };
            for (int i = 0; i < bannerX.Length; i++)
                Prim(PrimitiveType.Cube, "Banniere_" + (i + 1), p, new Vector3(bannerX[i], 11f, 25.4f), Vector3.zero, new Vector3(2f, 12f, 0.1f), i % 2 == 0 ? m.red : m.blue);

            // Lune : échelle 0 au départ, EnvironmentMood la fait grandir à partir de rave = 0.5
            GameObject moon = NoCollider(Prim(PrimitiveType.Sphere, "Lune", p, MoonPos, Vector3.zero, Vector3.zero, m.moon));

            int ti = 1;
            foreach (float z in new[] { 0f, 10f, 20f })
            {
                Torch(p, "Torch_" + ti++, new Vector3(-24.2f, 3f, z), m.wood, m.torch, torchLights);
                Torch(p, "Torch_" + ti++, new Vector3(24.2f, 3f, z), m.wood, m.torch, torchLights);
            }
            foreach (float x in new[] { -18f, -6f, 6f, 18f })
                Torch(p, "Torch_" + ti++, new Vector3(x, 4f, 25.2f), m.wood, m.torch, torchLights);
            Log("CastleShell : mur du fond + créneaux, 2 tours, murs latéraux, 4 bannières, 10 torches murales, lune (échelle 0), positions locales = monde v2.");
            return moon.GetComponent<Renderer>();
        }

        static void BuildPupitre(Material wood)
        {
            var pup = new GameObject("Pupitre");
            Transform t = pup.transform;
            Prim(PrimitiveType.Cylinder, "Pied", t, new Vector3(0f, 0.5f, 0f), Vector3.zero, new Vector3(0.06f, 0.5f, 0.06f), wood);
            Prim(PrimitiveType.Cylinder, "Socle", t, new Vector3(0f, 0.015f, 0f), Vector3.zero, new Vector3(0.4f, 0.015f, 0.4f), wood);
            Vector3 plateauEuler = new Vector3(PlateauTiltX, 0f, 0f);
            Prim(PrimitiveType.Cube, "Plateau", t, PlateauLocalPos, plateauEuler, new Vector3(0.5f, 0.02f, 0.35f), wood);
            Vector3 anchorPos = PlateauLocalPos + Quaternion.Euler(plateauEuler) * new Vector3(0f, 0.02f, 0f);
            Empty("PanelAnchor", t, anchorPos, plateauEuler);
            SavePrefab(pup, PrefabDir + "/Pupitre.prefab");
            pup.transform.position = PupitrePos;
        }

        public static Vector3 SoclePosition()
        {
            Quaternion tilt = Quaternion.Euler(PlateauTiltX, 0f, 0f);
            Vector3 surface = PupitrePos + PlateauLocalPos + tilt * new Vector3(0.2f, 0.01f, -0.12f);
            float lift = 0.03f * Mathf.Tan(Mathf.Abs(PlateauTiltX) * Mathf.Deg2Rad);
            return surface + Vector3.up * lift;
        }

        static GameObject BuildSocleBaguette(Material metal, Material cyan)
        {
            var soc = new GameObject("SocleBaguette");
            Transform t = soc.transform;
            Prim(PrimitiveType.Cylinder, "Base", t, new Vector3(0f, 0.01f, 0f), Vector3.zero, new Vector3(0.06f, 0.01f, 0.06f), metal);
            Prim(PrimitiveType.Cylinder, "Anneau", t, new Vector3(0f, 0.01f, 0f), Vector3.zero, new Vector3(0.07f, 0.0025f, 0.07f), cyan);
            Empty("RestPoint", t, new Vector3(0f, 0.03f, 0f), new Vector3(-20f, 0f, 0f));
            SavePrefab(soc, PrefabDir + "/SocleBaguette.prefab");
            t.position = SoclePosition();
            return soc;
        }

        /// <summary>Baguette : prefab jamais modifié s'il existe ; instance de scène conservée (XR Grab de test), replacée sur RestPoint, enfant Attach conservé.</summary>
        static void BuildBaguette(Scene scene, Material chrome, Material metal, Material cyan, Transform restPoint)
        {
            string path = PrefabDir + "/Baguette.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject instance = FindRoot(scene, "Baguette");
            if (prefab == null)
            {
                var bag = new GameObject("Baguette");
                Transform t = bag.transform;
                NoCollider(Prim(PrimitiveType.Capsule, "Corps", t, new Vector3(0f, 0f, 0.125f), new Vector3(90f, 0f, 0f), new Vector3(0.012f, 0.125f, 0.012f), chrome));
                NoCollider(Prim(PrimitiveType.Cylinder, "Poignee", t, new Vector3(0f, 0f, 0.03f), new Vector3(90f, 0f, 0f), new Vector3(0.02f, 0.03f, 0.02f), metal));
                NoCollider(Prim(PrimitiveType.Sphere, "Pointe", t, new Vector3(0f, 0f, 0.25f), Vector3.zero, Vector3.one * 0.015f, cyan));
                Empty("Tip", t, new Vector3(0f, 0f, 0.25f), Vector3.zero);
                CapsuleCollider cc = bag.AddComponent<CapsuleCollider>();
                cc.direction = 2;
                cc.radius = 0.01f;
                cc.height = 0.27f;
                cc.center = new Vector3(0f, 0f, 0.125f);
                Rigidbody rb = bag.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                SavePrefab(bag, path);
                instance = bag;
            }
            else
            {
                Log("Baguette.prefab existant conservé tel quel.");
                if (instance == null)
                {
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    Log("Instance Baguette recréée depuis le prefab.");
                }
                else
                {
                    Log("Instance Baguette existante conservée (composants de test préservés).");
                }
            }
            instance.transform.SetPositionAndRotation(restPoint.position, restPoint.rotation);
            EnsureBaguetteAttach(instance);
        }

        static void EnsureBaguetteAttach(GameObject bag)
        {
            Transform attach = bag.transform.Find("Attach");
            if (attach == null)
            {
                attach = Empty("Attach", bag.transform, BaguetteAttachLocalPos, BaguetteAttachLocalEuler).transform;
                Log("Enfant Attach créé sur l'instance Baguette : localPos " + attach.localPosition.ToString("F3") + " localEuler " + attach.localEulerAngles.ToString("F1"));
            }
            else
            {
                Log("Enfant Attach déjà présent, valeurs conservées : localPos " + attach.localPosition.ToString("F3") + " localEuler " + attach.localEulerAngles.ToString("F1"));
            }
            foreach (Component comp in bag.GetComponents<Component>())
            {
                if (comp == null || comp.GetType().Name != "XRGrabInteractable") continue;
                var so = new SerializedObject(comp);
                SerializedProperty prop = so.FindProperty("m_AttachTransform");
                if (prop != null && prop.objectReferenceValue != attach)
                {
                    prop.objectReferenceValue = attach;
                    so.ApplyModifiedProperties();
                    Log("XRGrabInteractable.attachTransform = Attach (instance de scène).");
                }
            }
        }

        static void BuildSection(string name, float[] xs, Material bodyMat, Material skin, Material wood, Material metal, AnimatorController ctrl, Transform anchor)
        {
            var root = new GameObject(name);
            float minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < xs.Length; i++)
            {
                minX = Mathf.Min(minX, xs[i]);
                maxX = Mathf.Max(maxX, xs[i]);
                GameObject mgo = Empty("Musician_" + (i + 1), root.transform, new Vector3(xs[i], 0f, 0f), Vector3.zero);
                Transform mt = mgo.transform;
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
                    default:
                        Prim(PrimitiveType.Cylinder, "Instrument", mt, new Vector3(0f, 0.75f, 0.5f), Vector3.zero, new Vector3(0.5f, 0.15f, 0.5f), wood);
                        break;
                }
                Animator a = mgo.AddComponent<Animator>();
                a.runtimeAnimatorController = ctrl;
                a.applyRootMotion = false;
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            BoxCollider bc = root.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            float halfBody = 0.25f, margin = 1f;
            bc.center = new Vector3((minX + maxX) * 0.5f, 1f, 0f);
            bc.size = new Vector3((maxX - minX) + 2f * (halfBody + margin), 2f, 2f * (halfBody + margin));
            SavePrefab(root, InstrDir + "/" + name + ".prefab");
            root.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            Log(name + " : " + xs.Length + " musiciens, collider de section " + bc.size.ToString("F2") + ", instance sur " + anchor.name);
        }

        static void BuildSpeakers(Material metal, Material magenta, AnimatorController ctrl, Transform anchor)
        {
            var root = new GameObject("Speakers");
            for (int i = 0; i < SpeakerX.Length; i++)
            {
                GameObject s = Empty("Speaker_" + (i + 1), root.transform, new Vector3(SpeakerX[i], SpeakerRestY, 0f), Vector3.zero);
                Prim(PrimitiveType.Cube, "Box", s.transform, Vector3.zero, Vector3.zero, new Vector3(2.5f, 3f, 2.5f), metal);
                Prim(PrimitiveType.Cylinder, "Cone", s.transform, new Vector3(0f, 0f, -1.28f), new Vector3(90f, 0f, 0f), new Vector3(1.8f, 0.03f, 1.8f), magenta);
            }
            Animator a = root.AddComponent<Animator>();
            a.runtimeAnimatorController = ctrl;
            a.applyRootMotion = false;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            SavePrefab(root, PrefabDir + "/Speakers.prefab");
            root.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
        }

        static void BuildStageLights(Material metal)
        {
            var root = new GameObject("StageLights");
            Vector3[] pos =
            {
                new Vector3(-21.5f, 14f, 22.5f), new Vector3(-21.5f, 20f, 22.5f),
                new Vector3(21.5f, 14f, 22.5f), new Vector3(21.5f, 20f, 22.5f),
                new Vector3(-10f, 16f, 25f), new Vector3(10f, 16f, 25f),
            };
            for (int i = 0; i < pos.Length; i++)
            {
                GameObject g = Empty("Spot_" + (i + 1), root.transform, pos[i], Vector3.zero);
                g.transform.LookAt(StageLightTarget);
                Light l = g.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = i % 2 == 0 ? new Color(1f, 0f, 1f) : new Color(0f, 1f, 1f);
                l.intensity = 20f;
                l.spotAngle = 35f;
                l.range = 40f;
                l.shadows = LightShadows.None;
                l.enabled = false;
                Prim(PrimitiveType.Cube, "Projecteur", g.transform, Vector3.zero, Vector3.zero, Vector3.one * 0.3f, metal);
            }
            SavePrefab(root, PrefabDir + "/StageLights.prefab");
        }

        static void BuildLasers(Material cyan)
        {
            var root = new GameObject("Lasers");
            Vector3[] starts = { new Vector3(-24f, 26.5f, 25f), new Vector3(-24f, 26.5f, 25f), new Vector3(24f, 26.5f, 25f), new Vector3(24f, 26.5f, 25f) };
            Vector3[] targets = { new Vector3(-4f, 1f, 10f), new Vector3(4f, 1f, 10f), new Vector3(-4f, 1f, 10f), new Vector3(4f, 1f, 10f) };
            for (int i = 0; i < starts.Length; i++)
            {
                Vector3 dir = (targets[i] - starts[i]).normalized;
                GameObject g = Prim(PrimitiveType.Cube, "Laser_" + (i + 1), root.transform, starts[i] + dir * 20f, Vector3.zero, new Vector3(0.03f, 0.03f, 40f), cyan);
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

        // ------------------------------------------------------------------ DebugPanel + EventSystem

        static RectTransform Rect(GameObject go) { return go.GetComponent<RectTransform>(); }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static Image UIImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        static TextMeshProUGUI UIText(string name, Transform parent, string text, TMP_FontAsset font, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Prefab DebugPanel : canvas World Space 0.6 × 0.45 m (caché au départ), 9 boutons câblés en persistant, cercle de progression du geste.</summary>
        static void BuildDebugPanel()
        {
            var root = new GameObject("DebugPanel");
            MaestroDebugPanel dp = root.AddComponent<MaestroDebugPanel>();
            Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Sprite bgSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;

            // ----- Panneau -----
            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(root.transform, false);
            Canvas canvas = panel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform prt = Rect(panel);
            prt.sizeDelta = new Vector2(600f, 450f);
            prt.localScale = Vector3.one * 0.001f;
            panel.AddComponent<GraphicRaycaster>();
            panel.AddComponent<TrackedDeviceGraphicRaycaster>();
            Image bg = UIImage("Fond", panel.transform, bgSprite, new Color(0.05f, 0.05f, 0.08f, 0.85f));
            bg.type = Image.Type.Sliced;
            Stretch(bg.rectTransform);
            TextMeshProUGUI title = UIText("Titre", panel.transform, "MAESTRO DEBUG", font, 20f);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -2f);
            title.rectTransform.sizeDelta = new Vector2(0f, 26f);
            title.color = new Color(0.7f, 0.8f, 1f);

            const float btnW = 275f, btnH = 80f, gap = 4f;
            for (int i = 0; i < DebugButtons.Length; i++)
            {
                bool last = i == DebugButtons.Length - 1;
                int col = i % 2, row = i / 2;
                float w = last ? btnW * 2f + gap : btnW;
                float x = last ? 0f : (col == 0 ? -(btnW * 0.5f + gap * 0.5f) : (btnW * 0.5f + gap * 0.5f));
                float y = -(30f + btnH * 0.5f + row * (btnH + gap));
                var b = new GameObject("Btn_" + DebugButtonMethods[i].Substring(2), typeof(RectTransform));
                b.transform.SetParent(panel.transform, false);
                RectTransform brt = Rect(b);
                brt.anchorMin = new Vector2(0.5f, 1f);
                brt.anchorMax = new Vector2(0.5f, 1f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.anchoredPosition = new Vector2(x, y);
                brt.sizeDelta = new Vector2(w, btnH);
                Image img = b.AddComponent<Image>();
                img.sprite = uiSprite;
                img.type = Image.Type.Sliced;
                img.color = new Color(0.18f, 0.22f, 0.35f, 1f);
                Button btn = b.AddComponent<Button>();
                btn.targetGraphic = img;
                ColorBlock cb = btn.colors;
                cb.highlightedColor = new Color(0.30f, 0.40f, 0.60f, 1f);
                cb.pressedColor = new Color(0.60f, 0.80f, 1.00f, 1f);
                cb.selectedColor = cb.normalColor;
                btn.colors = cb;
                TextMeshProUGUI label = UIText("Label", b.transform, DebugButtons[i], font, 28f);
                Stretch(label.rectTransform);
                label.enableAutoSizing = true;
                label.fontSizeMin = 14f;
                label.fontSizeMax = 30f;
                var call = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), dp, DebugButtonMethods[i]);
                UnityEventTools.AddPersistentListener(btn.onClick, call);
            }

            // ----- Cercle de progression du geste (6 cm, radial) -----
            var prog = new GameObject("Progress", typeof(RectTransform));
            prog.transform.SetParent(root.transform, false);
            Canvas pc = prog.AddComponent<Canvas>();
            pc.renderMode = RenderMode.WorldSpace;
            RectTransform pgrt = Rect(prog);
            pgrt.sizeDelta = new Vector2(100f, 100f);
            pgrt.localScale = Vector3.one * 0.0006f;
            Image ring = UIImage("Fond", prog.transform, knob, new Color(0f, 0f, 0f, 0.5f));
            Stretch(ring.rectTransform);
            Image fill = UIImage("Cercle", prog.transform, knob, new Color(0.2f, 0.9f, 1f, 1f));
            Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;
            prog.SetActive(false);

            dp.panel = panel;
            dp.progressRoot = prog;
            dp.progressImage = fill;
            panel.SetActive(false);
            SavePrefab(root, SystemsDir + "/DebugPanel.prefab");
            Log("DebugPanel : canvas World Space 0.6 × 0.45 m, " + DebugButtons.Length + " boutons (0.08 m) câblés sur MaestroDebugPanel, Tracked Device Graphic Raycaster, panneau caché au départ.");
        }

        static void EnsureEventSystem(Scene scene)
        {
            GameObject es = FindRoot(scene, EventSystemName);
            if (es == null) es = new GameObject(EventSystemName);
            if (es.GetComponent<EventSystem>() == null) es.AddComponent<EventSystem>();
            XRUIInputModule mod = es.GetComponent<XRUIInputModule>();
            if (mod == null)
            {
                mod = es.AddComponent<XRUIInputModule>();
                mod.enableXRInput = true;
                mod.enableMouseInput = true;
                mod.enableTouchInput = false;
                Log(EventSystemName + " créé avec XR UI Input Module (XR + souris).");
            }
            else
            {
                Log(EventSystemName + " existant avec XR UI Input Module conservé.");
            }
        }
    }
}
