using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Maestro.EditorTools
{
    /// <summary>Spécification d'un matériau de prop, d'après blender/README.md (un matériau par matière, M_&lt;Prop&gt;_&lt;Matiere&gt;).</summary>
    public class PropMaterialSpec
    {
        public string Prop;
        public string Matiere;
        public bool BaseColor;
        public bool Roughness;   // → texture _MS générée (R = metallic, A = 1 - roughness)
        public bool MetallicMap;
        public bool Normal;
        public Color SolidColor = Color.white;
        public float SolidRoughness = 0.5f;
        public float SolidMetallic = 0f;
        public bool Emissive;
        public Color EmissionColor = Color.black;
        public float EmissionStrength = 1f;

        public string Name { get { return "M_" + Prop + "_" + Matiere; } }
        public string TexturePrefix { get { return "T_" + Prop + "_" + Matiere + "_"; } }
    }

    /// <summary>
    /// Import des 11 props Blender : réglages d'import (AssetPostprocessor), matériaux URP/Lit créés par script dans
    /// Art/Materials/Props/, remap des slots du FBX par nom de matériau. Menu Tools/Maestro/Import Props.
    /// </summary>
    public static class MaestroProps
    {
        public const string ModelsDir = "Assets/_OrchestraRave/Art/Models";
        public const string PropMatDir = "Assets/_OrchestraRave/Art/Materials/Props";
        public static readonly string[] PropNames = { "Baguette", "SocleBaguette", "Pupitre", "Speaker", "Lustre", "Luth", "Tambour", "Cor", "Flute", "Torche", "Banniere" };

        // Couleurs émissives du README ; intensités = « strength » des scripts Blender (facteur linéaire).
        public static readonly Color EmissiveCyan = new Color(0.05f, 0.90f, 1.00f);
        public static readonly Color EmissiveMagenta = new Color(1.00f, 0.05f, 0.65f);
        public static readonly Color EmissiveWarm = new Color(1.00f, 0.82f, 0.55f);
        public static readonly Color EmissiveOrange = new Color(1.00f, 0.42f, 0.06f);

        public static readonly PropMaterialSpec[] Specs =
        {
            Tex("Baguette", "Poignee", true, true, false, true),
            Solid("Baguette", "Chrome", new Color(0.92f, 0.93f, 0.95f), 0.08f, 1f),
            Emis("Baguette", EmissiveCyan, 6f),
            Tex("SocleBaguette", "Metal", true, true, false, false), // metallic constant 1 → R = 1 dans la _MS
            Emis("SocleBaguette", EmissiveCyan, 6f),
            Tex("Pupitre", "Bois", true, true, false, true),
            Tex("Pupitre", "Laiton", true, true, true, false),
            Tex("Speaker", "Noir", true, true, false, true),
            Tex("Speaker", "Metal", true, true, false, false),
            Tex("Speaker", "Grille", true, true, true, true),
            Emis("Speaker", EmissiveMagenta, 5f),
            Tex("Lustre", "Laiton", true, true, true, false),
            Solid("Lustre", "Cristal", new Color(0.92f, 0.95f, 1.00f), 0.05f, 0f),
            Emis("Lustre", EmissiveWarm, 6f),
            Tex("Luth", "Bois", true, true, false, true),
            Tex("Luth", "BoisSombre", true, true, false, true),
            Solid("Luth", "Cordes", new Color(0.85f, 0.80f, 0.65f), 0.35f, 0.7f),
            Tex("Tambour", "Bois", true, true, false, true),
            Tex("Tambour", "Peau", true, true, false, true),
            Solid("Tambour", "Corde", new Color(0.52f, 0.42f, 0.26f), 0.9f, 0f),
            Tex("Cor", "Laiton", true, true, true, false),
            Tex("Flute", "Bois", true, true, false, true),
            Tex("Flute", "Laiton", true, true, true, false),
            Solid("Flute", "Sombre", new Color(0.01f, 0.008f, 0.006f), 0.8f, 0f),
            Tex("Torche", "Fer", true, true, false, false), // metallic constant 1 → R = 1 dans la _MS
            Tex("Torche", "Bois", true, true, false, true),
            Solid("Torche", "Tissu", new Color(0.07f, 0.045f, 0.03f), 0.95f, 0f),
            Emis("Torche", EmissiveOrange, 6f),
            Tex("Banniere", "VeloursRouge", true, true, false, true),
            Tex("Banniere", "VeloursBleu", true, true, false, true),
            Tex("Banniere", "Bois", true, true, false, true),
            Tex("Banniere", "Laiton", true, true, true, false),
        };

        static PropMaterialSpec Tex(string prop, string mat, bool bc, bool rough, bool metal, bool normal)
        {
            return new PropMaterialSpec { Prop = prop, Matiere = mat, BaseColor = bc, Roughness = rough, MetallicMap = metal, Normal = normal };
        }

        static PropMaterialSpec Solid(string prop, string mat, Color c, float rough, float metal)
        {
            return new PropMaterialSpec { Prop = prop, Matiere = mat, SolidColor = c, SolidRoughness = rough, SolidMetallic = metal };
        }

        static PropMaterialSpec Emis(string prop, Color c, float strength)
        {
            return new PropMaterialSpec { Prop = prop, Matiere = "Emissive", Emissive = true, EmissionColor = c, EmissionStrength = strength };
        }

        public static string FbxPath(string prop) { return ModelsDir + "/" + prop + "/" + prop + ".fbx"; }
        public static string MaterialPath(string name) { return PropMatDir + "/" + name + ".mat"; }
        public static GameObject LoadModel(string prop) { return AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath(prop)); }
        public static bool Available(string prop) { return LoadModel(prop) != null; }

        public static PropMaterialSpec FindSpec(string materialName)
        {
            foreach (PropMaterialSpec s in Specs) if (s.Name == materialName) return s;
            return null;
        }

        [MenuItem("Tools/Maestro/Import Props")]
        public static void ImportFromMenu()
        {
            MaestroRunner.Execute("props");
        }

        /// <summary>Crée/met à jour les 32 matériaux puis remappe les slots des 11 FBX. Idempotent.</summary>
        public static void ImportProps(StringBuilder sb)
        {
            int problems = 0;
            EnsureFolder(PropMatDir);
            int created = 0;
            foreach (PropMaterialSpec s in Specs)
            {
                if (CreateMaterial(s, sb, ref problems)) created++;
            }
            AssetDatabase.SaveAssets();
            sb.AppendLine(created + "/" + Specs.Length + " matériaux URP/Lit dans " + PropMatDir);
            foreach (string prop in PropNames) RemapModel(prop, sb, ref problems);
            AssetDatabase.SaveAssets();
            sb.AppendLine(problems == 0 ? "IMPORT PROPS: OK" : "IMPORT PROPS: " + problems + " problème(s)");
            if (problems > 0) throw new Exception("Import des props incomplet : " + problems + " problème(s), voir ci-dessus.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Texture2D LoadTexture(PropMaterialSpec s, string canal, StringBuilder sb, ref int problems)
        {
            string path = ModelsDir + "/" + s.Prop + "/" + s.TexturePrefix + canal + ".png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) { sb.AppendLine("PROBLEME: texture manquante " + path); problems++; }
            return t;
        }

        static bool CreateMaterial(PropMaterialSpec s, StringBuilder sb, ref int problems)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new Exception("Shader 'Universal Render Pipeline/Lit' introuvable.");
            string path = MaterialPath(s.Name);
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(shader);
            else if (m.shader != shader) m.shader = shader;

            m.SetTexture("_BaseMap", null);
            m.SetTexture("_MetallicGlossMap", null);
            m.SetTexture("_BumpMap", null);
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.DisableKeyword("_NORMALMAP");
            m.DisableKeyword("_EMISSION");
            m.SetFloat("_Cull", 2f);
            m.SetFloat("_SmoothnessTextureChannel", 0f); // Metallic Alpha

            if (s.Emissive)
            {
                m.SetColor("_BaseColor", s.EmissionColor);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0.5f);
                // URP 17 n'active _EMISSION que si le flag Realtime ou Baked est posé (BaseShaderGUI.SetMaterialKeywords) :
                // « Baked » = pas de GI temps réel, et sans lightmap bakée aucune contribution GI.
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                m.SetColor("_EmissionColor", s.EmissionColor * s.EmissionStrength);
                m.EnableKeyword("_EMISSION");
            }
            else
            {
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                if (s.BaseColor)
                {
                    m.SetTexture("_BaseMap", LoadTexture(s, "BaseColor", sb, ref problems));
                    m.SetColor("_BaseColor", Color.white);
                }
                else
                {
                    m.SetColor("_BaseColor", s.SolidColor);
                }
                if (s.Roughness)
                {
                    m.SetTexture("_MetallicGlossMap", LoadTexture(s, "MS", sb, ref problems));
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                    m.SetFloat("_Smoothness", 1f);
                    m.SetFloat("_Metallic", 1f);
                }
                else
                {
                    m.SetFloat("_Smoothness", 1f - s.SolidRoughness);
                    m.SetFloat("_Metallic", s.SolidMetallic);
                }
                if (s.Normal)
                {
                    m.SetTexture("_BumpMap", LoadTexture(s, "Normal", sb, ref problems));
                    m.EnableKeyword("_NORMALMAP");
                    m.SetFloat("_BumpScale", 1f);
                }
            }
            if (isNew) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
            return true;
        }

        /// <summary>Remappe chaque slot de matériau du FBX (par nom) vers Props/M_&lt;Prop&gt;_&lt;Matiere&gt;.mat, puis vérifie qu'aucun matériau incorporé ne subsiste.</summary>
        static void RemapModel(string prop, StringBuilder sb, ref int problems)
        {
            string fbx = FbxPath(prop);
            var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (mi == null) { sb.AppendLine("PROBLEME: FBX manquant " + fbx); problems++; return; }
            var names = new HashSet<string>();
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(fbx)) if (o is Material) names.Add(o.name);
            Dictionary<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> map = mi.GetExternalObjectMap();
            foreach (var kv in map) if (kv.Key.type == typeof(Material)) names.Add(kv.Key.name);
            int changed = 0;
            var missing = new List<string>();
            foreach (string n in names)
            {
                Material target = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(n));
                if (target == null) { missing.Add(n); continue; }
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                UnityEngine.Object current;
                if (map.TryGetValue(id, out current) && current == target) continue;
                mi.AddRemap(id, target);
                changed++;
            }
            if (changed > 0)
            {
                mi.SaveAndReimport();
            }
            int embedded = 0;
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(fbx)) if (o is Material) embedded++;
            bool ok = missing.Count == 0 && embedded == 0 && names.Count > 0;
            sb.AppendLine((ok ? "FBX OK : " : "PROBLEME FBX : ") + prop + " : " + names.Count + " slot(s) " + string.Join("/", names) + ", " + changed + " remap(s) ajouté(s), " + embedded + " matériau(x) incorporé(s) restant(s)" + (missing.Count > 0 ? ", sans matériau : " + string.Join(", ", missing) : ""));
            if (!ok) problems++;
        }
    }

    /// <summary>Réglages d'import appliqués avant la première importation de tout ce qui arrive sous Art/Models/.</summary>
    class MaestroPropPostprocessor : AssetPostprocessor
    {
        bool IsProp { get { return assetPath.StartsWith(MaestroProps.ModelsDir + "/"); } }

        void OnPreprocessModel()
        {
            if (!IsProp) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileUnits = true;          // Convert Units
            mi.bakeAxisConversion = true;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = false;
            mi.addCollider = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            // Les matériaux sont créés par script et remappés par nom de slot : l'import « via MaterialDescription » ne sert
            // qu'à exposer les noms des slots, aucun matériau généré n'est utilisé une fois le remap fait.
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            mi.materialSearch = ModelImporterMaterialSearch.Everywhere;
        }

        void OnPreprocessTexture()
        {
            if (!IsProp) return;
            var ti = (TextureImporter)assetImporter;
            string n = Path.GetFileNameWithoutExtension(assetPath);
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.mipmapEnabled = true;
            if (n.EndsWith("_Normal") || n.EndsWith("_N"))
            {
                ti.textureType = TextureImporterType.NormalMap;
            }
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = n.EndsWith("_BaseColor");
                ti.alphaSource = n.EndsWith("_MS") ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                ti.alphaIsTransparency = false;
            }
        }
    }
}
