using UnityEngine;

// Bục 1 chiều: nhảy XUYÊN từ dưới lên, đáp đứng trên mặt bục.
// Đứng trên bục + bấm XUỐNG (S / ↓ / kéo joystick xuống) để rơi xuyên xuống tầng dưới.
public class OneWayPlatform : MonoBehaviour
{
    static Collider playerCol;
    static float dropUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { playerCol = null; dropUntil = 0f; }

    Collider col;

    void Awake() { col = GetComponent<Collider>(); }

    void FixedUpdate()
    {
        if (col == null) return;
        if (playerCol == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return;
            playerCol = p.GetComponent<Collider>();
            if (playerCol == null) return;
        }
        if (!playerCol.enabled || !playerCol.gameObject.activeInHierarchy) return;

        bool wantDrop = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) || MobileControls.Vertical < -0.6f;
        if (wantDrop) dropUntil = Time.time + 0.3f;

        // Chân Player thấp hơn mặt bục -> cho đi xuyên (đang nhảy từ dưới lên / đang rơi xuống tầng dưới)
        bool below = playerCol.bounds.min.y < col.bounds.max.y - 0.08f;
        bool ignore = below || Time.time < dropUntil;
        if (Physics.GetIgnoreCollision(playerCol, col) != ignore)
            Physics.IgnoreCollision(playerCol, col, ignore);
    }
}
