using NetcodeSample.Presentation;
using UnityEditor;
using UnityEngine;

namespace NetcodeSample.Editor
{
    /// <summary>Creates the presentation settings asset and its materials (URP Lit/Unlit, opaque and transparent).</summary>
    public static class PresentationAssets
    {
        private const string SettingsFolder = "Assets/NetcodeSample/Settings";
        private const string MaterialsFolder = SettingsFolder + "/Materials";
        private const string SettingsPath = SettingsFolder + "/Presentation.asset";
        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string UnlitShader = "Universal Render Pipeline/Unlit";

        /// <summary>Loads the settings, creating the asset and any missing material.</summary>
        public static PresentationSettings LoadOrCreate()
        {
            EnsureFolder("Assets/NetcodeSample", "Settings");
            EnsureFolder(SettingsFolder, "Materials");

            PresentationSettings settings = AssetDatabase.LoadAssetAtPath<PresentationSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PresentationSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            SerializedObject serialized = new(settings);
            Assign(serialized, "_unitMaterial", "Unit", LitShader, transparent: false);
            Assign(serialized, "_fadeMaterial", "UnitFade", LitShader, transparent: true);
            Assign(serialized, "_baseMaterial", "Base", LitShader, transparent: false);
            Assign(serialized, "_effectMaterial", "Effect", UnlitShader, transparent: true);
            Assign(serialized, "_barMaterial", "Bar", UnlitShader, transparent: false);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return settings;
        }

        private static void Assign(SerializedObject settings, string property, string name, string shader, bool transparent)
        {
            SerializedProperty field = settings.FindProperty(property);
            if (field.objectReferenceValue != null)
            {
                return;
            }

            string path = $"{MaterialsFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shader)) { name = name };
                material.SetColor("_BaseColor", Color.white);
                if (material.HasProperty("_Smoothness"))
                {
                    material.SetFloat("_Smoothness", 0.25f);
                }

                if (transparent)
                {
                    // URP's own inspector logic derives blend modes, keywords and render queue from these.
                    material.SetFloat("_Surface", (float)BaseShaderGUI.SurfaceType.Transparent);
                    material.SetFloat("_Blend", (float)BaseShaderGUI.BlendMode.Alpha);
                }

                BaseShaderGUI.SetMaterialKeywords(material);
                AssetDatabase.CreateAsset(material, path);
            }

            field.objectReferenceValue = material;
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }
    }
}
