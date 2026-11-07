using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Skill của Hero:
//   J (hoặc chuột trái)  : CHÉM KIẾM      -> chỉ gây sát thương QUÁI ĐI BỘ (Ogre, Ogre nhỏ)
//   K (hoặc chuột phải)  : TIA LỬA XANH   -> chỉ gây sát thương QUÁI BAY (Rồng). Tự ngắm Rồng gần nhất phía trước.
//
// Hiệu ứng: Resources/hero_skills.png — 5 cột x 3 hàng, ô 192 x 192
//   Hàng 0: vệt kiếm (5)  |  Hàng 1: tia lửa xanh (4)  |  Hàng 2: nổ xanh (4)
// Tự gắn vào Player khi vào game, không cần kéo thả.
public class HeroSkills : MonoBehaviour
{
    [Header("Phím")]
    public KeyCode swordKey = KeyCode.J;
    public KeyCode boltKey = KeyCode.K;

    [Header("Chém Kiếm")]
    public float swordCooldown = 0.35f;
    public Vector2 swordOffset = new Vector2(0.85f, 0.15f); // tâm vùng chém so với tâm Player
    public Vector2 swordSize = new Vector2(1.7f, 1.5f);

    [Header("Sát thương lên BOSS (Boss nhận cả 2 loại ở mọi dạng)")]
    public int swordDamageToBoss = 2;   // chém kiếm mạnh hơn
    public int boltDamageToBoss = 1;    // tung chưởng yếu hơn

    [Header("Tia Lửa Xanh")]
    public float boltCooldown = 0.45f;
    public float boltSpeed = 14f;
    public float autoAimRange = 14f;

    [Header("Hiển thị")]
    public bool showControlsHint = true;

    // Frame trong player_sheet dùng làm tư thế ra chiêu
    const int SwordPose = 11;    // hàng Run: vung tay về phía trước
    const int CastPose = 10;     // hàng Run: đẩy tay ra trước
    const int CastUpPose = 17;   // hàng Jump: giơ tay lên cao (bắn lên trời)

    // --- Sprite hiệu ứng ---
    public const int FxCols = 5, FxCell = 192;
    public const float FxPPU = 60f;
    static Sprite[] fx;

    float swordTimer, boltTimer;
    PlayerSpriteAnimator anim;
    Collider col;
    SpriteRenderer slashSr;

    // Quái lắng nghe để né chiêu
    public static event System.Action<Vector3, float> OnSwordSwing;     // vị trí hero, hướng (+1 phải / -1 trái)
    public static event System.Action<Vector3, Vector3> OnBoltFired;    // điểm bắn, vận tốc tia lửa

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { fx = null; OnSwordSwing = null; OnBoltFired = null; }

    // Gọi từ GameBootstrap mỗi khi scene được nạp
    public static void AutoAttach()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null && player.GetComponent<HeroSkills>() == null)
            player.AddComponent<HeroSkills>();
    }

    public static Sprite[] Fx
    {
        get
        {
            if (fx != null && fx[0] != null) return fx;
            Texture2D tex = Resources.Load<Texture2D>("hero_skills");
            if (tex == null) { Debug.LogError("HeroSkills: không tìm thấy Resources/hero_skills.png"); return null; }
            fx = new Sprite[FxCols * 3];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < FxCols; c++)
                {
                    Rect rect = new Rect(c * FxCell, tex.height - (r + 1) * FxCell, FxCell, FxCell);
                    fx[r * FxCols + c] = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), FxPPU, 0, SpriteMeshType.FullRect);
                }
            return fx;
        }
    }

    void Start()
    {
        anim = GetComponent<PlayerSpriteAnimator>();
        col = GetComponent<Collider>();

        GameObject slash = new GameObject("SwordSlash");
        slashSr = slash.AddComponent<SpriteRenderer>();
        slashSr.sortingOrder = 10;
        slash.SetActive(false);
    }

    bool IsDead { get { return col != null && !col.enabled; } }
    bool FacingRight { get { return anim == null || anim.FacingRight; } }

    void Update()
    {
        swordTimer -= Time.deltaTime;
        boltTimer -= Time.deltaTime;
        if (IsDead || Fx == null) return;

        // Khi đang dùng nút cảm ứng thì bỏ qua chuột (tránh bấm nút UI lại tính là chém)
        bool mouse = !MobileControls.Active;
        bool swordInput = Input.GetKeyDown(swordKey) || (mouse && Input.GetMouseButtonDown(0)) || MobileControls.SwordHeld;
        bool boltInput  = Input.GetKeyDown(boltKey)  || (mouse && Input.GetMouseButtonDown(1)) || MobileControls.BoltHeld;

        if (swordInput && swordTimer <= 0f)
        {
            swordTimer = swordCooldown;
            StartCoroutine(SwordSlash());
        }

        if (boltInput && boltTimer <= 0f)
        {
            boltTimer = boltCooldown;
            CastBolt();
        }
    }

    // ------------------------------------------------------------------
    // CHÉM KIẾM
    IEnumerator SwordSlash()
    {
        bool right = FacingRight;
        float dir = right ? 1f : -1f;
        if (anim != null) anim.ShowPose(SwordPose, 0.25f);
        if (OnSwordSwing != null) OnSwordSwing(transform.position, dir);

        GameObject slash = slashSr.gameObject;
        slash.SetActive(true);
        slashSr.flipX = !right; // vệt chém gốc vẽ hướng sang phải

        HashSet<IStompable> hitOnce = new HashSet<IStompable>();
        for (int i = 0; i < 5; i++)
        {
            slash.transform.position = transform.position + new Vector3(0.25f * dir, 0.1f, -0.1f);
            slashSr.sprite = fx[i];
            if (i >= 1 && i <= 3) SwordHit(dir, hitOnce);   // lưỡi kiếm đang quét qua phía trước
            yield return new WaitForSeconds(0.05f);
        }
        slash.SetActive(false);
    }

    void SwordHit(float dir, HashSet<IStompable> hitOnce)
    {
        Vector3 center = transform.position + new Vector3(swordOffset.x * dir, swordOffset.y, 0f);
        Collider[] hits = Physics.OverlapBox(center, new Vector3(swordSize.x, swordSize.y, 2f) * 0.5f,
                                             Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider c in hits)
        {
            IStompable enemy = c.GetComponentInParent<IStompable>();
            if (enemy == null || hitOnce.Contains(enemy)) continue;

            // Boss: kiếm chém được ở mọi dạng, sát thương lớn hơn chưởng
            BossController boss = enemy as BossController;
            if (boss != null)
            {
                hitOnce.Add(enemy);
                boss.TakeDamage(swordDamageToBoss);
                continue;
            }

            if (enemy.IsFlying) continue;   // quái thường: kiếm không chém được quái bay
            hitOnce.Add(enemy);
            enemy.TakeHit();
        }
    }

    // ------------------------------------------------------------------
    // TIA LỬA XANH
    void CastBolt()
    {
        float dir = FacingRight ? 1f : -1f;
        Vector3 origin = transform.position + new Vector3(0.5f * dir, 0.3f, -0.1f);

        // Tự ngắm Rồng gần nhất ở phía trước
        Vector3 aim = new Vector3(dir, 0.15f, 0f).normalized;
        float best = autoAimRange;
        foreach (Collider c in Physics.OverlapSphere(transform.position, autoAimRange, ~0, QueryTriggerInteraction.Ignore))
        {
            IStompable enemy = c.GetComponentInParent<IStompable>();
            if (enemy == null || (!enemy.IsFlying && !(enemy is BossController))) continue;
            Vector3 to = c.bounds.center - origin; to.z = 0f;
            if (to.x * dir < -0.5f) continue;            // chỉ ngắm phía trước mặt
            float d = to.magnitude;
            if (d < best) { best = d; aim = to.normalized; }
        }

        bool up = aim.y > 0.6f;
        if (anim != null) anim.ShowPose(up ? CastUpPose : CastPose, 0.2f);
        HeroBolt.Spawn(origin, aim * boltSpeed);
        if (OnBoltFired != null) OnBoltFired(origin, aim * boltSpeed);
    }

    void OnGUI()
    {
        if (!showControlsHint || MobileControls.Active) return;
        // Góc dưới bên trái (góc trên đã có avatar + thanh máu)
        GUI.Label(new Rect(10, Screen.height - 50, 420, 22), "J / Chuột trái: Chém kiếm (quái đi bộ)");
        GUI.Label(new Rect(10, Screen.height - 30, 420, 22), "K / Chuột phải: Tia lửa xanh (quái bay)");
    }

    void OnDrawGizmosSelected()
    {
        float dir = (anim == null || anim.FacingRight) ? 1f : -1f;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position + new Vector3(swordOffset.x * dir, swordOffset.y, 0f),
                            new Vector3(swordSize.x, swordSize.y, 1f));
    }
}

// Tia lửa xanh của Hero — tự quản lý pool
public class HeroBolt : MonoBehaviour
{
    static readonly Queue<HeroBolt> pool = new Queue<HeroBolt>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { pool.Clear(); }

    public float hitRadius = 0.4f;
    public float lifeTime = 1.4f;
    int boltDamage = 1;

    SpriteRenderer sr;
    Vector3 velocity;
    float life, animTimer;
    int animFrame;
    bool exploding;

    public static void Spawn(Vector3 pos, Vector3 vel)
    {
        Sprite[] fx = HeroSkills.Fx;
        if (fx == null) return;

        HeroBolt b = null;
        while (pool.Count > 0 && b == null) b = pool.Dequeue();
        if (b == null)
        {
            GameObject go = new GameObject("HERO_BOLT");
            b = go.AddComponent<HeroBolt>();
            b.sr = go.AddComponent<SpriteRenderer>();
            b.sr.sortingOrder = 11;
        }
        b.transform.position = pos;
        b.velocity = vel;
        HeroSkills hs = Object.FindAnyObjectByType<HeroSkills>();
        b.boltDamage = hs != null ? hs.boltDamageToBoss : 1;
        b.life = b.lifeTime;
        b.exploding = false;
        b.animFrame = 0; b.animTimer = 0f;
        // Sprite tia lửa gốc bay sang phải -> xoay theo hướng bay
        b.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg);
        b.sr.sprite = fx[HeroSkills.FxCols];
        b.gameObject.SetActive(true);
    }

    void Update()
    {
        Sprite[] fx = HeroSkills.Fx;
        if (fx == null) { Recycle(); return; }

        if (exploding)
        {
            animTimer += Time.deltaTime;
            if (animTimer > 0.06f) { animTimer = 0f; animFrame++; }
            if (animFrame >= 4) { Recycle(); return; }
            sr.sprite = fx[HeroSkills.FxCols * 2 + animFrame];
            return;
        }

        life -= Time.deltaTime;
        if (life <= 0f) { Recycle(); return; }
        transform.position += velocity * Time.deltaTime;

        animTimer += Time.deltaTime;
        if (animTimer > 0.06f) { animTimer = 0f; animFrame = (animFrame + 1) % 4; }
        sr.sprite = fx[HeroSkills.FxCols + animFrame];

        // Đầu tia lửa nằm phía trước tâm sprite ~0.55 unit
        Vector3 head = transform.position + velocity.normalized * 0.55f;
        foreach (Collider c in Physics.OverlapSphere(head, hitRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.GetComponentInParent<PlayerController>() != null) continue;   // không tự bắn trúng mình
            IStompable enemy = c.GetComponentInParent<IStompable>();
            BossController boss = enemy as BossController;
            if (boss != null)
            {
                boss.TakeDamage(boltDamage);      // Boss nhận chưởng ở mọi dạng (sát thương nhỏ hơn kiếm)
                Explode(head);
                return;
            }
            if (enemy != null)
            {
                if (!enemy.IsFlying) continue;    // bay xuyên qua quái đi bộ
                enemy.TakeHit();
                Explode(head);
                return;
            }
            if (c.CompareTag("Enemy")) continue;
            Explode(head);                        // chạm đất / tường
            return;
        }
    }

    void Explode(Vector3 at)
    {
        exploding = true;
        animFrame = 0; animTimer = 0f;
        transform.position = at;
        transform.rotation = Quaternion.identity;
    }

    void Recycle()
    {
        gameObject.SetActive(false);
        pool.Enqueue(this);
    }
}
