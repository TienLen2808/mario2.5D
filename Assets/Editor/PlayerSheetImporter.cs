using UnityEditor;
using UnityEngine;

// Tự động chỉnh Import Settings cho các sprite sheet của game:
// Sprite, không scale về lũy thừa 2 (NPOT), không mipmap, không nén, nền trong suốt.
public class PlayerSheetImporter : AssetPostprocessor
{
    public static readonly string[] SheetNames = { "player_sheet", "ogre_sheet", "dragon_sheet", "dragon_fx", "hero_skills", "hero_avatar", "mobile_ui", "boss_shockwave", "boss_rock", "boss_marker", "boss_pillar", "hud_hearts", "coin_spin", "hero_aura" };

    public static bool IsSheet(string path)
    {
        if (path.Contains("/env_")) return true;   // ảnh môi trường: cỏ, đất, hoa, cây, nền
        foreach (string n in SheetNames) if (path.Contains(n)) return true;
        return false;
    }

    // Ogre là pixel art -> lọc Point cho nét sắc
    static bool IsPixelArt(string path) { return path.Contains("ogre_sheet"); }

    void OnPreprocessTexture()
    {
        if (!IsSheet(assetPath)) return;
        Apply((TextureImporter)assetImporter);
    }

    public static void Apply(TextureImporter ti)
    {
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.filterMode = IsPixelArt(ti.assetPath) ? FilterMode.Point : FilterMode.Bilinear;
        // Hoạ tiết cỏ / đất lặp lại liền mạch trên mesh mặt đất
        if (IsTiling(ti.assetPath)) { ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = ti.assetPath.Contains("env_grass_lip") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat; }
        else ti.wrapMode = TextureWrapMode.Clamp;
        ti.maxTextureSize = 4096; // dragon_sheet rộng 2718 px

        // Điện thoại: nén ASTC (nhẹ RAM hơn 4-8 lần). Pixel art (Ogre) dùng 4x4 cho nét sắc.
        TextureImporterFormat fmt = IsPixelArt(ti.assetPath) ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
        foreach (string platform in MobilePlatforms)
        {
            TextureImporterPlatformSettings ps = ti.GetPlatformTextureSettings(platform);
            ps.overridden = true;
            ps.maxTextureSize = 4096;
            ps.format = fmt;
            ti.SetPlatformTextureSettings(ps);
        }
    }

    static bool IsTiling(string path)
    {
        return path.Contains("env_grass_top") || path.Contains("env_dirt") || path.Contains("env_grass_lip");
    }

    static readonly string[] MobilePlatforms = { "Android", "iPhone" };

    public static bool NeedsFix(TextureImporter ti)
    {
        return ti.textureType != TextureImporterType.Sprite
            || ti.npotScale != TextureImporterNPOTScale.None
            || ti.mipmapEnabled
            || ti.textureCompression != TextureImporterCompression.Uncompressed
            || ti.maxTextureSize < 4096
            || (IsTiling(ti.assetPath) && ti.wrapModeU != TextureWrapMode.Repeat)
            || !ti.GetPlatformTextureSettings("Android").overridden
            || !ti.GetPlatformTextureSettings("iPhone").overridden
            || ti.filterMode != (IsPixelArt(ti.assetPath) ? FilterMode.Point : FilterMode.Bilinear);
    }
}

// Khi mở Unity / compile xong: nếu sheet nào import sai thì import lại 1 lần.
[InitializeOnLoad]
public static class PlayerSheetReimport
{
    static PlayerSheetReimport()
    {
        EditorApplication.delayCall += () =>
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!PlayerSheetImporter.IsSheet(path)) continue;
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti != null && PlayerSheetImporter.NeedsFix(ti))
                {
                    PlayerSheetImporter.Apply(ti);
                    ti.SaveAndReimport();
                    Debug.Log("Đã chỉnh lại Import Settings cho " + path);
                }
            }
        };
    }
}
