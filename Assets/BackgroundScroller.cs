using UnityEngine;

// Phông nền nhiều lớp đặt ở xa phía sau (camera phối cảnh tự tạo hiệu ứng parallax):
//   Trời (gradient) -> Mây trôi -> Núi tuyết -> Đồi xanh
// Các lớp lát gạch tự dời theo camera nên chạy bao xa cũng không hết nền.
public class BackgroundScroller : MonoBehaviour
{
    const float SkyZ = 160f, CloudZ = 120f, MountainZ = 90f, HillZ = 45f;
    const float MountainTile = 120f, HillTile = 60f;

    Transform cam, sky, mountains, hills;
    Transform[] clouds;
    float[] cloudSpeed;

    public static void AutoCreate()
    {
        if (FindAnyObjectByType<BackgroundScroller>() != null) return;
        if (!EnvArt.Load()) return;
        new GameObject("Background").AddComponent<BackgroundScroller>();
    }

    void Start()
    {
        cam = Camera.main != null ? Camera.main.transform : null;

        SpriteRenderer s = MakeSprite("Sky", EnvArt.Sky, -100);
        s.transform.localScale = new Vector3(600f, 1.2f, 1f);   // ảnh 4x256 px, PPU 1 -> 2400 x 307 unit
        sky = s.transform;

        SpriteRenderer m = EnvArt.Tiled(transform, "Mountains", EnvArt.Mountains, new Vector2(MountainTile * 4f, 45f), -90);
        mountains = m.transform;
        SpriteRenderer h = EnvArt.Tiled(transform, "Hills", EnvArt.Hills, new Vector2(HillTile * 4f, 15f), -85);
        hills = h.transform;

        // Phần đất xanh kéo dài phía dưới chân đồi (che khoảng trống ở đáy màn hình)
        Sprite px = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 1f), 4f);
        SpriteRenderer fill = MakeSprite("HillsFill", px, -85);
        fill.transform.SetParent(hills, false);
        fill.transform.localPosition = new Vector3(0f, 0.05f, 0.01f);
        fill.transform.localScale = new Vector3(HillTile * 4.5f, 80f, 1f);
        fill.color = new Color(70f / 255f, 135f / 255f, 70f / 255f);

        clouds = new Transform[7];
        cloudSpeed = new float[clouds.Length];
        for (int i = 0; i < clouds.Length; i++)
        {
            SpriteRenderer c = MakeSprite("Cloud" + i, EnvArt.Clouds[i % 2], -95);
            float sc = Random.Range(0.8f, 1.4f);
            c.transform.localScale = Vector3.one * sc;
            c.transform.position = new Vector3(CamX + Random.Range(-160f, 160f), Random.Range(18f, 45f), CloudZ + Random.Range(-10f, 10f));
            clouds[i] = c.transform;
            cloudSpeed[i] = Random.Range(0.6f, 1.6f);
        }
        LateUpdate();
    }

    float CamX { get { return cam != null ? cam.position.x : 0f; } }
    float CamY { get { return cam != null ? cam.position.y : 0f; } }

    SpriteRenderer MakeSprite(string n, Sprite s, int order)
    {
        GameObject go = new GameObject(n);
        go.transform.SetParent(transform, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        sr.sortingOrder = order;
        return sr;
    }

    void LateUpdate()
    {
        if (cam == null) { if (Camera.main != null) cam = Camera.main.transform; else return; }
        float x = CamX, y = CamY;

        sky.position = new Vector3(x, y + 20f, SkyZ);
        // Dời theo bội số chiều rộng 1 ô -> hình lặp lại liền mạch
        mountains.position = new Vector3(Mathf.Round(x / MountainTile) * MountainTile, -18f + y * 0.3f, MountainZ);
        hills.position = new Vector3(Mathf.Round(x / HillTile) * HillTile, -9f + y * 0.6f, HillZ);

        for (int i = 0; i < clouds.Length; i++)
        {
            Vector3 p = clouds[i].position;
            p.x -= cloudSpeed[i] * Time.deltaTime;
            if (p.x < x - 170f) p.x += 340f;
            if (p.x > x + 170f) p.x -= 340f;
            clouds[i].position = p;
        }
    }
}
