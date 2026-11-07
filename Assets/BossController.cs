using System.Collections;
using UnityEngine;

// ============================ BOSS ============================
// Mỗi Boss có 1 tên, 1 hoặc nhiều DẠNG (thanh máu chia đều theo số dạng). Danh sách Boss từng màn: BossRoster.
// Trùm cuối MA VƯƠNG có đủ 3 dạng; các Boss khác chỉ có 1 dạng. Bộ chiêu mỗi dạng (đa số là chiêu diện rộng / lan ra):
//
// DẠNG 1 — OGRE VƯƠNG (> 66% máu)  · đi bộ  -> chỉ KIẾM gây sát thương
//   1. Chém Khổng Lồ   : giơ rìu rồi chém mạnh phía trước
//   2. Địa Chấn        : đập rìu xuống đất -> 2 SÓNG CHẤN chạy dọc mặt đất sang 2 bên (phải NHẢY qua)
//   3. Mưa Đá          : gầm lên -> đá rơi khắp đấu trường, có VÒNG ĐỎ cảnh báo trước
//
// DẠNG 2 — HỎA LONG (33% - 66%)  · bay  -> chỉ TIA LỬA XANH gây sát thương
//   1. Quạt Lửa        : phun 5 quả cầu lửa toả hình quạt về phía Player
//   2. Mưa Lửa         : bay lên cao, thả lửa quét từ đầu này sang đầu kia đấu trường (có vòng cảnh báo)
//   3. Bổ Nhào         : nháy sáng báo hiệu rồi lao thẳng xuống chỗ Player
//
// DẠNG 3 — HẮC LONG (< 33%)  · lửa tím, nhanh hơn
//   1. Vòng Lửa Hỗn Mang : bắn 2-3 vòng cầu lửa toả ra 360 độ
//   2. Cột Lửa Lan       : cột lửa phun từ đất, LAN dần từ chỗ Boss ra 2 bên, rồi thêm 3 cột ngay chỗ Player
//   3. Thiên Thạch Giáng : bổ nhào xuống đất tạo sóng chấn tím, rồi ĐỨNG CHOÁNG 1.8s
//                          (lúc đứng dưới đất thì chém KIẾM được, lúc bay thì bắn TIA LỬA)
//
// Chuyển dạng: Boss bất tử ~1.5s, nháy sáng, rung màn hình, hiện tên dạng mới.
public class BossController : MonoBehaviour, IStompable
{
    public enum Form { OgreKing = 0, FireDragon = 1, DarkDragon = 2 }
    public static readonly string[] FormNames = { "OGRE VƯƠNG", "HỎA LONG", "HẮC LONG" };
    static readonly string[] FormHints =
    {
        "KIẾM (J) mạnh gấp đôi CHƯỞNG (K) · nhảy qua Boss & sóng chấn, tránh vòng đỏ",
        "Nhảy lên CHÉM khi Boss sà thấp, hoặc bắn CHƯỞNG từ xa",
        "Lúc Boss đáp đất bị choáng là cơ hội CHÉM liên tục!"
    };

    public const string BossName = "MA VƯƠNG";   // tên trùm cuối

    public string Name { get; private set; }
    public int FormCount { get { return forms.Length; } }
    Form[] forms = { Form.OgreKing, Form.FireDragon, Form.DarkDragon };
    int formIndex;
    int slot;   // thứ tự Boss trong đấu trường (2 Boss cùng lúc thì đứng 2 phía khác nhau)

    public int MaxHp { get; private set; }
    public int Hp { get; private set; }
    public Form CurrentForm { get; private set; }
    public string FormName { get { return FormNames[(int)CurrentForm]; } }
    public bool IsFlying { get { return CurrentForm == Form.FireDragon || (CurrentForm == Form.DarkDragon && !landed); } }
    public bool IsTransforming { get { return transforming; } }

    // --- Sprite (dùng chung sheet với Ogre và Rồng) ---
    const int OgreCols = 11, OgreW = 150, OgreH = 98; const float OgrePPU = 40f;
    static readonly int[] OgreLen = { 2, 4, 5, 11, 4, 7, 2 };   // Idle Walk Chop Smash Charge Spin Roar
    const int DragCols = 9, DragW = 302, DragH = 312; const float DragPPU = 115f;
    static readonly int[] DragLen = { 7, 9, 6 };                 // Fly Attack HurtDie
    static Sprite[] ogre, dragon;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { ogre = dragon = null; }

    static readonly Color Gold = new Color(1f, 0.88f, 0.6f);
    static readonly Color Purple = new Color(0.72f, 0.45f, 1f);
    static readonly Color PurpleFire = new Color(0.85f, 0.55f, 1f);
    static readonly Color DustColor = new Color(1f, 0.92f, 0.75f);

    int level;
    float minX, maxX, groundY;
    bool dead, transforming, landed;
    float hitInvul, flashTimer, bobPhase, loopTimer;
    float tintTimer; Color tintColor;   // màu báo hiệu chiêu (nháy vàng trước khi bổ nhào)
    int loopFrame;
    bool facingRight;
    Color baseColor = Color.white;
    float sizeScale = 1f;

    SpriteRenderer sr;
    BoxCollider box;
    Transform visual;
    Transform player;

    // ------------------------------------------------------------------
    // Giữ tương thích: trùm cuối Ma Vương 3 dạng
    public static BossController Create(int level, float minX, float maxX, float groundY, float x)
    {
        return Create(level, minX, maxX, groundY, x, BossName,
                      new[] { Form.OgreKing, Form.FireDragon, Form.DarkDragon }, 30 + 10 * (Mathf.Max(1, level) - 1), 0);
    }

    public static BossController Create(int level, float minX, float maxX, float groundY, float x,
                                        string name, Form[] forms, int hp, int slot)
    {
        LoadSprites();
        GameObject go = new GameObject("BOSS");
        go.SetActive(false);
        try { go.tag = "Enemy"; } catch (UnityException) { }

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        go.AddComponent<BoxCollider>();

        GameObject vis = new GameObject("Visual");
        vis.transform.SetParent(go.transform, false);
        vis.AddComponent<SpriteRenderer>().sortingOrder = 7;

        BossController b = go.AddComponent<BossController>();
        b.level = Mathf.Max(1, level);
        b.minX = minX; b.maxX = maxX; b.groundY = groundY;
        b.Name = name;
        b.forms = (forms != null && forms.Length > 0) ? forms : new[] { Form.OgreKing };
        b.slot = slot;
        b.MaxHp = Mathf.Max(1, hp);
        b.Hp = b.MaxHp;
        bool flying = b.forms[0] != Form.OgreKing;
        go.transform.position = new Vector3(x, flying ? groundY + 4f : groundY, 0f);
        go.SetActive(true);
        return b;
    }

    static void LoadSprites()
    {
        if (ogre != null && ogre[0] != null) return;
        ogre = Slice("ogre_sheet", OgreCols, OgreLen, OgreW, OgreH, OgrePPU, new Vector2(0.5f, 0f));
        dragon = Slice("dragon_sheet", DragCols, DragLen, DragW, DragH, DragPPU, new Vector2(0.5f, 0.5f));
    }

    static Sprite[] Slice(string res, int cols, int[] lens, int w, int h, float ppu, Vector2 pivot)
    {
        Texture2D tex = Resources.Load<Texture2D>(res);
        if (tex == null) { Debug.LogError("BossController: không tìm thấy Resources/" + res + ".png"); return new Sprite[cols * lens.Length]; }
        Sprite[] s = new Sprite[cols * lens.Length];
        for (int r = 0; r < lens.Length; r++)
            for (int c = 0; c < lens[r]; c++)
                s[r * cols + c] = Sprite.Create(tex, new Rect(c * w, tex.height - (r + 1) * h, w, h), pivot, ppu, 0, SpriteMeshType.FullRect);
        return s;
    }

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        visual = transform.Find("Visual");
        sr = visual.GetComponent<SpriteRenderer>();
    }

    void Start()
    {
        FindPlayer();
        formIndex = 0;
        SetForm(forms[0]);
        StartCoroutine(Intro());
    }

    void FindPlayer()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        player = p != null ? p.transform : null;
    }

    // ------------------------------------------------------------------
    void SetForm(Form f)
    {
        CurrentForm = f;
        landed = false;
        switch (f)
        {
            case Form.OgreKing:
                sizeScale = 1.9f; baseColor = Gold;
                box.size = new Vector3(1.9f, 3.4f, 1f); box.center = new Vector3(0f, 1.7f, 0f);
                break;
            case Form.FireDragon:
                sizeScale = 1.5f; baseColor = Color.white;
                box.size = new Vector3(2.3f, 1.5f, 1f); box.center = Vector3.zero;
                break;
            case Form.DarkDragon:
                sizeScale = 1.8f; baseColor = Purple;
                box.size = new Vector3(2.7f, 1.8f, 1f); box.center = Vector3.zero;
                break;
        }
        visual.localScale = Vector3.one * sizeScale;
        sr.color = baseColor;
        sr.sprite = f == Form.OgreKing ? O(0, 0) : D(0, 0);
    }

    Sprite O(int row, int i) { return ogre[row * OgreCols + i]; }
    Sprite D(int row, int i) { return dragon[row * DragCols + i]; }

    void Update()
    {
        if (player == null) FindPlayer();
        IgnorePlayerBody();
        hitInvul -= Time.deltaTime;
        flashTimer -= Time.deltaTime;
        tintTimer -= Time.deltaTime;
        if (!transforming && !dead)
            sr.color = flashTimer > 0f ? new Color(1f, 0.35f, 0.35f) : tintTimer > 0f ? tintColor : baseColor;
        sr.flipX = facingRight; // sprite gốc quay mặt sang TRÁI
    }

    // Cho Player đi/nhảy XUYÊN QUA thân Boss (dễ né chiêu). Chạm thân không mất mạng,
    // chỉ dính chiêu mới mất. (Unity xoá trạng thái này khi collider Player bị tắt lúc chết -> đặt lại mỗi frame)
    Collider playerCol;
    void IgnorePlayerBody()
    {
        if (player == null) return;
        if (playerCol == null) playerCol = player.GetComponent<Collider>();
        if (playerCol == null || !playerCol.enabled || box == null || !box.enabled) return;
        if (!Physics.GetIgnoreCollision(playerCol, box)) Physics.IgnoreCollision(playerCol, box, true);
    }

    float PlayerX { get { return player != null ? player.position.x : transform.position.x; } }
    void FacePlayer() { if (player != null) facingRight = player.position.x > transform.position.x; }
    float Dir { get { return facingRight ? 1f : -1f; } }

    void MoveTo(Vector3 p)
    {
        p.x = Mathf.Clamp(p.x, minX, maxX);
        p.z = 0f;
        transform.position = p;
    }

    IEnumerator Show(Sprite s, float t) { sr.sprite = s; yield return new WaitForSeconds(t); }

    // ------------------------------------------------------------------
    IEnumerator Intro()
    {
        transforming = true;
        if (slot == 0)
        {
            string sub = forms.Length > 1 ? "Có " + forms.Length + " dạng · Dạng 1: " + FormNames[(int)forms[0]] + " — " + FormHints[(int)forms[0]]
                                          : FormHints[(int)forms[0]];
            GameSession.Banner("BOSS: " + Name, sub);
        }
        Shake(1.0f, 0.12f);
        for (int k = 0; k < 8; k++)
        {
            if (CurrentForm == Form.OgreKing) yield return Show(O(6, k % 2), 0.15f);   // gầm thét
            else { LoopFly(14f); yield return new WaitForSeconds(0.15f); }
        }
        transforming = false;
        StartCoroutine(Brain());
    }

    IEnumerator Brain()
    {
        while (!dead)
        {
            if (player == null || !player.gameObject.activeInHierarchy) { yield return null; continue; }
            switch (CurrentForm)
            {
                case Form.OgreKing: yield return OgreTurn(); break;
                case Form.FireDragon: yield return FireDragonTurn(); break;
                case Form.DarkDragon: yield return DarkDragonTurn(); break;
            }
        }
    }

    // =========================== DẠNG 1: OGRE VƯƠNG ===========================
    IEnumerator OgreTurn()
    {
        // Tiến về phía Player
        float t = 0f, frameT = 0f; int f = 0;
        float speed = 1.8f + 0.2f * level;
        while (t < 1.1f && Mathf.Abs(PlayerX - transform.position.x) > 2.8f)
        {
            t += Time.deltaTime; frameT += Time.deltaTime;
            FacePlayer();
            MoveTo(new Vector3(transform.position.x + Dir * speed * Time.deltaTime, groundY, 0f));
            if (frameT > 0.16f) { frameT = 0f; f = (f + 1) % 4; }
            sr.sprite = O(1, f);
            yield return null;
        }
        FacePlayer();

        float dx = Mathf.Abs(PlayerX - transform.position.x);
        float r = Random.value;
        if (dx < 3.2f && r < 0.45f) yield return GiantChop();
        else if (r < 0.78f) yield return EarthQuake();
        else yield return RockRain();

        for (int k = 0; k < 3; k++) yield return Show(O(0, k % 2), 0.2f);
    }

    IEnumerator GiantChop()
    {
        yield return Show(O(2, 0), 0.45f);                       // giơ rìu (báo hiệu)
        float k = sizeScale;
        yield return Show(O(2, 1), 0.08f);
        HitBox(new Vector2(0.65f, 0.8f) * k, new Vector2(0.9f, 1.4f) * k);
        yield return Show(O(2, 2), 0.08f);
        HitBox(new Vector2(0.47f, 0.45f) * k, new Vector2(0.55f, 0.9f) * k);
        Shake(0.15f, 0.08f);
        yield return Show(O(2, 3), 0.1f);
        yield return Show(O(2, 4), 0.25f);
    }

    IEnumerator EarthQuake()
    {
        for (int i = 0; i <= 2; i++) yield return Show(O(3, i), 0.26f);   // giơ rìu cao (báo hiệu)
        float k = sizeScale;
        yield return Show(O(3, 3), 0.08f);
        HitBox(new Vector2(0.85f, 1.15f) * k, new Vector2(1.3f, 2.1f) * k);
        yield return Show(O(3, 4), 0.06f);
        HitBox(new Vector2(0.92f, 0.4f) * k, new Vector2(1.45f, 0.8f) * k);

        // 2 sóng chấn chạy sang 2 bên
        float x = transform.position.x;
        float speed = 6f + 0.5f * level;
        BossHazard.Shockwave(x + 1.2f, groundY, 1f, speed, minX - 1f, maxX + 1f, DustColor);
        BossHazard.Shockwave(x - 1.2f, groundY, -1f, speed, minX - 1f, maxX + 1f, DustColor);
        Shake(0.35f, 0.18f);

        // Từ màn 3: đập thêm 1 lần nữa
        if (level >= 3)
        {
            yield return new WaitForSeconds(0.45f);
            BossHazard.Shockwave(x + 1.2f, groundY, 1f, speed * 0.8f, minX - 1f, maxX + 1f, DustColor);
            BossHazard.Shockwave(x - 1.2f, groundY, -1f, speed * 0.8f, minX - 1f, maxX + 1f, DustColor);
            Shake(0.25f, 0.12f);
        }

        yield return Show(O(3, 5), 0.5f);                        // rìu cắm đất (cơ hội chém)
        for (int i = 6; i <= 10; i++) yield return Show(O(3, i), 0.08f);
    }

    IEnumerator RockRain()
    {
        Shake(0.9f, 0.1f);
        int count = 6 + level;
        for (int i = 0; i < count; i++)
        {
            float x = i == 0 ? PlayerX : Random.Range(minX + 1f, maxX - 1f);
            BossHazard.Rock(x, groundY, 0.9f + i * 0.18f);
        }
        for (int k = 0; k < 6; k++) yield return Show(O(6, k % 2), 0.15f);
        yield return new WaitForSeconds(0.6f);
    }

    // =========================== DẠNG 2: HỎA LONG ===========================
    IEnumerator FireDragonTurn()
    {
        yield return Hover(1.0f, 5f, 3.8f, 3.2f);   // bay thấp hơn chút để nhảy lên chém được
        float r = Random.value;
        if (r < 0.4f) yield return FanFire();
        else if (r < 0.75f) yield return FireRain();
        else yield return Dive(12f, false);
    }

    IEnumerator FanFire()
    {
        int waves = level >= 2 ? 2 : 1;
        for (int w = 0; w < waves; w++)
        {
            for (int i = 0; i <= 4; i++) yield return Show(D(1, i), 0.09f);
            yield return Show(D(1, 5), 0.07f);
            FacePlayer();
            Vector3 mouth = Mouth();
            Vector3 aim = (player.position - mouth); aim.z = 0f; aim.Normalize();
            for (int a = -2; a <= 2; a++)
                Fireball.Spawn(mouth, Quaternion.Euler(0, 0, a * 13f) * aim * 7.5f, Color.white, 1.4f);
            for (int i = 6; i <= 8; i++) yield return Show(D(1, i), 0.1f);
        }
    }

    IEnumerator FireRain()
    {
        // Bay lên cao giữa đấu trường
        Vector3 top = new Vector3((minX + maxX) * 0.5f, groundY + 8f, 0f);
        yield return FlyTo(top, 0.8f);

        int n = 8 + level;
        bool leftToRight = Random.value < 0.5f;
        float fallSpeed = 9f;
        float startY = groundY + 9f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1);
            float x = Mathf.Lerp(minX + 1.5f, maxX - 1.5f, leftToRight ? t : 1f - t);
            float fallTime = (startY - groundY) / fallSpeed;
            BossHazard.Marker(x, groundY, fallTime, new Color(1f, 0.5f, 0.2f, 0.9f));
            Fireball.Spawn(new Vector3(x, startY, 0f), Vector3.down * fallSpeed, Color.white, 1.3f);
            sr.sprite = D(1, 6 + i % 3);
            yield return new WaitForSeconds(0.18f);
        }
        // Thêm 1 quả nhắm thẳng Player
        BossHazard.Marker(PlayerX, groundY, 1f, new Color(1f, 0.5f, 0.2f, 0.9f));
        Fireball.Spawn(new Vector3(PlayerX, startY, 0f), Vector3.down * fallSpeed, Color.white, 1.5f);
        yield return new WaitForSeconds(0.5f);
    }

    // =========================== DẠNG 3: HẮC LONG ===========================
    IEnumerator DarkDragonTurn()
    {
        yield return Hover(0.7f, 4.5f, 4.0f, 4.2f);
        float r = Random.value;
        if (r < 0.35f) yield return ChaosRing();
        else if (r < 0.7f) yield return SpreadingPillars();
        else yield return Dive(14f, true);
    }

    IEnumerator ChaosRing()
    {
        for (int i = 0; i <= 4; i++) yield return Show(D(1, i), 0.08f);
        int waves = level >= 2 ? 3 : 2;
        for (int w = 0; w < waves; w++)
        {
            sr.sprite = D(1, 6 + w % 3);
            float offset = w * 15f;
            for (int a = 0; a < 12; a++)
            {
                Vector3 dir = Quaternion.Euler(0, 0, offset + a * 30f) * Vector3.right;
                Fireball.Spawn(transform.position, dir * 6f, PurpleFire, 1.3f);
            }
            Shake(0.1f, 0.06f);
            yield return new WaitForSeconds(0.38f);
        }
        yield return Show(D(0, 0), 0.3f);
    }

    IEnumerator SpreadingPillars()
    {
        for (int i = 0; i <= 5; i++) yield return Show(D(1, i), 0.08f);
        sr.sprite = D(1, 7);

        // Cột lửa lan từ dưới chân Boss ra 2 bên
        float x0 = Mathf.Clamp(transform.position.x, minX, maxX);
        float step = 1.7f;
        int maxK = Mathf.CeilToInt((maxX - minX) / step);
        for (int k = 0; k <= maxK; k++)
        {
            float delay = 0.6f + k * 0.13f;
            float xr = x0 + k * step, xl = x0 - k * step;
            if (xr <= maxX) BossHazard.Pillar(xr, groundY, delay, 0.6f, PurpleFire);
            if (k > 0 && xl >= minX) BossHazard.Pillar(xl, groundY, delay, 0.6f, PurpleFire);
        }
        yield return new WaitForSeconds(0.6f + maxK * 0.13f * 0.5f);

        // 3 cột ngay chỗ Player đang đứng
        float px = PlayerX;
        for (int j = -1; j <= 1; j++)
            BossHazard.Pillar(Mathf.Clamp(px + j * step, minX, maxX), groundY, 0.7f, 0.6f, PurpleFire);
        yield return new WaitForSeconds(1.2f);
    }

    // Dạng 2: Bổ Nhào thường | Dạng 3 (meteor = true): đáp đất tạo sóng chấn rồi đứng choáng
    IEnumerator Dive(float speed, bool meteor)
    {
        FacePlayer();
        for (int i = 1; i <= 4; i++)
        {
            tintColor = new Color(1f, 1f, 0.45f);
            tintTimer = (i % 2 == 0) ? 0.12f : 0f;
            yield return Show(D(1, i), meteor ? 0.15f : 0.13f);
        }
        tintTimer = 0f;

        Vector3 start = transform.position;
        float bodyHalf = box.size.y * 0.5f;
        Vector3 target = new Vector3(PlayerX, groundY + bodyHalf + (meteor ? 0.05f : 0.6f), 0f);
        Vector3 dir = target - start; dir.z = 0f;
        float dist = dir.magnitude; dir.Normalize();
        facingRight = dir.x > 0f;

        sr.sprite = D(1, 5);
        float travelled = 0f;
        bool hit = false;
        while (travelled < dist)
        {
            float s = speed * Time.deltaTime;
            travelled += s;
            MoveTo(transform.position + dir * s);
            if (!hit && BodyHit()) hit = true;
            yield return null;
        }

        if (meteor)
        {
            // Đáp đất: sóng chấn tím 2 bên + đứng choáng (chém kiếm được)
            MoveTo(new Vector3(transform.position.x, groundY + bodyHalf, 0f));
            float x = transform.position.x;
            BossHazard.Shockwave(x + 1.4f, groundY, 1f, 7f, minX - 1f, maxX + 1f, PurpleFire);
            BossHazard.Shockwave(x - 1.4f, groundY, -1f, 7f, minX - 1f, maxX + 1f, PurpleFire);
            Shake(0.4f, 0.2f);
            landed = true;
            float stun = 1.8f, t = 0f;
            while (t < stun)
            {
                t += Time.deltaTime;
                sr.sprite = D(2, 3 + (int)(t * 6f) % 2);   // nằm choáng
                yield return null;
            }
            landed = false;
        }

        // Bay vọt lên lại
        yield return FlyTo(new Vector3(transform.position.x, groundY + 4.5f, 0f), 0.6f);
    }

    // ------------------------------------------------------------------
    // Bay lượn giữ khoảng cách với Player
    IEnumerator Hover(float duration, float keepDist, float height, float speed)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            bobPhase += Time.deltaTime * 3f;
            FacePlayer();
            float side = PlayerX > (minX + maxX) * 0.5f ? -1f : 1f;   // đứng phía rộng hơn của đấu trường
            if (slot % 2 == 1) side = -side;                             // Boss thứ 2 đứng phía còn lại
            Vector3 target = new Vector3(PlayerX + side * keepDist, groundY + height + Mathf.Sin(bobPhase) * 0.3f, 0f);
            Vector3 p = transform.position;
            MoveTo(new Vector3(Mathf.MoveTowards(p.x, target.x, speed * Time.deltaTime),
                               Mathf.MoveTowards(p.y, target.y, speed * Time.deltaTime), 0f));
            LoopFly(12f);
            yield return null;
        }
    }

    IEnumerator FlyTo(Vector3 target, float duration)
    {
        Vector3 start = transform.position;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            MoveTo(Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / duration)));
            LoopFly(16f);
            yield return null;
        }
    }

    void LoopFly(float fps)
    {
        loopTimer += Time.deltaTime;
        if (loopTimer > 1f / fps) { loopTimer = 0f; loopFrame = (loopFrame + 1) % DragLen[0]; }
        sr.sprite = D(0, loopFrame);
    }

    Vector3 Mouth() { return transform.position + new Vector3(Dir * 1.3f * sizeScale / 1.5f, -0.2f, 0f); }

    // ------------------------------------------------------------------
    // Sát thương lên Player
    void HitBox(Vector2 offset, Vector2 size)
    {
        Vector3 c = transform.position + new Vector3(offset.x * Dir, offset.y, 0f);
        HurtPlayerIn(c, new Vector3(size.x, size.y, 2f));
    }

    bool BodyHit()
    {
        Vector3 c = transform.position + box.center;
        return HurtPlayerIn(c, box.size * 0.9f);
    }

    bool HurtPlayerIn(Vector3 center, Vector3 size)
    {
        foreach (Collider col in Physics.OverlapBox(center, size * 0.5f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            PlayerController pc = col.GetComponentInParent<PlayerController>();
            if (pc != null) { pc.Hurt(1, transform.position); return true; }
        }
        return false;
    }

    void Shake(float d, float s)
    {
        if (Camera.main == null) return;
        CameraShaker sh = Camera.main.GetComponent<CameraShaker>();
        if (sh == null) sh = Camera.main.gameObject.AddComponent<CameraShaker>();
        sh.Shake(d, s);
    }

    // ------------------------------------------------------------------
    // Giẫm đầu = 1 sát thương
    public void TakeHit() { TakeDamage(1); }

    // Kiếm (2) / Chưởng (1) — HeroSkills gọi
    public void TakeDamage(int amount)
    {
        if (dead || transforming || hitInvul > 0f) return;
        hitInvul = 0.12f;
        Hp = Mathf.Max(0, Hp - Mathf.Max(1, amount));
        flashTimer = 0.1f;

        if (Hp <= 0) { StopAllCoroutines(); StartCoroutine(Die()); return; }

        // Thanh máu chia đều theo số dạng: hết 1 khúc -> sang dạng tiếp theo
        int idx = Mathf.Min(forms.Length - 1, (int)((1f - (float)Hp / MaxHp) * forms.Length));
        if (idx > formIndex)
        {
            formIndex = idx;
            StopAllCoroutines();
            StartCoroutine(TransformTo(forms[idx]));
        }
    }

    IEnumerator TransformTo(Form f)
    {
        transforming = true;
        landed = false;
        GameSession.Banner("DẠNG " + (formIndex + 1) + ": " + FormNames[(int)f], FormHints[(int)f]);
        Shake(1.4f, 0.15f);

        // Nháy sáng + phình to
        for (int i = 0; i < 14; i++)
        {
            sr.color = (i % 2 == 0) ? Color.white * 1.5f : (f == Form.DarkDragon ? Purple : new Color(1f, 0.6f, 0.3f));
            visual.localScale = Vector3.one * sizeScale * (1f + 0.08f * Mathf.Sin(i * 1.3f));
            yield return new WaitForSeconds(0.08f);
        }

        Vector3 feet = new Vector3(transform.position.x, groundY, 0f);
        SetForm(f);
        if (CurrentForm != Form.OgreKing)
        {
            transform.position = feet + Vector3.up * 1.2f;
            yield return FlyTo(new Vector3(transform.position.x, groundY + 4.5f, 0f), 0.7f);
        }
        transforming = false;
        StartCoroutine(Brain());
    }

    IEnumerator Die()
    {
        dead = true;
        box.enabled = false;
        if (GameSession.Bosses.Count <= 1) GameSession.Banner("CHIẾN THẮNG!", "Đã hạ " + Name);
        else GameSession.Banner("ĐÃ HẠ " + Name + "!", "Còn " + (GameSession.Bosses.Count - 1) + " Boss nữa");
        Shake(1.2f, 0.2f);
        sr.color = Color.white;

        // Rơi xuống đất
        bool dragonForm = CurrentForm != Form.OgreKing;
        float vy = 0f, t = 0f;
        float floorY = dragonForm ? groundY + 0.8f : groundY;
        while (transform.position.y > floorY + 0.01f && t < 3f)
        {
            t += Time.deltaTime;
            vy -= 15f * Time.deltaTime;
            transform.position = new Vector3(transform.position.x, Mathf.Max(floorY, transform.position.y + vy * Time.deltaTime), 0f);
            if (dragonForm) sr.sprite = D(2, Mathf.Min(5, 3 + (int)(t * 8f)));
            else sr.sprite = O(3, 6);
            yield return null;
        }
        for (int i = 0; i < 16; i++) { sr.enabled = !sr.enabled; yield return new WaitForSeconds(0.08f); }

        GameSession.BossDied(this);
        Destroy(gameObject);
    }
}
