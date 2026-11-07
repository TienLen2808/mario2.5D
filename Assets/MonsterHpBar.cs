using UnityEngine;

// Thanh máu nhỏ trên đầu quái (vẽ bằng SpriteRenderer, không cần Canvas)
//   - Phần trắng tụt chậm phía sau cho thấy lượng máu vừa mất
//   - Màu đổi theo % máu: xanh lá -> vàng -> đỏ
public class MonsterHpBar : MonoBehaviour
{
    static Sprite pixel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { pixel = null; }

    static Sprite Pixel
    {
        get
        {
            if (pixel == null)
                pixel = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0f, 0.5f), 4f);
            return pixel;
        }
    }

    float width, thickness;
    float target = 1f, trail = 1f;
    SpriteRenderer border, bg, trailSr, fill;

    // owner: quái | height: độ cao so với gốc của quái | width: chiều dài thanh (unit)
    public static MonsterHpBar Attach(Transform owner, float height, float width, float thickness = 0.12f)
    {
        GameObject go = new GameObject("HpBar");
        go.transform.SetParent(owner, false);
        go.transform.localPosition = new Vector3(0f, height, -0.3f);
        MonsterHpBar bar = go.AddComponent<MonsterHpBar>();
        bar.width = width;
        bar.thickness = thickness;
        bar.border  = bar.MakeQuad("Border", 40, new Color(0f, 0f, 0f, 0.85f), width + 0.06f, thickness + 0.06f, -0.03f);
        bar.bg      = bar.MakeQuad("Bg",     41, new Color(0.25f, 0.05f, 0.05f, 0.9f), width, thickness, 0f);
        bar.trailSr = bar.MakeQuad("Trail",  42, new Color(1f, 0.95f, 0.8f, 0.95f), width, thickness, 0f);
        bar.fill    = bar.MakeQuad("Fill",   43, Color.green, width, thickness, 0f);
        bar.Set(1f, true);
        return bar;
    }

    SpriteRenderer MakeQuad(string n, int order, Color c, float w, float h, float xOffset)
    {
        GameObject q = new GameObject(n);
        q.transform.SetParent(transform, false);
        q.transform.localPosition = new Vector3(-width * 0.5f + xOffset, 0f, 0f);
        q.transform.localScale = new Vector3(w, h, 1f);
        SpriteRenderer s = q.AddComponent<SpriteRenderer>();
        s.sprite = Pixel;
        s.color = c;
        s.sortingOrder = order;
        return s;
    }

    public void Set(float ratio, bool instant = false)
    {
        target = Mathf.Clamp01(ratio);
        if (instant) trail = target;
        Apply();
    }

    public void SetVisible(bool v) { gameObject.SetActive(v); }

    void Update()
    {
        if (trail > target) { trail = Mathf.MoveTowards(trail, target, Time.deltaTime * 0.8f); Apply(); }
        // Giữ thanh máu luôn thẳng dù quái có bị lật
        transform.rotation = Quaternion.identity;
    }

    void Apply()
    {
        if (fill == null) return;
        SetWidth(fill, target);
        SetWidth(trailSr, Mathf.Max(trail, target));
        fill.color = target > 0.6f ? new Color(0.3f, 0.9f, 0.3f)
                   : target > 0.3f ? new Color(1f, 0.8f, 0.2f)
                   : new Color(1f, 0.25f, 0.2f);
    }

    void SetWidth(SpriteRenderer s, float r)
    {
        Vector3 sc = s.transform.localScale;
        sc.x = Mathf.Max(0.0001f, width * r);
        s.transform.localScale = sc;
    }
}
