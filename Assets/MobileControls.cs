using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Điều khiển cảm ứng cho điện thoại:
//   Trái : Joystick ảo (chạy trái/phải)
//   Phải : Nhảy | Kiếm | Tia lửa xanh   (giữ nút Kiếm / Tia lửa = đánh liên tục)
// - Tự hiện trên điện thoại (và trong Device Simulator), tự ẩn trên máy tính.
// - Trong Editor bấm F2 để bật/tắt thử bằng chuột.
// - Ảnh: Resources/mobile_ui.png (6 ô 256px: nền joystick, núm, nền nút, icon nhảy, icon kiếm, icon tia lửa)
public class MobileControls : MonoBehaviour
{
    // ----- API cho PlayerController / HeroSkills -----
    public static bool Active { get; private set; }
    public static float Horizontal { get; private set; }
    public static float Vertical { get; private set; }     // kéo joystick xuống để rơi xuyên bục
    public static bool SwordHeld { get; private set; }
    public static bool BoltHeld { get; private set; }
    static float jumpPressTime = -10f;

    // Nhảy có "đệm" 0.15s: bấm hơi sớm trước khi chạm đất vẫn nhảy được
    public static bool JumpBuffered(float window = 0.15f) { return Time.time - jumpPressTime <= window; }
    public static void ClearJump() { jumpPressTime = -10f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Active = false; Horizontal = 0f; Vertical = 0f; SwordHeld = BoltHeld = false; jumpPressTime = -10f;
    }

    // Gọi từ GameBootstrap mỗi khi scene được nạp
    public static void AutoCreate()
    {
        if (FindAnyObjectByType<MobileControls>() != null) return;
        new GameObject("MobileControls").AddComponent<MobileControls>();
    }

    // ---------------------------------------------------------------------
    const int Cell = 256;
    static Sprite[] ui;
    GameObject root;

    void Awake()
    {
        EnsureEventSystem();
        LoadSprites();

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f; // giữ kích thước nút theo chiều cao màn hình
        gameObject.AddComponent<GraphicRaycaster>();

        RectTransform safe = SafeArea.Create(transform);
        root = safe.gameObject;

        // Joystick (góc dưới trái)
        RectTransform joyBase = MakeImage("JoystickBase", safe, ui[0], new Vector2(0, 0), new Vector2(230, 200), 300);
        RectTransform knob = MakeImage("Knob", joyBase, ui[1], new Vector2(0.5f, 0.5f), Vector2.zero, 150);
        VirtualJoystick joy = joyBase.gameObject.AddComponent<VirtualJoystick>();
        joy.knob = knob;
        joy.radius = 110f;

        // Nút (góc dưới phải)
        MakeButton("JumpButton", safe, ui[3], new Vector2(-170, 170), 210, TouchButton.Kind.Jump);
        MakeButton("SwordButton", safe, ui[4], new Vector2(-400, 130), 180, TouchButton.Kind.Sword);
        MakeButton("BoltButton", safe, ui[5], new Vector2(-260, 380), 170, TouchButton.Kind.Bolt);

        bool mobile = Application.isMobilePlatform;
        SetActive(mobile);

        if (mobile)
        {
            Application.targetFrameRate = 60;
            // Chỉ cho xoay ngang
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
        }
    }

    void Update()
    {
#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.F2)) SetActive(!Active); // test bằng chuột trong Editor
#endif
    }

    void SetActive(bool on)
    {
        Active = on;
        root.SetActive(on);
        if (!on) { Horizontal = 0f; Vertical = 0f; SwordHeld = BoltHeld = false; }
    }

    // ---------------------------------------------------------------------
    static void LoadSprites()
    {
        if (ui != null && ui[0] != null) return;
        Texture2D tex = Resources.Load<Texture2D>("mobile_ui");
        ui = new Sprite[6];
        for (int i = 0; i < 6; i++)
            ui[i] = tex != null
                ? Sprite.Create(tex, new Rect(i * Cell, 0, Cell, Cell), new Vector2(0.5f, 0.5f), 100f)
                : null;
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>(); // project bật cả Input cũ + mới nên dùng được
    }

    RectTransform MakeImage(string n, Transform parent, Sprite s, Vector2 anchor, Vector2 pos, float size)
    {
        GameObject go = new GameObject(n, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.sprite = s;
        img.color = new Color(1f, 1f, 1f, 0.75f);
        RectTransform r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos;
        r.sizeDelta = new Vector2(size, size);
        return r;
    }

    void MakeButton(string n, Transform parent, Sprite icon, Vector2 pos, float size, TouchButton.Kind kind)
    {
        RectTransform bg = MakeImage(n, parent, ui[2], new Vector2(1, 0), pos, size);
        RectTransform ic = MakeImage("Icon", bg, icon, new Vector2(0.5f, 0.5f), Vector2.zero, size * 0.72f);
        ic.GetComponent<Image>().raycastTarget = false;
        TouchButton b = bg.gameObject.AddComponent<TouchButton>();
        b.kind = kind;
        b.images = new[] { bg.GetComponent<Image>(), ic.GetComponent<Image>() };
    }

    // ---------------------------------------------------------------------
    // Gọi từ các nút / joystick
    internal static void SetHorizontal(float v) { Horizontal = v; }
    internal static void SetVertical(float v) { Vertical = v; }
    internal static void Press(TouchButton.Kind k, bool down)
    {
        switch (k)
        {
            case TouchButton.Kind.Jump: if (down) jumpPressTime = Time.time; break;
            case TouchButton.Kind.Sword: SwordHeld = down; break;
            case TouchButton.Kind.Bolt: BoltHeld = down; break;
        }
    }
}

// Nút cảm ứng: sáng lên + phóng to nhẹ khi chạm
public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public enum Kind { Jump, Sword, Bolt }
    public Kind kind;
    public Image[] images;
    int pointers;

    public void OnPointerDown(PointerEventData e)
    {
        pointers++;
        MobileControls.Press(kind, true);
        SetLook(true);
    }

    public void OnPointerUp(PointerEventData e) { Release(); }
    public void OnPointerExit(PointerEventData e) { if (pointers > 0 && kind != Kind.Jump) Release(); }

    void Release()
    {
        pointers = Mathf.Max(0, pointers - 1);
        if (pointers > 0) return;
        MobileControls.Press(kind, false);
        SetLook(false);
    }

    void OnDisable() { pointers = 0; MobileControls.Press(kind, false); SetLook(false); }

    void SetLook(bool pressed)
    {
        if (images == null) return;
        foreach (Image i in images) if (i != null) i.color = new Color(1f, 1f, 1f, pressed ? 1f : 0.75f);
        transform.localScale = Vector3.one * (pressed ? 1.08f : 1f);
    }
}

// Joystick ảo (chỉ dùng trục ngang)
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public RectTransform knob;
    public float radius = 110f;
    public float deadZone = 0.2f;
    RectTransform rt;
    Image baseImg;

    void Awake() { rt = GetComponent<RectTransform>(); baseImg = GetComponent<Image>(); }

    public void OnPointerDown(PointerEventData e) { OnDrag(e); if (baseImg) baseImg.color = Color.white; }

    public void OnDrag(PointerEventData e)
    {
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out local)) return;
        Vector2 v = Vector2.ClampMagnitude(local, radius);
        knob.anchoredPosition = v;
        float h = v.x / radius;
        MobileControls.SetHorizontal(Mathf.Abs(h) < deadZone ? 0f : Mathf.Sign(h) * Mathf.InverseLerp(deadZone, 1f, Mathf.Abs(h)));
        MobileControls.SetVertical(v.y / radius);
    }

    public void OnPointerUp(PointerEventData e)
    {
        knob.anchoredPosition = Vector2.zero;
        MobileControls.SetHorizontal(0f);
        MobileControls.SetVertical(0f);
        if (baseImg) baseImg.color = new Color(1f, 1f, 1f, 0.75f);
    }

    void OnDisable() { if (knob) knob.anchoredPosition = Vector2.zero; MobileControls.SetHorizontal(0f); MobileControls.SetVertical(0f); }
}
