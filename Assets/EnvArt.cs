using UnityEngine;

// Nạp & cắt toàn bộ ảnh môi trường (Resources/env_*.png) dùng chung cho đường đi, trang trí, phông nền.
// Mặt đất / vách đất / bục dùng MESH ĐẶC (có ghi chiều sâu) -> không bị vẽ đè, chỗ nối 2 mảnh liền mạch.
public static class EnvArt
{
    public const float Tile = 2f;   // 1 ô hoạ tiết cỏ / đất = 2 unit

    public static Sprite GrassTop, Dirt, GrassLip, Sky, Mountains, Hills;
    public static Sprite[] Decor;   // 0-3 hoa, 4 cỏ, 5 bụi, 6 đá, 7 nấm
    public static Sprite[] Trees;   // 2 loại cây
    public static Sprite[] Clouds;  // 2 loại mây

    // Material cho mesh mặt đất (Resources/EnvMaterials/*.mat do Editor tự tạo; thiếu thì tạo lúc chạy)
    public static Material GrassMat, DirtMat, DirtShadeMat, DirtDarkMat, LipMat;

    // Chiều cao phần hình thật trong ô 256px (để scale cho đúng kích thước mong muốn)
    public static readonly float[] DecorContent = { 0.57f, 0.57f, 0.57f, 0.57f, 0.37f, 0.62f, 0.37f, 0.55f };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { GrassTop = null; GrassMat = null; }

    public static bool Load()
    {
        if (GrassTop != null && GrassMat != null) return true;
        GrassTop = Single("env_grass_top", 128f, new Vector2(0.5f, 0.5f));
        Dirt = Single("env_dirt", 128f, new Vector2(0.5f, 1f));
        GrassLip = Single("env_grass_lip", 128f, new Vector2(0.5f, 1f));
        Sky = Single("env_sky", 1f, new Vector2(0.5f, 0.5f));
        Mountains = Single("env_mountains", 1024f / 120f, new Vector2(0.5f, 0f));   // 1 ô = 120 unit
        Hills = Single("env_hills", 1024f / 60f, new Vector2(0.5f, 0f));            // 1 ô = 60 unit
        Decor = Strip("env_decor", 8, 256, 256, 256f, new Vector2(0.5f, 0.01f));
        Trees = Strip("env_trees", 2, 512, 512, 128f, new Vector2(0.5f, 0.01f));
        Clouds = Strip("env_clouds", 2, 512, 256, 16f, new Vector2(0.5f, 0.5f));

        GrassMat = LoadMat("Grass", "env_grass_top", Color.white, false);
        DirtMat = LoadMat("Dirt", "env_dirt", Color.white, false);
        DirtShadeMat = LoadMat("DirtShade", "env_dirt", new Color(0.78f, 0.78f, 0.78f), false);
        DirtDarkMat = LoadMat("DirtDark", "env_dirt", new Color(0.55f, 0.55f, 0.55f), false);
        LipMat = LoadMat("GrassLip", "env_grass_lip", Color.white, true);
        return GrassTop != null && GrassMat != null;
    }

    static Material LoadMat(string name, string tex, Color c, bool cutout)
    {
        Material m = Resources.Load<Material>("EnvMaterials/" + name);
        if (m != null) return m;
        return CreateMaterial(Resources.Load<Texture2D>(tex), c, cutout);
    }

    // Dùng chung cho Editor (tạo file .mat) và lúc chạy (dự phòng)
    public static Material CreateMaterial(Texture2D tex, Color c, bool cutout)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Texture");
        Material m = new Material(sh);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        m.mainTexture = tex;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        else if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        m.SetFloat("_Cull", 0f);   // vẽ cả 2 mặt
        if (cutout)
        {
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = 2450;
        }
        return m;
    }

    static Sprite Single(string res, float ppu, Vector2 pivot)
    {
        Texture2D t = Resources.Load<Texture2D>(res);
        if (t == null) { Debug.LogError("EnvArt: không tìm thấy Resources/" + res + ".png"); return null; }
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), pivot, ppu, 0, SpriteMeshType.FullRect);
    }

    static Sprite[] Strip(string res, int count, int w, int h, float ppu, Vector2 pivot)
    {
        Texture2D t = Resources.Load<Texture2D>(res);
        Sprite[] s = new Sprite[count];
        if (t == null) { Debug.LogError("EnvArt: không tìm thấy Resources/" + res + ".png"); return s; }
        for (int i = 0; i < count; i++)
            s[i] = Sprite.Create(t, new Rect(i * w, 0, w, h), pivot, ppu, 0, SpriteMeshType.FullRect);
        return s;
    }

    // SpriteRenderer lát gạch (Tiled) — dùng cho phông nền
    public static SpriteRenderer Tiled(Transform parent, string name, Sprite s, Vector2 size, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.tileMode = SpriteTileMode.Continuous;
        sr.size = size;
        sr.sortingOrder = order;
        return sr;
    }

    // ------------------------------------------------------------------
    // Mesh hình chữ nhật (1 mặt) — p0 + du + dv, UV tuỳ chỉnh
    public static MeshFilter Quad(Transform parent, string name, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        Mesh m = new Mesh();
        m.name = name;
        m.vertices = new Vector3[4];
        m.uv = new Vector2[4];
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mf.sharedMesh = m;
        return mf;
    }

    public static void SetQuad(MeshFilter mf, Vector3 p0, Vector3 du, Vector3 dv, Vector2 uv0, Vector2 uv1)
    {
        Mesh m = mf.sharedMesh;
        m.vertices = new[] { p0, p0 + du, p0 + dv, p0 + du + dv };
        m.uv = new[] { uv0, new Vector2(uv1.x, uv0.y), new Vector2(uv0.x, uv1.y), uv1 };
        m.RecalculateBounds();
        mf.gameObject.SetActive(true);
    }
}
