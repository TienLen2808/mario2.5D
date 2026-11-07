using UnityEditor;
using UnityEngine;

// Tự tạo các material mặt đất trong Assets/Resources/EnvMaterials (để chắc chắn shader được đưa vào bản build).
[InitializeOnLoad]
public static class EnvMaterialsBuilder
{
    const string Folder = "Assets/Resources/EnvMaterials";

    static EnvMaterialsBuilder()
    {
        EditorApplication.delayCall += Build;
    }

    static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "EnvMaterials");
        Make("Grass", "env_grass_top", Color.white, false);
        Make("Dirt", "env_dirt", Color.white, false);
        Make("DirtShade", "env_dirt", new Color(0.78f, 0.78f, 0.78f), false);
        Make("DirtDark", "env_dirt", new Color(0.55f, 0.55f, 0.55f), false);
        Make("GrassLip", "env_grass_lip", Color.white, true);
        AssetDatabase.SaveAssets();
    }

    static void Make(string name, string tex, Color c, bool cutout)
    {
        string path = Folder + "/" + name + ".mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/" + tex + ".png");
        if (t == null) return;
        AssetDatabase.CreateAsset(EnvArt.CreateMaterial(t, c, cutout), path);
    }
}
