using UnityEngine;

// Hoạt ảnh 2D cho Player từ sprite sheet 5 cột x 4 hàng:
//   Hàng 1: Idle  | Hàng 2: Walk | Hàng 3: Run | Hàng 4: Jump
// - Tự cắt sprite lúc chạy game (không cần Slice trong Sprite Editor)
// - Tự tạo object con "Visual" có SpriteRenderer, ẩn khối hộp 3D
// - Tự gắn vào object có tag "Player" khi vào game (không cần kéo thả gì)
public class PlayerSpriteAnimator : MonoBehaviour
{
    [Header("Sprite sheet")]
    [Tooltip("Để trống = tự load Resources/player_sheet")]
    public Texture2D spriteSheet;
    public int columns = 5;
    public int rows = 4;

    [Header("Kích thước")]
    [Tooltip("Chiều cao nhân vật so với chiều cao Collider (1 = bằng Collider)")]
    public float heightScale = 1.25f;
    public float pixelsPerUnitOverride = 0f; // > 0 thì dùng số này, bỏ qua heightScale

    [Header("Tốc độ hoạt ảnh (frame/giây)")]
    public float idleFps = 6f;
    public float walkFps = 10f;
    public float runFps = 14f;

    [Header("Ngưỡng trạng thái")]
    public float moveThreshold = 0.1f;     // Nhanh hơn mức này = đang di chuyển
    public float runSpeedThreshold = 4f;   // Nhanh hơn mức này = chạy, chậm hơn = đi bộ
    public float landingTime = 0.12f;      // Thời gian hiện frame tiếp đất

    private enum State { Idle, Walk, Run, Jump, Land }

    private Sprite[] sprites;
    private SpriteRenderer sr;
    private Rigidbody rb;
    private Collider col;
    private PlayerController pc;

    private State state = State.Idle;
    private float frameTimer;
    private int frameIndex;
    private float landTimer;
    private bool wasGrounded = true;
    private bool facingRight = true;

    // Tư thế ra chiêu (HeroSkills gọi)
    private int poseIndex = -1;
    private float poseTimer;

    public bool FacingRight { get { return facingRight; } }

    // Giữ 1 frame bất kỳ trong sheet trong 'duration' giây (vd: tư thế chém, tư thế bắn)
    public void ShowPose(int spriteIndex, float duration)
    {
        if (sprites == null || spriteIndex < 0 || spriteIndex >= sprites.Length) return;
        poseIndex = spriteIndex;
        poseTimer = duration;
    }

    // Tự gắn script vào Player khi scene load
    // Gọi từ GameBootstrap mỗi khi scene được nạp
    public static void AutoAttach()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null && player.GetComponent<PlayerSpriteAnimator>() == null)
            player.AddComponent<PlayerSpriteAnimator>();
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        pc = GetComponent<PlayerController>();

        if (spriteSheet == null) spriteSheet = Resources.Load<Texture2D>("player_sheet");
        if (spriteSheet == null)
        {
            Debug.LogError("PlayerSpriteAnimator: không tìm thấy Resources/player_sheet.png");
            enabled = false;
            return;
        }

        BuildSprites();
        CreateVisual();
    }

    void BuildSprites()
    {
        int cellW = spriteSheet.width / columns;
        int cellH = spriteSheet.height / rows;

        float colliderHeight = col != null ? col.bounds.size.y : 1f;
        float ppu = pixelsPerUnitOverride > 0f
            ? pixelsPerUnitOverride
            : cellH / Mathf.Max(0.01f, colliderHeight * heightScale);

        sprites = new Sprite[columns * rows];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                // Texture tính Y từ dưới lên, sheet đánh số hàng từ trên xuống
                Rect rect = new Rect(c * cellW, spriteSheet.height - (r + 1) * cellH, cellW, cellH);
                Sprite s = Sprite.Create(spriteSheet, rect, new Vector2(0.5f, 0f), ppu, 0, SpriteMeshType.FullRect);
                s.name = "player_" + (r * columns + c);
                sprites[r * columns + c] = s;
            }
        }
    }

    void CreateVisual()
    {
        Transform visual = transform.Find("Visual");
        if (visual == null)
        {
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
        }

        // Bù scale của Player để kích thước sprite chỉ phụ thuộc PPU
        Vector3 ls = transform.lossyScale;
        visual.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);

        // Đặt chân nhân vật trùng đáy Collider
        float bottomOffset = col != null ? (col.bounds.min.y - transform.position.y) / ls.y : -0.5f;
        visual.localPosition = new Vector3(0f, bottomOffset, 0f);
        visual.localRotation = Quaternion.identity;

        sr = visual.GetComponent<SpriteRenderer>();
        if (sr == null) sr = visual.gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = sprites[0];

        // Ẩn khối hộp 3D (vẫn giữ Collider + Rigidbody)
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;
    }

    void Update()
    {
        if (sprites == null) return;

        Vector3 v = rb != null ? rb.linearVelocity : Vector3.zero;
        bool grounded = IsGrounded();
        bool dead = col != null && !col.enabled; // PlayerController tắt Collider khi chết

        // Quay mặt theo hướng di chuyển
        if (v.x > moveThreshold) facingRight = true;
        else if (v.x < -moveThreshold) facingRight = false;
        sr.flipX = !facingRight;

        // Đang ra chiêu -> giữ tư thế, bỏ qua hoạt ảnh thường
        if (poseTimer > 0f && !dead)
        {
            poseTimer -= Time.deltaTime;
            sr.sprite = sprites[poseIndex];
            wasGrounded = grounded;
            return;
        }

        // Chọn trạng thái
        State next;
        if (dead || !grounded)
        {
            next = State.Jump;
        }
        else if (!wasGrounded)
        {
            next = State.Land;
            landTimer = landingTime;
        }
        else if (state == State.Land && landTimer > 0f)
        {
            next = State.Land;
        }
        else if (Mathf.Abs(v.x) > runSpeedThreshold) next = State.Run;
        else if (Mathf.Abs(v.x) > moveThreshold) next = State.Walk;
        else next = State.Idle;

        wasGrounded = grounded && !dead;

        if (next != state)
        {
            state = next;
            frameIndex = 0;
            frameTimer = 0f;
        }

        switch (state)
        {
            case State.Idle: Loop(0, idleFps); break;
            case State.Walk: Loop(1, walkFps); break;
            case State.Run:  Loop(2, runFps); break;
            case State.Jump: SetFrame(3, JumpFrame(v.y)); break;
            case State.Land:
                landTimer -= Time.deltaTime;
                SetFrame(3, 4);
                break;
        }
    }

    // Hàng Jump: 0 lấy đà, 1 bật lên, 2 vươn lên cao, 3 rơi xuống, 4 tiếp đất
    int JumpFrame(float vy)
    {
        if (vy > 6f) return 1;
        if (vy > 1f) return 2;
        return 3;
    }

    void Loop(int row, float fps)
    {
        frameTimer += Time.deltaTime;
        float frameTime = 1f / Mathf.Max(0.01f, fps);
        while (frameTimer >= frameTime)
        {
            frameTimer -= frameTime;
            frameIndex = (frameIndex + 1) % columns;
        }
        SetFrame(row, frameIndex);
    }

    void SetFrame(int row, int column)
    {
        sr.sprite = sprites[row * columns + column];
    }

    bool IsGrounded()
    {
        float dist = pc != null ? pc.groundCheckDistance : 0.7f;
        LayerMask mask = pc != null ? pc.groundLayer : (LayerMask)~0;
        return Physics.Raycast(transform.position, Vector3.down, dist, mask, QueryTriggerInteraction.Ignore);
    }
}
