using UnityEngine;

// Đồng xu vàng: ảnh xoay 8 frame (Resources/coin_spin.png) thay cho khối trụ xám, nhấp nhô nhẹ.
// Ăn xu -> cộng vào bộ đếm xu trên HUD (GameSession.Coins).
public class Coin : MonoBehaviour
{
    public float spinFps = 12f;      // tốc độ xoay
    public float size = 0.7f;        // đường kính đồng xu (unit)
    public float bobHeight = 0.08f;  // nhấp nhô lên xuống

    const int Frames = 8, Cell = 128;
    static Sprite[] frames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { frames = null; }

    SpriteRenderer sr;
    Transform visual;
    float phase;

    static Sprite[] Frames8
    {
        get
        {
            if (frames != null && frames[0] != null) return frames;
            Texture2D t = Resources.Load<Texture2D>("coin_spin");
            if (t == null) return null;
            frames = new Sprite[Frames];
            for (int i = 0; i < Frames; i++)
                frames[i] = Sprite.Create(t, new Rect(i * Cell, 0, Cell, Cell), new Vector2(0.5f, 0.5f), Cell, 0, SpriteMeshType.FullRect);
            return frames;
        }
    }

    void Awake()
    {
        if (Frames8 == null) return;
        // Ẩn khối trụ xám (vẫn giữ Collider trigger để ăn xu)
        Renderer mr = GetComponent<Renderer>();
        if (mr != null) mr.forceRenderingOff = true;

        visual = new GameObject("CoinSprite").transform;
        visual.SetParent(transform, false);
        sr = visual.gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        sr.sortingOrder = 3;
        phase = Random.value * 10f;
    }

    void OnEnable() { phase = Random.value * 10f; }

    void Update()
    {
        if (visual == null) return;
        phase += Time.deltaTime;
        sr.sprite = frames[(int)(phase * spinFps) % Frames];

        // Giữ sprite luôn đứng thẳng, đúng kích thước dù object gốc bị xoay / scale
        visual.rotation = Quaternion.identity;
        Vector3 ls = transform.lossyScale;
        visual.localScale = new Vector3(size / Mathf.Max(0.0001f, ls.x), size / Mathf.Max(0.0001f, ls.z), 1f);
        visual.position = transform.position + Vector3.up * Mathf.Sin(phase * 3f) * bobHeight;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Khi đối tượng chạm vào có Tag là "Player"
        if (other.CompareTag("Player"))
        {
            GameSession.AddCoin();
            // Ẩn thay vì Destroy để LevelGenerator tái sử dụng (Object Pooling)
            gameObject.SetActive(false);
        }
    }
}
