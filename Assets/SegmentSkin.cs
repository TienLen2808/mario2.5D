using System.Collections.Generic;
using UnityEngine;

// "Da" cho 1 mảnh map (Segment_1):
//   - Ẩn khối hộp xám, thay bằng MESH ĐẶC: mặt cỏ + mép cỏ rủ + vách đất có sỏi (vẫn giữ Collider của Ground)
//     Mesh có ghi chiều sâu nên vách đất không bao giờ bị vẽ đè lên mặt cỏ.
//     Toạ độ hoạ tiết (UV) tính theo vị trí thế giới -> cỏ / đất nối liền giữa các mảnh, không lộ đường viền.
//   - Hoa, cỏ, bụi, đá, nấm, cây to random mỗi lần mảnh được dùng lại
//   - Bục đất nổi ĐẶC (dày, có cỏ trên mặt): nhảy lên từ đầu / cuối bục, đi ra mép để xuống
public class SegmentSkin : MonoBehaviour
{
    public const float WallDepth = 14f;          // vách đất kéo sâu xuống để che khoảng trống giữa các độ cao
    const float BackExtra = 2.5f;                // mặt cỏ kéo dài ra phía sau (chỉ hình) để cây to có chỗ đứng
    const int MaxDecor = 26, MaxPlatforms = 3;
    const float PlatformThickness = 1.1f;        // bục dày như 1 khối đất nổi

    float minX, maxX, topY, frontZ, backZ;
    public float Width { get { return maxX - minX; } }

    MeshFilter top, front, lip, sideL, sideR;
    readonly List<SpriteRenderer> decor = new List<SpriteRenderer>();
    readonly List<Platform> platforms = new List<Platform>();
    bool built;

    class Platform
    {
        public GameObject root;
        public BoxCollider col;
        public MeshFilter top, front, lip, under, sideL, sideR, back;
        public float x, y, w;
    }

    public struct PlatformInfo { public Vector3 topCenter; public float width; }

    static PhysicsMaterial noFriction;
    static PhysicsMaterial NoFriction
    {
        get
        {
            if (noFriction == null)
            {
                noFriction = new PhysicsMaterial("PlatformNoFriction");
                noFriction.dynamicFriction = 0f;
                noFriction.staticFriction = 0f;
                noFriction.frictionCombine = PhysicsMaterialCombine.Minimum;
                noFriction.bounciness = 0f;
            }
            return noFriction;
        }
    }

    // ------------------------------------------------------------------
    public void Build(Transform ground)
    {
        if (built || !EnvArt.Load()) return;
        built = true;

        Renderer gr = ground.GetComponent<Renderer>();
        Bounds b = gr != null ? gr.bounds : new Bounds(ground.position, new Vector3(20f, 1f, 3f));
        Vector3 o = transform.position;
        minX = b.min.x - o.x; maxX = b.max.x - o.x;
        topY = b.max.y - o.y;
        frontZ = b.min.z - o.z; backZ = b.max.z - o.z;
        if (gr != null) gr.forceRenderingOff = true;   // ẩn hộp xám (vẫn giữ bounds để đo kích thước)
        Collider gc = ground.GetComponent<Collider>();
        if (gc != null) gc.sharedMaterial = NoFriction; // không bị dính vào vách bậc thềm khi nhảy sát

        Transform skin = new GameObject("Skin").transform;
        skin.SetParent(transform, false);

        top = EnvArt.Quad(skin, "GrassTop", EnvArt.GrassMat);
        front = EnvArt.Quad(skin, "DirtFront", EnvArt.DirtMat);
        lip = EnvArt.Quad(skin, "GrassLip", EnvArt.LipMat);
        sideL = EnvArt.Quad(skin, "DirtLeft", EnvArt.DirtShadeMat);
        sideR = EnvArt.Quad(skin, "DirtRight", EnvArt.DirtShadeMat);

        for (int i = 0; i < MaxDecor; i++)
        {
            GameObject d = new GameObject("Decor" + i);
            d.transform.SetParent(skin, false);
            decor.Add(d.AddComponent<SpriteRenderer>());
            d.SetActive(false);
        }
        for (int i = 0; i < MaxPlatforms; i++) platforms.Add(MakePlatform(skin, i));
    }

    Platform MakePlatform(Transform parent, int i)
    {
        Platform p = new Platform();
        p.root = new GameObject("Platform" + i);
        p.root.transform.SetParent(parent, false);
        // Bục ĐẶC: không nhảy xuyên từ dưới lên, phải nhảy lên từ đầu / cuối bục; đứng trên nhảy vẫn đáp lại trên bục
        p.col = p.root.AddComponent<BoxCollider>();
        p.col.sharedMaterial = NoFriction;   // không bị dính vào vách bục khi nhảy sát cạnh
        p.top = EnvArt.Quad(p.root.transform, "Top", EnvArt.GrassMat);
        p.front = EnvArt.Quad(p.root.transform, "Front", EnvArt.DirtMat);
        p.lip = EnvArt.Quad(p.root.transform, "Lip", EnvArt.LipMat);
        p.under = EnvArt.Quad(p.root.transform, "Under", EnvArt.DirtDarkMat);
        p.sideL = EnvArt.Quad(p.root.transform, "SideL", EnvArt.DirtShadeMat);
        p.sideR = EnvArt.Quad(p.root.transform, "SideR", EnvArt.DirtShadeMat);
        p.back = EnvArt.Quad(p.root.transform, "Back", EnvArt.DirtShadeMat);
        p.root.SetActive(false);
        return p;
    }

    // ------------------------------------------------------------------
    // Dựng lại hình mặt đất theo vị trí thế giới hiện tại (gọi sau khi mảnh đã được đặt chỗ)
    void UpdateGround()
    {
        Vector3 o = transform.position;
        float T = EnvArt.Tile;
        float wx0 = o.x + minX, wx1 = o.x + maxX, wTop = o.y + topY;
        float w = maxX - minX, depth = backZ - frontZ;
        float zBack = backZ + BackExtra;

        // Mặt cỏ: UV theo (x, z) thế giới
        EnvArt.SetQuad(top, new Vector3(minX, topY, frontZ), new Vector3(w, 0, 0), new Vector3(0, 0, zBack - frontZ),
                       new Vector2(wx0 / T, frontZ / T), new Vector2(wx1 / T, zBack / T));
        // Vách đất trước: UV theo (x, y) thế giới -> đất nối liền giữa các mảnh dù khác độ cao
        EnvArt.SetQuad(front, new Vector3(minX, topY - WallDepth, frontZ), new Vector3(w, 0, 0), new Vector3(0, WallDepth, 0),
                       new Vector2(wx0 / T, (wTop - WallDepth) / T), new Vector2(wx1 / T, wTop / T));
        // Mép cỏ rủ (nằm sát trước vách đất)
        EnvArt.SetQuad(lip, new Vector3(minX, topY + 0.06f - 0.5f, frontZ - 0.015f), new Vector3(w, 0, 0), new Vector3(0, 0.5f, 0),
                       new Vector2(wx0 / T, 0f), new Vector2(wx1 / T, 1f));
        // Vách 2 bên (thấy được khi mảnh bên cạnh thấp hơn; bằng nhau thì bị mặt cỏ che nhờ chiều sâu)
        EnvArt.SetQuad(sideL, new Vector3(minX, topY - WallDepth, frontZ), new Vector3(0, 0, depth), new Vector3(0, WallDepth, 0),
                       new Vector2(frontZ / T, (wTop - WallDepth) / T), new Vector2(backZ / T, wTop / T));
        EnvArt.SetQuad(sideR, new Vector3(maxX, topY - WallDepth, frontZ), new Vector3(0, 0, depth), new Vector3(0, WallDepth, 0),
                       new Vector2(frontZ / T, (wTop - WallDepth) / T), new Vector2(backZ / T, wTop / T));
    }

    // Bục: tâm mặt trên ở (x, y) local, rộng w
    void SetupPlatform(Platform p, float x, float y, float w)
    {
        float t = PlatformThickness, depth = backZ - frontZ, T = EnvArt.Tile;
        p.x = x; p.y = y; p.w = w;
        p.root.transform.localPosition = new Vector3(x, y, 0f);
        p.col.center = new Vector3(0f, -t * 0.5f, (frontZ + backZ) * 0.5f);
        p.col.size = new Vector3(w, t, depth);

        Vector3 o = transform.position + new Vector3(x, y, 0f);
        float x0 = -w * 0.5f, wx0 = o.x + x0, wx1 = o.x + x0 + w;
        EnvArt.SetQuad(p.top, new Vector3(x0, 0f, frontZ), new Vector3(w, 0, 0), new Vector3(0, 0, depth),
                       new Vector2(wx0 / T, frontZ / T), new Vector2(wx1 / T, backZ / T));
        EnvArt.SetQuad(p.front, new Vector3(x0, -t, frontZ), new Vector3(w, 0, 0), new Vector3(0, t, 0),
                       new Vector2(wx0 / T, (o.y - t) / T), new Vector2(wx1 / T, o.y / T));
        EnvArt.SetQuad(p.lip, new Vector3(x0, 0.06f - 0.45f, frontZ - 0.015f), new Vector3(w, 0, 0), new Vector3(0, 0.45f, 0),
                       new Vector2(wx0 / T, 0f), new Vector2(wx1 / T, 1f));
        EnvArt.SetQuad(p.under, new Vector3(x0, -t, frontZ), new Vector3(w, 0, 0), new Vector3(0, 0, depth),
                       new Vector2(wx0 / T, frontZ / T), new Vector2(wx1 / T, backZ / T));
        EnvArt.SetQuad(p.sideL, new Vector3(x0, -t, frontZ), new Vector3(0, 0, depth), new Vector3(0, t, 0),
                       new Vector2(frontZ / T, (o.y - t) / T), new Vector2(backZ / T, o.y / T));
        EnvArt.SetQuad(p.sideR, new Vector3(x0 + w, -t, frontZ), new Vector3(0, 0, depth), new Vector3(0, t, 0),
                       new Vector2(frontZ / T, (o.y - t) / T), new Vector2(backZ / T, o.y / T));
        EnvArt.SetQuad(p.back, new Vector3(x0, -t, backZ), new Vector3(w, 0, 0), new Vector3(0, t, 0),
                       new Vector2(wx0 / T, (o.y - t) / T), new Vector2(wx1 / T, o.y / T));
        p.root.SetActive(true);
    }

    // ------------------------------------------------------------------
    // Random lại trang trí + bục. Trả về danh sách mặt bục (toạ độ world) để đặt xu.
    public List<PlatformInfo> Randomize(bool arena)
    {
        List<PlatformInfo> result = new List<PlatformInfo>();
        if (!built) return result;

        UpdateGround();

        // ---- Bục đất nổi (thấp vừa tầm nhảy, dày như khối đất) ----
        foreach (Platform p in platforms) p.root.SetActive(false);
        float w = maxX - minX;
        if (arena)
        {
            // Đấu trường: 2 bục để né sóng chấn / leo lên chém Boss bay
            AddPlatform(0, minX + w * 0.3f, topY + 2.4f, 4f, result);
            AddPlatform(1, minX + w * 0.7f, topY + 2.4f, 4f, result);
        }
        else
        {
            float r = Random.value;
            int count = r < 0.25f ? 0 : r < 0.75f ? 1 : 2;
            float usedUntil = minX + 2.5f;
            for (int i = 0; i < count; i++)
            {
                float pw = Random.Range(3.5f, 6.5f);
                float x0 = Random.Range(usedUntil, Mathf.Max(usedUntil, maxX - 2.5f - pw));
                if (x0 + pw > maxX - 2f) break;
                float h = Random.Range(2.1f, 2.6f);   // đáy bục cao ~1-1.5 -> đứng dưới không bị cụng đầu
                AddPlatform(i, x0 + pw * 0.5f, topY + h, pw, result);
                usedUntil = x0 + pw + 2f;
            }
            // Đôi khi có tầng thứ 3 cao hơn, lệch sang 1 bên bục đầu tiên để nhảy bậc thang
            if (result.Count > 0 && Random.value < 0.3f)
            {
                Platform first = platforms[0];
                float side = Random.value < 0.5f ? -1f : 1f;
                AddPlatform(2, first.x + side * (first.w * 0.5f + 1.2f), first.y + Random.Range(2.0f, 2.4f), 3f, result);
            }
        }

        // ---- Trang trí ----
        int n = 0;
        int trees = arena ? 2 : Random.Range(1, 3);
        for (int i = 0; i < trees; i++)
        {
            float x = arena ? (i == 0 ? minX + 1.5f : maxX - 1.5f) : Random.Range(minX + 1f, maxX - 1f);
            PlaceSprite(n++, EnvArt.Trees[Random.Range(0, 2)], x, backZ + Random.Range(0.6f, 1.6f), Random.Range(1.0f, 1.35f), -14);
        }
        int bushes = arena ? 1 : Random.Range(1, 4);
        for (int i = 0; i < bushes; i++) PlaceDecor(ref n, 5, 1.0f, true);
        int rocks = Random.Range(0, 3);
        for (int i = 0; i < rocks; i++) PlaceDecor(ref n, 6, Random.Range(0.45f, 0.75f), true);
        int shrooms = Random.Range(0, 3);
        for (int i = 0; i < shrooms; i++) PlaceDecor(ref n, 7, Random.Range(0.4f, 0.55f), Random.value < 0.7f);
        int flowers = arena ? 3 : Random.Range(4, 9);
        for (int i = 0; i < flowers; i++) PlaceDecor(ref n, Random.Range(0, 4), Random.Range(0.45f, 0.65f), Random.value < 0.6f);
        int tufts = Random.Range(5, 10);
        for (int i = 0; i < tufts; i++) PlaceDecor(ref n, 4, Random.Range(0.3f, 0.45f), Random.value < 0.5f);
        for (; n < decor.Count; n++) decor[n].gameObject.SetActive(false);

        return result;
    }

    void AddPlatform(int idx, float x, float y, float w, List<PlatformInfo> result)
    {
        if (idx >= platforms.Count) return;
        x = Mathf.Clamp(x, minX + w * 0.5f + 0.5f, maxX - w * 0.5f - 0.5f);
        SetupPlatform(platforms[idx], x, y, w);
        result.Add(new PlatformInfo { topCenter = transform.TransformPoint(new Vector3(x, y, 0f)), width = w });
    }

    // behind = đặt phía sau đường chạy (z > 0), ngược lại đặt sát mép trước (che nhẹ chân nhân vật cho có chiều sâu)
    void PlaceDecor(ref int n, int kind, float height, bool behind)
    {
        if (n >= decor.Count) return;
        float z = behind ? Random.Range(0.35f, backZ - 0.3f) : Random.Range(frontZ + 0.15f, -0.5f);
        float scale = height / EnvArt.DecorContent[kind];
        PlaceSprite(n++, EnvArt.Decor[kind], Random.Range(minX + 0.4f, maxX - 0.4f), z, scale, behind ? -7 : 15);
    }

    void PlaceSprite(int i, Sprite s, float x, float z, float scale, int order)
    {
        if (i >= decor.Count || s == null) return;
        SpriteRenderer sr = decor[i];
        sr.sprite = s;
        sr.sortingOrder = order;
        sr.flipX = Random.value < 0.5f;
        sr.transform.localPosition = new Vector3(x, topY, z);
        sr.transform.localScale = Vector3.one * scale;
        sr.gameObject.SetActive(true);
    }
}
