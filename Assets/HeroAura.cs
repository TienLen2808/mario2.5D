using System.Collections;
using UnityEngine;

// Aura quanh Hero: nhặt càng nhiều XU -> aura càng to, càng đẹp (8 cấp). Lên cấp có hiệu ứng lốc xoáy.
// Ảnh: Resources/hero_aura.png — 7 cột x 9 hàng, ô 140 x 162 px, gốc ở đáy (chân nhân vật)
//   Hàng 0-7 = aura cấp 1-8 | Hàng 8 = lốc xoáy lên cấp
public class HeroAura : MonoBehaviour
{
    // Số xu cần để đạt từng cấp aura
    public static readonly int[] Thresholds = { 10, 25, 45, 70, 100, 140, 190, 250 };
    public static readonly string[] TierNames =
        { "Lửa Vàng", "Hoả Diệm", "Băng Lam", "Lam Bạo", "Siêu Kim", "Kim Lam", "Hồng Lam", "Xích Thần" };

    const int Cols = 7, CellW = 140, CellH = 162, Rows = 9, SpinRow = 8;
    const float PPU = 72f;   // ô cao 162 px -> ~2.25 unit
    static readonly int[] RowLen = { 3, 3, 4, 3, 2, 7, 3, 3, 6 };
    // Chiều cao phần hình thật của từng hàng (px) -> để tính scale cho aura luôn TO HƠN nhân vật
    static readonly float[] ContentPx = { 85f, 81f, 70f, 99f, 128f, 111f, 92f, 155f, 106f };
    // Aura cao gấp bao nhiêu lần nhân vật (cấp càng cao càng toả rộng)
    public static readonly float[] HeightFactor = { 1.7f, 1.8f, 1.9f, 2.0f, 2.2f, 2.25f, 2.35f, 2.5f };
    const float SpinFactor = 2.3f;
    static Sprite[] sprites;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { sprites = null; }

    public static int TierFor(int coins)
    {
        int t = 0;
        while (t < Thresholds.Length && coins >= Thresholds[t]) t++;
        return t;   // 0 = chưa có aura
    }

    // Gọi từ GameBootstrap
    public static void AutoAttach()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null && p.GetComponent<HeroAura>() == null) p.AddComponent<HeroAura>();
    }

    SpriteRenderer aura, spin;
    Collider col;
    int tier = -1;
    float timer, pulse;
    float heroHeight = 1.25f;   // chiều cao nhân vật (unit), đo lúc Start
    bool spinning;

    static bool Load()
    {
        if (sprites != null && sprites[0] != null) return true;
        Texture2D t = Resources.Load<Texture2D>("hero_aura");
        if (t == null) { Debug.LogError("HeroAura: không tìm thấy Resources/hero_aura.png"); return false; }
        sprites = new Sprite[Cols * Rows];
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < RowLen[r]; c++)
                sprites[r * Cols + c] = Sprite.Create(t, new Rect(c * CellW, t.height - (r + 1) * CellH, CellW, CellH),
                                                      new Vector2(0.5f, 0.02f), PPU, 0, SpriteMeshType.FullRect);
        return true;
    }

    void Start()
    {
        if (!Load()) { enabled = false; return; }
        col = GetComponent<Collider>();
        float feet = col != null ? col.bounds.min.y - transform.position.y : -0.5f;

        // Chiều cao nhân vật = chiều cao Collider x heightScale của PlayerSpriteAnimator
        PlayerSpriteAnimator psa = GetComponent<PlayerSpriteAnimator>();
        float colH = col != null ? col.bounds.size.y : 1f;
        heroHeight = colH * (psa != null ? psa.heightScale : 1.25f);

        // Đáy aura đặt ĐÚNG ở chân (không thấp hơn), vì mặt đất là khối đặc sẽ che mất phần nằm dưới mặt cỏ.
        // Aura đã cao gấp 1.7-2.5 lần nhân vật nên vẫn bao trọn người.
        float bottom = feet + 0.02f;
        aura = MakeLayer("Aura", bottom, 0.05f, -1);    // ngay sau lưng nhân vật
        spin = MakeLayer("AuraSpin", bottom, -0.15f, 2); // lốc xoáy lên cấp: trước mặt
        spin.gameObject.SetActive(false);
        Refresh(true);
    }

    SpriteRenderer MakeLayer(string n, float feetY, float z, int order)
    {
        GameObject go = new GameObject(n);
        go.transform.SetParent(transform, false);
        Vector3 ls = transform.lossyScale;
        go.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
        go.transform.localPosition = new Vector3(0f, feetY, z);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        return sr;
    }

    void Update()
    {
        Refresh(false);

        bool dead = col != null && !col.enabled;
        aura.enabled = tier > 0 && !dead;
        if (!aura.enabled) return;

        timer += Time.deltaTime;
        int row = tier - 1;
        aura.sprite = sprites[row * Cols + (int)(timer * 10f) % RowLen[row]];

        // Phập phồng nhẹ, aura cao cấp toả mạnh hơn
        pulse += Time.deltaTime * (4f + tier * 0.5f);
        float s = ScaleFor(row, HeightFactor[row]) * (1f + 0.04f * Mathf.Sin(pulse));
        Vector3 ls = transform.lossyScale;
        aura.transform.localScale = new Vector3(s / ls.x, s / ls.y, 1f / ls.z);
        aura.color = new Color(1f, 1f, 1f, 0.85f + 0.15f * Mathf.Sin(pulse * 0.5f));
    }

    // scale để phần hình aura cao đúng 'factor' lần chiều cao nhân vật
    float ScaleFor(int row, float factor)
    {
        float contentUnits = ContentPx[row] / PPU;
        return heroHeight * factor / contentUnits;
    }

    void Refresh(bool silent)
    {
        int t = TierFor(GameSession.Coins);
        if (t == tier) return;
        bool up = tier >= 0 && t > tier;
        tier = t;
        if (up && !silent)
        {
            GameSession.Banner("AURA CẤP " + t + ": " + TierNames[t - 1].ToUpper() + "!", "Nhặt thêm xu để aura toả sáng hơn nữa");
            if (!spinning) StartCoroutine(SpinFx());
        }
    }

    IEnumerator SpinFx()
    {
        spinning = true;
        spin.gameObject.SetActive(true);
        Vector3 ls = transform.lossyScale;
        float ss = ScaleFor(SpinRow, SpinFactor);
        spin.transform.localScale = new Vector3(ss / ls.x, ss / ls.y, 1f / ls.z);
        for (int i = 0; i < RowLen[SpinRow]; i++)
        {
            spin.sprite = sprites[SpinRow * Cols + i];
            yield return new WaitForSeconds(0.07f);
        }
        spin.gameObject.SetActive(false);
        spinning = false;
    }
}
