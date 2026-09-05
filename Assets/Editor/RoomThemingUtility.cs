using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates a simple procedural acoustic-panel-style wall texture (no
/// external downloads) and applies it to the room's walls, and computes a
/// mild fan/trapezoid shape for the side walls so the room reads more like
/// an auditorium narrowing toward the stage.
/// </summary>
public static class RoomThemingUtility
{
    public static void GenerateAndApplyWallMaterialDefault()
    {
        GenerateAndApplyWallMaterial(
            "Assets/Textures/AuditoriumWallPanel.png",
            "Assets/Materials/AuditoriumWall.mat",
            "Room_Wall_Left,Room_Wall_Right,Room_Wall_Front",
            0.7f, 0.7f);
    }

    public static void GenerateAndApplyWallMaterial(
        string texturePath, string materialPath,
        string wallObjectNamesCsv, float tilesPerMeterX, float tilesPerMeterY)
    {
        Texture2D tex = GeneratePanelTexture();
        byte[] png = tex.EncodeToPNG();
        Directory.CreateDirectory(Path.GetDirectoryName(texturePath));
        File.WriteAllBytes(texturePath, png);
        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer != null)
        {
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        Texture2D importedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material mat = new Material(shader);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", importedTex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", importedTex);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.25f);

        AssetDatabase.CreateAsset(mat, materialPath);
        AssetDatabase.SaveAssets();

        string[] names = wallObjectNamesCsv.Split(',');
        foreach (string rawName in names)
        {
            string name = rawName.Trim();
            if (name.Length == 0) continue;

            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                Debug.LogWarning($"RoomThemingUtility: wall '{name}' not found.");
                continue;
            }

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null) continue;

            renderer.sharedMaterial = mat;

            Bounds b = renderer.bounds;
            float width = Mathf.Max(b.size.x, b.size.z); // whichever is the wall's long axis
            float height = b.size.y;
            renderer.sharedMaterial.mainTextureScale = new Vector2(
                Mathf.Max(1f, width * tilesPerMeterX),
                Mathf.Max(1f, height * tilesPerMeterY));

            EditorUtility.SetDirty(go);
        }
    }

    private static Texture2D GeneratePanelTexture()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);

        Color panelA = new Color(0.13f, 0.11f, 0.16f);   // deep plum/navy panel
        Color panelB = new Color(0.17f, 0.14f, 0.20f);   // slightly lighter alternate panel
        Color seamLine = new Color(0.06f, 0.05f, 0.08f); // dark seam between panels
        Color trimLine = new Color(0.55f, 0.45f, 0.25f); // warm wood/brass trim accent

        const int panelWidthPx = 32;    // vertical panel stripes
        const int seamWidthPx = 2;
        int trimY = (int)(size * 0.18f); // a horizontal trim band near the top (chair-rail style, inverted since we tile)
        const int trimHeightPx = 4;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int stripeIndex = x / panelWidthPx;
                int xInStripe = x % panelWidthPx;

                Color c = (stripeIndex % 2 == 0) ? panelA : panelB;

                if (xInStripe < seamWidthPx || xInStripe > panelWidthPx - seamWidthPx)
                {
                    c = seamLine;
                }

                if (Mathf.Abs(y - trimY) < trimHeightPx)
                {
                    c = trimLine;
                }

                // subtle vertical shading gradient for a bit of depth
                float shade = 0.92f + 0.08f * Mathf.PerlinNoise(x * 0.05f, y * 0.05f);
                c *= shade;
                c.a = 1f;

                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        return tex;
    }

    /// <summary>
    /// Reshapes the two side walls into a mild fan/trapezoid: the given
    /// stageEndX offset near backZ (upstage side) is narrower than the
    /// original backX offset near frontZ (audience/entrance side), tapering
    /// the room toward the stage.
    /// </summary>
    public static void ApplyFanShape(
        string leftWallName, string rightWallName,
        float frontZ, float backZ,
        float outerX, float stageEndX,
        float wallHeight, float wallThickness)
    {
        SetFannedWall(leftWallName, -outerX, frontZ, -stageEndX, backZ, wallHeight, wallThickness);
        SetFannedWall(rightWallName, outerX, frontZ, stageEndX, backZ, wallHeight, wallThickness);
    }

    private static void SetFannedWall(
        string wallName,
        float x1, float z1, float x2, float z2,
        float wallHeight, float wallThickness)
    {
        GameObject go = GameObject.Find(wallName);
        if (go == null)
        {
            Debug.LogError($"RoomThemingUtility: wall '{wallName}' not found.");
            return;
        }

        float dx = x2 - x1;
        float dz = z2 - z1;
        float length = Mathf.Sqrt(dx * dx + dz * dz);
        float angleY = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;

        Vector3 midpoint = new Vector3((x1 + x2) * 0.5f, wallHeight * 0.5f, (z1 + z2) * 0.5f);

        go.transform.SetPositionAndRotation(midpoint, Quaternion.Euler(0f, angleY, 0f));
        go.transform.localScale = new Vector3(wallThickness, wallHeight, length);

        EditorUtility.SetDirty(go);
    }
}
