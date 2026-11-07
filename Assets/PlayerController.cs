using System.Collections;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Chỉ số di chuyển & Nhảy")]
    [Tooltip("Tốc độ chạy (đã giảm từ 8 xuống 5.5)")]
    public float runSpeed = 5.5f;
    public float jumpForce = 10f;             // Lực nhảy bình thường
    public float bounceForce = 8f;            // Lực bật nẩy khi giẫm lên đầu quái
    public float groundCheckDistance = 0.7f;
    public LayerMask groundLayer;

    [Header("Mạng & Hồi sinh")]
    public float respawnDelay = 1.6f;         // Chết xong bao lâu thì hồi sinh
    public float respawnInvulTime = 2f;       // Miễn sát thương sau khi hồi sinh (nhấp nháy)
    public float fallDeathY = -12f;           // Rơi xuống dưới độ cao này = mất 1 mạng

    [Header("Chạm thân quái")]
    public float knockbackForceX = 7f;        // Bị đẩy lùi khi chạm thân quái (không mất mạng)
    public float knockbackForceY = 4f;
    public float knockbackTime = 0.25f;       // Thời gian mất điều khiển sau khi bị đẩy

    private Rigidbody rb;
    private Collider col;
    private float moveInput;
    private bool isGrounded;
    private bool isDead = false;             // Đang trong lúc chết / chờ hồi sinh
    private bool isGameOver = false;
    private float knockbackTimer = 0f;
    private float invulTimer = 0f;

    public bool IsDead { get { return isDead; } }
    public bool IsGameOver { get { return isGameOver; } }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        GameSession.Notify();
    }

    void Update()
    {
        if (isGameOver || GameSession.IsVictory)
        {
            // Hết mạng / phá đảo: bấm R / Enter / chạm màn hình để chơi lại
            if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0))
                GameSession.Restart();
            return;
        }
        if (isDead) return; // Đang chết thì không nhận phím

        invulTimer -= Time.deltaTime;

        // Rơi khỏi map
        if (transform.position.y < fallDeathY)
        {
            LoseLife(false);
            return;
        }

        moveInput = Input.GetAxisRaw("Horizontal");
        // Joystick cảm ứng (điện thoại)
        if (MobileControls.Active && Mathf.Abs(MobileControls.Horizontal) > Mathf.Abs(moveInput))
            moveInput = MobileControls.Horizontal;

        // Không cho đi lùi ra khỏi tường đấu trường boss (LevelGenerator lo), ở đây chỉ kiểm tra mặt đất
        isGrounded = Physics.Raycast(transform.position, Vector3.down, groundCheckDistance, groundLayer);

        // Bấm Spacebar (hoặc nút Nhảy trên điện thoại) để nhảy
        bool jumpPressed = Input.GetButtonDown("Jump") || MobileControls.JumpBuffered();
        if (jumpPressed && isGrounded)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, 0f);
            MobileControls.ClearJump();
        }
    }

    void FixedUpdate()
    {
        if (isDead) return;

        // Đang bị đẩy lùi thì không nhận điều khiển trái/phải
        if (knockbackTimer > 0f)
        {
            knockbackTimer -= Time.fixedDeltaTime;
            return;
        }

        rb.linearVelocity = new Vector3(moveInput * runSpeed, rb.linearVelocity.y, 0f);
    }

    // Xử lý va chạm giữa Player và Quái vật
    private void OnCollisionEnter(Collision collision)
    {
        if (isDead) return;

        IStompable stompable = collision.gameObject.GetComponentInParent<IStompable>();
        if (collision.gameObject.CompareTag("Enemy") || stompable != null)
        {
            foreach (ContactPoint contact in collision.contacts)
            {
                // Hướng va chạm từ trên xuống (Chân Player đập vào đầu Quái)
                if (contact.normal.y > 0.5f)
                {
                    rb.linearVelocity = new Vector3(rb.linearVelocity.x, bounceForce, 0f);
                    if (stompable != null) stompable.TakeHit();
                    else collision.gameObject.SetActive(false);
                    return;
                }
            }

            // Chạm thân Ogre / Rồng / Boss -> chỉ bị đẩy lùi. Dính đòn đánh mới mất mạng (quái gọi Hurt()).
            if (stompable != null)
            {
                float dir = Mathf.Sign(transform.position.x - collision.transform.position.x);
                if (dir == 0f) dir = -1f;
                rb.linearVelocity = new Vector3(dir * knockbackForceX, knockbackForceY, 0f);
                knockbackTimer = knockbackTime;
                return;
            }

            // Quái khác đâm vào Player -> mất mạng
            Hurt(1, collision.transform.position);
        }
    }

    // Quái / chiêu boss gọi khi đánh trúng Player -> mất 1 mạng, hồi sinh ở segment trước đó
    public void Hurt(int damage, Vector3 from)
    {
        if (isDead || invulTimer > 0f) return;
        LoseLife(true);
    }

    // Giữ tương thích với code cũ
    public void Die() { Hurt(1, transform.position); }

    void LoseLife(bool bounce)
    {
        if (isDead) return;
        isDead = true;
        GameSession.LoseLife();

        // Hiệu ứng chết kiểu Mario: tắt va chạm, nảy lên rồi rơi
        rb.linearVelocity = bounce ? new Vector3(0f, 7f, 0f) : Vector3.zero;
        if (col != null) col.enabled = false;

        StartCoroutine(AfterDeath());
    }

    IEnumerator AfterDeath()
    {
        yield return new WaitForSeconds(respawnDelay);

        if (GameSession.Lives <= 0)
        {
            isGameOver = true;
            rb.isKinematic = true;
            GameSession.Notify();   // HUD hiện GAME OVER
            yield break;
        }

        // Hồi sinh ở đầu segment trước đó (LevelGenerator tính)
        Vector3 spawn = LevelGenerator.Instance != null
            ? LevelGenerator.Instance.GetCheckpoint(transform.position.x)
            : new Vector3(0f, 2f, 0f);

        rb.linearVelocity = Vector3.zero;
        rb.position = spawn;
        transform.position = spawn;
        if (col != null) col.enabled = true;
        isDead = false;
        knockbackTimer = 0f;
        invulTimer = respawnInvulTime;
        GameSession.Notify();
        StartCoroutine(Blink());
    }

    IEnumerator Blink()
    {
        SpriteRenderer[] srs = GetComponentsInChildren<SpriteRenderer>();
        while (invulTimer > 0f && !isDead)
        {
            foreach (SpriteRenderer s in srs) if (s != null) s.enabled = !s.enabled;
            yield return new WaitForSeconds(0.08f);
        }
        foreach (SpriteRenderer s in srs) if (s != null) s.enabled = true;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * groundCheckDistance);
    }
}
