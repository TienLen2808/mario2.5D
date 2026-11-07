using System.Collections;
using UnityEngine;

// Quái khổng lồ cầm rìu (OGRE)
// Sprite: Resources/ogre_sheet.png — lưới 11 cột x 7 hàng, mỗi ô 150 x 98 px
//   Hàng 0 Idle(2)  1 Walk(4)  2 Chop(5)  3 Smash(11)  4 Charge(4)  5 Spin(7)  6 Roar(2)
//
// Chiêu thức:
//   1. Chém Bổ   (Chop)  : vung rìu chém phía trước, tầm gần, ra đòn nhanh
//   2. Đập Đất   (Smash) : giơ rìu lâu (dễ né) rồi đập xuống, tầm xa hơn, hồi chiêu lâu
//   3. Lốc Xoáy  (Spin)  : tụ lực rồi xoay rìu, đánh cả 2 bên, vừa xoay vừa lao tới
//   4. Gầm Thét  (Roar)  : khi mất máu lần đầu -> nổi giận: nhanh hơn, ra chiêu dồn dập hơn
//
// Player giẫm lên đầu 3 lần để hạ. Chạm thân hoặc dính đòn = chết.
public class OgreController : MonoBehaviour, IStompable
{
    public const string OgreName = "OGRE";
    public const string MiniName = "OGRE_MINI";

    public bool IsFlying { get { return false; } } // quái đi bộ -> chém bằng kiếm

    [Header("Loại quái")]
    public bool isMini = false;      // Bản nhỏ: thay cho GOOMBA, 1 máu, chỉ biết Chém Bổ
    public float sizeScale = 1f;

    [Header("Chỉ số")]
    public int maxHp = 3;
    public float patrolSpeed = 1.2f;
    public float chaseSpeed = 2.4f;
    public float detectionRange = 9f;
    public float attackCooldown = 1.4f;
    public float enragedMultiplier = 1.5f;   // Tốc độ khi nổi giận
    public float enrageDuration = 6f;

    [Header("Sức mạnh theo màn (LevelGenerator gọi ApplyLevel)")]
    public int monsterLevel = 1;
    public float leadTime = 0f;          // "khôn": đoán trước vị trí Player sau leadTime giây
    Color levelTint = Color.white;
    bool baseCaptured;
    int baseHp; float baseChase, basePatrol, baseCooldown, baseDodge, baseDetect;
    Rigidbody playerRb;

    // Màn càng cao: máu nhiều hơn, nhanh hơn, ra đòn dồn dập hơn, né giỏi hơn, đoán hướng chạy, màu dữ hơn
    public void ApplyLevel(int level)
    {
        if (!baseCaptured)
        {
            baseCaptured = true;
            baseHp = maxHp; baseChase = chaseSpeed; basePatrol = patrolSpeed;
            baseCooldown = attackCooldown; baseDodge = dodgeChance; baseDetect = detectionRange;
        }
        monsterLevel = Mathf.Max(1, level);
        int k = monsterLevel - 1;
        maxHp = baseHp + (isMini ? (monsterLevel >= 3 ? 1 : 0) + (monsterLevel >= 5 ? 1 : 0) : k / 2);
        chaseSpeed = baseChase * (1f + 0.07f * k);
        patrolSpeed = basePatrol * (1f + 0.05f * k);
        attackCooldown = Mathf.Max(0.55f, baseCooldown * (1f - 0.08f * k));
        dodgeChance = Mathf.Min(0.75f, baseDodge + 0.06f * k);
        detectionRange = baseDetect + k;
        leadTime = Mathf.Min(0.4f, 0.08f * k);
        levelTint = monsterLevel >= 5 ? new Color(0.82f, 0.62f, 1f)      // tím: quái tinh anh
                  : monsterLevel >= 3 ? new Color(1f, 0.72f, 0.66f)      // đỏ: hung dữ
                  : Color.white;
        Transform v = transform.Find("Visual");
        if (v != null) v.localScale = Vector3.one * sizeScale * (1f + 0.04f * k);
    }

    [Header("Né chiêu")]
    [Range(0f, 1f)] public float dodgeChance = 0.4f;   // Xác suất né khi Player chém (khi nổi giận +20%)
    public float dodgeCooldown = 1.8f;                 // Né xong phải chờ bao lâu mới né tiếp
    public float dodgeDistance = 1.8f;

    [Header("Tầm đánh")]
    // Tầm tính từ tâm Ogre tới tâm Player (đã cộng nửa thân Player ~0.5)
    public float chopRange = 1.5f;
    public float smashRange = 2.0f;
    public float spinRange = 1.6f;

    [Header("Vùng đi tuần (LevelGenerator tự gán)")]
    public float minX = -1000f;
    public float maxX = 1000f;

    // --- Sprite sheet ---
    const int Cols = 11, CellW = 150, CellH = 98;
    const float PixelsPerUnit = 40f; // ô cao 98px -> ~2.45 unit (thân ~1.9 unit)
    enum Anim { Idle = 0, Walk = 1, Chop = 2, Smash = 3, Charge = 4, Spin = 5, Roar = 6 }
    static readonly int[] AnimLength = { 2, 4, 5, 11, 4, 7, 2 };
    static Sprite[] sprites;

    // --- Trạng thái ---
    int hp;
    bool busy;          // đang ra chiêu
    bool dead;
    bool enraged;
    bool hasRoared;
    float enrageTimer;
    float cooldownTimer;
    float invulTimer;
    float evadeTimer;        // đang né -> miễn sát thương
    float dodgeTimer;
    MonsterHpBar hpBar;
    bool facingRight;
    int patrolDir = -1;

    Anim loopAnim = Anim.Idle;
    float loopFps = 4f;
    float loopTimer;
    int loopFrame;

    Rigidbody rb;
    BoxCollider box;
    SpriteRenderer sr;
    Transform player;
    PlayerController playerCtrl;

    // ------------------------------------------------------------------
    // Tạo Ogre hoàn toàn bằng code (không cần prefab)
    // mini = true: bản nhỏ thay cho GOOMBA. Object trả về đang TẮT, bật lên khi đã đặt vị trí.
    public static OgreController Create(bool mini = false)
    {
        GameObject go = new GameObject(mini ? MiniName : OgreName);
        go.SetActive(false); // cấu hình xong mới chạy Awake/OnEnable
        try { go.tag = "Enemy"; } catch (UnityException) { }
        float k = mini ? 0.55f : 1f;

        Rigidbody body = go.AddComponent<Rigidbody>();
        body.mass = 5f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;

        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.size = new Vector3(1.1f * k, 1.8f * k, 1f);
        bc.center = new Vector3(0f, 0.9f * k, 0f); // gốc toạ độ = chân

        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform, false);
        visual.transform.localScale = Vector3.one * k;
        visual.AddComponent<SpriteRenderer>();

        OgreController ogre = go.AddComponent<OgreController>();
        ogre.sizeScale = k;
        if (mini)
        {
            ogre.isMini = true;
            ogre.maxHp = 1;
            ogre.patrolSpeed = 1.5f;
            ogre.chaseSpeed = 2.6f;
            ogre.detectionRange = 5f;
            ogre.attackCooldown = 1.6f;
            ogre.chopRange = 1.05f;
            ogre.smashRange = 1.05f;
            ogre.spinRange = 0f;
            ogre.dodgeChance = 0.25f;
        }
        return ogre;
    }


    // Unity đang tắt "Reload Domain" khi bấm Play -> biến static không tự reset.
    // Sprite tạo lúc chạy bị Unity xoá khi dừng game, nên phải xoá cache mỗi lần bấm Play.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { sprites = null; }

    static void LoadSprites()
    {
        if (sprites != null && sprites[0] != null) return; // còn dùng được
        Texture2D tex = Resources.Load<Texture2D>("ogre_sheet");
        if (tex == null)
        {
            Debug.LogError("OgreController: không tìm thấy Resources/ogre_sheet.png");
            return;
        }
        int rows = AnimLength.Length;
        sprites = new Sprite[Cols * rows];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < AnimLength[r]; c++)
            {
                Rect rect = new Rect(c * CellW, tex.height - (r + 1) * CellH, CellW, CellH);
                sprites[r * Cols + c] = Sprite.Create(tex, rect, new Vector2(0.5f, 0f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        box = GetComponent<BoxCollider>();
        sr = GetComponentInChildren<SpriteRenderer>();
        LoadSprites();
        hpBar = MonsterHpBar.Attach(transform, 2.15f * sizeScale + 0.15f, isMini ? 0.8f : 1.4f, isMini ? 0.1f : 0.14f);
    }

    // Mỗi lần lấy ra từ pool -> reset
    void OnEnable()
    {
        hp = maxHp;
        evadeTimer = dodgeTimer = 0f;
        if (hpBar != null) { hpBar.SetVisible(true); hpBar.Set(1f, true); }
        HeroSkills.OnSwordSwing += OnSwordSwing;
        busy = dead = enraged = false;
        hasRoared = isMini; // bản nhỏ không biết Gầm Thét
        cooldownTimer = 1f;
        invulTimer = 0f;
        if (box != null) box.enabled = true;
        if (rb != null) { rb.isKinematic = false; rb.linearVelocity = Vector3.zero; }
        if (sr != null) { sr.color = levelTint; sr.enabled = true; }
        patrolDir = Random.value < 0.5f ? -1 : 1;
        SetLoop(Anim.Idle, 3f);
        FindPlayer();
    }

    void OnDisable()
    {
        HeroSkills.OnSwordSwing -= OnSwordSwing;
    }

    // ------------------------------------------------------------------
    // NÉ CHIÊU: Player vung kiếm gần đó -> có tỉ lệ nhảy lùi né (miễn sát thương 0.3s)
    void OnSwordSwing(Vector3 heroPos, float heroDir)
    {
        if (dead || busy || dodgeTimer > 0f || sprites == null) return;
        float dx = transform.position.x - heroPos.x;
        float dy = Mathf.Abs(transform.position.y - heroPos.y);
        bool inFront = dx * heroDir > -0.3f;
        if (!inFront || Mathf.Abs(dx) > 3f || dy > 2.5f) return;

        float chance = dodgeChance + (enraged ? 0.2f : 0f);
        if (Random.value > chance) return;
        StartCoroutine(Dodge(Mathf.Sign(heroDir)));
    }

    IEnumerator Dodge(float awayDir)
    {
        busy = true;
        dodgeTimer = dodgeCooldown;
        evadeTimer = 0.32f;
        facingRight = awayDir < 0f;   // vẫn quay mặt về phía Player
        sr.color = new Color(1f, 1f, 1f, 0.45f);
        sr.sprite = sprites[(int)Anim.Walk * Cols + 1];

        // Nhảy lùi (không vượt ra ngoài vùng đi tuần)
        float speed = dodgeDistance / 0.22f;
        if ((awayDir < 0 && transform.position.x <= minX) || (awayDir > 0 && transform.position.x >= maxX)) speed = 0f;
        rb.linearVelocity = new Vector3(awayDir * speed, 3.5f, 0f);
        yield return new WaitForSeconds(0.22f);
        rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
        yield return new WaitForSeconds(0.1f);

        sr.color = enraged ? new Color(1f, 0.7f, 0.7f) : levelTint;
        cooldownTimer = Mathf.Min(cooldownTimer, 0.35f);   // né xong phản công nhanh
        busy = false;
    }

    void FindPlayer()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) { player = p.transform; playerCtrl = p.GetComponent<PlayerController>(); }
    }

    // ------------------------------------------------------------------
    void Update()
    {
        if (sprites == null || sprites[0] == null || dead) return;

        cooldownTimer -= Time.deltaTime;
        invulTimer -= Time.deltaTime;
        evadeTimer -= Time.deltaTime;
        dodgeTimer -= Time.deltaTime;
        if (enraged)
        {
            enrageTimer -= Time.deltaTime;
            if (enrageTimer <= 0f) { enraged = false; sr.color = levelTint; }
        }

        if (!busy) Think();
        UpdateLoopAnim();
        sr.flipX = facingRight; // Sprite gốc quay mặt sang TRÁI
    }

    void Think()
    {
        if (player == null) FindPlayer();
        float speedMul = enraged ? enragedMultiplier : 1f;

        bool seesPlayer = false;
        float dx = 0f;
        if (player != null && player.gameObject.activeInHierarchy)
        {
            // Quái màn cao đoán trước Player sẽ chạy tới đâu
            if (playerRb == null) playerRb = player.GetComponent<Rigidbody>();
            float predicted = player.position.x + (playerRb != null ? playerRb.linearVelocity.x * leadTime : 0f);
            dx = predicted - transform.position.x;
            float dy = Mathf.Abs(player.position.y - transform.position.y);
            seesPlayer = Mathf.Abs(dx) < detectionRange && dy < 3f
                         && player.position.x > minX - 2f && player.position.x < maxX + 2f;
        }

        if (seesPlayer)
        {
            facingRight = dx > 0f;
            float adx = Mathf.Abs(dx);

            if (cooldownTimer <= 0f && adx <= smashRange)
            {
                StartCoroutine(ChooseAttack(adx));
                return;
            }

            // Đuổi theo (giữ khoảng cách vừa tầm chém)
            if (adx > chopRange * 0.8f) Move(Mathf.Sign(dx) * chaseSpeed * speedMul, 9f);
            else { Move(0f, 3f); SetLoop(Anim.Idle, 3f); }
        }
        else
        {
            // Đi tuần qua lại trong vùng cho phép
            if (transform.position.x <= minX) patrolDir = 1;
            else if (transform.position.x >= maxX) patrolDir = -1;
            facingRight = patrolDir > 0;
            Move(patrolDir * patrolSpeed * speedMul, 5f);
        }
    }

    void Move(float vx, float animFps)
    {
        // Không cho đi ra khỏi vùng (tránh rơi khỏi mảnh map)
        if ((vx < 0f && transform.position.x <= minX) || (vx > 0f && transform.position.x >= maxX)) vx = 0f;
        rb.linearVelocity = new Vector3(vx, rb.linearVelocity.y, 0f);
        if (Mathf.Abs(vx) > 0.01f) SetLoop(Anim.Walk, animFps);
        else SetLoop(Anim.Idle, 3f);
    }

    void Stop() { rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f); }

    // ------------------------------------------------------------------
    // CHIÊU THỨC

    IEnumerator ChooseAttack(float adx)
    {
        busy = true;
        Stop();

        float roll = Random.value;
        if (isMini)                                yield return Chop();
        else if (adx <= chopRange && roll < 0.45f) yield return Chop();
        else if (adx <= spinRange && roll < 0.75f) yield return Spin();
        else                                       yield return Smash();

        cooldownTimer = attackCooldown * (enraged ? 0.5f : 1f);
        busy = false;
    }

    // 1. Chém Bổ
    IEnumerator Chop()
    {
        float f = enraged ? 13f : 10f;
        yield return Frame(Anim.Chop, 0, 0.35f);                 // giơ rìu (báo hiệu)
        for (int i = 1; i <= 4; i++)
        {
            yield return Frame(Anim.Chop, i, 1f / f);
            // Vùng đánh khớp với lưỡi rìu trên hình (đo từ sprite)
            if (i == 1) HitBox(new Vector2(0.65f, 0.8f), new Vector2(0.9f, 1.4f));   // rìu vung ngang, xa ~1.1
            if (i == 2) HitBox(new Vector2(0.47f, 0.45f), new Vector2(0.55f, 0.9f)); // rìu bổ xuống, xa ~0.75
        }
        yield return Frame(Anim.Idle, 0, 0.25f);
    }

    // 2. Đập Đất
    IEnumerator Smash()
    {
        float windup = enraged ? 0.14f : 0.2f;
        for (int i = 0; i <= 2; i++) yield return Frame(Anim.Smash, i, windup); // giơ rìu cao
        yield return Frame(Anim.Smash, 3, 0.08f);                // vệt chém (xa ~1.5)
        HitBox(new Vector2(0.85f, 1.15f), new Vector2(1.3f, 2.1f));
        yield return Frame(Anim.Smash, 4, 0.08f);                // rìu chạm đất (xa ~1.65)
        HitBox(new Vector2(0.92f, 0.4f), new Vector2(1.45f, 0.8f));
        ShakeCamera(0.25f, 0.15f);
        yield return Frame(Anim.Smash, 5, 0.35f);                // rìu cắm đất (cơ hội phản công)
        for (int i = 6; i <= 10; i++) yield return Frame(Anim.Smash, i, 0.1f); // kéo rìu lên
    }

    // 3. Lốc Xoáy
    IEnumerator Spin()
    {
        for (int loop = 0; loop < (enraged ? 1 : 2); loop++)    // tụ lực (báo hiệu)
            for (int i = 0; i < AnimLength[(int)Anim.Charge]; i++)
                yield return Frame(Anim.Charge, i, 0.09f);

        float dir = facingRight ? 1f : -1f;
        for (int i = 0; i < AnimLength[(int)Anim.Spin]; i++)
        {
            bool active = i >= 4;
            if (active)
            {
                Vector3 v = new Vector3(dir * 3.5f, rb.linearVelocity.y, 0f);
                if ((dir < 0 && transform.position.x <= minX) || (dir > 0 && transform.position.x >= maxX)) v.x = 0f;
                rb.linearVelocity = v;
                HitBox(new Vector2(0.38f, 0.75f), new Vector2(1.65f, 1.3f)); // trước ~1.2, sau ~0.45
            }
            yield return Frame(Anim.Spin, i, active ? 0.1f : 0.07f);
        }
        Stop();
        yield return Frame(Anim.Idle, 0, 0.3f);
    }

    // 4. Gầm Thét
    IEnumerator Roar()
    {
        busy = true;
        Stop();
        ShakeCamera(0.6f, 0.08f);
        for (int k = 0; k < 6; k++) yield return Frame(Anim.Roar, k % 2, 0.15f);
        enraged = true;
        enrageTimer = enrageDuration;
        sr.color = new Color(1f, 0.7f, 0.7f);
        cooldownTimer = 0.3f;
        busy = false;
    }

    // ------------------------------------------------------------------
    // Nhận sát thương (PlayerController gọi khi giẫm lên đầu)
    public void TakeHit()
    {
        if (dead || invulTimer > 0f || evadeTimer > 0f) return;
        invulTimer = 0.5f;
        hp--;
        if (hpBar != null) hpBar.Set((float)hp / maxHp);

        if (hp <= 0) { StopAllCoroutines(); StartCoroutine(Die()); return; }

        StartCoroutine(Flash());
        if (!hasRoared)
        {
            hasRoared = true;
            StopAllCoroutines();   // nổi giận ngay cả khi đang ra chiêu
            StartCoroutine(Flash());
            StartCoroutine(Roar());
        }
    }

    IEnumerator Flash()
    {
        Color baseColor = enraged ? new Color(1f, 0.7f, 0.7f) : levelTint;
        for (int i = 0; i < 3; i++)
        {
            sr.color = new Color(1f, 0.3f, 0.3f);
            yield return new WaitForSeconds(0.06f);
            sr.color = baseColor;
            yield return new WaitForSeconds(0.06f);
        }
        sr.color = enraged ? new Color(1f, 0.7f, 0.7f) : levelTint;
    }

    IEnumerator Die()
    {
        dead = true;
        busy = true;
        box.enabled = false;
        rb.isKinematic = true;
        if (hpBar != null) hpBar.SetVisible(false);
        sr.sprite = sprites[(int)Anim.Smash * Cols + 6]; // tư thế khuỵu xuống
        for (int i = 0; i < 10; i++)
        {
            sr.enabled = !sr.enabled;
            yield return new WaitForSeconds(0.08f);
        }
        gameObject.SetActive(false); // LevelGenerator sẽ thu hồi về pool
    }

    // ------------------------------------------------------------------
    // Tiện ích

    // offset tính theo hướng đang quay mặt, size = kích thước vùng đánh
    void HitBox(Vector2 offset, Vector2 size)
    {
        float dir = facingRight ? 1f : -1f;
        offset *= sizeScale;
        size *= sizeScale;
        Vector3 center = transform.position + new Vector3(offset.x * dir, offset.y, 0f);
        Collider[] hits = Physics.OverlapBox(center, new Vector3(size.x, size.y, 2f) * 0.5f,
                                             Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider c in hits)
        {
            PlayerController pc = c.GetComponentInParent<PlayerController>();
            if (pc != null) { pc.Hurt(1, transform.position); break; }
        }
        lastHitCenter = center; lastHitSize = size; lastHitTime = Time.time;
    }

    IEnumerator Frame(Anim anim, int index, float duration)
    {
        sr.sprite = sprites[(int)anim * Cols + index];
        yield return new WaitForSeconds(duration);
    }

    void SetLoop(Anim anim, float fps)
    {
        if (loopAnim != anim) { loopAnim = anim; loopFrame = 0; loopTimer = 0f; }
        loopFps = fps;
    }

    void UpdateLoopAnim()
    {
        if (busy) return;
        loopTimer += Time.deltaTime;
        float t = 1f / Mathf.Max(0.1f, loopFps);
        while (loopTimer >= t) { loopTimer -= t; loopFrame = (loopFrame + 1) % AnimLength[(int)loopAnim]; }
        sr.sprite = sprites[(int)loopAnim * Cols + loopFrame];
    }

    void ShakeCamera(float duration, float strength)
    {
        if (isMini || Camera.main == null) return;
        CameraShaker shaker = Camera.main.GetComponent<CameraShaker>();
        if (shaker == null) shaker = Camera.main.gameObject.AddComponent<CameraShaker>();
        shaker.Shake(duration, strength);
    }

    // Va chạm thân với Player do PlayerController xử lý (tag Enemy)

    // Vẽ vùng đánh trong cửa sổ Scene để dễ căn chỉnh
    Vector3 lastHitCenter; Vector2 lastHitSize; float lastHitTime = -10f;
    void OnDrawGizmosSelected()
    {
        if (Time.time - lastHitTime < 0.3f)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(lastHitCenter, new Vector3(lastHitSize.x, lastHitSize.y, 1f));
        }
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(new Vector3(minX, transform.position.y, 0f), new Vector3(maxX, transform.position.y, 0f));
    }
}

// Rung camera nhẹ (chạy sau CameraFollow nên không bị ghi đè)
[DefaultExecutionOrder(1000)]
public class CameraShaker : MonoBehaviour
{
    float timer, strength;
    Vector3 offset;

    public void Shake(float duration, float power)
    {
        timer = Mathf.Max(timer, duration);
        strength = Mathf.Max(strength, power);
    }

    void LateUpdate()
    {
        transform.position -= offset; // bỏ độ lệch của frame trước
        offset = Vector3.zero;
        if (timer > 0f)
        {
            timer -= Time.deltaTime;
            offset = (Vector3)(Random.insideUnitCircle * strength);
            transform.position += offset;
            if (timer <= 0f) strength = 0f;
        }
    }
}
