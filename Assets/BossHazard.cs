using System.Collections.Generic;
using UnityEngine;

// Các chiêu diện rộng của Boss (vật thể trên sân):
//   Shockwave : sóng chấn chạy dọc mặt đất -> phải NHẢY qua
//   Rock      : đá rơi từ trời, có vòng đỏ cảnh báo trước -> đứng ra khỏi vòng
//   Pillar    : cột lửa phun từ đất, có vòng cảnh báo trước -> tránh chỗ đó
//   Marker    : chỉ là vòng cảnh báo (dùng cho Mưa Lửa)
// Ảnh: Resources/boss_shockwave.png (4 frame 192x128), boss_rock.png, boss_marker.png, boss_pillar.png (4 frame 128x384)
public class BossHazard : MonoBehaviour
{
    public enum Kind { Shockwave, Rock, Pillar, Marker }

    static Sprite[] wave, pillar;
    static Sprite rock, marker;
    static readonly Queue<BossHazard> pool = new Queue<BossHazard>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { wave = pillar = null; rock = marker = null; pool.Clear(); }

    static void Load()
    {
        if (wave != null && wave[0] != null) return;
        wave = Slice("boss_shockwave", 4, 192, 128, 64f, new Vector2(0.5f, 0f));
        pillar = Slice("boss_pillar", 4, 128, 384, 96f, new Vector2(0.5f, 0f));
        Sprite[] r = Slice("boss_rock", 1, 128, 128, 80f, new Vector2(0.5f, 0.5f));
        Sprite[] m = Slice("boss_marker", 1, 192, 64, 96f, new Vector2(0.5f, 0.5f));
        rock = r != null ? r[0] : null;
        marker = m != null ? m[0] : null;
    }

    static Sprite[] Slice(string res, int count, int w, int h, float ppu, Vector2 pivot)
    {
        Texture2D tex = Resources.Load<Texture2D>(res);
        if (tex == null) { Debug.LogError("BossHazard: không tìm thấy Resources/" + res + ".png"); return null; }
        Sprite[] s = new Sprite[count];
        for (int i = 0; i < count; i++)
            s[i] = Sprite.Create(tex, new Rect(i * w, tex.height - h, w, h), pivot, ppu, 0, SpriteMeshType.FullRect);
        return s;
    }

    // ------------------------------------------------------------------
    Kind kind;
    SpriteRenderer sr, markerSr;
    float groundY, timer, delay, speed, dir, minX, maxX, active;
    Vector3 rockPos;
    Color tint;
    int frame; float frameTimer;
    bool hitDone;

    static BossHazard Get()
    {
        Load();
        BossHazard h = null;
        while (pool.Count > 0 && h == null) h = pool.Dequeue();
        if (h == null)
        {
            GameObject go = new GameObject("BOSS_HAZARD");
            h = go.AddComponent<BossHazard>();
            GameObject body = new GameObject("Body");      // hình chính (đá / sóng / cột lửa)
            body.transform.SetParent(go.transform, false);
            h.sr = body.AddComponent<SpriteRenderer>();
            h.sr.sortingOrder = 8;
            GameObject m = new GameObject("Marker");
            m.transform.SetParent(go.transform, false);
            h.markerSr = m.AddComponent<SpriteRenderer>();
            h.markerSr.sortingOrder = 2;
        }
        h.timer = 0f; h.frame = 0; h.frameTimer = 0f; h.hitDone = false;
        h.transform.rotation = Quaternion.identity;
        h.transform.localScale = Vector3.one;
        h.sr.transform.localPosition = Vector3.zero;
        h.sr.transform.localRotation = Quaternion.identity;
        h.sr.transform.localScale = Vector3.one;
        h.sr.enabled = true; h.sr.flipX = false;
        h.markerSr.enabled = false;
        h.markerSr.transform.localPosition = Vector3.zero;
        h.markerSr.transform.localScale = Vector3.one;
        return h;
    }

    // Sóng chấn chạy dọc mặt đất theo hướng dir (+1 phải / -1 trái), biến mất khi tới mép đấu trường
    public static void Shockwave(float x, float groundY, float dir, float speed, float minX, float maxX, Color tint)
    {
        BossHazard h = Get();
        h.kind = Kind.Shockwave;
        h.groundY = groundY; h.dir = dir; h.speed = speed; h.minX = minX; h.maxX = maxX; h.tint = tint;
        h.transform.position = new Vector3(x, groundY, -0.2f);
        h.sr.sprite = wave[0];
        h.sr.flipX = dir < 0f;   // ảnh gốc chạy sang phải
        h.sr.color = tint;
        h.gameObject.SetActive(true);
    }

    // Đá rơi xuống vị trí x sau 'delay' giây (vòng đỏ cảnh báo trong lúc chờ)
    public static void Rock(float x, float groundY, float delay)
    {
        BossHazard h = Get();
        h.kind = Kind.Rock;
        h.groundY = groundY; h.delay = delay;
        h.transform.position = new Vector3(x, groundY, -0.1f);
        h.rockPos = new Vector3(x, groundY + 14f, -0.15f);
        h.sr.sprite = rock; h.sr.color = Color.white; h.sr.enabled = false;
        h.ShowMarker(new Color(1f, 1f, 1f, 0.9f), 1.1f);
        h.gameObject.SetActive(true);
    }

    // Cột lửa phun lên tại x sau 'delay' giây, cháy trong 'duration' giây
    public static void Pillar(float x, float groundY, float delay, float duration, Color tint)
    {
        BossHazard h = Get();
        h.kind = Kind.Pillar;
        h.groundY = groundY; h.delay = delay; h.active = duration; h.tint = tint;
        h.transform.position = new Vector3(x, groundY, -0.2f);
        h.sr.sprite = pillar[0]; h.sr.color = tint; h.sr.enabled = false;
        h.ShowMarker(new Color(tint.r, tint.g * 0.7f, tint.b, 0.9f), 0.9f);
        h.gameObject.SetActive(true);
    }

    // Chỉ hiện vòng cảnh báo (vd: chỗ quả cầu lửa sắp rơi xuống)
    public static void Marker(float x, float groundY, float duration, Color c)
    {
        BossHazard h = Get();
        h.kind = Kind.Marker;
        h.delay = duration;
        h.transform.position = new Vector3(x, groundY, -0.1f);
        h.sr.enabled = false;
        h.ShowMarker(c, 1f);
        h.gameObject.SetActive(true);
    }

    void ShowMarker(Color c, float scale)
    {
        markerSr.enabled = true;
        markerSr.sprite = marker;
        markerSr.color = c;
        markerSr.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        markerSr.transform.localScale = new Vector3(scale, scale, 1f);
    }

    // ------------------------------------------------------------------
    void Update()
    {
        timer += Time.deltaTime;
        switch (kind)
        {
            case Kind.Shockwave: UpdateWave(); break;
            case Kind.Rock: UpdateRock(); break;
            case Kind.Pillar: UpdatePillar(); break;
            case Kind.Marker:
                PulseMarker();
                if (timer >= delay) Recycle();
                break;
        }
    }

    void UpdateWave()
    {
        Vector3 p = transform.position + new Vector3(dir * speed * Time.deltaTime, 0f, 0f);
        transform.position = p;
        Animate(wave, 0.07f);
        // Vùng nguy hiểm: phần đỉnh sóng phía trước, cao ~1 unit -> nhảy qua được
        HurtBox(new Vector3(p.x + dir * 0.9f, groundY + 0.5f, 0f), new Vector3(1.0f, 1.0f, 2f));
        if (p.x < minX || p.x > maxX || timer > 8f) Recycle();
    }

    void UpdateRock()
    {
        PulseMarker();
        if (timer < delay) return;
        if (!sr.enabled) { sr.enabled = true; sr.transform.position = rockPos; }

        // Rơi nhanh dần
        float t = timer - delay;
        float y = rockPos.y - 0.5f * 40f * t * t;
        sr.transform.rotation = Quaternion.Euler(0, 0, t * 360f);
        if (y <= groundY + 0.7f)
        {
            y = groundY + 0.7f;
            if (!hitDone)
            {
                hitDone = true;
                HurtBox(new Vector3(transform.position.x, groundY + 0.7f, 0f), new Vector3(1.8f, 1.4f, 2f));
                CameraShakeLite(0.12f, 0.1f);
            }
            if (t > Mathf.Sqrt(2f * 13.3f / 40f) + 0.25f) { Recycle(); return; }
        }
        // đặt vị trí theo trục y riêng cho sprite đá (marker vẫn nằm dưới đất)
        sr.transform.position = new Vector3(transform.position.x, y, rockPos.z);
        if (!hitDone) HurtBox(new Vector3(transform.position.x, y, 0f), new Vector3(1.2f, 1.2f, 2f));
    }

    void UpdatePillar()
    {
        if (timer < delay) { PulseMarker(); return; }
        if (!sr.enabled) { sr.enabled = true; markerSr.enabled = false; }
        Animate(pillar, 0.06f);
        // Lúc mới phun và lúc tắt thì co lại
        float t = timer - delay;
        float grow = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01((active - t) / 0.15f);
        sr.transform.localScale = new Vector3(1f, Mathf.Max(0.05f, grow), 1f);
        if (grow > 0.5f) HurtBox(new Vector3(transform.position.x, groundY + 1.8f, 0f), new Vector3(0.9f, 3.6f, 2f));
        if (t >= active) Recycle();
    }

    void PulseMarker()
    {
        if (!markerSr.enabled) return;
        float a = 0.55f + 0.45f * Mathf.Sin(timer * 18f);
        Color c = markerSr.color; c.a = a; markerSr.color = c;
    }

    void Animate(Sprite[] frames, float dt)
    {
        frameTimer += Time.deltaTime;
        if (frameTimer >= dt) { frameTimer = 0f; frame = (frame + 1) % frames.Length; }
        sr.sprite = frames[frame];
    }

    static void HurtBox(Vector3 center, Vector3 size)
    {
        foreach (Collider c in Physics.OverlapBox(center, size * 0.5f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            PlayerController pc = c.GetComponentInParent<PlayerController>();
            if (pc != null) { pc.Hurt(1, center); return; }
        }
    }

    static void CameraShakeLite(float d, float s)
    {
        if (Camera.main == null) return;
        CameraShaker sh = Camera.main.GetComponent<CameraShaker>();
        if (sh == null) sh = Camera.main.gameObject.AddComponent<CameraShaker>();
        sh.Shake(d, s);
    }

    void Recycle()
    {
        gameObject.SetActive(false);
        pool.Enqueue(this);
    }
}
