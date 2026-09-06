using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-off setup: turns the raw Noto Sans script TTFs in Assets/Fonts into
/// TMP_FontAsset instances and registers them as global TMP fallback fonts,
/// so any existing/future TextMeshPro text in the project can render
/// Devanagari/Gujarati/Tamil/Kannada glyphs without needing every text
/// component's own font field changed - the project's only font
/// (LiberationSans SDF) is Latin-only, so any character it can't draw falls
/// through to these instead of rendering as a tofu box.
/// </summary>
public static class MultilingualFontSetupUtility
{
    private static readonly (string fontFile, string label)[] Fonts =
    {
        ("NotoSansDevanagari-Regular.ttf", "NotoSansDevanagari"),
        ("NotoSansGujarati-Regular.ttf", "NotoSansGujarati"),
        ("NotoSansTamil-Regular.ttf", "NotoSansTamil"),
        ("NotoSansKannada-Regular.ttf", "NotoSansKannada"),
    };

    private const string FontsFolder = "Assets/Fonts";
    private const string TmpFontAssetsFolder = "Assets/Fonts/TMP";

    public static string SetupFallbackFonts()
    {
        if (!AssetDatabase.IsValidFolder(TmpFontAssetsFolder))
        {
            AssetDatabase.CreateFolder(FontsFolder, "TMP");
        }

        var createdAssets = new List<TMP_FontAsset>();
        var report = new System.Text.StringBuilder();

        foreach ((string fontFile, string label) in Fonts)
        {
            string sourcePath = $"{FontsFolder}/{fontFile}";
            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (sourceFont == null)
            {
                report.AppendLine($"SKIP {label}: could not load Font at {sourcePath} (not imported yet?).");
                continue;
            }

            string tmpAssetPath = $"{TmpFontAssetsFolder}/{label} SDF.asset";
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(tmpAssetPath);
            if (existing != null)
            {
                createdAssets.Add(existing);
                report.AppendLine($"REUSE {label}: TMP_FontAsset already exists at {tmpAssetPath}.");
                continue;
            }

            TMP_FontAsset tmpFontAsset = TMP_FontAsset.CreateFontAsset(sourceFont);
            if (tmpFontAsset == null)
            {
                report.AppendLine($"FAIL {label}: TMP_FontAsset.CreateFontAsset returned null.");
                continue;
            }

            AssetDatabase.CreateAsset(tmpFontAsset, tmpAssetPath);
            createdAssets.Add(tmpFontAsset);
            report.AppendLine($"CREATED {label}: {tmpAssetPath}");
        }

        AssetDatabase.SaveAssets();

        // Register on the global TMP Settings fallback list via SerializedObject
        // rather than the public TMP_Settings API, since the exact accessor name
        // has changed across TMPro package versions - this is stable regardless.
        TMP_Settings settings = TMP_Settings.instance;
        if (settings == null)
        {
            report.AppendLine("ERROR: TMP_Settings.instance is null - could not register fallback fonts.");
            return report.ToString();
        }

        var so = new SerializedObject(settings);
        SerializedProperty fallbackListProp = so.FindProperty("m_fallbackFontAssets");
        if (fallbackListProp == null)
        {
            report.AppendLine("ERROR: TMP Settings has no 'm_fallbackFontAssets' field on this TMPro version - fallback fonts were created but not registered. Assign them manually via Project Settings > TextMeshPro > Settings > Fallback Font Assets.");
            return report.ToString();
        }

        foreach (TMP_FontAsset asset in createdAssets)
        {
            bool alreadyPresent = false
                ;
            for (int i = 0; i < fallbackListProp.arraySize; i++)
            {
                if (fallbackListProp.GetArrayElementAtIndex(i).objectReferenceValue == asset)
                {
                    alreadyPresent = true;
                    break;
                }
            }

            if (alreadyPresent)
            {
                continue;
            }

            int newIndex = fallbackListProp.arraySize;
            fallbackListProp.InsertArrayElementAtIndex(newIndex);
            fallbackListProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = asset;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        report.AppendLine($"Registered {createdAssets.Count} fallback font(s) on TMP Settings.");
        return report.ToString();
    }
}
