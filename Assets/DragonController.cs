using System.Collections;
using UnityEngine;

// Rồng lửa (quái bay)
// Sprite: Resources/dragon_sheet.png — 9 cột x 3 hàng, mỗi ô 302 x 312 px, thân rồng ở giữa ô
//   Hàng 0 Fly(7)  |  Hàng 1 Attack(9): 0-4 chồm lên, 5 lao xuống, 6-8 phun lửa  |  Hàng 2 HurtDie(6): 0-2 trúng đòn, 3-5 rơi chết
// Hiệu ứng: Resources/dragon_fx.png — 3 cột x 2 hàng, ô 64 x 68: hàng 0 quả cầu lửa(3), hàng 1 nổ(2)
//
// Chiêu thức:
//   1. Phun Lửa  : chồm lên (báo hiệu) rồi phun quả cầu lửa bay thẳng tới chỗ Player đang đứng
//                  Còn 1 máu -> phun 3 quả toả ra
//   2. Bổ Nhào   : chồm lên + nháy sáng (báo hiệu), khoá vị trí Player rồi lao xuống theo đường thẳng
//
// Bay lơ lửng, không bị trọng lực. Giẫm lên lưng 2 lần để hạ. Chạm thân = bị đẩy lùi.
public class DragonController : MonoBehaviour, IStompable
{
    public const string DragonName = "DRAGON";

    public bool IsFlying { get { return true; } } // quái bay -> bắn Tia Lửa Xanh

    [Header("Chỉ số")]
    public int maxHp = 2;
    public float patrolSpeed = 2f;
    public float chaseSpeed = 3f;
    public float hoverHeight = 3.5f;        // Bay cao hơn mặt đất / Player bao nhiêu
    public float bobAmplitude = 0.25f;      // Nhấp nhô khi bay
    public float detectionRange = 11f;
    public float keepDistance = 4.5f;       // Giữ khoảng cách ngang với Player khi rình
    public float attackCooldown = 2.2f;

    [Header("Phun Lửa")]
    public float fireballSpeed = 6.5f;
    public float spreadAngle = 18f;

    [Header("Bổ Nhào")]
    public float diveSpeed = 9f;
    public float diveRange = 6.5f;          // Chỉ bổ nhào khi Player trong tầm ngang này

    [Header("Sức mạnh theo màn (LevelGenerator gọi ApplyLevel)")]
    public int monsterLevel = 1;
    public float aimLead = 0f;           // "khôn": 0 = bắn vào chỗ Player đang đứng, 1 = đón đầu hướng Player chạy
    public float diveChance = 0.35f;
    Color levelTint = Color.white;
    bool baseCaptured;
    int baseHp; float baseFire, baseCooldown, baseDodge, baseChase;
    Rigidbody playerRb;

    public void ApplyLevel(int level)
    {
        if (!baseCaptured)
        {
            baseCaptured = true;
            baseHp = maxHp; baseFire = fireballSpeed; baseCooldown = attackCooldown; baseDodge = dodgeChance; baseChase = chaseSpeed;
        }
        monsterLevel = Mathf.Max(1, level);
        int k = monsterLevel - 1;
        maxHp = baseHp + k / 2;
        fireballSpeed = baseFire + 0.5f * k;
        attackCooldown = Mathf.Max(1.0f, baseCooldown * (1f - 0.08f * k));
        dodgeChance = Mathf.Min(0.8f, baseDodge + 0.06f * k);
        chaseSpeed = baseChase * (1f + 0.06f * k);
        aimLead = Mathf.Min(1f, 0.3f * k);
        diveChance = Mathf.Min(0.6f, 0.35f + 0.05f * k);
        levelTint = monsterLevel >= 5 ? new Color(0.78f, 0.6f, 1f)
                  : monsterLevel >= 3 ? new Color(1f, 0.75f, 0.7f)
                  : Color.white;
        Transform v = transform.Find("Visual");
        if (v != null) v.localScale = Vector3.one * (1f + 0.04f * k);
    }

    [Header("Né chiêu")]
    [Range(0f, 1f)] public float dodgeChance = 0.5f;   // Xác suất né Tia Lửa Xanh (còn 1 máu +20%)
    public float dodgeCooldown = 1.5f;
    public float dodgeDistance = 1.8f;

    [Header("Vùng bay (LevelGenerator tự gán)")]
    public float minX = -1000f;
    public float maxX = 1000f;
    public float groundY = 0f;

    // --- Sprite ---
    const int Cols = 9, CellW = 302, CellH = 312;
    const float PixelsPerUnit = 115f;       // ô ~2.7 unit, thân ~1.5 unit
    enum Anim { Fly = 0, Attack = 1, HurtDie = 2 }
    static readonly int[] AnimLength = { 7, 9, 6 };
    static Sprite[] sprites;

    // Thân rồng (dùng cho va chạm và đòn Bổ Nhào)
    static readonly Vector3 BodySize = new Vector3(1.5f, 1.0f, 1f);

    int hp;
    bool busy, dead;
    float cooldownTimer, invulTimer, bobPhase;
    float evadeTimer, dodgeTimer;
    MonsterHpBar hpBar;
    bool facingRight;
    int patrolDir = -1;
    float hoverY;
    float loopTimer; int loopFrame;
    bool diving;

    Rigidbody rb;
    BoxCollider box;
    SpriteRenderer sr;
    Transform player;
    Coroutine attackRoutine;

    // ------------------------------------------------------------------
    public static DragonController Create()
    {
        GameObject go = new GameObject(DragonName);
        go.SetActive(false);
        try { go.tag = "Enemy"; } catch (UnityException) { }

        Rigidbody body = go.AddComponent<Rigidbody>();
        body.isKinematic = true;   // tự điều khiển bay, không trọng lực
        body.useGravity = false;

        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.size = BodySize;
        bc.center = Vector3.zero;

        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform, false);
        visual.AddComponent<SpriteRenderer>().sortingOrder = 5;

        return go.AddComponent<DragonController>();
    }


    // Unity đang tắt "Reload Domain" khi bấm Play -> biến static không tự reset.
    // Sprite tạo lúc chạy bị Unity xoá khi dừng game, nên phải xoá cache mỗi lần bấm Play.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { sprites = null; }

    static void LoadSprites()
    {
        if (sprites != null && sprites[0] != null) return; // còn dùng được
        Texture2D tex = Resources.Load<Texture2D>("dragon_sheet");
        if (tex == null) { Debug.LogError("DragonController: không tìm thấy Resources/dragon_sheet.png"); return; }
        sprites = new Sprite[Cols * AnimLength.Length];
        for (int r = 0; r < AnimLength.Length; r++)
            for (int c = 0; c < AnimLength[r]; c++)
            {
                Rect rect = new Rect(c * CellW, tex.height - (r + 1) * CellH, CellW, CellH);
                sprites[r * Cols + c] = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        box = GetComponent<BoxCollider>();
        sr = GetComponentInChildren<SpriteRenderer>();
        LoadSprites();
        hpBar = MonsterHpBar.Attach(transform, 1.15f, 1.4f, 0.14f);
    }

    void OnEnable()
    {
        hp = maxHp;
        evadeTimer = dodgeTimer = 0f;
        if (hpBar != null) { hpBar.SetVisible(true); hpBar.Set(1f, true); }
        HeroSkills.OnBoltFired += OnBoltFired;
        busy = dead = diving = false;
        cooldownTimer = 1.5f;
        invulTimer = 0f;
        bobPhase = Random.value * 10f;
        patrolDir = Random.value < 0.5f ? -1 : 1;
        hoverY = groundY + hoverHeight;
        if (box != null) box.enabled = true;
        if (sr != null) { sr.color = levelTint; sr.enabled = true; }
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        player = p != null ? p.transform : null;
    }

    void OnDisable()
    {
        HeroSkills.OnBoltFired -= OnBoltFired;
    }

    // ------------------------------------------------------------------
    // NÉ CHIÊU: Tia Lửa Xanh bay tới -> có tỉ lệ vọt lên/xuống né khỏi đường bay
    void OnBoltFired(Vector3 origin, Vector3 velocity)
    {
        if (dead || diving || dodgeTimer > 0f || sprites == null) return;
        if (busy && attackRoutine == null) return;         // đang bị đánh / đang né

        // Tia lửa có bay qua sát người Rồng không?
        Vector3 to = transform.position - origin; to.z = 0f;
        float v2 = velocity.sqrMagnitude;
        if (v2 < 0.01f) return;
        float t = Vector3.Dot(to, velocity) / v2;           // thời điểm tia lửa tới gần nhất
        if (t < 0f || t > 1.2f) return;
        Vector3 closest = origin + velocity * t;
        Vector3 off = transform.position - closest; off.z = 0f;
        if (off.magnitude > 1.4f) return;

        float chance = dodgeChance + (hp <= 1 ? 0.2f : 0f);
        if (Random.value > chance) return;

        // Né theo hướng vuông góc với đường bay, ra xa đường bay
        Vector3 perp = new Vector3(-velocity.y, velocity.x, 0f).normalized;
        if (Vector3.Dot(perp, off) < 0f) perp = -perp;
        if (off.magnitude < 0.05f) perp = Vector3.up;
        // Không né xuống quá thấp
        if (perp.y < 0f && transform.position.y + perp.y * dodgeDistance < groundY + 1.5f) perp = -perp;

        if (attackRoutine != null) { StopCoroutine(attackRoutine); attackRoutine = null; }
        StartCoroutine(Dodge(perp));
    }

    IEnumerator Dodge(Vector3 dir)
    {
        busy = true;
        dodgeTimer = dodgeCooldown;
        evadeTimer = 0.3f;
        sr.color = new Color(1f, 1f, 1f, 0.45f);
        float t = 0f, dur = 0.18f;
        while (t < dur)
        {
            t += Time.deltaTime;
            MoveTo(transform.position + dir * (dodgeDistance / dur) * Time.deltaTime);
            LoopFly(20f);
            yield return null;
        }
        sr.color = levelTint;
        cooldownTimer = Mathf.Min(cooldownTimer, 0.6f);     // né xong phản công
        busy = false;
    }

    // ------------------------------------------------------------------
    void Update()
    {
        if (sprites == null || sprites[0] == null || dead) return;
        cooldownTimer -= Time.deltaTime;
        invulTimer -= Time.deltaTime;
        evadeTimer -= Time.deltaTime;
        dodgeTimer -= Time.deltaTime;

        if (!busy)
        {
            Think();
            LoopFly(10f);
        }
        sr.flipX = facingRight; // Sprite gốc quay mặt sang TRÁI
    }

    void Think()
    {
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }

        bobPhase += Time.deltaTime * 3f;
        float bob = Mathf.Sin(bobPhase) * bobAmplitude;
        Vector3 pos = transform.position;

        bool sees = false;
        float dx = 0f;
        if (player != null && player.gameObject.activeInHierarchy)
        {
            dx = player.position.x - pos.x;
            sees = Mathf.Abs(dx) < detectionRange && player.position.x > minX - 6f && player.position.x < maxX + 6f;
        }

        float targetX, targetY, speed;
        if (sees)
        {
            facingRight = dx > 0f;
            // Lượn ở phía trên, giữ khoảng cách ngang với Player
            targetX = player.position.x - Mathf.Sign(dx) * keepDistance;
            targetY = Mathf.Max(groundY, player.position.y) + hoverHeight + bob;
            speed = chaseSpeed;

            if (cooldownTimer <= 0f)
            {
                bool canDive = Mathf.Abs(dx) < diveRange;
                attackRoutine = StartCoroutine(canDive && Random.value < diveChance ? Dive() : FireBreath());
                return;
            }
        }
        else
        {
            if (pos.x <= minX) patrolDir = 1;
            else if (pos.x >= maxX) patrolDir = -1;
            facingRight = patrolDir > 0;
            targetX = pos.x + patrolDir * 5f;
            targetY = hoverY + bob;
            speed = patrolSpeed;
        }

        targetX = Mathf.Clamp(targetX, minX - 6f, maxX + 6f);
        Vector3 next = new Vector3(
            Mathf.MoveTowards(pos.x, targetX, speed * Time.deltaTime),
            Mathf.MoveTowards(pos.y, targetY, speed * 0.8f * Time.deltaTime),
            0f);
        MoveTo(next);
    }

    // ------------------------------------------------------------------
    // CHIÊU 1: Phun Lửa
    IEnumerator FireBreath()
    {
        busy = true;
        for (int i = 0; i <= 4; i++) yield return Frame(Anim.Attack, i, 0.12f);   // chồm lên (báo hiệu)
        yield return Frame(Anim.Attack, 5, 0.08f);                                   // vươn cổ

        // Ngắm vào vị trí Player lúc này
        Vector3 mouth = MouthPosition();
        Vector3 target = player != null ? player.position : mouth + Vector3.down * 3f;
        // Rồng màn cao bắn ĐÓN ĐẦU hướng Player đang chạy
        if (player != null && aimLead > 0f)
        {
            if (playerRb == null) playerRb = player.GetComponent<Rigidbody>();
            if (playerRb != null)
            {
                float travel = Vector3.Distance(mouth, player.position) / Mathf.Max(1f, fireballSpeed);
                Vector3 v = playerRb.linearVelocity; v.y = 0f; v.z = 0f;
                target += v * travel * aimLead;
            }
        }
        Vector3 dir = (target - mouth); dir.z = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = facingRight ? Vector3.right : Vector3.left;
        dir.Normalize();

        bool triple = hp <= 1 || monsterLevel >= 4;   // từ Màn 4 luôn phun 3 quả
        Fireball.Spawn(mouth, dir * fireballSpeed);
        if (triple)
        {
            Fireball.Spawn(mouth, Quaternion.Euler(0, 0, spreadAngle) * dir * fireballSpeed);
            Fireball.Spawn(mouth, Quaternion.Euler(0, 0, -spreadAngle) * dir * fireballSpeed);
        }

        for (int i = 6; i <= 8; i++) yield return Frame(Anim.Attack, i, 0.1f);     // phun lửa
        yield return Frame(Anim.Fly, 0, 0.15f);
        EndAttack();
    }

    // CHIÊU 2: Bổ Nhào
    IEnumerator Dive()
    {
        busy = true;
        // Báo hiệu: chồm lên + nháy sáng
        for (int i = 1; i <= 4; i++)
        {
            sr.color = (i % 2 == 0) ? new Color(1f, 1f, 0.6f) : levelTint;
            yield return Frame(Anim.Attack, i, 0.13f);
        }
        sr.color = levelTint;

        Vector3 start = transform.position;
        Vector3 target = player != null ? player.position + Vector3.up * 0.4f : start + Vector3.down * 3f;
        float floorY = FloorBelow(target + Vector3.up * 0.5f);
        target.y = Mathf.Max(target.y, floorY + 0.6f);
        Vector3 dir = (target - start); dir.z = 0f;
        float dist = dir.magnitude;
        dir.Normalize();
        facingRight = dir.x > 0f;
        sr.flipX = facingRight;

        // Lao xuống theo đường thẳng (vị trí đã khoá, Player có thể né)
        diving = true;
        sr.sprite = sprites[(int)Anim.Attack * Cols + 5];
        float travelled = 0f;
        while (travelled < dist + 0.5f)
        {
            float step = diveSpeed * Time.deltaTime;
            travelled += step;
            Vector3 next = transform.position + dir * step;
            if (next.y < floorY + 0.6f) { next.y = floorY + 0.6f; travelled = dist + 1f; }
            MoveTo(next);
            if (BodyHitsPlayer()) break;
            yield return null;
        }
        diving = false;

        // Bay vọt lên lại
        float t = 0f;
        while (t < 0.6f)
        {
            t += Time.deltaTime;
            MoveTo(transform.position + Vector3.up * chaseSpeed * 1.2f * Time.deltaTime);
            LoopFly(14f);
            yield return null;
        }
        EndAttack();
    }

    // Rigidbody kinematic: đặt thẳng vị trí, vật lý vẫn tính va chạm với Player
    void MoveTo(Vector3 p)
    {
        p.z = 0f;
        transform.position = p;
    }

    float FloorBelow(Vector3 from)
    {
        RaycastHit hit;
        if (Physics.Raycast(from + Vector3.up * 0.1f, Vector3.down, out hit, 50f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider != box)
            return hit.point.y;
        return groundY;
    }

    void EndAttack()
    {
        cooldownTimer = attackCooldown * (hp <= 1 ? 0.7f : 1f);
        busy = false;
        attackRoutine = null;
    }

    bool BodyHitsPlayer()
    {
        Collider[] hits = Physics.OverlapBox(transform.position, BodySize * 0.45f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider c in hits)
        {
            PlayerController pc = c.GetComponentInParent<PlayerController>();
            if (pc != null) { pc.Hurt(1, transform.position); return true; }
        }
        return false;
    }

    Vector3 MouthPosition()
    {
        float dir = facingRight ? 1f : -1f;
        return transform.position + new Vector3(0.9f * dir, -0.15f, 0f);
    }

    // ------------------------------------------------------------------
    // Bị Player giẫm lên lưng
    public void TakeHit()
    {
        if (dead || invulTimer > 0f || evadeTimer > 0f) return;
        invulTimer = 0.6f;
        hp--;
        if (hpBar != null) hpBar.Set((float)hp / maxHp);

        if (attackRoutine != null) { StopCoroutine(attackRoutine); attackRoutine = null; }
        diving = false;
        sr.color = levelTint;

        if (hp <= 0) { StopAllCoroutines(); StartCoroutine(Die()); return; }
        StartCoroutine(Hurt());
    }

    IEnumerator Hurt()
    {
        busy = true;
        for (int i = 0; i < 3; i++)
        {
            sr.color = new Color(1f, 0.4f, 0.4f);
            yield return Frame(Anim.HurtDie, i, 0.1f);
            sr.color = levelTint;
        }
        // Bay lên lại độ cao an toàn
        float t = 0f;
        while (t < 0.4f)
        {
            t += Time.deltaTime;
            MoveTo(transform.position + Vector3.up * 3f * Time.deltaTime);
            yield return null;
        }
        cooldownTimer = Mathf.Max(cooldownTimer, 0.8f);
        busy = false;
    }

    IEnumerator Die()
    {
        dead = true;
        busy = true;
        box.enabled = false;
        if (hpBar != null) hpBar.SetVisible(false);
        sr.color = levelTint;

        // Rơi xuống đất
        float floor = FloorBelow(transform.position);
        float vy = 0f;
        int frame = 3;
        float frameTimer = 0f;
        float fallTime = 0f;
        while (transform.position.y > floor + 0.4f && fallTime < 3f)
        {
            fallTime += Time.deltaTime;
            vy -= 15f * Time.deltaTime;
            MoveTo(transform.position + new Vector3(0f, vy * Time.deltaTime, 0f));
            frameTimer += Time.deltaTime;
            if (frameTimer > 0.12f && frame < 5) { frame++; frameTimer = 0f; }
            sr.sprite = sprites[(int)Anim.HurtDie * Cols + frame];
            yield return null;
        }
        sr.sprite = sprites[(int)Anim.HurtDie * Cols + 5];
        yield return new WaitForSeconds(0.4f);
        for (int i = 0; i < 10; i++) { sr.enabled = !sr.enabled; yield return new WaitForSeconds(0.08f); }
        gameObject.SetActive(false); // LevelGenerator sẽ thu hồi về pool
    }

    // ------------------------------------------------------------------
    IEnumerator Frame(Anim anim, int index, float duration)
    {
        sr.sprite = sprites[(int)anim * Cols + index];
        yield return new WaitForSeconds(duration);
    }

    void LoopFly(float fps)
    {
        loopTimer += Time.deltaTime;
        float t = 1f / fps;
        while (loopTimer >= t) { loopTimer -= t; loopFrame = (loopFrame + 1) % AnimLength[(int)Anim.Fly]; }
        sr.sprite = sprites[(int)Anim.Fly * Cols + loopFrame];
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = diving ? Color.red : Color.yellow;
        Gizmos.DrawWireCube(transform.position, BodySize);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(new Vector3(minX, groundY + hoverHeight, 0f), new Vector3(maxX, groundY + hoverHeight, 0f));
    }
}

// Quả cầu lửa của Rồng — tự quản lý pool riêng
public class Fireball : MonoBehaviour
{
    const int Cols = 3, CellW = 64, CellH = 68;
    const float PixelsPerUnit = 55f;
    static Sprite[] fxSprites;
    static readonly System.Collections.Generic.Queue<Fireball> pool = new System.Collections.Generic.Queue<Fireball>();

    public float hitRadius = 0.3f;     // chỉ phần lõi lửa mới gây chết
    public float lifeTime = 4f;

    Vector3 velocity;
    float life;
    bool exploding;
    float animTimer; int animFrame;
    SpriteRenderer sr;

    // Sprite quả cầu lửa gốc bay theo hướng chéo xuống-phải (-45 độ)
    const float SpriteBaseAngle = -45f;

    float radiusMul = 1f;

    public static void Spawn(Vector3 position, Vector3 velocity)
    {
        Spawn(position, velocity, Color.white, 1f);
    }

    // tint: màu (vd Boss Hắc Long dùng lửa tím) | scale: phóng to quả cầu lửa
    public static void Spawn(Vector3 position, Vector3 velocity, Color tint, float scale)
    {
        LoadSprites();
        if (fxSprites == null) return;

        Fireball fb = null;
        while (pool.Count > 0 && fb == null) fb = pool.Dequeue();
        if (fb == null)
        {
            GameObject go = new GameObject("FIREBALL");
            SpriteRenderer s = go.AddComponent<SpriteRenderer>();
            s.sortingOrder = 6;
            fb = go.AddComponent<Fireball>();
            fb.sr = s;
        }
        fb.transform.position = position;
        fb.velocity = velocity;
        fb.life = fb.lifeTime;
        fb.exploding = false;
        fb.animFrame = 0; fb.animTimer = 0f;
        float ang = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        fb.transform.rotation = Quaternion.Euler(0f, 0f, ang - SpriteBaseAngle);
        fb.sr.sprite = fxSprites[0];
        fb.sr.color = tint;
        fb.transform.localScale = Vector3.one * scale;
        fb.radiusMul = scale;
        fb.gameObject.SetActive(true);
    }


    // Unity đang tắt "Reload Domain" khi bấm Play -> biến static không tự reset.
    // Sprite tạo lúc chạy bị Unity xoá khi dừng game, nên phải xoá cache mỗi lần bấm Play.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { fxSprites = null; pool.Clear(); }

    static void LoadSprites()
    {
        if (fxSprites != null && fxSprites[0] != null) return; // còn dùng được
        Texture2D tex = Resources.Load<Texture2D>("dragon_fx");
        if (tex == null) { Debug.LogError("Fireball: không tìm thấy Resources/dragon_fx.png"); return; }
        fxSprites = new Sprite[Cols * 2];
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < Cols; c++)
            {
                Rect rect = new Rect(c * CellW, tex.height - (r + 1) * CellH, CellW, CellH);
                // Hàng nổ: tâm ở đáy (nổ trên mặt đất)
                Vector2 pivot = r == 0 ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 0.05f);
                fxSprites[r * Cols + c] = Sprite.Create(tex, rect, pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
    }

    void Update()
    {
        if (exploding)
        {
            animTimer += Time.deltaTime;
            if (animTimer > 0.12f) { animTimer = 0f; animFrame++; }
            if (animFrame >= 2) { Recycle(); return; }
            sr.sprite = fxSprites[Cols + animFrame];
            return;
        }

        life -= Time.deltaTime;
        if (life <= 0f) { Recycle(); return; }

        transform.position += velocity * Time.deltaTime;

        animTimer += Time.deltaTime;
        if (animTimer > 0.08f) { animTimer = 0f; animFrame = (animFrame + 1) % 3; }
        sr.sprite = fxSprites[animFrame];

        // Kiểm tra va chạm
        Collider[] hits = Physics.OverlapSphere(transform.position, hitRadius * radiusMul, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider c in hits)
        {
            PlayerController pc = c.GetComponentInParent<PlayerController>();
            if (pc != null) { pc.Hurt(1, transform.position - velocity); Explode(); return; }
            if (c.CompareTag("Enemy")) continue;   // bay xuyên qua quái
            Explode();                              // chạm đất / tường
            return;
        }
    }

    void Explode()
    {
        exploding = true;
        animFrame = 0; animTimer = 0f;
        transform.rotation = Quaternion.identity;
        sr.sprite = fxSprites[Cols];
    }

    void Recycle()
    {
        gameObject.SetActive(false);
        pool.Enqueue(this);
    }
}
