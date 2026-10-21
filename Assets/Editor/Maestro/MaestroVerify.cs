using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Maestro.Core;
using Maestro.Environment;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Maestro.EditorTools
{
    /// <summary>
    /// Vérifications du greybox v3 : assets, instances, ancres, coques (Stage / OperaShell / CastleShell), EnvironmentMood,
    /// Animator EnvironmentController, Volume, DebugPanel + EventSystem, rig XR, Attach, visibilité en mode opéra et en mode rave.
    /// </summary>
    public static class MaestroVerify
    {
        [MenuItem("Tools/Maestro/Verify Greybox")]
        public static void VerifyFromMenu()
        {
            MaestroRunner.Execute("verify");
        }

        public static void Run(StringBuilder sb)
        {
            int problems = 0;
            Scene scene = SceneManager.GetActiveScene();
            sb.AppendLine("Scène active : " + scene.path);
            if (scene.path != MaestroGreybox.ScenePath) { sb.AppendLine("PROBLEME: la scène active n'est pas Art_Sandbox"); problems++; }

            // ---------------------------------------------------------------- 1. Assets
            int prefabCount = 0;
            foreach (string n in MaestroGreybox.RootPrefabs) { problems += CheckPrefab(sb, MaestroGreybox.PrefabDir + "/" + n + ".prefab", n); prefabCount++; }
            foreach (string n in MaestroGreybox.SectionPrefabs) { problems += CheckPrefab(sb, MaestroGreybox.InstrDir + "/" + n + ".prefab", n); prefabCount++; }
            foreach (string n in MaestroGreybox.SystemPrefabs) { problems += CheckPrefab(sb, MaestroGreybox.SystemsDir + "/" + n + ".prefab", n); prefabCount++; }
            sb.AppendLine("Prefabs attendus : " + prefabCount + (prefabCount == 13 ? " (OK)" : " (attendu 13)"));
            if (prefabCount != 13) problems++;

            foreach (string n in MaestroGreybox.LitMaterialNames)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(MaestroGreybox.MatDir + "/" + n + ".mat");
                if (m == null) { sb.AppendLine("PROBLEME: matériau manquant " + n); problems++; }
                else if (m.shader == null || m.shader.name != "Universal Render Pipeline/Lit") { sb.AppendLine("PROBLEME: shader de " + n + " = " + (m.shader != null ? m.shader.name : "null")); problems++; }
            }
            var plaster = AssetDatabase.LoadAssetAtPath<Material>(MaestroGreybox.MatDir + "/M_Plaster.mat");
            if (plaster != null && Mathf.RoundToInt(plaster.GetFloat("_Cull")) != (int)CullMode.Off) { sb.AppendLine("PROBLEME: M_Plaster n'est pas en Render Face = Both"); problems++; }
            var skyMat = AssetDatabase.LoadAssetAtPath<Material>(MaestroGreybox.MatDir + "/" + MaestroGreybox.SkyboxMaterialName + ".mat");
            if (skyMat == null || skyMat.shader == null || skyMat.shader.name != "Skybox/Procedural") { sb.AppendLine("PROBLEME: " + MaestroGreybox.SkyboxMaterialName + " manquant ou mauvais shader"); problems++; }
            sb.AppendLine("Matériaux vérifiés : " + MaestroGreybox.LitMaterialNames.Length + " URP/Lit (M_Plaster double face) + " + MaestroGreybox.SkyboxMaterialName + " (Skybox/Procedural)");

            foreach (string n in new[] { "MusicianController", "SpeakersController", "EnvironmentController" })
            {
                if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(MaestroGreybox.AnimDir + "/" + n + ".controller") == null) { sb.AppendLine("PROBLEME: contrôleur manquant " + n); problems++; }
                else sb.AppendLine("Contrôleur OK : " + n);
            }
            // nom → (loop, nb de courbes, durée attendue ou -1)
            var clipSpec = new Dictionary<string, Tuple<bool, int, float>>
            {
                { "Anim_Idle", Tuple.Create(true, 12, -1f) }, { "Anim_Play", Tuple.Create(true, 12, -1f) }, { "Anim_Headbang", Tuple.Create(true, 12, -1f) },
                { "Anim_SpeakersDrop", Tuple.Create(false, 3 * MaestroGreybox.SpeakerX.Length, -1f) }, { "Anim_SpeakersHidden", Tuple.Create(false, 3 * MaestroGreybox.SpeakerX.Length, -1f) },
                { "Anim_Opera", Tuple.Create(false, 16, -1f) }, { "Anim_ToRave", Tuple.Create(false, 16, MaestroGreybox.TransitionDuration) }, { "Anim_ToOpera", Tuple.Create(false, 16, MaestroGreybox.TransitionDuration) },
            };
            foreach (var kv in clipSpec)
            {
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(MaestroGreybox.AnimDir + "/" + kv.Key + ".anim");
                if (c == null) { sb.AppendLine("PROBLEME: clip manquant " + kv.Key); problems++; continue; }
                int curves = AnimationUtility.GetCurveBindings(c).Length;
                bool ok = c.isLooping == kv.Value.Item1 && curves == kv.Value.Item2 && (kv.Value.Item3 < 0f || Mathf.Abs(c.length - kv.Value.Item3) < 0.05f);
                sb.AppendLine((ok ? "Clip OK : " : "PROBLEME clip : ") + kv.Key + " (" + c.length.ToString("F2") + " s, loop=" + c.isLooping + ", courbes=" + curves + ", attendu " + kv.Value.Item2 + ")");
                if (!ok) problems++;
            }
            var envCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(MaestroGreybox.AnimDir + "/EnvironmentController.controller");
            if (envCtrl != null)
            {
                bool hasRave = false, hasReset = false, hasNight = false;
                foreach (var p in envCtrl.parameters)
                {
                    if (p.name == "Rave" && p.type == AnimatorControllerParameterType.Trigger) hasRave = true;
                    if (p.name == "Reset" && p.type == AnimatorControllerParameterType.Trigger) hasReset = true;
                    if (p.name == "Night") hasNight = true;
                }
                var states = new HashSet<string>();
                foreach (var s in envCtrl.layers[0].stateMachine.states) states.Add(s.state.name);
                AnimatorState def = envCtrl.layers[0].stateMachine.defaultState;
                bool ok = hasRave && hasReset && !hasNight && states.Contains("Opera") && states.Contains("Rave") && states.Contains("ToOpera") && def != null && def.name == "Opera";
                sb.AppendLine((ok ? "EnvironmentController OK : " : "PROBLEME EnvironmentController : ") + "triggers Rave=" + hasRave + " Reset=" + hasReset + " Night=" + hasNight + ", états " + string.Join("/", states) + ", défaut " + (def != null ? def.name : "?"));
                if (!ok) problems++;
            }
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(MaestroGreybox.MatDir + "/" + MaestroGreybox.VolumeProfileName + ".asset");
            Bloom bloom = null;
            if (profile == null || !profile.TryGet(out bloom)) { sb.AppendLine("PROBLEME: profil " + MaestroGreybox.VolumeProfileName + " manquant ou sans Bloom"); problems++; }
            else
            {
                bool ok = bloom.active && bloom.intensity.overrideState && Mathf.Abs(bloom.intensity.value - 0.8f) < 0.001f && Mathf.Abs(bloom.threshold.value - 1f) < 0.001f && Mathf.Abs(bloom.scatter.value - 0.6f) < 0.001f && bloom.highQualityFiltering.value == false;
                sb.AppendLine((ok ? "Profil OK : " : "PROBLEME profil : ") + MaestroGreybox.VolumeProfileName + " Bloom intensité " + bloom.intensity.value + " seuil " + bloom.threshold.value + " scatter " + bloom.scatter.value + " HQ=" + bloom.highQualityFiltering.value);
                if (!ok) problems++;
            }

            // ---------------------------------------------------------------- 2. Instances
            GameObject[] roots = scene.GetRootGameObjects();
            foreach (string n in MaestroGreybox.RootPrefabs) problems += CheckInstance(sb, roots, n, MaestroGreybox.PrefabDir + "/" + n + ".prefab");
            foreach (string n in MaestroGreybox.SectionPrefabs) problems += CheckInstance(sb, roots, n, MaestroGreybox.InstrDir + "/" + n + ".prefab");
            foreach (string n in MaestroGreybox.SystemPrefabs) problems += CheckInstance(sb, roots, n, MaestroGreybox.SystemsDir + "/" + n + ".prefab");

            // ---------------------------------------------------------------- 3. Rig XR et Main Camera
            GameObject rig = Find(roots, MaestroGreybox.RigName);
            if (rig == null || !rig.activeSelf) { sb.AppendLine("PROBLEME: " + MaestroGreybox.RigName + " absent ou inactif"); problems++; }
            else sb.AppendLine(MaestroGreybox.RigName + " présent et actif @ " + rig.transform.position.ToString("F2"));
            GameObject mainCam = Find(roots, MaestroGreybox.MainCameraName);
            if (mainCam == null) sb.AppendLine("Main Camera absente de la racine (toléré)");
            else if (mainCam.activeSelf) { sb.AppendLine("PROBLEME: Main Camera réactivée"); problems++; }
            else sb.AppendLine("Main Camera toujours désactivée");
            int activeCams = 0;
            foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (cam.gameObject.scene == scene && cam.enabled && cam.gameObject.activeInHierarchy) activeCams++;
            sb.AppendLine("Caméras actives dans la scène : " + activeCams + (activeCams == 1 ? " (OK)" : " (attendu 1)"));
            if (activeCams != 1) problems++;

            // ---------------------------------------------------------------- 4. Environment
            GameObject env = Find(roots, "Environment");
            var anchors = new Dictionary<string, Transform>();
            Transform operaShell = null, castleShell = null;
            if (env != null)
            {
                foreach (string a in MaestroGreybox.AnchorNames)
                {
                    Transform at = env.transform.Find(a);
                    if (at == null) { sb.AppendLine("PROBLEME: ancre manquante " + a); problems++; continue; }
                    anchors[a] = at;
                    Vector3 expected = MaestroGreybox.AnchorPos[a];
                    float expectedY = a == "Anchor_Speakers" ? 0f : 180f;
                    bool ok = (at.position - expected).magnitude < 0.001f && Mathf.Abs(Mathf.DeltaAngle(at.eulerAngles.y, expectedY)) < 0.1f && at.parent == env.transform;
                    sb.AppendLine((ok ? "Ancre OK : " : "PROBLEME ancre : ") + a + " @ " + at.position.ToString("F2") + " rotY=" + at.eulerAngles.y.ToString("F0"));
                    if (!ok) problems++;
                }
                foreach (string g in MaestroGreybox.EnvironmentGroups)
                {
                    Transform gt = env.transform.Find(g);
                    if (gt == null) { sb.AppendLine("PROBLEME: groupe manquant Environment/" + g); problems++; continue; }
                    int staticCount = 0, total = 0;
                    foreach (Transform c in gt.GetComponentsInChildren<Transform>(true)) { total++; if (c.gameObject.isStatic) staticCount++; }
                    string note;
                    bool ok;
                    switch (g)
                    {
                        case "Stage": ok = staticCount == total; note = staticCount + "/" + total + " objets Static"; break;
                        case "OperaShell": operaShell = gt; ok = gt.localPosition.sqrMagnitude < 1e-6f && staticCount == 0; note = "local " + gt.localPosition.ToString("F2") + " (attendu 0), " + staticCount + " Static (attendu 0), " + total + " objets"; break;
                        case "CastleShell": castleShell = gt; ok = (gt.localPosition - new Vector3(0f, MaestroGreybox.CastleShellHiddenY, 0f)).magnitude < 1e-4f && staticCount == 0; note = "local " + gt.localPosition.ToString("F2") + " (attendu (0, " + MaestroGreybox.CastleShellHiddenY + ", 0)), " + staticCount + " Static (attendu 0)"; break;
                        default:
                            {
                                Volume v = gt.GetComponent<Volume>();
                                ok = v != null && v.isGlobal && v.sharedProfile != null && v.sharedProfile.name == MaestroGreybox.VolumeProfileName;
                                note = v == null ? "pas de Volume" : "Volume global=" + v.isGlobal + " profil=" + (v.sharedProfile != null ? v.sharedProfile.name : "null");
                                break;
                            }
                    }
                    sb.AppendLine((ok ? "Groupe OK : " : "PROBLEME groupe : ") + g + " : " + note);
                    if (!ok) problems++;
                }
                Animator envAnim = env.GetComponent<Animator>();
                bool animOk = envAnim != null && envAnim.runtimeAnimatorController != null && envAnim.runtimeAnimatorController.name == "EnvironmentController";
                sb.AppendLine((animOk ? "Environment Animator OK : " : "PROBLEME Environment Animator : ") + (envAnim != null && envAnim.runtimeAnimatorController != null ? envAnim.runtimeAnimatorController.name : "absent"));
                if (!animOk) problems++;

                EnvironmentMood mood = env.GetComponent<EnvironmentMood>();
                if (mood == null) { sb.AppendLine("PROBLEME: EnvironmentMood absent de la racine Environment"); problems++; }
                else
                {
                    int refs = 0, nulls = 0;
                    var so = new SerializedObject(mood);
                    SerializedProperty it = so.GetIterator();
                    bool enter = true;
                    while (it.NextVisible(enter))
                    {
                        enter = true;
                        if (it.propertyType != SerializedPropertyType.ObjectReference || it.name == "m_Script") continue;
                        refs++;
                        if (it.objectReferenceValue == null) { nulls++; sb.AppendLine("  PROBLEME: référence nulle EnvironmentMood." + it.propertyPath); }
                    }
                    int opera = mood.operaLights != null ? mood.operaLights.Length : 0, torches = mood.torchLights != null ? mood.torchLights.Length : 0, braziers = mood.brazierLights != null ? mood.brazierLights.Length : 0;
                    bool bases = mood.operaLightBases != null && mood.operaLightBases.Length == opera;
                    bool moonOk = mood.moonRenderer != null && mood.moonRenderer.transform.localScale.sqrMagnitude < 1e-8f && mood.moonRenderer.transform.IsChildOf(castleShell != null ? castleShell : env.transform);
                    bool intensOk = torches > 0 && braziers > 0;
                    foreach (var l in mood.torchLights ?? new Light[0]) if (l == null || Mathf.Abs(l.intensity - MaestroGreybox.TorchBase) > 0.001f) intensOk = false;
                    foreach (var l in mood.brazierLights ?? new Light[0]) if (l == null || Mathf.Abs(l.intensity - MaestroGreybox.BrazierBase) > 0.001f) intensOk = false;
                    bool ok = nulls == 0 && opera == MaestroGreybox.OperaLightCount && torches == 12 && braziers == 4 && bases && moonOk && intensOk && Mathf.Abs(mood.rave) < 1e-6f;
                    sb.AppendLine((ok ? "EnvironmentMood OK : " : "PROBLEME EnvironmentMood : ") + refs + " références (" + nulls + " nulle(s)), rave=" + mood.rave + ", lumières opéra " + opera + " (attendu " + MaestroGreybox.OperaLightCount + " : lustre + 6 balcons + 2 avant-scène), torches " + torches + " à " + MaestroGreybox.TorchBase + " (attendu 12), braseros " + braziers + " à " + MaestroGreybox.BrazierBase + " (attendu 4), lune échelle 0 sous CastleShell=" + moonOk + ", Directional=" + (mood.directionalLight != null ? mood.directionalLight.name : "null"));
                    if (!ok) problems++;
                }
            }

            // ---------------------------------------------------------------- 5. Sections, caissons, lights, lasers, baguette, pupitre
            int musicianTotal = 0;
            foreach (string n in MaestroGreybox.SectionPrefabs)
            {
                GameObject s = Find(roots, n);
                if (s == null) continue;
                Animator[] anims = s.GetComponentsInChildren<Animator>(true);
                int bad = 0;
                foreach (var a in anims) if (a.runtimeAnimatorController == null || a.runtimeAnimatorController.name != "MusicianController") bad++;
                int expected = MaestroGreybox.SectionX[n].Length;
                musicianTotal += anims.Length;
                bool countOk = anims.Length == expected && bad == 0;
                sb.AppendLine((countOk ? "Section OK : " : "PROBLEME section : ") + n + " : " + anims.Length + " musicien(s) (attendu " + expected + "), Animator MusicianController " + (bad > 0 ? "manquant sur " + bad : "OK"));
                if (!countOk) problems++;

                Transform anchor;
                if (anchors.TryGetValue("Anchor_" + n, out anchor))
                {
                    bool onAnchor = (s.transform.position - anchor.position).magnitude < 0.001f && Quaternion.Angle(s.transform.rotation, anchor.rotation) < 0.1f;
                    sb.AppendLine((onAnchor ? "  posée sur " : "  PROBLEME: pas sur ") + anchor.name + " (" + s.transform.position.ToString("F2") + ")");
                    if (!onAnchor) problems++;
                }
                var bc = s.GetComponent<BoxCollider>();
                if (bc == null || !bc.isTrigger) { sb.AppendLine("  PROBLEME: BoxCollider trigger de section manquant"); problems++; }
                else
                {
                    int outside = 0;
                    foreach (Transform m in s.transform)
                    {
                        Transform body = m.Find("Body");
                        if (body == null) continue;
                        Vector3 local = bc.transform.InverseTransformPoint(body.position) - bc.center;
                        bool inside = Mathf.Abs(local.x) <= bc.size.x * 0.5f && Mathf.Abs(local.y) <= bc.size.y * 0.5f && Mathf.Abs(local.z) <= bc.size.z * 0.5f;
                        if (!inside) outside++;
                    }
                    sb.AppendLine("  BoxCollider trigger " + bc.size.ToString("F2") + " centre " + bc.center.ToString("F2") + (outside == 0 ? ", tous les musiciens couverts" : ", PROBLEME: " + outside + " musicien(s) hors collider"));
                    if (outside > 0) problems++;
                }
            }
            sb.AppendLine("Musiciens au total : " + musicianTotal + (musicianTotal == 17 ? " (OK)" : " (attendu 17)"));
            if (musicianTotal != 17) problems++;

            GameObject spk = Find(roots, "Speakers");
            if (spk != null)
            {
                var a = spk.GetComponent<Animator>();
                bool ok = a != null && a.runtimeAnimatorController != null && a.runtimeAnimatorController.name == "SpeakersController" && spk.transform.childCount == MaestroGreybox.SpeakerX.Length;
                for (int i = 0; i < MaestroGreybox.SpeakerX.Length && i < spk.transform.childCount; i++)
                    if (Mathf.Abs(spk.transform.GetChild(i).localPosition.x - MaestroGreybox.SpeakerX[i]) > 0.001f) ok = false;
                Transform anchor;
                bool onAnchor = anchors.TryGetValue("Anchor_Speakers", out anchor) && (spk.transform.position - anchor.position).magnitude < 0.001f;
                var xs = new List<string>();
                foreach (Transform c in spk.transform) xs.Add(c.localPosition.x.ToString("F0"));
                sb.AppendLine((ok && onAnchor ? "Speakers OK : " : "PROBLEME Speakers : ") + spk.transform.childCount + " caissons x = " + string.Join("/", xs) + " (attendu -9/-6/6/9), Animator " + (ok ? "SpeakersController" : "incorrect") + (onAnchor ? ", sur Anchor_Speakers" : ", pas sur Anchor_Speakers"));
                if (!ok || !onAnchor) problems++;
            }
            GameObject sl = Find(roots, "StageLights");
            if (sl != null)
            {
                Light[] ls = sl.GetComponentsInChildren<Light>(true);
                int on = 0;
                foreach (var l in ls) if (l.enabled) on++;
                sb.AppendLine("StageLights : " + ls.Length + " spots (attendu " + MaestroGreybox.SpotCount + "), " + on + " allumé(s) (attendu 0)");
                if (ls.Length != MaestroGreybox.SpotCount || on != 0) problems++;
            }
            GameObject lz = Find(roots, "Lasers");
            if (lz != null)
            {
                int active = 0, total = 0;
                foreach (Transform c in lz.transform) { total++; if (c.gameObject.activeSelf) active++; }
                sb.AppendLine("Lasers : " + total + " faisceaux (attendu " + MaestroGreybox.LaserCount + "), " + active + " actif(s) (attendu 0)");
                if (total != MaestroGreybox.LaserCount || active != 0) problems++;
            }
            problems += CheckChild(sb, roots, "Baguette", "Tip");
            problems += CheckChild(sb, roots, "Baguette", "Attach");
            GameObject bag = Find(roots, "Baguette");
            if (bag != null && bag.transform.Find("Attach") != null)
            {
                Transform at = bag.transform.Find("Attach");
                bool intact = (at.localPosition - MaestroGreybox.BaguetteAttachLocalPos).magnitude < 1e-4f && Quaternion.Angle(at.localRotation, Quaternion.Euler(MaestroGreybox.BaguetteAttachLocalEuler)) < 0.1f;
                sb.AppendLine((intact ? "Attach intact : " : "PROBLEME Attach modifié : ") + "local " + at.localPosition.ToString("F3") + " / " + at.localEulerAngles.ToString("F0") + " (attendu " + MaestroGreybox.BaguetteAttachLocalPos.ToString("F3") + " / " + MaestroGreybox.BaguetteAttachLocalEuler.ToString("F0") + ")");
                if (!intact) problems++;
            }
            problems += CheckChild(sb, roots, "SocleBaguette", "RestPoint");
            problems += CheckChild(sb, roots, "Pupitre", "PanelAnchor");
            GameObject pup = Find(roots, "Pupitre");
            if (pup != null)
            {
                bool dressed = MaestroProps.Available("Pupitre") && pup.transform.Find("Model") != null;
                Transform plateau = pup.transform.Find("Plateau");
                bool plateauOk = dressed || (plateau != null && Mathf.Abs(plateau.localPosition.y - 1.1f) < 0.001f);
                bool pivotOk = (pup.transform.position - MaestroGreybox.PupitrePos).magnitude < 0.001f && plateauOk;
                sb.AppendLine((pivotOk ? "Pupitre OK : " : "PROBLEME Pupitre : ") + "pivot à la base, instance @ " + pup.transform.position.ToString("F2") + (dressed ? ", plateau = modèle Pupitre.fbx (1.10 m, 28°)" : ", plateau local y=" + (plateau != null ? plateau.localPosition.y.ToString("F2") : "?")));
                if (!pivotOk) problems++;
            }

            // ---------------------------------------------------------------- 6. DebugPanel + EventSystem
            problems += CheckDebugPanel(sb, roots);

            // ---------------------------------------------------------------- 6 bis. Habillage par les props Blender (v4)
            problems += CheckDressing(sb, roots);

            // ---------------------------------------------------------------- 7. Visibilité mode opéra (état sauvegardé)
            sb.AppendLine("--- Visibilité mode opéra (OperaShell à 0, CastleShell enterrée) ---");
            problems += CheckVisibility(sb, roots, false);

            // ---------------------------------------------------------------- 8. Visibilité mode rave (coques déplacées temporairement)
            if (operaShell != null && castleShell != null)
            {
                Vector3 o0 = operaShell.localPosition, c0 = castleShell.localPosition;
                try
                {
                    operaShell.localPosition = new Vector3(0f, MaestroGreybox.OperaShellRaveY, 0f);
                    castleShell.localPosition = Vector3.zero;
                    Physics.SyncTransforms();
                    sb.AppendLine("--- Visibilité mode rave (OperaShell à " + MaestroGreybox.OperaShellRaveY + ", CastleShell à 0, caissons posés) ---");
                    problems += CheckVisibility(sb, roots, true);
                }
                finally
                {
                    operaShell.localPosition = o0;
                    castleShell.localPosition = c0;
                    Physics.SyncTransforms();
                }
            }

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
            sb.AppendLine(rootName + "/" + childName + " OK @ " + c.position.ToString("F3") + " rot " + c.eulerAngles.ToString("F0") + " (local " + c.localPosition.ToString("F3") + " / " + c.localEulerAngles.ToString("F0") + ")");
            return 0;
        }

        static int CheckDebugPanel(StringBuilder sb, GameObject[] roots)
        {
            int problems = 0;
            GameObject go = Find(roots, "DebugPanel");
            if (go == null) { sb.AppendLine("PROBLEME: DebugPanel absent"); return 1; }
            MaestroDebugPanel dp = go.GetComponent<MaestroDebugPanel>();
            if (dp == null) { sb.AppendLine("PROBLEME: composant MaestroDebugPanel absent"); return 1; }
            if (dp.panel == null) { sb.AppendLine("PROBLEME: DebugPanel.panel non référencé"); return 1; }
            bool hidden = !dp.panel.activeSelf;
            Canvas canvas = dp.panel.GetComponent<Canvas>();
            RectTransform rt = dp.panel.GetComponent<RectTransform>();
            Vector2 worldSize = rt != null ? new Vector2(rt.sizeDelta.x * rt.localScale.x, rt.sizeDelta.y * rt.localScale.y) : Vector2.zero;
            bool canvasOk = canvas != null && canvas.renderMode == RenderMode.WorldSpace && Mathf.Abs(worldSize.x - 0.6f) < 0.01f && Mathf.Abs(worldSize.y - 0.45f) < 0.01f;
            bool raycaster = dp.panel.GetComponent<TrackedDeviceGraphicRaycaster>() != null;
            bool progress = dp.progressRoot != null && dp.progressImage != null && dp.progressImage.type == Image.Type.Filled && dp.progressImage.fillMethod == Image.FillMethod.Radial360;
            int tmp = dp.panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).Length;
            sb.AppendLine((hidden && canvasOk && raycaster && progress ? "DebugPanel OK : " : "PROBLEME DebugPanel : ") + "panneau caché=" + hidden + ", canvas World Space " + worldSize.ToString("F2") + " m, TrackedDeviceGraphicRaycaster=" + raycaster + ", cercle de progression radial=" + progress + ", textes TMP=" + tmp);
            if (!(hidden && canvasOk && raycaster && progress)) problems++;

            Button[] buttons = dp.panel.GetComponentsInChildren<Button>(true);
            var methods = new HashSet<string>();
            int wired = 0, tooSmall = 0;
            foreach (Button b in buttons)
            {
                int n = b.onClick.GetPersistentEventCount();
                bool ok = n == 1 && b.onClick.GetPersistentTarget(0) == dp;
                if (ok) { wired++; methods.Add(b.onClick.GetPersistentMethodName(0)); }
                RectTransform brt = b.GetComponent<RectTransform>();
                float h = brt.sizeDelta.y * brt.lossyScale.y;
                if (h < 0.08f - 1e-4f) tooSmall++;
            }
            var missing = new List<string>();
            foreach (string m in MaestroGreybox.DebugButtonMethods) if (!methods.Contains(m)) missing.Add(m);
            bool buttonsOk = buttons.Length == 9 && wired == 9 && missing.Count == 0 && tooSmall == 0;
            sb.AppendLine((buttonsOk ? "Boutons OK : " : "PROBLEME boutons : ") + buttons.Length + " boutons (attendu 9), " + wired + " câblés en persistant sur MaestroDebugPanel, méthodes manquantes : " + (missing.Count == 0 ? "aucune" : string.Join(", ", missing)) + ", " + tooSmall + " bouton(s) < 0.08 m");
            if (!buttonsOk) problems++;

            GameObject es = Find(roots, MaestroGreybox.EventSystemName);
            bool esOk = es != null && es.GetComponent<EventSystem>() != null && es.GetComponent<XRUIInputModule>() != null;
            sb.AppendLine((esOk ? "EventSystem OK : " : "PROBLEME EventSystem : ") + (es == null ? "absent" : "EventSystem=" + (es.GetComponent<EventSystem>() != null) + " XRUIInputModule=" + (es.GetComponent<XRUIInputModule>() != null)));
            if (!esOk) problems++;
            return problems;
        }

        // ---------------------------------------------------------------- habillage v4

        static int CheckModel(StringBuilder sb, Transform owner, string label, string[] removed, Func<Bounds, bool> dim, string dimLabel)
        {
            if (owner == null) { sb.AppendLine("PROBLEME: " + label + " introuvable"); return 1; }
            Transform model = owner.Find("Model");
            if (model == null) { sb.AppendLine("PROBLEME: " + label + " sans enfant Model"); return 1; }
            var left = new List<string>();
            foreach (string r in removed) if (owner.Find(r) != null) left.Add(r);
            bool selfPrimitive = removed.Length == 0 && owner.GetComponent<MeshRenderer>() != null;
            bool scaleOk = (model.lossyScale - Vector3.one).magnitude < 1e-3f;
            Bounds b = MaestroGreybox.ModelBounds(owner, model.gameObject);
            bool dimOk = dim == null || dim(b);
            bool ok = left.Count == 0 && dimOk && !selfPrimitive && scaleOk;
            sb.AppendLine((ok ? "Modèle OK : " : "PROBLEME modèle : ") + label + " : Model présent, bornes " + b.size.ToString("F3") + (dimOk ? " (" + dimLabel + ")" : " (ATTENDU " + dimLabel + ")")
                + (left.Count > 0 ? ", primitives restantes : " + string.Join(", ", left) : "") + (selfPrimitive ? ", MeshRenderer primitif toujours sur l'objet" : "") + (scaleOk ? "" : ", ÉCHELLE MONDE " + model.lossyScale.ToString("F2") + " (attendu 1)"));
            return ok ? 0 : 1;
        }

        static int CheckDressing(StringBuilder sb, GameObject[] roots)
        {
            int problems = 0;
            int available = 0;
            foreach (string p in MaestroProps.PropNames) if (MaestroProps.Available(p)) available++;
            sb.AppendLine("--- Habillage (props Blender) : " + available + "/" + MaestroProps.PropNames.Length + " modèles importés ---");
            if (available == 0) { sb.AppendLine("PROBLEME: aucun modèle importé, primitives conservées"); return 1; }
            int embeddedTotal = 0;
            foreach (string p in MaestroProps.PropNames)
            {
                string fbx = MaestroProps.FbxPath(p);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(fbx) == null) { sb.AppendLine("PROBLEME: FBX manquant " + fbx); problems++; continue; }
                foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(fbx)) if (o is Material) embeddedTotal++;
            }
            sb.AppendLine((embeddedTotal == 0 ? "FBX OK : " : "PROBLEME FBX : ") + "matériaux incorporés restants " + embeddedTotal + " (attendu 0, tous les slots remappés)");
            if (embeddedTotal > 0) problems++;

            GameObject bag = Find(roots, "Baguette");
            problems += CheckModel(sb, bag != null ? bag.transform : null, "Baguette", new[] { "Corps", "Poignee", "Pointe" }, b => Mathf.Abs(b.size.z - 0.25f) < 0.02f && b.max.z > 0.2f, "longueur ≈ 0.25 m vers +Z");
            if (bag != null)
            {
                Transform tip = bag.transform.Find("Tip");
                CapsuleCollider cc = bag.GetComponent<CapsuleCollider>();
                bool tipOk = tip != null && (tip.localPosition - new Vector3(0f, 0f, 0.25f)).magnitude < 1e-4f;
                bool ccOk = cc != null && cc.direction == 2 && Mathf.Abs(cc.height - 0.25f) < 0.02f;
                sb.AppendLine((tipOk && ccOk ? "Baguette OK : " : "PROBLEME Baguette : ") + "Tip local " + (tip != null ? tip.localPosition.ToString("F3") : "absent") + " (attendu (0, 0, 0.25)), CapsuleCollider " + (cc != null ? "hauteur " + cc.height.ToString("F3") + " rayon " + cc.radius.ToString("F3") + " centre " + cc.center.ToString("F3") : "absent"));
                if (!(tipOk && ccOk)) problems++;
            }

            GameObject socle = Find(roots, "SocleBaguette");
            problems += CheckModel(sb, socle != null ? socle.transform : null, "SocleBaguette", new[] { "Base", "Anneau" }, b => Mathf.Abs(b.size.x - 0.07f) < 0.01f && b.size.y < 0.03f, "Ø 7 cm, 2 cm de haut");
            if (socle != null)
            {
                Transform rp = socle.transform.Find("RestPoint");
                Vector3 expected = MaestroGreybox.SoclePosition() + new Vector3(0f, 0.03f, 0f);
                bool ok = rp != null && (rp.localPosition - new Vector3(0f, 0.03f, 0f)).magnitude < 1e-4f && (rp.position - expected).magnitude < 1e-3f;
                sb.AppendLine((ok ? "SocleBaguette OK : " : "PROBLEME SocleBaguette : ") + "RestPoint local " + (rp != null ? rp.localPosition.ToString("F3") : "absent") + ", monde " + (rp != null ? rp.position.ToString("F3") : "?") + " (attendu " + expected.ToString("F3") + ")");
                if (!ok) problems++;
            }

            GameObject pup = Find(roots, "Pupitre");
            problems += CheckModel(sb, pup != null ? pup.transform : null, "Pupitre", new[] { "Pied", "Socle", "Plateau" }, b => Mathf.Abs(b.size.y - 1.208f) < 0.03f, "hauteur ≈ 1.21 m, plateau à 1.10 m");
            if (pup != null)
            {
                Transform pa = pup.transform.Find("PanelAnchor");
                Vector3 expected = MaestroGreybox.PupitrePos + MaestroGreybox.PanelAnchorModelLocalPos;
                bool ok = pa != null && (pa.position - expected).magnitude < 1e-3f && Quaternion.Angle(pa.localRotation, Quaternion.Euler(MaestroGreybox.PanelAnchorModelLocalEuler)) < 0.05f;
                sb.AppendLine((ok ? "Pupitre OK : " : "PROBLEME Pupitre : ") + "PanelAnchor monde " + (pa != null ? pa.position.ToString("F4") + " rot " + pa.eulerAngles.ToString("F0") : "absent") + " (attendu " + expected.ToString("F4") + " rot " + MaestroGreybox.PanelAnchorModelLocalEuler.ToString("F0") + " = centre du plateau du modèle)");
                if (!ok) problems++;
            }

            GameObject spk = Find(roots, "Speakers");
            if (spk != null)
                foreach (Transform s in spk.transform)
                    if (s.name.StartsWith("Speaker_")) problems += CheckModel(sb, s, "Speakers/" + s.name, new[] { "Box", "Cone" }, b => Mathf.Abs(b.size.y - 3f) < 0.05f && b.min.y < -1.45f, "3 m de haut, base à -1.5");
            else { sb.AppendLine("PROBLEME: Speakers introuvable"); problems++; }

            foreach (string n in MaestroGreybox.SectionPrefabs)
            {
                GameObject s = Find(roots, n);
                if (s == null) continue;
                int withModel = 0, total = 0, primitive = 0, badScale = 0;
                foreach (Transform m in s.transform)
                {
                    Transform inst = m.Find("Instrument");
                    if (inst == null) continue;
                    total++;
                    Transform model = inst.Find("Model");
                    if (model != null) withModel++;
                    if (model != null && (model.lossyScale - Vector3.one).magnitude > 1e-3f) badScale++;
                    if (inst.GetComponent<MeshRenderer>() != null) primitive++;
                }
                bool ok = total > 0 && withModel == total && primitive == 0 && badScale == 0;
                sb.AppendLine((ok ? "Instruments OK : " : "PROBLEME instruments : ") + n + " : " + withModel + "/" + total + " Instrument avec Model (" + MaestroGreybox.InstrumentProp[n] + "), " + primitive + " primitive(s) restante(s), " + badScale + " échelle(s) ≠ 1");
                if (!ok) problems++;
            }

            GameObject env = Find(roots, "Environment");
            if (env != null)
            {
                Transform lustre = env.transform.Find("OperaShell/Lustre");
                var removedLustre = new List<string> { "Plateau", "Coeur" };
                for (int i = 1; i <= 12; i++) removedLustre.Add("Perle_" + i);
                problems += CheckModel(sb, lustre, "OperaShell/Lustre", removedLustre.ToArray(), b => Mathf.Abs(b.size.y - 2.53f) < 0.05f && b.max.y < 0.05f, "2.5 m de haut, pend sous le pivot (anneau d'accroche ≤ 5 cm au-dessus)");
                if (lustre != null)
                {
                    bool posOk = (lustre.localPosition - MaestroGreybox.LustrePos).magnitude < 1e-3f && lustre.Find("Chaine") != null && lustre.Find("Lustre_Light") != null;
                    sb.AppendLine((posOk ? "Lustre OK : " : "PROBLEME Lustre : ") + "point d'accroche local " + lustre.localPosition.ToString("F2") + " (attendu " + MaestroGreybox.LustrePos.ToString("F2") + "), chaîne conservée=" + (lustre.Find("Chaine") != null) + ", Lustre_Light local " + (lustre.Find("Lustre_Light") != null ? lustre.Find("Lustre_Light").localPosition.ToString("F2") : "absente"));
                    if (!posOk) problems++;
                }
                Transform castle = env.transform.Find("CastleShell");
                if (castle != null)
                {
                    int torches = 0, torchOk = 0;
                    foreach (Transform t in castle)
                    {
                        if (!t.name.StartsWith("Torch_")) continue;
                        torches++;
                        Transform tm = t.Find("Model");
                        if (tm != null && (tm.lossyScale - Vector3.one).magnitude < 1e-3f && t.Find("Manche") == null && t.Find("Flamme") == null && t.Find("Torch_Light") != null) torchOk++;
                    }
                    sb.AppendLine((torches == 10 && torchOk == 10 ? "Torches OK : " : "PROBLEME torches : ") + torchOk + "/" + torches + " torches murales avec Model, sans primitives, Torch_Light conservée (attendu 10)");
                    if (!(torches == 10 && torchOk == 10)) problems++;
                    int banners = 0, bannerOk = 0;
                    var colors = new List<string>();
                    for (int i = 1; i <= 4; i++)
                    {
                        Transform b = castle.Find("Banniere_" + i);
                        if (b == null) continue;
                        banners++;
                        Transform model = b.Find("Model");
                        Renderer r = model != null ? model.GetComponent<Renderer>() : null;
                        string velvet = "?";
                        if (r != null) foreach (Material m in r.sharedMaterials) if (m != null && m.name.StartsWith("M_Banniere_Velours")) velvet = m.name.Replace("M_Banniere_Velours", "");
                        colors.Add(velvet);
                        bool expectBlue = i % 2 == 0;
                        bool scaleOk = model != null && (model.lossyScale - Vector3.one).magnitude < 1e-3f;
                        bool heightOk = model != null && Mathf.Abs(MaestroGreybox.ModelBounds(b, model.gameObject).size.y - 6.1f) < 0.1f && Mathf.Abs(model.position.y - 17f) < 0.01f + Mathf.Abs(castle.localPosition.y);
                        if (model != null && b.GetComponent<MeshRenderer>() == null && velvet == (expectBlue ? "Bleu" : "Rouge") && scaleOk && heightOk) bannerOk++;
                        else if (model != null && (!scaleOk || !heightOk)) sb.AppendLine("  PROBLEME: Banniere_" + i + " échelle monde " + model.lossyScale.ToString("F2") + ", bornes " + MaestroGreybox.ModelBounds(b, model.gameObject).size.ToString("F2") + " (attendu échelle 1, 6.1 m de haut)");
                    }
                    sb.AppendLine((banners == 4 && bannerOk == 4 ? "Bannières OK : " : "PROBLEME bannières : ") + bannerOk + "/" + banners + " avec Model (échelle 1, 6.1 m, hampe à y = 17 local), sans cube, velours " + string.Join("/", colors) + " (attendu Rouge/Bleu/Rouge/Bleu)");
                    if (!(banners == 4 && bannerOk == 4)) problems++;
                }
            }

            problems += CheckModelMaterials(sb, roots);

            // Caméra du rig : Post Processing coché sur l'instance
            GameObject rig = Find(roots, MaestroGreybox.RigName);
            Transform camT = rig != null ? rig.transform.Find("Camera Offset/Main Camera") : null;
            Camera cam = camT != null ? camT.GetComponent<Camera>() : null;
            bool pp = cam != null && cam.GetUniversalAdditionalCameraData().renderPostProcessing;
            sb.AppendLine((pp ? "Rig OK : " : "PROBLEME rig : ") + "Post Processing " + (pp ? "coché" : "non coché") + " sur Camera Offset/Main Camera");
            if (!pp) problems++;

            // Coupole : Anim_ToRave finit à l'échelle 0, Anim_ToOpera repart de 0
            var toRave = AssetDatabase.LoadAssetAtPath<AnimationClip>(MaestroGreybox.AnimDir + "/Anim_ToRave.anim");
            var toOpera = AssetDatabase.LoadAssetAtPath<AnimationClip>(MaestroGreybox.AnimDir + "/Anim_ToOpera.anim");
            EditorCurveBinding domeScale = EditorCurveBinding.FloatCurve("OperaShell/Dome", typeof(Transform), "localScale.x");
            AnimationCurve cr = toRave != null ? AnimationUtility.GetEditorCurve(toRave, domeScale) : null;
            AnimationCurve co = toOpera != null ? AnimationUtility.GetEditorCurve(toOpera, domeScale) : null;
            bool domeOk = cr != null && co != null && cr.keys.Length > 0 && co.keys.Length > 0 && Mathf.Abs(cr.keys[cr.keys.Length - 1].value) < 1e-4f && Mathf.Abs(co.keys[0].value) < 1e-4f;
            sb.AppendLine((domeOk ? "Coupole OK : " : "PROBLEME coupole : ") + "Anim_ToRave finit à l'échelle " + (cr != null && cr.keys.Length > 0 ? cr.keys[cr.keys.Length - 1].value.ToString("F2") : "?") + ", Anim_ToOpera repart de " + (co != null && co.keys.Length > 0 ? co.keys[0].value.ToString("F2") : "?") + " (attendu 0 / 0)");
            if (!domeOk) problems++;
            return problems;
        }

        /// <summary>Tous les renderers sous un enfant « Model » : matériau non nul, shader URP/Lit, asset dans Materials/Props, _BaseMap présente quand le README en prévoit une.</summary>
        static int CheckModelMaterials(StringBuilder sb, GameObject[] roots)
        {
            var issues = new HashSet<string>();
            int renderers = 0, slots = 0;
            string[] scan = { "Baguette", "SocleBaguette", "Pupitre", "Speakers", "Strings", "Vent", "Brass", "Percution", "Environment" };
            foreach (string name in scan)
            {
                GameObject go = Find(roots, name);
                if (go == null) continue;
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "Model") continue;
                    foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                    {
                        renderers++;
                        foreach (Material m in r.sharedMaterials)
                        {
                            slots++;
                            string where = name + "/" + r.name;
                            if (m == null) { issues.Add(where + " : matériau nul"); continue; }
                            if (m.shader == null || m.shader.name != "Universal Render Pipeline/Lit") { issues.Add(where + " : " + m.name + " shader " + (m.shader != null ? m.shader.name : "null")); continue; }
                            string path = AssetDatabase.GetAssetPath(m);
                            if (!path.StartsWith(MaestroProps.PropMatDir)) { issues.Add(where + " : " + m.name + " hors de " + MaestroProps.PropMatDir + " (" + path + ")"); continue; }
                            PropMaterialSpec spec = MaestroProps.FindSpec(m.name);
                            if (spec == null) { issues.Add(where + " : " + m.name + " inconnu du README"); continue; }
                            if (spec.BaseColor && m.GetTexture("_BaseMap") == null) issues.Add(where + " : " + m.name + " sans _BaseMap");
                            if (spec.Normal && m.GetTexture("_BumpMap") == null) issues.Add(where + " : " + m.name + " sans _BumpMap");
                            if (spec.Emissive && !m.IsKeywordEnabled("_EMISSION")) issues.Add(where + " : " + m.name + " sans émission");
                        }
                    }
                }
            }
            foreach (string i in issues) sb.AppendLine("  PROBLEME: " + i);
            sb.AppendLine((issues.Count == 0 ? "Matériaux des modèles OK : " : "PROBLEME matériaux des modèles : ") + renderers + " renderers, " + slots + " slots (shader URP/Lit, dossier Props, _BaseMap / _BumpMap / émission selon le README), " + issues.Count + " problème(s)");
            return issues.Count;
        }

        struct Target
        {
            public string label;
            public Vector3 point;
            public Transform owner;
            public float stopBefore;
        }

        const float FovV = 65f;
        const float Aspect = 16f / 9f;

        static bool InFrustum(Vector3 eye, Vector3 point, float pitchUp)
        {
            float tanV = Mathf.Tan(FovV * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * Aspect;
            Vector3 d = Quaternion.Inverse(Quaternion.Euler(-pitchUp, 0f, 0f)) * (point - eye);
            return d.z > 0.01f && Mathf.Abs(d.x / d.z) <= tanH && Mathf.Abs(d.y / d.z) <= tanV;
        }

        /// <summary>Premier angle de redressement du regard (0 → 90° par pas de 5°) qui place le point dans le champ ; -1 si aucun.</summary>
        static float PitchToSee(Vector3 eye, Vector3 point)
        {
            for (float pitch = 0f; pitch <= 90.01f; pitch += 5f) if (InFrustum(eye, point, pitch)) return pitch;
            return -1f;
        }

        static string Blocker(Vector3 eye, Vector3 point, Transform owner, float stopBefore, out bool isEnvironment, out bool isOpera)
        {
            isEnvironment = false;
            isOpera = false;
            Vector3 d = point - eye;
            RaycastHit[] hits = Physics.RaycastAll(eye, d.normalized, Mathf.Max(0.01f, d.magnitude - stopBefore), ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            foreach (var h in hits)
            {
                if (owner != null && h.transform.IsChildOf(owner)) continue;
                isEnvironment = h.transform.root.name == "Environment";
                Transform p = h.transform;
                while (p != null) { if (p.name == "OperaShell") { isOpera = true; break; } p = p.parent; }
                return h.transform.root.name + "/" + h.transform.name;
            }
            return null;
        }

        static int CheckVisibility(StringBuilder sb, GameObject[] roots, bool raveMode)
        {
            int problems = 0, warnings = 0;
            Vector3 eye = new Vector3(0f, 1.6f, 0f);
            Physics.SyncTransforms();

            var targets = new List<Target>();
            foreach (string n in MaestroGreybox.SectionPrefabs)
            {
                var s = Find(roots, n);
                if (s == null) continue;
                foreach (Transform m in s.transform)
                {
                    var head = m.Find("Head");
                    if (head != null) targets.Add(new Target { label = n + "/" + m.name + "/Head", point = head.position, owner = m, stopBefore = 0.16f });
                }
            }
            sb.AppendLine("Visibilité depuis (0, 1.6, 0) regard +Z, FOV vertical " + FovV + "°, 16:9 (triggers ignorés) :");
            int visibleHeads = 0, headCount = 0;
            foreach (var tg in targets)
            {
                headCount++;
                bool inFrustum = InFrustum(eye, tg.point, 0f);
                bool isEnv, isOpera;
                string blocker = Blocker(eye, tg.point, tg.owner, tg.stopBefore, out isEnv, out isOpera);
                string state = inFrustum ? "dans le champ" : "HORS CHAMP";
                state += blocker == null ? ", ligne de vue libre" : (isEnv ? (isOpera ? ", MASQUÉ PAR L'OPÉRA " : ", MASQUÉ PAR LE DÉCOR ") : ", masqué (avertissement) par ") + blocker;
                sb.AppendLine("  " + tg.label + " @ " + tg.point.ToString("F2") + " : " + state);
                if (!inFrustum || isEnv) problems++;
                else if (blocker != null) warnings++;
                else visibleHeads++;
            }
            sb.AppendLine("  Têtes visibles sans obstacle : " + visibleHeads + "/" + headCount + (warnings > 0 ? " (" + warnings + " masquée(s) par un objet hors décor : avertissement)" : ""));

            GameObject env = Find(roots, "Environment");
            if (!raveMode)
            {
                if (env != null)
                {
                    Transform opera = env.transform.Find("OperaShell");
                    Transform lintel = opera != null ? opera.Find("Proscenium/Linteau") : null;
                    Transform lustre = opera != null ? opera.Find("Lustre") : null;
                    problems += LookUpCheck(sb, eye, "Proscenium/Linteau", lintel, 1.5f);
                    problems += LookUpCheck(sb, eye, "Lustre", lustre, 1.8f);
                }
            }
            else
            {
                Vector3 tower0 = MaestroGreybox.TowerPos[0];
                Vector3 tower1 = MaestroGreybox.TowerPos[1];
                bool t0 = InFrustum(eye, tower0, 0f), t1 = InFrustum(eye, tower1, 0f);
                sb.AppendLine("  Tour_1 @ " + tower0.ToString("F0") + " : " + (t0 ? "dans le champ" : "HORS CHAMP") + " ; Tour_2 @ " + tower1.ToString("F0") + " : " + (t1 ? "dans le champ" : "HORS CHAMP"));
                if (!t0 || !t1) problems++;

                Vector3 dm = MaestroGreybox.MoonPos - eye;
                float moonElev = Mathf.Atan2(dm.y, new Vector2(dm.x, dm.z).magnitude) * Mathf.Rad2Deg;
                float wallTopElev = Mathf.Atan2(19.5f - eye.y, 26f) * Mathf.Rad2Deg;
                bool isEnv, isOpera;
                string moonBlocker = Blocker(eye, MaestroGreybox.MoonPos, null, 21f, out isEnv, out isOpera);
                bool moonOk = moonBlocker == null && moonElev > wallTopElev;
                sb.AppendLine("  Lune @ " + MaestroGreybox.MoonPos.ToString("F0") + " (échelle " + MaestroGreybox.MoonScale + " à rave = 1) : élévation " + moonElev.ToString("F1") + "° (bord du mur du fond " + wallTopElev.ToString("F1") + "°), " + (moonBlocker == null ? "ligne de vue libre" : "MASQUÉE par " + moonBlocker) + (moonOk ? " → visible en levant les yeux" : " → PROBLEME"));
                if (!moonOk) problems++;
            }
            return problems;
        }

        static int LookUpCheck(StringBuilder sb, Vector3 eye, string label, Transform target, float stopBefore)
        {
            if (target == null) { sb.AppendLine("  PROBLEME: " + label + " introuvable"); return 1; }
            Vector3 p = target.position;
            float pitch = PitchToSee(eye, p);
            bool isEnv, isOpera;
            string blocker = Blocker(eye, p, target, stopBefore, out isEnv, out isOpera);
            Vector3 d = p - eye;
            float elev = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            bool ok = pitch >= 0f && blocker == null;
            sb.AppendLine("  " + label + " @ " + p.ToString("F1") + " : élévation " + elev.ToString("F0") + "°, " + (pitch >= 0f ? "dans le champ en levant les yeux de " + pitch.ToString("F0") + "°" : "JAMAIS DANS LE CHAMP") + (blocker == null ? ", ligne de vue libre" : ", MASQUÉ par " + blocker) + (ok ? "" : " → PROBLEME"));
            return ok ? 0 : 1;
        }
    }

    /// <summary>Captures mode édition (état opéra) : vue joueur (0, 1.6, 0) regard +Z et vue large (-25, 14, -20) vers l'orchestre.</summary>
    public static class MaestroScreenshot
    {
        [MenuItem("Tools/Maestro/Capture Greybox Screenshot")]
        public static void CaptureFromMenu()
        {
            MaestroRunner.Execute("screenshot");
        }

        public static string Dir { get { return Path.Combine(MaestroRunner.ProjectRoot, "Docs", "screenshots"); } }

        public static void Capture(StringBuilder sb)
        {
            CaptureView(sb, new Vector3(0f, 1.6f, 0f), Quaternion.identity, 65f, Path.Combine(Dir, "v4-opera.png"));
            // Gros plan de la baguette sur son socle : caméra à (0.3, 1.4, 0.6) regardant le socle
            Vector3 socle = MaestroGreybox.SoclePosition();
            GameObject socleGo = MaestroGreybox.FindRoot(SceneManager.GetActiveScene(), "SocleBaguette");
            if (socleGo != null) socle = socleGo.transform.position;
            Vector3 cam = new Vector3(0.3f, 1.4f, 0.6f);
            CaptureView(sb, cam, Quaternion.LookRotation(socle + Vector3.up * 0.03f - cam, Vector3.up), 40f, Path.Combine(Dir, "v4-baguette.png"));
        }

        public static void CaptureView(StringBuilder sb, Vector3 position, Quaternion rotation, float fov, string outPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            var go = new GameObject("Maestro_TempCamera");
            go.hideFlags = HideFlags.HideAndDontSave;
            Camera cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(position, rotation);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.Skybox;
            try { cam.GetUniversalAdditionalCameraData().renderPostProcessing = true; } catch (Exception) { }
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
                sb.AppendLine("Capture écrite : " + outPath + " (" + w + "x" + h + ", depuis " + position.ToString("F1") + ", luminosité moyenne " + avg.ToString("F1") + "/255)");
            }
            finally
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
