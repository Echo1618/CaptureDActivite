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

            // ---------------------------------------------------------------- 0. Rendu URP (v8) : asset du Quest = niveau de qualité Mobile (défaut Android)
            var mobileRp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            if (mobileRp == null) { sb.AppendLine("PROBLEME: Assets/Settings/Mobile_RPAsset.asset introuvable"); problems++; }
            else
            {
                bool ok = mobileRp.additionalLightsRenderingMode == LightRenderingMode.PerPixel && mobileRp.maxAdditionalLightsCount >= 4 && !mobileRp.supportsAdditionalLightShadows && mobileRp.supportsHDR;
                sb.AppendLine((ok ? "URP OK : " : "PROBLEME URP : ") + "Mobile_RPAsset : Additional Lights " + mobileRp.additionalLightsRenderingMode + ", Per Object Limit " + mobileRp.maxAdditionalLightsCount + ", Additional Light Shadows " + mobileRp.supportsAdditionalLightShadows + ", HDR " + mobileRp.supportsHDR + " (attendu Per Pixel, ≥ 4, off, on)");
                if (!ok) problems++;
                var active = UniversalRenderPipeline.asset;
                sb.AppendLine("URP actif dans l'éditeur : " + (active != null ? active.name + " (Additional Lights " + active.additionalLightsRenderingMode + ", limite " + active.maxAdditionalLightsCount + ", ombres additionnelles " + active.supportsAdditionalLightShadows + ", HDR " + active.supportsHDR + ")" : "aucun") + " ; niveau de qualité courant : " + QualitySettings.names[QualitySettings.GetQualityLevel()]);
            }

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
            // v8 : capsules des musiciens éclaircies (× 1.15 plafonné) et Smoothness 0.35
            foreach (var kv in MaestroGreybox.MusicianBaseColors)
            {
                var mm = AssetDatabase.LoadAssetAtPath<Material>(MaestroGreybox.MatDir + "/" + kv.Key + ".mat");
                if (mm == null) continue;
                bool ok = Mathf.Abs(mm.GetFloat("_Smoothness") - MaestroGreybox.MusicianSmoothness) < 1e-3f && ColorClose(mm.GetColor("_BaseColor"), kv.Value);
                sb.AppendLine((ok ? "Matériau musicien OK : " : "PROBLEME matériau musicien : ") + kv.Key + " base " + mm.GetColor("_BaseColor").ToString("F3") + " smoothness " + mm.GetFloat("_Smoothness").ToString("F2") + " (attendu " + kv.Value.ToString("F3") + " / " + MaestroGreybox.MusicianSmoothness + ")");
                if (!ok) problems++;
            }
            // v10 : mot-clé _EMISSION activé (émission noire, flags Realtime) sur les matériaux des capsules et de la peau, pour la surbrillance par MaterialPropertyBlock
            int emOk = 0;
            var emBad = new List<string>();
            foreach (string mn in MaestroGreybox.HighlightMaterials)
            {
                var em = AssetDatabase.LoadAssetAtPath<Material>(MaestroGreybox.MatDir + "/" + mn + ".mat");
                bool ok2 = em != null && em.IsKeywordEnabled("_EMISSION") && em.GetColor("_EmissionColor").maxColorComponent < 1e-4f && (em.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive) != 0;
                if (ok2) emOk++; else emBad.Add(mn);
            }
            sb.AppendLine((emOk == MaestroGreybox.HighlightMaterials.Length ? "Émission OK : " : "PROBLEME émission : ") + emOk + "/" + MaestroGreybox.HighlightMaterials.Length + " matériaux avec _EMISSION activé, émission noire et flags Realtime (" + string.Join(", ", MaestroGreybox.HighlightMaterials) + ")" + (emBad.Count > 0 ? " ; en défaut : " + string.Join(", ", emBad) : ""));
            if (emOk != MaestroGreybox.HighlightMaterials.Length) problems++;
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
                bool hasLevel = false;
                foreach (var p in envCtrl.parameters) if (p.name == MaestroGreybox.LevelParameter && p.type == AnimatorControllerParameterType.Float) hasLevel = true;
                problems += CheckEnvironmentTransitions(sb, envCtrl.layers[0].stateMachine);
                bool ok = hasRave && hasReset && hasLevel && !hasNight && states.Contains("Opera") && states.Contains("Rave") && states.Contains("ToOpera") && def != null && def.name == "Opera";
                sb.AppendLine((ok ? "EnvironmentController OK : " : "PROBLEME EnvironmentController : ") + "triggers Rave=" + hasRave + " Reset=" + hasReset + " Level(float)=" + hasLevel + " Night=" + hasNight + ", états " + string.Join("/", states) + ", défaut " + (def != null ? def.name : "?"));
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
                        case "OperaShell":
                            {
                                operaShell = gt;
                                // Le groupe bouge au Rave : aucun objet ne doit être en Batching Static ni ContributeGI (les autres flags Static du théâtre sont tolérés)
                                int batching = 0;
                                foreach (Transform c in gt.GetComponentsInChildren<Transform>(true))
                                    if ((GameObjectUtility.GetStaticEditorFlags(c.gameObject) & (StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI)) != 0) batching++;
                                ok = gt.localPosition.sqrMagnitude < 1e-6f && batching == 0;
                                note = "local " + gt.localPosition.ToString("F2") + " (attendu 0), " + batching + " objet(s) en Batching Static / ContributeGI (attendu 0), " + staticCount + " avec d'autres flags Static, " + total + " objets";
                                break;
                            }
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
                    sb.AppendLine((ok ? "EnvironmentMood OK : " : "PROBLEME EnvironmentMood : ") + refs + " références (" + nulls + " nulle(s)), rave=" + mood.rave + ", lumières opéra " + opera + " (attendu " + MaestroGreybox.OperaLightCount + " : lustre + 6 balcons + 2 avant-scène + 5 poursuites + fill), torches " + torches + " à " + MaestroGreybox.TorchBase + " (attendu 12), braseros " + braziers + " à " + MaestroGreybox.BrazierBase + " (attendu 4), lune échelle 0 sous CastleShell=" + moonOk + ", Directional=" + (mood.directionalLight != null ? mood.directionalLight.name : "null"));
                    if (!ok) problems++;
                    // v9 : réflexions d'environnement plates (cubemap uniforme = ambiant opéra), plus de cubemap de skybox
                    var cm = AssetDatabase.LoadAssetAtPath<Cubemap>(MaestroGreybox.MatDir + "/" + MaestroGreybox.FlatReflectionName + ".cubemap");
                    Color cmColor = cm != null ? cm.GetPixel(CubemapFace.PositiveY, 0, 0).gamma : Color.black;
                    bool reflOk = cm != null && cm.width == MaestroGreybox.FlatReflectionSize && RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom && RenderSettings.customReflectionTexture == cm && mood.flatReflection == cm && ColorClose(cmColor, MaestroGreybox.AmbientOpera);
                    sb.AppendLine((reflOk ? "Réflexions OK : " : "PROBLEME réflexions : ") + "mode " + RenderSettings.defaultReflectionMode + ", custom=" + (RenderSettings.customReflectionTexture != null ? RenderSettings.customReflectionTexture.name : "null") + ", asset " + MaestroGreybox.FlatReflectionName + (cm != null ? " " + cm.width + " px couleur " + cmColor.ToString("F2") : " absent") + ", EnvironmentMood.flatReflection=" + (mood.flatReflection != null) + " (attendu Custom, cubemap uniforme = ambiant opéra " + MaestroGreybox.AmbientOpera.ToString("F2") + ")");
                    if (!reflOk) problems++;
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
                Vector2[] layout = MaestroGreybox.SectionLayout[n];
                int expected = layout.Length;
                musicianTotal += anims.Length;
                bool countOk = anims.Length == expected && bad == 0;
                sb.AppendLine((countOk ? "Section OK : " : "PROBLEME section : ") + n + " : " + anims.Length + " musicien(s) (attendu " + expected + "), Animator MusicianController " + (bad > 0 ? "manquant sur " + bad : "OK"));
                if (!countOk) problems++;
                // v10 : SectionHighlight sur la racine de la section, couleur = base du matériau, réglages du build
                SectionHighlight hl = s.GetComponent<SectionHighlight>();
                Color secColor = MaestroGreybox.MusicianBaseColors.ContainsKey("M_" + n) ? MaestroGreybox.MusicianBaseColors["M_" + n] : Color.white;
                bool hlOk = hl != null && ColorClose(hl.sectionColor, secColor) && Mathf.Abs(hl.softIntensity - MaestroGreybox.HighlightSoft) < 1e-3f && Mathf.Abs(hl.strongIntensity - MaestroGreybox.HighlightStrong) < 1e-3f
                    && Mathf.Abs(hl.pulseAmplitude - MaestroGreybox.HighlightPulseAmplitude) < 1e-3f && Mathf.Abs(hl.pulseHz - MaestroGreybox.HighlightPulseHz) < 1e-3f && Mathf.Abs(hl.fadeSeconds - MaestroGreybox.HighlightFade) < 1e-3f;
                sb.AppendLine((hlOk ? "  SectionHighlight OK : " : "  PROBLEME SectionHighlight : ") + (hl != null ? "couleur " + hl.sectionColor.ToString("F3") + ", doux " + hl.softIntensity + ", fort " + hl.strongIntensity + " ± " + hl.pulseAmplitude + " à " + hl.pulseHz + " Hz, fondu " + hl.fadeSeconds + " s" : "absent") + " (attendu couleur du matériau " + secColor.ToString("F3") + ", " + MaestroGreybox.HighlightSoft + " / " + MaestroGreybox.HighlightStrong + " ± " + MaestroGreybox.HighlightPulseAmplitude + " / " + MaestroGreybox.HighlightPulseHz + " Hz / " + MaestroGreybox.HighlightFade + " s)");
                if (!hlOk) problems++;
                int placed = 0;
                for (int i = 0; i < layout.Length; i++)
                {
                    Transform mt = s.transform.Find("Musician_" + (i + 1));
                    if (mt != null && (mt.localPosition - new Vector3(layout[i].x, 0f, layout[i].y)).magnitude < 1e-3f) placed++;
                }
                sb.AppendLine((placed == layout.Length ? "  positions OK : " : "  PROBLEME positions : ") + placed + "/" + layout.Length + " Musician_N aux positions locales du contrat v8 (x " + layout[0].x + " … " + layout[layout.Length - 1].x + ", z " + layout[0].y + " … " + layout[layout.Length - 1].y + ")");
                if (placed != layout.Length) problems++;

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
                    float minMargin = float.MaxValue;
                    foreach (Transform m in s.transform)
                    {
                        Transform body = m.Find("Body");
                        if (body == null) continue;
                        Vector3 local = bc.transform.InverseTransformPoint(body.position) - bc.center;
                        bool inside = Mathf.Abs(local.x) <= bc.size.x * 0.5f && Mathf.Abs(local.y) <= bc.size.y * 0.5f && Mathf.Abs(local.z) <= bc.size.z * 0.5f;
                        if (!inside) outside++;
                        // v8 : marge entre la capsule (rayon 0.25) et les faces x / z du collider, attendue ≥ 1 m
                        minMargin = Mathf.Min(minMargin, Mathf.Min(bc.size.x * 0.5f - Mathf.Abs(local.x), bc.size.z * 0.5f - Mathf.Abs(local.z)) - MaestroGreybox.MusicianBodyRadius);
                    }
                    bool marginOk = outside == 0 && minMargin >= MaestroGreybox.SectionColliderMargin - 1e-3f;
                    sb.AppendLine("  BoxCollider trigger " + bc.size.ToString("F2") + " centre " + bc.center.ToString("F2") + (marginOk ? ", tous les corps couverts, marge minimale " + minMargin.ToString("F2") + " m (attendu ≥ " + MaestroGreybox.SectionColliderMargin + ")" : ", PROBLEME: " + outside + " musicien(s) hors collider, marge minimale " + minMargin.ToString("F2") + " m (attendu ≥ " + MaestroGreybox.SectionColliderMargin + ")"));
                    if (!marginOk) problems++;
                }
            }
            sb.AppendLine("Musiciens au total : " + musicianTotal + (musicianTotal == MaestroGreybox.MusicianCount ? " (OK)" : " (attendu " + MaestroGreybox.MusicianCount + ")"));
            if (musicianTotal != MaestroGreybox.MusicianCount) problems++;
            problems += CheckTiers(sb, roots);

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
            problems += CheckRaveSequence(sb, roots);
            problems += CheckOrchestraApi(sb);
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

            // ---------------------------------------------------------------- 6 ter + 7. Théâtre (v5), visibilité mode opéra et estimation de rendu,
            // avec des MeshColliders temporaires sur le théâtre (le FBX est importé sans colliders)
            List<MeshCollider> temp = AddTheatreColliders(roots);
            try
            {
                problems += CheckTheatre(sb, roots);
                problems += CheckPoursuites(sb, roots);
                problems += CheckSpeakersLanding(sb, roots);
                sb.AppendLine("--- Visibilité mode opéra (OperaShell à 0, CastleShell enterrée) ---");
                problems += CheckVisibility(sb, roots, false);
                EstimateRendering(sb, "mode opéra", 0, 0, null);
            }
            finally
            {
                RemoveColliders(temp);
            }

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
                    // v11 : estimation en party (néons et lasers activés temporairement ; particules et spots comptés à part)
                    var toggled = new List<GameObject>();
                    Transform neonGroup = castleShell.Find("Neons");
                    if (neonGroup != null) foreach (Transform c in neonGroup) if (!c.gameObject.activeSelf) { c.gameObject.SetActive(true); toggled.Add(c.gameObject); }
                    GameObject lzParty = Find(roots, "Lasers");
                    if (lzParty != null) foreach (Transform c in lzParty.transform) if (!c.gameObject.activeSelf) { c.gameObject.SetActive(true); toggled.Add(c.gameObject); }
                    try
                    {
                        EstimateRendering(sb, "mode rave + party (néons et lasers actifs)", MaestroGreybox.NotesParticlesBudget * 2, 4, "particules de notes comptées à part : " + MaestroGreybox.NotesParticlesBudget + " billboards max = " + (MaestroGreybox.NotesParticlesBudget * 2) + " triangles et 4 batches ; " + MaestroGreybox.SpotCount + " spots StageLights per-pixel en plus (limite 4 par objet)");
                    }
                    finally { foreach (GameObject g in toggled) g.SetActive(false); }
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
            MaestroArtDebugPanel dp = go.GetComponent<MaestroArtDebugPanel>();
            if (dp == null) { sb.AppendLine("PROBLEME: composant MaestroArtDebugPanel absent"); return 1; }
            if (dp.panel == null) { sb.AppendLine("PROBLEME: DebugPanel.panel non référencé"); return 1; }
            bool hidden = !dp.panel.activeSelf;
            Canvas canvas = dp.panel.GetComponent<Canvas>();
            RectTransform rt = dp.panel.GetComponent<RectTransform>();
            Vector2 worldSize = rt != null ? new Vector2(rt.sizeDelta.x * rt.localScale.x, rt.sizeDelta.y * rt.localScale.y) : Vector2.zero;
            bool canvasOk = canvas != null && canvas.renderMode == RenderMode.WorldSpace && Mathf.Abs(worldSize.x - MaestroGreybox.DebugPanelWidthPx * 0.001f) < 0.01f && Mathf.Abs(worldSize.y - MaestroGreybox.DebugPanelHeightPx * 0.001f) < 0.01f;
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
            int expectedButtons = MaestroGreybox.DebugButtons.Length;
            bool buttonsOk = buttons.Length == expectedButtons && wired == expectedButtons && missing.Count == 0 && tooSmall == 0;
            sb.AppendLine((buttonsOk ? "Boutons OK : " : "PROBLEME boutons : ") + buttons.Length + " boutons (attendu " + expectedButtons + ", dont « Poursuites on/off » → OnTogglePoursuites), " + wired + " câblés en persistant sur MaestroArtDebugPanel, méthodes manquantes : " + (missing.Count == 0 ? "aucune" : string.Join(", ", missing)) + ", " + tooSmall + " bouton(s) < 0.08 m");
            if (!buttonsOk) problems++;
            bool labelOk = dp.raveLabel != null && dp.raveLabel.transform.parent != null && dp.raveLabel.transform.parent.name == "Btn_Rave" && dp.raveLabel.text == MaestroGreybox.DebugButtons[0];
            sb.AppendLine((labelOk ? "Bouton Rave OK : " : "PROBLEME bouton Rave : ") + "raveLabel " + (dp.raveLabel != null && dp.raveLabel.transform.parent != null ? "→ " + dp.raveLabel.transform.parent.name + " (texte « " + dp.raveLabel.text + " »)" : "non assigné") + ", bascule « " + dp.raveLabelOpera + " » / « " + dp.raveLabelRave + " » selon l'état (attendu libellé de Btn_Rave, texte « " + MaestroGreybox.DebugButtons[0] + " » hors Play Mode)");
            if (!labelOk) problems++;
            bool hlLabelOk = dp.highlightLabel != null && dp.highlightLabel.transform.parent != null && dp.highlightLabel.transform.parent.name == "Btn_HighlightTest" && dp.highlightLabel.text == dp.highlightLabelIdle;
            sb.AppendLine((hlLabelOk ? "Bouton Highlight test OK : " : "PROBLEME bouton Highlight test : ") + "highlightLabel " + (dp.highlightLabel != null && dp.highlightLabel.transform.parent != null ? "→ " + dp.highlightLabel.transform.parent.name + " (texte « " + dp.highlightLabel.text + " »)" : "non assigné") + ", cycle " + string.Join(" → ", MaestroArtDebugPanel.HighlightCycleLabels) + " (attendu libellé de Btn_HighlightTest, texte « " + dp.highlightLabelIdle + " » hors Play Mode)");
            if (!hlLabelOk) problems++;

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
            EditorCurveBinding domeScale = EditorCurveBinding.FloatCurve(MaestroGreybox.DomePath, typeof(Transform), "localScale.x");
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
                            if (!path.StartsWith(MaestroProps.PropMatDir) && !path.StartsWith(MaestroProps.TheatreMatDir)) { issues.Add(where + " : " + m.name + " hors de " + MaestroProps.PropMatDir + " / " + MaestroProps.TheatreMatDir + " (" + path + ")"); continue; }
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

        // ---------------------------------------------------------------- théâtre v5

        static Transform TheatreModel(GameObject[] roots)
        {
            GameObject env = Find(roots, "Environment");
            return env != null ? env.transform.Find("OperaShell/Model") : null;
        }

        /// <summary>MeshColliders temporaires sur les objets du théâtre (hors fauteuils) pour les tests de ligne de vue.</summary>
        static List<MeshCollider> AddTheatreColliders(GameObject[] roots)
        {
            var list = new List<MeshCollider>();
            Transform model = TheatreModel(roots);
            if (model == null) return list;
            foreach (Transform c in model.GetComponentsInChildren<Transform>(true))
            {
                if (c.name == "Seats" || c.name.StartsWith("Seat.")) continue;
                MeshFilter mf = c.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || c.GetComponent<Collider>() != null) continue;
                MeshCollider mc = c.gameObject.AddComponent<MeshCollider>();
                mc.hideFlags = HideFlags.DontSave;
                list.Add(mc);
            }
            Physics.SyncTransforms();
            return list;
        }

        static void RemoveColliders(List<MeshCollider> list)
        {
            foreach (MeshCollider mc in list) if (mc != null) UnityEngine.Object.DestroyImmediate(mc);
            Physics.SyncTransforms();
        }

        /// <summary>Premier impact sur le théâtre le long du rayon (triggers ignorés), null sinon.</summary>
        static RaycastHit? RayTheatre(Transform model, Vector3 origin, Vector3 dir, float dist)
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, dir.normalized, dist, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            foreach (RaycastHit h in hits) if (h.transform.IsChildOf(model)) return h;
            return null;
        }

        static int CheckTheatre(StringBuilder sb, GameObject[] roots)
        {
            if (!MaestroGreybox.TheatreAvailable) { sb.AppendLine("Théâtre : Theatre.fbx non importé, coque opéra greybox conservée"); return 0; }
            int problems = 0;
            sb.AppendLine("--- Théâtre à l'italienne (Theatre.fbx) ---");
            GameObject env = Find(roots, "Environment");
            Transform opera = env != null ? env.transform.Find("OperaShell") : null;
            if (opera == null) { sb.AppendLine("PROBLEME: OperaShell introuvable"); return 1; }
            Transform model = opera.Find("Model");
            if (model == null) { sb.AppendLine("PROBLEME: OperaShell sans enfant Model (théâtre non posé)"); return 1; }

            bool poseOk = model.localPosition.magnitude < 1e-4f && Quaternion.Angle(model.localRotation, Quaternion.Euler(MaestroGreybox.TheatreModelEuler)) < 0.05f && (model.lossyScale - Vector3.one).magnitude < 1e-3f;
            var missing = new List<string>();
            foreach (string o in MaestroProps.TheatreObjects) if (model.Find(o) == null) missing.Add(o);
            Transform seats = model.Find("Seats");
            int seatCount = seats != null ? seats.childCount : 0;
            Transform dome = model.Find("Ceiling/Dome");
            bool objectsOk = missing.Count == 0 && seatCount == MaestroProps.TheatreSeatCount && dome != null;
            sb.AppendLine((poseOk && objectsOk ? "Theatre OK : " : "PROBLEME Theatre : ") + "Model local " + model.localPosition.ToString("F2") + " rot " + model.localEulerAngles.ToString("F0") + " (attendu 0 / (0, 180, 0)), objets " + (MaestroProps.TheatreObjects.Length - missing.Count) + "/" + MaestroProps.TheatreObjects.Length + (missing.Count > 0 ? " manquants : " + string.Join(", ", missing) : "") + ", fauteuils " + seatCount + " (attendu " + MaestroProps.TheatreSeatCount + "), Dome sous Ceiling=" + (dome != null));
            if (!(poseOk && objectsOk)) problems++;

            var left = new List<string>();
            foreach (string g in MaestroGreybox.OperaGreyboxToRemove) if (opera.Find(g) != null) left.Add(g);
            sb.AppendLine((left.Count == 0 ? "Coque greybox OK : " : "PROBLEME coque greybox : ") + (left.Count == 0 ? "sol, murs, balcons, sphère, corniche, cadre de scène primitifs supprimés" : "restent " + string.Join(", ", left)));
            if (left.Count > 0) problems++;

            // Coupole animée : les clips visent le Dome du modèle (fresque), échelle 1 → 0, +80 m
            Transform animated = env.transform.Find(MaestroGreybox.DomePath);
            Renderer dr = animated != null ? animated.GetComponent<Renderer>() : null;
            bool fresco = dr != null && Array.Exists(dr.sharedMaterials, m => m != null && m.name == "M_Theatre_Fresco");
            var toRave = AssetDatabase.LoadAssetAtPath<AnimationClip>(MaestroGreybox.AnimDir + "/Anim_ToRave.anim");
            AnimationCurve sc = toRave != null ? AnimationUtility.GetEditorCurve(toRave, EditorCurveBinding.FloatCurve(MaestroGreybox.DomePath, typeof(Transform), "localScale.x")) : null;
            AnimationCurve py = toRave != null ? AnimationUtility.GetEditorCurve(toRave, EditorCurveBinding.FloatCurve(MaestroGreybox.DomePath, typeof(Transform), "localPosition.y")) : null;
            bool domeOk = animated != null && animated == dome && fresco && sc != null && py != null && Mathf.Abs(sc.keys[sc.keys.Length - 1].value) < 1e-4f && Mathf.Abs(py.keys[py.keys.Length - 1].value - (MaestroGreybox.DomeRestPos.y + MaestroGreybox.DomeRise)) < 1e-3f && dome.localScale == Vector3.one;
            sb.AppendLine((domeOk ? "Coupole animée OK : " : "PROBLEME coupole animée : ") + MaestroGreybox.DomePath + " → " + (animated != null ? "objet trouvé (fresque=" + fresco + ")" : "objet introuvable") + ", Anim_ToRave échelle finale " + (sc != null ? sc.keys[sc.keys.Length - 1].value.ToString("F2") : "?") + " et y final " + (py != null ? py.keys[py.keys.Length - 1].value.ToString("F0") : "?") + " (attendu 0 / " + (MaestroGreybox.DomeRestPos.y + MaestroGreybox.DomeRise) + ")");
            if (!domeOk) problems++;

            // Sols remontés, flags Static
            int lifted = 0;
            foreach (string name in MaestroGreybox.TheatreLiftedObjects)
            {
                Transform f = model.Find(name);
                if (f != null && Mathf.Abs(f.localPosition.y - MaestroGreybox.TheatreFloorLift) < 1e-4f) lifted++;
            }
            int total = 0, wrong = 0;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                bool isDome = dome != null && (t == dome || t.IsChildOf(dome));
                StaticEditorFlags want = isDome ? 0 : MaestroGreybox.TheatreStaticFlags;
                total++;
                if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != want) wrong++;
            }
            bool flagsOk = lifted == MaestroGreybox.TheatreLiftedObjects.Length && wrong == 0;
            sb.AppendLine((flagsOk ? "Static OK : " : "PROBLEME Static : ") + (total - wrong) + "/" + total + " objets avec les flags attendus (occluder/occludee/reflection sans Batching ni ContributeGI, Dome sans flag), " + lifted + "/" + MaestroGreybox.TheatreLiftedObjects.Length + " sols remontés de " + (MaestroGreybox.TheatreFloorLift * 100f) + " cm");
            if (!flagsOk) problems++;

            // Lumières d'opéra : 6 points aux galeries 2 et 3, 2 spots d'avant-scène ; lustre à son point d'accroche
            Transform lum = opera.Find("Lumieres_Opera");
            int pointsOk = 0, spots = 0;
            if (lum != null)
            {
                for (int i = 0; i < MaestroGreybox.TheatreOperaLightPos.Length; i++)
                {
                    Transform l = lum.Find("Opera_Light_" + (i + 1));
                    if (l != null && l.GetComponent<Light>() != null && l.GetComponent<Light>().type == LightType.Point && (l.localPosition - MaestroGreybox.TheatreOperaLightPos[i]).magnitude < 1e-3f) pointsOk++;
                }
                foreach (Light l in lum.GetComponentsInChildren<Light>(true)) if (l.type == LightType.Spot) spots++;
            }
            Transform lustre = opera.Find("Lustre");
            bool lustreOk = lustre != null && (lustre.localPosition - MaestroGreybox.LustrePos).magnitude < 1e-3f && lustre.Find("Model") != null && lustre.Find("Lustre_Light") != null;
            bool lightsOk = pointsOk == 6 && spots == 2 && lustreOk;
            sb.AppendLine((lightsOk ? "Lumières OK : " : "PROBLEME lumières : ") + pointsOk + "/6 Point Lights de balcon aux galeries (±17 / fond, y 9.5 et 14), " + spots + " spot(s) d'avant-scène (attendu 2), lustre " + (lustreOk ? "au point d'accroche " + MaestroGreybox.LustrePos.ToString("F0") + " avec sa Point Light" : "incorrect"));
            if (!lightsOk) problems++;

            // Matériaux du théâtre
            var issues = new List<string>();
            foreach (PropMaterialSpec s in MaestroProps.TheatreSpecs)
            {
                Material m = AssetDatabase.LoadAssetAtPath<Material>(MaestroProps.MaterialPath(s.Name));
                if (m == null) { issues.Add(s.Name + " manquant"); continue; }
                bool both = Array.IndexOf(MaestroProps.TheatreDoubleSided, s.Name) >= 0;
                if (m.shader == null || m.shader.name != "Universal Render Pipeline/Lit") issues.Add(s.Name + " shader " + (m.shader != null ? m.shader.name : "null"));
                if (m.GetTexture("_BaseMap") == null) issues.Add(s.Name + " sans _BaseMap");
                if (m.GetTexture("_BumpMap") == null) issues.Add(s.Name + " sans _BumpMap");
                if (!m.enableInstancing) issues.Add(s.Name + " sans GPU Instancing");
                if (Mathf.RoundToInt(m.GetFloat("_Cull")) != (both ? (int)CullMode.Off : (int)CullMode.Back)) issues.Add(s.Name + " Render Face incorrect (attendu " + (both ? "Both" : "Front") + ")");
            }
            int embedded = 0;
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(MaestroProps.FbxPath(MaestroProps.TheatreName))) if (o is Material) embedded++;
            if (embedded > 0) issues.Add(embedded + " matériau(x) incorporé(s) dans Theatre.fbx");
            foreach (string i in issues) sb.AppendLine("  PROBLEME: " + i);
            sb.AppendLine((issues.Count == 0 ? "Matériaux théâtre OK : " : "PROBLEME matériaux théâtre : ") + MaestroProps.TheatreSpecs.Length + " matériaux (URP/Lit, BaseMap + Normal, GPU Instancing, double face velours / fresque), " + issues.Count + " problème(s)");
            problems += issues.Count;

            // Géométrie dans la scène (colliders temporaires) : colonnes à x = ±15.5, z = 7 (rayon 1.25), ouverture libre entre x = ±14, salle derrière le joueur
            RaycastHit? cl = RayTheatre(model, new Vector3(-15.5f, 5f, 0f), Vector3.forward, 20f);
            RaycastHit? cr = RayTheatre(model, new Vector3(15.5f, 5f, 0f), Vector3.forward, 20f);
            RaycastHit? ct = RayTheatre(model, new Vector3(-15.5f, 16.5f, 0f), Vector3.forward, 20f);
            // Ouverture : rien du théâtre entre le podium et l'orchestre (axe central, puis vers les musiciens extrêmes des vents et des cuivres)
            Vector3 eyeP = new Vector3(0f, 1.6f, 0f);
            RaycastHit? open = RayTheatre(model, new Vector3(0f, 5f, 0f), Vector3.forward, 40f);
            RaycastHit? open2 = RayTheatre(model, eyeP, new Vector3(-7.25f, 2.85f, 11f) - eyeP, 40f);
            RaycastHit? open3 = RayTheatre(model, eyeP, new Vector3(7.25f, 2.85f, 11f) - eyeP, 40f);
            Func<RaycastHit?, bool> isColumn = h => h.HasValue && h.Value.transform.name == "Proscenium" && Mathf.Abs(h.Value.point.z - 5.75f) < 0.4f;
            Func<RaycastHit?, bool> clearTo10 = h => !h.HasValue || h.Value.point.z > 10f;
            bool openOk = (!open.HasValue || open.Value.point.z > 13f) && clearTo10(open2) && clearTo10(open3);
            Transform galleries = model.Find("Galleries");
            Renderer gr = galleries != null ? galleries.GetComponent<Renderer>() : null;
            bool behind = gr != null && gr.bounds.center.z < 0f && gr.bounds.max.z < 4f && seats != null && seats.childCount > 0 && seats.GetChild(0).position.z < 0f;
            bool geomOk = isColumn(cl) && isColumn(cr) && isColumn(ct) && openOk && behind;
            sb.AppendLine((geomOk ? "Géométrie OK : " : "PROBLEME géométrie : ") + "colonne gauche " + (cl.HasValue ? cl.Value.transform.name + " @ z " + cl.Value.point.z.ToString("F2") : "non touchée") + ", droite " + (cr.HasValue ? "z " + cr.Value.point.z.ToString("F2") : "non touchée") + ", haut (y 16.5) " + (ct.HasValue ? "z " + ct.Value.point.z.ToString("F2") : "non touché") + " (attendu Proscenium à z ≈ 5.75) ; ouverture : axe (0, 5) → " + (open.HasValue ? open.Value.transform.name + " z " + open.Value.point.z.ToString("F1") : "aucun") + " (attendu > 13), vers les vents / cuivres extrêmes → " + (open2.HasValue ? open2.Value.transform.name + " z " + open2.Value.point.z.ToString("F1") : "aucun") + " / " + (open3.HasValue ? open3.Value.transform.name + " z " + open3.Value.point.z.ToString("F1") : "aucun") + " (attendu > 10) ; galeries " + (gr != null ? "z " + gr.bounds.min.z.ToString("F1") + " → " + gr.bounds.max.z.ToString("F1") : "?") + " et fauteuils derrière le joueur=" + behind);
            if (!geomOk) problems++;
            return problems;
        }

        // ---------------------------------------------------------------- v11 : séquence rave

        /// <summary>RaveSequence câblé sans référence nulle, SpeakerPulse et NotesParticles sur les 4 caissons (désactivés / arrêtés), StageLightSweep désactivé,
        /// néons comptés et désactivés, compte à rebours caché, Flash à 0.</summary>
        static int CheckRaveSequence(StringBuilder sb, GameObject[] roots)
        {
            int problems = 0;
            sb.AppendLine("--- Séquence rave (v11) ---");
            GameObject env = Find(roots, "Environment");
            RaveSequence seq = env != null ? env.GetComponent<RaveSequence>() : null;
            if (seq == null) { sb.AppendLine("PROBLEME: RaveSequence absent de la racine Environment"); problems++; }
            else
            {
                int refs = 0, nulls = 0;
                var nullNames = new List<string>();
                var so = new SerializedObject(seq);
                SerializedProperty it = so.GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.name == "m_Script") continue;
                    refs++;
                    if (it.objectReferenceValue == null) { nulls++; nullNames.Add(it.propertyPath); }
                }
                bool ok = nulls == 0 && seq.sections != null && seq.sections.Length == MaestroGreybox.SectionPrefabs.Length && seq.speakersAnimator != null && seq.speakersAnimator.gameObject.name == "Speakers";
                sb.AppendLine((ok ? "RaveSequence OK : " : "PROBLEME RaveSequence : ") + refs + " références objets, " + nulls + " nulle(s)" + (nulls > 0 ? " (" + string.Join(", ", nullNames) + ")" : "") + ", sections " + (seq.sections != null ? seq.sections.Length : 0) + "/" + MaestroGreybox.SectionPrefabs.Length + ", timings : countdown " + seq.countdownSeconds + " s + DROP " + seq.dropLabelSeconds + " s, drop " + seq.dropSeconds + " s, transform +" + seq.transformDelay + " s, timeout rave " + seq.raveTimeout + " s");
                if (!ok) problems++;
            }
            GameObject spk = Find(roots, "Speakers");
            if (spk != null)
            {
                int pulses = 0, pulsesEnabled = 0, cones = 0, emitters = 0, playing = 0, maxTotal = 0, bpmOk = 0;
                var phases = new List<string>();
                foreach (Transform s in spk.transform)
                {
                    if (!s.name.StartsWith("Speaker_")) continue;
                    SpeakerPulse p = s.GetComponent<SpeakerPulse>();
                    if (p != null)
                    {
                        pulses++;
                        if (p.enabled) pulsesEnabled++;
                        if (Mathf.Abs(p.bpm - 128f) < 1e-3f) bpmOk++;
                        phases.Add(p.phaseOffsetBeats.ToString("F2"));
                        if (p.Cone != null) cones++;
                    }
                    foreach (ParticleSystem ps in s.GetComponentsInChildren<ParticleSystem>(true)) { emitters++; if (ps.isPlaying) playing++; maxTotal += ps.main.maxParticles; }
                }
                bool pulseOk = pulses == 4 && pulsesEnabled == 0 && cones == 4 && bpmOk == 4;
                sb.AppendLine((pulseOk ? "SpeakerPulse OK : " : "PROBLEME SpeakerPulse : ") + pulses + "/4 caissons, " + pulsesEnabled + " actif(s) (attendu 0), cône résolu sur " + cones + ", 128 BPM sur " + bpmOk + ", décalages " + string.Join("/", phases) + " temps");
                if (!pulseOk) problems++;
                bool notesOk = emitters == 4 && playing == 0 && maxTotal <= MaestroGreybox.NotesParticlesBudget;
                sb.AppendLine((notesOk ? "NotesParticles OK : " : "PROBLEME NotesParticles : ") + emitters + " émetteurs (attendu 4), " + playing + " en lecture (attendu 0), plafond cumulé " + maxTotal + " particules (attendu ≤ " + MaestroGreybox.NotesParticlesBudget + ")");
                if (!notesOk) problems++;
            }
            GameObject sl = Find(roots, "StageLights");
            StageLightSweep sweep = sl != null ? sl.GetComponent<StageLightSweep>() : null;
            bool sweepOk = sweep != null && !sweep.enabled;
            sb.AppendLine((sweepOk ? "StageLightSweep OK : " : "PROBLEME StageLightSweep : ") + (sweep != null ? "présent, actif=" + sweep.enabled + ", ±" + sweep.sweepDegrees + "° à " + sweep.sweepHz + " Hz, couleurs toutes les " + sweep.colorPeriod + " s" : "absent") + " (attendu présent et désactivé)");
            if (!sweepOk) problems++;
            Transform neons = env != null ? env.transform.Find("CastleShell/Neons") : null;
            int neonCount = 0, neonActive = 0, neonMat = 0;
            if (neons != null)
                foreach (Transform c in neons)
                {
                    neonCount++;
                    if (c.gameObject.activeSelf) neonActive++;
                    Renderer r = c.GetComponent<Renderer>();
                    if (r != null && r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("M_Neon")) neonMat++;
                }
            EnvironmentMood mood = env != null ? env.GetComponent<EnvironmentMood>() : null;
            int moodNeons = mood != null && mood.neons != null ? mood.neons.Length : 0;
            bool neonOk = neons != null && neonCount == MaestroGreybox.NeonCount && neonActive == 0 && neonMat == neonCount && moodNeons == neonCount;
            sb.AppendLine((neonOk ? "Néons OK : " : "PROBLEME néons : ") + neonCount + " bandes (attendu " + MaestroGreybox.NeonCount + " : 2 au mur du fond + 2 tours × 3 anneaux × " + MaestroGreybox.NeonRingSegments + "), " + neonActive + " active(s) (attendu 0), " + neonMat + " en M_Neon*, " + moodNeons + " référencées par EnvironmentMood");
            if (!neonOk) problems++;
            GameObject vfx = Find(roots, "VFX");
            Transform cd = vfx != null ? vfx.transform.Find("CountdownPanel") : null;
            Transform fl = vfx != null ? vfx.transform.Find("Flash") : null;
            Light flash = fl != null ? fl.GetComponent<Light>() : null;
            TMPro.TextMeshProUGUI digit = cd != null ? cd.GetComponentInChildren<TMPro.TextMeshProUGUI>(true) : null;
            bool cdOk = cd != null && !cd.gameObject.activeSelf && digit != null && flash != null && !flash.enabled && flash.intensity < 1e-3f && flash.type == LightType.Directional;
            sb.AppendLine((cdOk ? "Compte à rebours OK : " : "PROBLEME compte à rebours : ") + "CountdownPanel " + (cd != null ? "caché=" + !cd.gameObject.activeSelf + ", chiffre TMP=" + (digit != null) + (digit != null ? " (taille " + digit.fontSize.ToString("F0") + ", contour=" + (digit.fontSharedMaterial != null && digit.fontSharedMaterial.IsKeywordEnabled("OUTLINE_ON")) + ")" : "") : "absent") + ", Flash " + (flash != null ? flash.type + " intensité " + flash.intensity + " actif=" + flash.enabled : "absent") + " (attendu caché, Directional à 0 désactivée)");
            if (!cdOk) problems++;
            return problems;
        }

        // ---------------------------------------------------------------- v12 : API des devs (MaestroOrchestraApi)

        public const string ApiPath = "Assets/_OrchestraRave/Scripts/Environment/MaestroOrchestraApi.cs";
        public const string BridgePath = "Assets/_OrchestraRave/Scripts/Environment/Examples/RaveGestureBridge.cs";
        public const string ApiDocPath = "Docs/API-DA.md";
        public const string ApiTypeName = "Maestro.Environment.MaestroOrchestraApi";

        /// <summary>Méthodes publiques statiques attendues : nom puis types des paramètres.</summary>
        public static readonly object[][] ApiMethods =
        {
            new object[] { "Diagnose" },
            new object[] { "StartRaveSequence" },
            new object[] { "AbortRaveSequence" },
            new object[] { "DropSpeakers" },
            new object[] { "TransformToRave" },
            new object[] { "ResetToOpera" },
            new object[] { "SetParty", typeof(bool) },
            new object[] { "SetLasers", typeof(bool) },
            new object[] { "SetStageLights", typeof(bool) },
            new object[] { "SetPoursuites", typeof(bool) },
            new object[] { "MusiciansPlay", typeof(string) },
            new object[] { "MusiciansStop", typeof(string) },
            new object[] { "MusiciansHeadbang", typeof(string) },
            new object[] { "HighlightSection", typeof(string), typeof(bool) },
            new object[] { "ClearHighlights" },
            new object[] { "Beat" },
        };

        /// <summary>Propriétés publiques statiques attendues (lecture seule) et leur type.</summary>
        public static readonly object[][] ApiProperties =
        {
            new object[] { "IsReady", typeof(bool) },
            new object[] { "IsRaveSequenceRunning", typeof(bool) },
            new object[] { "RavePhase", typeof(string) },
            new object[] { "Sections", typeof(string[]) },
        };

        public static Type FindLoadedType(string fullName)
        {
            foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>Fichiers présents ; classe statique compilée exposant toutes les méthodes, propriétés et l'event (réflexion) ; exemple compilé et absent
        /// de la scène et des prefabs ; doc citant chaque membre ; panneau de debug branché sur l'API ; IsReady vrai en mode édition après Diagnose().</summary>
        static int CheckOrchestraApi(StringBuilder sb)
        {
            int problems = 0;
            sb.AppendLine("--- API des devs (v12 : MaestroOrchestraApi) ---");
            foreach (string path in new[] { ApiPath, BridgePath, ApiDocPath })
            {
                bool exists = File.Exists(Path.Combine(MaestroRunner.ProjectRoot, path));
                sb.AppendLine((exists ? "Fichier OK : " : "PROBLEME fichier absent : ") + path);
                if (!exists) problems++;
            }

            const System.Reflection.BindingFlags PS = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
            Type api = FindLoadedType(ApiTypeName);
            var memberNames = new List<string>();
            if (api == null) { sb.AppendLine("PROBLEME: type " + ApiTypeName + " absent des assemblies compilées"); problems++; }
            else
            {
                bool isStatic = api.IsAbstract && api.IsSealed && api.IsClass;
                var missing = new List<string>();
                foreach (object[] m in ApiMethods)
                {
                    var types = new Type[m.Length - 1];
                    for (int i = 1; i < m.Length; i++) types[i - 1] = (Type)m[i];
                    System.Reflection.MethodInfo mi = api.GetMethod((string)m[0], PS, null, types, null);
                    bool ok = mi != null;
                    if (ok && types.Length == 1 && types[0] == typeof(string))
                    {
                        System.Reflection.ParameterInfo p = mi.GetParameters()[0];
                        ok = p.IsOptional && p.DefaultValue == null; // section = null
                    }
                    if (ok) memberNames.Add((string)m[0]); else missing.Add((string)m[0] + "(" + string.Join(", ", Array.ConvertAll(types, t => t.Name)) + ")");
                }
                foreach (object[] p in ApiProperties)
                {
                    System.Reflection.PropertyInfo pi = api.GetProperty((string)p[0], PS);
                    if (pi != null && pi.PropertyType == (Type)p[1] && pi.CanRead && pi.GetSetMethod() == null) memberNames.Add((string)p[0]);
                    else missing.Add((string)p[0] + " { get; }");
                }
                System.Reflection.EventInfo ev = api.GetEvent("OnRavePhaseChanged", PS);
                if (ev != null && ev.EventHandlerType == typeof(Action<string>)) memberNames.Add("OnRavePhaseChanged");
                else missing.Add("event Action<string> OnRavePhaseChanged");
                int publicMethods = 0;
                foreach (System.Reflection.MethodInfo mi in api.GetMethods(PS)) if (!mi.IsSpecialName) publicMethods++;
                bool ok2 = isStatic && missing.Count == 0;
                sb.AppendLine((ok2 ? "MaestroOrchestraApi OK : " : "PROBLEME MaestroOrchestraApi : ") + "classe " + (isStatic ? "statique" : "NON statique") + ", "
                    + memberNames.Count + "/" + (ApiMethods.Length + ApiProperties.Length + 1) + " membres trouvés par réflexion (" + publicMethods + " méthodes publiques, "
                    + ApiProperties.Length + " propriétés, 1 event)" + (missing.Count > 0 ? ", manquants : " + string.Join(", ", missing) : ""));
                if (!ok2) problems++;

                // En-tête du fichier : commentaire de dix lignes maximum.
                string apiFile = Path.Combine(MaestroRunner.ProjectRoot, ApiPath);
                if (File.Exists(apiFile))
                {
                    int header = 0;
                    foreach (string line in File.ReadAllLines(apiFile)) { if (line.TrimStart().StartsWith("//")) header++; else break; }
                    bool headerOk = header >= 1 && header <= 10;
                    sb.AppendLine((headerOk ? "En-tête OK : " : "PROBLEME en-tête : ") + header + " ligne(s) de commentaire en tête de MaestroOrchestraApi.cs (attendu 1 à 10)");
                    if (!headerOk) problems++;
                }

                // Mode édition : résolution par nom de racine dans Art_Sandbox.
                try
                {
                    string diag = (string)api.GetMethod("Diagnose", PS).Invoke(null, null);
                    bool ready = (bool)api.GetProperty("IsReady", PS).GetValue(null, null);
                    sb.AppendLine((ready ? "IsReady OK : " : "PROBLEME IsReady : ") + "IsReady = " + ready + " en mode édition après Diagnose()");
                    sb.AppendLine(diag);
                    if (!ready) problems++;
                }
                catch (Exception e) { sb.AppendLine("PROBLEME: exception pendant Diagnose() / IsReady : " + (e.InnerException ?? e).Message); problems++; }
            }

            // Exemple RaveGestureBridge : compilé, MonoBehaviour avec un champ public bool gestureDetected, posé nulle part.
            Type bridge = FindLoadedType("RaveGestureBridge");
            if (bridge == null) { sb.AppendLine("PROBLEME: type RaveGestureBridge absent des assemblies compilées"); problems++; }
            else
            {
                System.Reflection.FieldInfo f = bridge.GetField("gestureDetected", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                int inScene = UnityEngine.Object.FindObjectsByType(bridge, FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                string guid = AssetDatabase.AssetPathToGUID(BridgePath);
                int inAssets = 0;
                if (!string.IsNullOrEmpty(guid))
                {
                    var files = new List<string>(Directory.GetFiles(Path.Combine(MaestroRunner.ProjectRoot, MaestroGreybox.PrefabDir), "*.prefab", SearchOption.AllDirectories));
                    files.Add(Path.Combine(MaestroRunner.ProjectRoot, MaestroGreybox.ScenePath));
                    foreach (string file in files) if (File.ReadAllText(file).Contains(guid)) inAssets++;
                }
                bool ok = typeof(MonoBehaviour).IsAssignableFrom(bridge) && f != null && f.FieldType == typeof(bool) && inScene == 0 && inAssets == 0;
                sb.AppendLine((ok ? "RaveGestureBridge OK : " : "PROBLEME RaveGestureBridge : ") + "MonoBehaviour=" + typeof(MonoBehaviour).IsAssignableFrom(bridge) + ", champ public bool gestureDetected=" + (f != null && f.FieldType == typeof(bool)) + ", instances dans la scène " + inScene + ", références dans la scène / les prefabs " + inAssets + " (attendu 0 et 0)");
                if (!ok) problems++;
            }

            // Doc : chaque membre cité sous la forme MaestroOrchestraApi.<Nom>, triggers bas niveau et touches du panneau.
            string docFile = Path.Combine(MaestroRunner.ProjectRoot, ApiDocPath);
            if (File.Exists(docFile))
            {
                string doc = File.ReadAllText(docFile);
                var undocumented = new List<string>();
                var all = new List<string>();
                foreach (object[] m in ApiMethods) all.Add((string)m[0]);
                foreach (object[] p in ApiProperties) all.Add((string)p[0]);
                all.Add("OnRavePhaseChanged");
                int documented = 0;
                foreach (string n in all) { if (doc.Contains("MaestroOrchestraApi." + n)) documented++; else undocumented.Add(n); }
                foreach (string t in new[] { "`Drop`", "`Rave`", "`Reset`", "`Play`", "`Stop`", "`Headbang`", "F1", "F2", "MainScene" }) if (!doc.Contains(t)) undocumented.Add(t);
                int words = doc.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                bool ok = undocumented.Count == 0 && words <= 1400;
                sb.AppendLine((ok ? "API-DA.md OK : " : "PROBLEME API-DA.md : ") + documented + "/" + all.Count + " membres cités avec un appel MaestroOrchestraApi.<Nom>, " + words + " mots (≤ 1400, deux pages)" + (undocumented.Count > 0 ? ", absents : " + string.Join(", ", undocumented) : ""));
                if (!ok) problems++;
            }

            // Panneau de debug : boutons Séquence rave / Abort et diagnostic de démarrage passent par l'API.
            string panelFile = Path.Combine(MaestroRunner.ProjectRoot, "Assets/_OrchestraRave/Scripts/Core/MaestroArtDebugPanel.cs");
            if (File.Exists(panelFile))
            {
                string src = File.ReadAllText(panelFile);
                bool ok = src.Contains("MaestroOrchestraApi.StartRaveSequence()") && src.Contains("MaestroOrchestraApi.AbortRaveSequence()") && src.Contains("Debug.Log(MaestroOrchestraApi.Diagnose())")
                    && !src.Contains("_sequence.StartSequence()") && !src.Contains("_sequence.AbortSequence()");
                sb.AppendLine((ok ? "Panneau de debug OK : " : "PROBLEME panneau de debug : ") + "Séquence rave et Abort via MaestroOrchestraApi, Diagnose() loggé au démarrage");
                if (!ok) problems++;
            }
            return problems;
        }

        // ---------------------------------------------------------------- v9 : transitions de l'EnvironmentController

        /// <summary>Any State → Rave (Rave, Level < ~0.001) et Any State → ToOpera (Reset, Level > ~0.999), sans exit time, durée 0, sans transition vers soi ;
        /// aucune transition directe entre états sauf ToOpera → Opera par exit time.</summary>
        static int CheckEnvironmentTransitions(StringBuilder sb, AnimatorStateMachine sm)
        {
            int problems = 0;
            int anyRave = 0, anyOpera = 0, bad = 0;
            foreach (AnimatorStateTransition t in sm.anyStateTransitions)
            {
                bool rave = false, reset = false, levelLow = false, levelHigh = false;
                foreach (AnimatorCondition c in t.conditions)
                {
                    if (c.parameter == "Rave" && c.mode == AnimatorConditionMode.If) rave = true;
                    if (c.parameter == "Reset" && c.mode == AnimatorConditionMode.If) reset = true;
                    if (c.parameter == MaestroGreybox.LevelParameter && c.mode == AnimatorConditionMode.Less && c.threshold < 0.01f) levelLow = true;
                    if (c.parameter == MaestroGreybox.LevelParameter && c.mode == AnimatorConditionMode.Greater && c.threshold > 0.99f) levelHigh = true;
                }
                bool shape = !t.hasExitTime && t.duration < 1e-4f && !t.canTransitionToSelf && t.destinationState != null;
                if (shape && t.destinationState.name == "Rave" && rave && levelLow && !reset) anyRave++;
                else if (shape && t.destinationState.name == "ToOpera" && reset && levelHigh && !rave) anyOpera++;
                else bad++;
            }
            int direct = 0, back = 0;
            var directNames = new List<string>();
            foreach (ChildAnimatorState cs in sm.states)
                foreach (AnimatorStateTransition t in cs.state.transitions)
                {
                    if (cs.state.name == "ToOpera" && t.destinationState != null && t.destinationState.name == "Opera" && t.hasExitTime && Mathf.Abs(t.exitTime - 1f) < 1e-3f && t.conditions.Length == 0) back++;
                    else { direct++; directNames.Add(cs.state.name + "→" + (t.destinationState != null ? t.destinationState.name : "?")); }
                }
            bool ok = anyRave == 1 && anyOpera == 1 && bad == 0 && back == 1 && direct == 0;
            sb.AppendLine((ok ? "Transitions OK : " : "PROBLEME transitions : ") + "Any State → Rave (Rave, Level < 0.01) ×" + anyRave + ", Any State → ToOpera (Reset, Level > 0.99) ×" + anyOpera + ", autres Any State " + bad + ", ToOpera → Opera par exit time ×" + back + ", transitions directes " + direct + (direct > 0 ? " (" + string.Join(", ", directNames) + ")" : "") + " (attendu 1 / 1 / 0 / 1 / 0, durée 0, sans exit time, sans transition vers soi)");
            if (!ok) problems++;
            return problems;
        }

        // ---------------------------------------------------------------- v8 : gradins, poursuites, caissons posés

        static bool ColorClose(Color a, Color b) { return Mathf.Abs(a.r - b.r) < 0.003f && Mathf.Abs(a.g - b.g) < 0.003f && Mathf.Abs(a.b - b.b) < 0.003f; }

        /// <summary>Premier impact du décor (racine Environment) sous le point, 5 cm au-dessus ; musiciens, caissons et triggers ignorés.</summary>
        static bool RayDown(Vector3 p, out RaycastHit hit)
        {
            RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * 0.05f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (h.transform.root.name != "Environment") continue;
                hit = h;
                return true;
            }
            hit = new RaycastHit();
            return false;
        }

        /// <summary>Chaque Musician_N posé sur son gradin (rayon vers le bas depuis le pivot : segment Gradin_T à la hauteur des pieds), |x| monde < 14 ;
        /// en avertissement, les bords de la capsule (4 points au rayon 0.25) qui sortent du gradin.</summary>
        static int CheckTiers(StringBuilder sb, GameObject[] roots)
        {
            int problems = 0;
            Physics.SyncTransforms();
            sb.AppendLine("--- Musiciens sur leurs gradins (rayon vers le bas depuis chaque Musician_N, puis 4 points au rayon de la capsule) ---");
            foreach (string n in MaestroGreybox.SectionPrefabs)
            {
                GameObject s = Find(roots, n);
                if (s == null) continue;
                int tier = MaestroGreybox.SectionTier[n];
                string prefix = "Gradin_" + tier + "_Seg";
                int onTier = 0, total = 0, badX = 0, overhang = 0;
                var notes = new List<string>();
                foreach (Transform m in s.transform)
                {
                    if (!m.name.StartsWith("Musician_")) continue;
                    total++;
                    Vector3 p = m.position;
                    if (Mathf.Abs(p.x) >= 14f) badX++;
                    RaycastHit hit;
                    bool found = RayDown(p, out hit);
                    bool ok = found && hit.transform.name.StartsWith(prefix) && Mathf.Abs(hit.point.y - p.y) < 0.02f;
                    if (ok) onTier++;
                    else notes.Add("PROBLEME: " + n + "/" + m.name + " @ " + p.ToString("F2") + " : sous le pivot " + (found ? hit.transform.name + " à y " + hit.point.y.ToString("F2") : "rien") + " (attendu " + prefix + "K à y " + p.y.ToString("F2") + ")");
                    var off = new List<string>();
                    foreach (Vector3 d in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                    {
                        Vector3 q = p + d * MaestroGreybox.MusicianBodyRadius;
                        RaycastHit h2;
                        bool on = RayDown(q, out h2) && h2.transform.name.StartsWith(prefix) && Mathf.Abs(h2.point.y - p.y) < 0.02f;
                        if (on) continue;
                        Collider[] inside = Physics.OverlapSphere(q + Vector3.up * 0.05f, 0.01f, ~0, QueryTriggerInteraction.Ignore);
                        string what = inside.Length > 0 ? "entre dans " + inside[0].name : (h2.transform != null ? "au-dessus de " + h2.transform.name + " (y " + h2.point.y.ToString("F2") + ")" : "au-dessus du vide");
                        off.Add((d == Vector3.forward ? "+z" : d == Vector3.back ? "-z" : d == Vector3.left ? "-x" : "+x") + " monde " + what);
                    }
                    if (off.Count > 0) { overhang++; notes.Add("avertissement : " + n + "/" + m.name + " @ " + p.ToString("F2") + " : bord de capsule hors du gradin " + tier + " : " + string.Join(" ; ", off)); }
                }
                foreach (string note in notes) sb.AppendLine("  " + note);
                bool secOk = onTier == total && badX == 0;
                sb.AppendLine((secOk ? "Gradin OK : " : "PROBLEME gradin : ") + n + " : " + onTier + "/" + total + " musiciens posés sur le gradin " + tier + " (pieds à la hauteur du dessus), " + badX + " hors |x| < 14, " + overhang + " capsule(s) débordant du gradin (avertissement)");
                if (!secOk) problems++;
            }
            return problems;
        }

        static Bounds RendererBounds(Transform t)
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.zero);
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        /// <summary>La lumière atteint-elle la boîte : portée (point le plus proche) et, pour un spot, au moins un coin ou le centre dans le cône.</summary>
        static bool LightAffects(Light l, Bounds b)
        {
            Vector3 lp = l.transform.position;
            if ((b.ClosestPoint(lp) - lp).magnitude > l.range) return false;
            if (l.type != LightType.Spot) return true;
            float half = l.spotAngle * 0.5f;
            for (int i = 0; i < 9; i++)
            {
                Vector3 q = i == 8 ? b.center : new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 d = q - lp;
                if (d.magnitude <= l.range && Vector3.Angle(l.transform.forward, d) <= half) return true;
            }
            return false;
        }

        /// <summary>Groupe OperaShell/Poursuites : rail, 5 spots + fill conformes et pilotés par EnvironmentMood, faisceaux libres (colliders temporaires du théâtre
        /// requis), couverture des têtes par les cônes, nombre de lumières atteignant un musicien par section vs limite URP par objet.</summary>
        static int CheckPoursuites(StringBuilder sb, GameObject[] roots)
        {
            int problems = 0;
            sb.AppendLine("--- Poursuites de scène (v8) ---");
            GameObject env = Find(roots, "Environment");
            Transform group = env != null ? env.transform.Find("OperaShell/Poursuites") : null;
            if (group == null) { sb.AppendLine("PROBLEME: groupe OperaShell/Poursuites absent"); return 1; }
            EnvironmentMood mood = env.GetComponent<EnvironmentMood>();
            var moodLights = new List<Light>(mood != null && mood.operaLights != null ? mood.operaLights : new Light[0]);

            Transform rail = group.Find("Rail");
            bool railOk = rail != null && (rail.localPosition - MaestroGreybox.PoursuiteRailPos).magnitude < 1e-3f && (rail.localScale - MaestroGreybox.PoursuiteRailSize).magnitude < 1e-3f && rail.GetComponent<Collider>() == null && rail.GetComponent<MeshRenderer>() != null;
            sb.AppendLine((railOk ? "Rail OK : " : "PROBLEME rail : ") + (rail != null ? rail.localScale.ToString("F2") + " @ " + rail.localPosition.ToString("F2") : "absent") + " (attendu " + MaestroGreybox.PoursuiteRailSize.ToString("F2") + " @ " + MaestroGreybox.PoursuiteRailPos.ToString("F2") + ", sans collider)");
            if (!railOk) problems++;

            int spotsOk = 0;
            var spots = new List<Light>();
            for (int i = 0; i < MaestroGreybox.PoursuiteNames.Length; i++)
            {
                string name = MaestroGreybox.PoursuiteNames[i];
                Transform t = group.Find(name);
                Light l = t != null ? t.GetComponent<Light>() : null;
                if (l == null) { sb.AppendLine("  PROBLEME: " + name + " absente"); continue; }
                spots.Add(l);
                Vector3 expectedPos = new Vector3(MaestroGreybox.PoursuiteX[i], MaestroGreybox.PoursuiteRailPos.y, MaestroGreybox.PoursuiteRailPos.z);
                Vector3 target = MaestroGreybox.PoursuiteTarget(name);
                float aim = Vector3.Angle(t.forward, target - t.position);
                bool ok = l.type == LightType.Spot && (t.localPosition - expectedPos).magnitude < 1e-3f && aim < 0.1f
                    && ColorClose(l.color, MaestroGreybox.PoursuiteColor) && Mathf.Abs(l.intensity - MaestroGreybox.PoursuiteIntensity) < 1e-3f
                    && Mathf.Abs(l.spotAngle - MaestroGreybox.PoursuiteAngle) < 1e-3f && Mathf.Abs(l.innerSpotAngle - MaestroGreybox.PoursuiteInnerAngle) < 1e-3f
                    && Mathf.Abs(l.range - MaestroGreybox.PoursuiteRange) < 1e-3f && l.renderMode == LightRenderMode.ForcePixel && l.shadows == LightShadows.None
                    && t.Find("Projecteur") != null && l.enabled;
                int mi = moodLights.IndexOf(l);
                bool baseOk = mi >= 0 && mood.operaLightBases != null && mi < mood.operaLightBases.Length && Mathf.Abs(mood.operaLightBases[mi] - MaestroGreybox.PoursuiteIntensity) < 1e-3f;
                if (ok && baseOk) spotsOk++;
                sb.AppendLine((ok && baseOk ? "  Poursuite OK : " : "  PROBLEME poursuite : ") + name + " @ " + t.localPosition.ToString("F1") + " → " + target.ToString("F2") + " (écart de visée " + aim.ToString("F2") + "°), " + l.spotAngle + "° / interne " + l.innerSpotAngle.ToString("F0") + "°, intensité " + l.intensity + ", portée " + l.range + ", " + l.renderMode + ", ombres " + l.shadows + ", projecteur=" + (t.Find("Projecteur") != null) + ", dans EnvironmentMood=" + (mi >= 0) + " (base " + (baseOk ? "OK" : "incorrecte") + "), allumée=" + l.enabled);
            }
            Transform ft = group.Find(MaestroGreybox.FillName);
            Light fl = ft != null ? ft.GetComponent<Light>() : null;
            int fi = fl != null ? moodLights.IndexOf(fl) : -1;
            bool fillOk = fl != null && fl.type == LightType.Point && (ft.localPosition - MaestroGreybox.FillPos).magnitude < 1e-3f && ColorClose(fl.color, MaestroGreybox.FillColor) && Mathf.Abs(fl.intensity - MaestroGreybox.FillIntensity) < 1e-3f && Mathf.Abs(fl.range - MaestroGreybox.FillRange) < 1e-3f && fl.renderMode == LightRenderMode.ForcePixel && fl.shadows == LightShadows.None && fi >= 0 && fl.enabled;
            sb.AppendLine((fillOk ? "Fill OK : " : "PROBLEME fill : ") + MaestroGreybox.FillName + (fl != null ? " @ " + ft.localPosition.ToString("F1") + " intensité " + fl.intensity + " portée " + fl.range + " " + fl.renderMode + " ombres " + fl.shadows + " dans EnvironmentMood=" + (fi >= 0) + " allumé=" + fl.enabled : " absent"));
            if (!fillOk) problems++;
            int extra = 0;
            foreach (Light l in group.GetComponentsInChildren<Light>(true)) if (!spots.Contains(l) && l != fl) extra++;
            bool countOk = spotsOk == MaestroGreybox.PoursuiteCount && extra == 0;
            sb.AppendLine((countOk ? "Poursuites OK : " : "PROBLEME poursuites : ") + spotsOk + "/" + MaestroGreybox.PoursuiteCount + " spots conformes, " + extra + " lumière(s) inattendue(s) dans le groupe");
            if (!countOk) problems++;

            // Faisceaux : axe de chaque poursuite jusqu'à sa cible, musiciens ignorés (ils sont la cible)
            foreach (Light l in spots)
            {
                Vector3 target = MaestroGreybox.PoursuiteTarget(l.name);
                Vector3 d = target - l.transform.position;
                RaycastHit[] hits = Physics.RaycastAll(l.transform.position, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                string blocker = null;
                foreach (RaycastHit h in hits) { if (h.transform.root.name != "Environment") continue; blocker = h.transform.name + " @ " + h.point.ToString("F2"); break; }
                sb.AppendLine((blocker == null ? "  Faisceau OK : " : "  PROBLEME faisceau : ") + l.name + " → " + target.ToString("F2") + " (" + d.magnitude.ToString("F1") + " m) : " + (blocker == null ? "rien ne coupe l'axe (rideaux, entablement, colonnes)" : "COUPÉ par " + blocker));
                if (blocker != null) problems++;
            }
            // Couverture : angle de chaque tête à l'axe de la poursuite de sa section la plus proche
            foreach (string n in MaestroGreybox.SectionPrefabs)
            {
                GameObject s = Find(roots, n);
                if (s == null) continue;
                float maxAngle = 0f;
                int inner = 0, outer = 0, total = 0;
                string worst = "";
                foreach (Transform m in s.transform)
                {
                    Transform head = m.Find("Head");
                    if (head == null) continue;
                    total++;
                    float best = 999f;
                    foreach (Light l in spots)
                    {
                        if (MaestroGreybox.PoursuiteSection(l.name) != n) continue;
                        float a = Vector3.Angle(l.transform.forward, head.position - l.transform.position);
                        if (a < best) best = a;
                    }
                    if (best <= MaestroGreybox.PoursuiteInnerAngle * 0.5f) inner++;
                    if (best <= MaestroGreybox.PoursuiteAngle * 0.5f) outer++;
                    if (best > maxAngle) { maxAngle = best; worst = m.name; }
                }
                bool covOk = outer == total;
                sb.AppendLine((covOk ? "  Couverture OK : " : "  PROBLEME couverture : ") + n + " : " + outer + "/" + total + " têtes dans le cône de " + MaestroGreybox.PoursuiteAngle + "° d'une poursuite de la section, " + inner + " dans le cône interne de " + MaestroGreybox.PoursuiteInnerAngle + "°, angle maximal " + maxAngle.ToString("F1") + "° (" + worst + ")");
                if (!covOk) problems++;
            }
            // Lumières atteignant un musicien (bornes du corps vs portée / cône) vs limite URP par objet
            int limit = 4;
            var mobile = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            if (mobile != null) limit = mobile.maxAdditionalLightsCount;
            Light[] allLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (string n in MaestroGreybox.SectionPrefabs)
            {
                GameObject s = Find(roots, n);
                if (s == null) continue;
                int max = -1, maxImportant = 0;
                string maxNames = "", maxMusician = "";
                foreach (Transform m in s.transform)
                {
                    Transform body = m.Find("Body");
                    Renderer r = body != null ? body.GetComponent<Renderer>() : null;
                    if (r == null) continue;
                    var names = new List<string>();
                    int important = 0;
                    foreach (Light l in allLights)
                    {
                        if (!l.enabled || l.intensity <= 0.001f || l.type == LightType.Directional || !l.gameObject.activeInHierarchy) continue;
                        if (!LightAffects(l, r.bounds)) continue;
                        names.Add(l.name + (l.renderMode == LightRenderMode.ForcePixel ? "*" : ""));
                        if (l.renderMode == LightRenderMode.ForcePixel) important++;
                    }
                    if (names.Count > max) { max = names.Count; maxImportant = important; maxNames = string.Join(", ", names); maxMusician = m.name; }
                }
                bool within = max <= limit;
                sb.AppendLine((within ? "  Lumières par musicien OK : " : "  AVERTISSEMENT lumières par musicien : ") + n + " : au plus " + max + " lumière(s) atteignant un corps (" + maxMusician + " : " + maxNames + "), dont " + maxImportant + " Important (*), limite par objet " + limit + (within ? "" : " → les " + maxImportant + " Important gardent leurs créneaux, les autres (Auto : avant-scène, lustre, braseros, torches) sont départagées par intensité"));
            }
            return problems;
        }

        /// <summary>Caissons posés (Speaker_N à SpeakerRestY, temporairement) : aucun Model ne chevauche un segment de gradin, le podium, un brasero ni une colonne,
        /// face avant à z ≥ 1.2 ; puis clip de chute échantillonné à 60 Hz : point le plus bas de chaque caisson ≥ sol détecté sous l'ancre.</summary>
        static int CheckSpeakersLanding(StringBuilder sb, GameObject[] roots)
        {
            int problems = 0;
            GameObject spk = Find(roots, "Speakers");
            GameObject env = Find(roots, "Environment");
            Transform anchor = env != null ? env.transform.Find("Anchor_Speakers") : null;
            if (spk == null || anchor == null) { sb.AppendLine("PROBLEME: Speakers ou Anchor_Speakers introuvable"); return 1; }
            sb.AppendLine("--- Caissons posés (Anchor_Speakers @ " + anchor.position.ToString("F2") + ", repos y = " + MaestroGreybox.SpeakerRestY + ") ---");
            float floorY = float.NaN;
            string floorName = "aucun";
            RaycastHit[] fhits = Physics.RaycastAll(anchor.position + Vector3.up * 1f, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(fhits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in fhits) { if (h.transform.IsChildOf(spk.transform)) continue; floorY = h.point.y; floorName = h.transform.name; break; }
            var saved = new List<Vector3>();
            try
            {
                foreach (Transform s in spk.transform) { saved.Add(s.localPosition); s.localPosition = new Vector3(s.localPosition.x, MaestroGreybox.SpeakerRestY, s.localPosition.z); }
                Physics.SyncTransforms();
                foreach (Transform s in spk.transform)
                {
                    if (!s.name.StartsWith("Speaker_")) continue;
                    Transform model = s.Find("Model");
                    if (model == null) { sb.AppendLine("  PROBLEME: " + s.name + " sans Model"); problems++; continue; }
                    Bounds b = RendererBounds(model);
                    Collider[] cols = Physics.OverlapBox(b.center, b.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                    var overlaps = new List<string>();
                    foreach (Collider c in cols)
                    {
                        if (c.transform.IsChildOf(spk.transform)) continue;
                        string nm = c.transform.name;
                        if (nm == "Cour" || nm == "OuterWalls" || nm == "StageFloor") continue; // sols : contact attendu
                        if (!overlaps.Contains(nm)) overlaps.Add(nm);
                    }
                    bool frontOk = b.min.z >= 1.2f;
                    bool ok = overlaps.Count == 0 && frontOk && Mathf.Abs(b.min.y - (anchor.position.y + MaestroGreybox.SpeakerRestY - MaestroGreybox.SpeakerHalfHeight)) < 0.02f;
                    sb.AppendLine((ok ? "  Caisson OK : " : "  PROBLEME caisson : ") + s.name + " posé : x " + b.min.x.ToString("F2") + " → " + b.max.x.ToString("F2") + ", y " + b.min.y.ToString("F2") + " → " + b.max.y.ToString("F2") + ", z " + b.min.z.ToString("F2") + " → " + b.max.z.ToString("F2") + " : " + (overlaps.Count == 0 ? "aucun chevauchement (gradins, podium, braseros, colonnes)" : "CHEVAUCHE " + string.Join(", ", overlaps)) + (frontOk ? ", face avant ≥ 1.2" : ", PROBLEME face avant < 1.2"));
                    if (!ok) problems++;
                }
            }
            finally
            {
                int k = 0;
                foreach (Transform s in spk.transform) s.localPosition = saved[k++];
                Physics.SyncTransforms();
            }
            var drop = AssetDatabase.LoadAssetAtPath<AnimationClip>(MaestroGreybox.AnimDir + "/Anim_SpeakersDrop.anim");
            if (drop == null || float.IsNaN(floorY)) { sb.AppendLine("PROBLEME: clip Anim_SpeakersDrop ou sol sous l'ancre introuvable"); return problems + 1; }
            float minMargin = float.MaxValue, minT = 0f;
            string minName = "";
            int frames = Mathf.RoundToInt(MaestroGreybox.SpeakerDropDuration * 60f);
            foreach (Transform s in spk.transform)
            {
                if (!s.name.StartsWith("Speaker_")) continue;
                Transform model = s.Find("Model");
                if (model == null) continue;
                float modelMinY = MaestroGreybox.ModelBounds(s, model.gameObject).min.y;
                AnimationCurve cy = AnimationUtility.GetEditorCurve(drop, EditorCurveBinding.FloatCurve(s.name, typeof(Transform), "localPosition.y"));
                if (cy == null) { sb.AppendLine("  PROBLEME: pas de courbe localPosition.y pour " + s.name + " dans Anim_SpeakersDrop"); problems++; continue; }
                for (int f = 0; f <= frames; f++)
                {
                    float t = f / 60f;
                    float margin = anchor.position.y + cy.Evaluate(t) + modelMinY - floorY;
                    if (margin < minMargin) { minMargin = margin; minT = t; minName = s.name; }
                }
            }
            bool clipOk = minMargin >= -0.001f;
            sb.AppendLine((clipOk ? "Chute OK : " : "PROBLEME chute : ") + "sol sous l'ancre = " + floorName + " à y " + floorY.ToString("F2") + " ; " + (frames + 1) + " échantillons à 60 Hz × 4 caissons : point le plus bas au minimum à " + minMargin.ToString("F3") + " m au-dessus du sol (" + minName + ", t = " + minT.ToString("F2") + " s), jamais sous le sol=" + clipOk);
            if (!clipOk) problems++;
            return problems;
        }

        static Plane[] FrustumPlanes(Vector3 eye, Quaternion rot, float fov, float aspect, out Camera cam)
        {
            var go = new GameObject("Maestro_FrustumCamera");
            go.hideFlags = HideFlags.HideAndDontSave;
            cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.transform.SetPositionAndRotation(eye, rot);
            cam.fieldOfView = fov;
            cam.aspect = aspect;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            return GeometryUtility.CalculateFrustumPlanes(cam);
        }

        /// <summary>Vue vers la salle (regard -Z) : galeries et fauteuils dans le champ ; vue vers le haut : coupole (fresque) dans le champ, rayon libre.</summary>
        static int CheckTheatreViews(StringBuilder sb, Vector3 eye, Transform model)
        {
            int problems = 0;
            Camera cam;
            Plane[] back = FrustumPlanes(eye, Quaternion.Euler(0f, 180f, 0f), FovV, Aspect, out cam);
            try
            {
                Transform galleries = model.Find("Galleries");
                Renderer gr = galleries != null ? galleries.GetComponent<Renderer>() : null;
                bool galleriesIn = gr != null && GeometryUtility.TestPlanesAABB(back, gr.bounds);
                int seatsIn = 0;
                Transform seats = model.Find("Seats");
                if (seats != null) foreach (Transform s in seats) { Renderer r = s.GetComponent<Renderer>(); if (r != null && GeometryUtility.TestPlanesAABB(back, r.bounds)) seatsIn++; }
                RaycastHit? h = RayTheatre(model, eye, new Vector3(0f, 8f, -23f) - eye, 40f);
                bool ok = galleriesIn && seatsIn > 0 && h.HasValue && (h.Value.transform.name == "Galleries" || h.Value.transform.name == "OuterWalls");
                sb.AppendLine((ok ? "  Salle (regard -Z) OK : " : "  PROBLEME salle (regard -Z) : ") + "galeries dans le champ=" + galleriesIn + ", fauteuils dans le champ=" + seatsIn + ", rayon vers la galerie du fond → " + (h.HasValue ? h.Value.transform.name + " @ " + h.Value.point.ToString("F1") : "rien"));
                if (!ok) problems++;
            }
            finally { UnityEngine.Object.DestroyImmediate(cam.gameObject); }

            Plane[] up = FrustumPlanes(eye, Quaternion.Euler(-90f, 0f, 0f), FovV, Aspect, out cam);
            try
            {
                Transform dome = model.Find("Ceiling/Dome");
                Renderer dr = dome != null ? dome.GetComponent<Renderer>() : null;
                bool domeIn = dr != null && GeometryUtility.TestPlanesAABB(up, dr.bounds);
                RaycastHit? h = RayTheatre(model, eye, Vector3.up, 60f);
                bool ok = domeIn && h.HasValue && h.Value.transform.name == "Dome";
                sb.AppendLine((ok ? "  Plafond (regard vers le haut) OK : " : "  PROBLEME plafond : ") + "coupole dans le champ=" + domeIn + ", rayon vertical → " + (h.HasValue ? h.Value.transform.name + " (fresque) @ y " + h.Value.point.y.ToString("F1") : "rien") + " (attendu Dome à y ≈ 29.8)");
                if (!ok) problems++;
            }
            finally { UnityEngine.Object.DestroyImmediate(cam.gameObject); }
            return problems;
        }

        /// <summary>Estimation du rendu depuis le podium (mode opéra) : renderers actifs dont les bornes coupent le frustum, triangles, batches avec instancing des fauteuils.</summary>
        static void EstimateRendering(StringBuilder sb, string mode, int extraTris, int extraBatches, string extraNote)
        {
            Vector3 eye = new Vector3(0f, 1.6f, 0f);
            sb.AppendLine("--- Estimation de rendu depuis (0, 1.6, 0) regard +Z, " + mode + " (objectifs Quest 3 : < 350 000 triangles, < 150 batches) ---");
            if (!string.IsNullOrEmpty(extraNote)) sb.AppendLine("  " + extraNote);
            Renderer[] all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var view in new[] { new { fov = 65f, aspect = 16f / 9f, label = "FOV 65° 16:9" }, new { fov = 110f, aspect = 1f, label = "FOV 110° 1:1 (ordre de grandeur casque)" } })
            {
                Camera cam;
                Plane[] planes = FrustumPlanes(eye, Quaternion.identity, view.fov, view.aspect, out cam);
                try
                {
                    long tris = extraTris;
                    int visible = 0, seats = 0, draws = 0;
                    foreach (Renderer r in all)
                    {
                        if (!r.enabled || !r.gameObject.activeInHierarchy || !GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                        visible++;
                        MeshFilter mf = r.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                        if (r.name.StartsWith("Seat.")) seats++;
                        else draws += Mathf.Max(1, r.sharedMaterials.Length);
                    }
                    int seatBatches = seats > 0 ? Mathf.CeilToInt(seats / 1023f) : 0;
                    int batches = draws + seatBatches + extraBatches;
                    sb.AppendLine("  " + view.label + " : " + visible + " renderers dans le frustum (dont " + seats + " fauteuils instanciés), " + tris.ToString("N0") + " triangles, ≈ " + batches + " batches (" + draws + " sous-meshes + " + seatBatches + " lot(s) de fauteuils" + (extraBatches > 0 ? " + " + extraBatches + " hors maillages" : "") + ") → " + (tris > 350000 || batches > 150 ? "OBJECTIF DÉPASSÉ" : "dans les objectifs"));
                }
                finally { UnityEngine.Object.DestroyImmediate(cam.gameObject); }
            }
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
                    Transform lustre = opera != null ? opera.Find("Lustre") : null;
                    Transform theatre = opera != null ? opera.Find("Model") : null;
                    if (theatre != null)
                    {
                        // Cadre de scène du théâtre : entablement doré (0, 18.5, 7), le théâtre lui-même ne compte pas comme obstacle
                        problems += LookUpCheck(sb, eye, "Theatre/Proscenium (entablement)", theatre, 1f, MaestroGreybox.TheatreEntablature);
                        problems += CheckTheatreViews(sb, eye, theatre);
                    }
                    else
                    {
                        Transform lintel = opera != null ? opera.Find("Proscenium/Linteau") : null;
                        problems += LookUpCheck(sb, eye, "Proscenium/Linteau", lintel, 1.5f);
                    }
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

        static int LookUpCheck(StringBuilder sb, Vector3 eye, string label, Transform target, float stopBefore, Vector3? point = null)
        {
            if (target == null) { sb.AppendLine("  PROBLEME: " + label + " introuvable"); return 1; }
            Vector3 p = point ?? target.position;
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

        /// <summary>v8 : orchestre depuis le podium, poursuites allumées puis éteintes (enabled basculé temporairement sur les Lights du groupe Poursuites).</summary>
        public static void Capture(StringBuilder sb)
        {
            Vector3 eye = new Vector3(0f, 1.6f, 0f);
            GameObject env = GameObject.Find("Environment");
            Transform group = env != null ? env.transform.Find("OperaShell/Poursuites") : null;
            Light[] pours = group != null ? group.GetComponentsInChildren<Light>(true) : new Light[0];
            foreach (Light l in pours) l.enabled = true;
            CaptureView(sb, eye, Quaternion.identity, 65f, Path.Combine(Dir, "v8-orchestre-eclaire.png"));
            foreach (Light l in pours) l.enabled = false;
            try { CaptureView(sb, eye, Quaternion.identity, 65f, Path.Combine(Dir, "v8-orchestre-sans.png")); }
            finally { foreach (Light l in pours) l.enabled = true; }
            sb.AppendLine("Captures orchestre : " + pours.Length + " lumière(s) du groupe Poursuites allumées pour la première, éteintes pour la seconde, rallumées ensuite.");
        }

        public const int MaskLayer = 31; // calque libre (non nommé dans TagManager) utilisé le temps d'un rendu de masque

        public static void CaptureView(StringBuilder sb, Vector3 position, Quaternion rotation, float fov, string outPath)
        {
            Texture2D tex = Render(sb, position, rotation, fov, true, -1, CameraClearFlags.Skybox, Color.black, false);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                Color32[] px = tex.GetPixels32();
                long sum = 0;
                foreach (var p in px) sum += p.r + p.g + p.b;
                float avg = sum / (3f * px.Length);
                File.WriteAllBytes(outPath, tex.EncodeToPNG());
                sb.AppendLine("Capture écrite : " + outPath + " (" + tex.width + "x" + tex.height + ", depuis " + position.ToString("F1") + ", luminosité moyenne " + avg.ToString("F1") + "/255)");
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        /// <summary>v9 : capture (écrite dans outPath si non vide) puis, pour chaque groupe de renderers, couleur moyenne (0-255) des pixels de l'image couverts par
        /// ce groupe, obtenue par un rendu de masque (groupe seul sur le calque MaskLayer, fond magenta, sans post-process) ; coverage = part de l'image couverte.</summary>
        public static void CaptureAndMeasure(StringBuilder sb, Vector3 position, Quaternion rotation, float fov, string outPath, Renderer[][] groups, out Color[] means, out float[] coverages)
        {
            means = new Color[groups.Length];
            coverages = new float[groups.Length];
            Texture2D full = Render(sb, position, rotation, fov, true, -1, CameraClearFlags.Skybox, Color.black, false);
            try
            {
                if (!string.IsNullOrEmpty(outPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    File.WriteAllBytes(outPath, full.EncodeToPNG());
                    sb.AppendLine("Capture écrite : " + outPath + " (" + full.width + "x" + full.height + ", depuis " + position.ToString("F1") + ")");
                }
                Color32[] px = full.GetPixels32();
                for (int g = 0; g < groups.Length; g++)
                {
                    Renderer[] rs = groups[g];
                    var layers = new int[rs.Length];
                    for (int i = 0; i < rs.Length; i++) { layers[i] = rs[i].gameObject.layer; rs[i].gameObject.layer = MaskLayer; }
                    Texture2D mask = null;
                    try
                    {
                        mask = Render(sb, position, rotation, fov, false, 1 << MaskLayer, CameraClearFlags.SolidColor, new Color(1f, 0f, 1f, 1f), false);
                        Color32[] mp = mask.GetPixels32();
                        double r = 0, gg = 0, b = 0;
                        int n = 0;
                        for (int i = 0; i < mp.Length && i < px.Length; i++)
                        {
                            Color32 m = mp[i];
                            if (m.r == 255 && m.g == 0 && m.b == 255) continue;
                            r += px[i].r; gg += px[i].g; b += px[i].b; n++;
                        }
                        coverages[g] = px.Length > 0 ? n / (float)px.Length : 0f;
                        means[g] = n > 0 ? new Color((float)(r / n), (float)(gg / n), (float)(b / n), 255f) : Color.black;
                    }
                    finally
                    {
                        for (int i = 0; i < rs.Length; i++) rs[i].gameObject.layer = layers[i];
                        if (mask != null) UnityEngine.Object.DestroyImmediate(mask);
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(full); }
        }

        /// <summary>Rendu 1600 × 900 par une caméra temporaire (SingleCameraRequest URP, repli Camera.Render) ; la texture retournée est à détruire par l'appelant.</summary>
        static Texture2D Render(StringBuilder sb, Vector3 position, Quaternion rotation, float fov, bool postProcess, int cullingMask, CameraClearFlags clear, Color background, bool alpha)
        {
            var go = new GameObject("Maestro_TempCamera");
            go.hideFlags = HideFlags.HideAndDontSave;
            Camera cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(position, rotation);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.clearFlags = clear;
            cam.backgroundColor = background;
            cam.cullingMask = cullingMask;
            try { cam.GetUniversalAdditionalCameraData().renderPostProcessing = postProcess; } catch (Exception) { }
            const int w = 1600, h = 900;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();
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
                var tex = new Texture2D(w, h, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                return tex;
            }
            finally
            {
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
