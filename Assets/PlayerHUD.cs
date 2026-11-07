using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// HUD:
//   Góc trên trái : avatar Hero + 5 trái tim (số mạng) + MÀN x/6 + số XU đã nhặt
//   Trên giữa     : thanh máu BOSS cỡ lớn (tối đa 2 Boss cùng lúc), vạch chia theo số dạng của Boss
//   Giữa màn hình : banner thông báo + màn hình GAME OVER / PHÁ ĐẢO
// Tự tạo khi scene được nạp (GameBootstrap), không cần dựng Canvas trong scene.
public class PlayerHUD : MonoBehaviour
{
    const float BossBarW = 900f, BossBarH = 30f;

    PlayerController player;
    Font font;
    Sprite heartFull, heartEmpty, coinSprite;
    Image[] hearts = new Image[GameSession.StartLives];
    RectTransform avatarRect;
    Image avatarImg;
    Text levelText, livesText, coinText, auraText;
    Vector2 avatarHome;
    int lastLives = -1;

    class BossBar
    {
        public GameObject root;
        public RectTransform fill, trail;
        public Image fillImg;
        public Text name, form;
        public RectTransform[] ticks;
        public float trailValue = 1f;
        public BossController boss;
    }
    BossBar[] bossBars = new BossBar[2];

    Text bannerTitle, bannerSub;
    CanvasGroup bannerGroup;
    Coroutine bannerRoutine;
    GameObject endRoot;
    Text endTitle, endInfo;

    // Gọi từ GameBootstrap mỗi khi scene được nạp
    public static void AutoCreate()
    {
        if (FindAnyObjectByType<PlayerHUD>() != null) return;
        new GameObject("PlayerHUD").AddComponent<PlayerHUD>();
    }

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Texture2D ht = Resources.Load<Texture2D>("hud_hearts");
        if (ht != null)
        {
            int c = ht.height;
            heartFull = Sprite.Create(ht, new Rect(0, 0, c, c), new Vector2(0.5f, 0.5f), 100f);
            heartEmpty = Sprite.Create(ht, new Rect(c, 0, c, c), new Vector2(0.5f, 0.5f), 100f);
        }
        Texture2D ct = Resources.Load<Texture2D>("coin_spin");
        if (ct != null) coinSprite = Sprite.Create(ct, new Rect(0, 0, ct.height, ct.height), new Vector2(0.5f, 0.5f), 100f);

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform safe = SafeArea.Create(transform);

        BuildPlayerPanel(safe);
        BuildCoinPanel(safe);
        for (int i = 0; i < bossBars.Length; i++) bossBars[i] = BuildBossBar(safe, i);
        BuildBanner(safe);
        BuildEndScreen(safe);

        GameSession.OnChanged += Refresh;
        GameSession.OnBanner += ShowBanner;
    }

    void OnDestroy()
    {
        GameSession.OnChanged -= Refresh;
        GameSession.OnBanner -= ShowBanner;
    }

    // ------------------------------------------------------------------
    void BuildPlayerPanel(RectTransform safe)
    {
        RectTransform panel = MakeImage("Panel", safe, new Color(0f, 0f, 0f, 0.45f));
        TopLeft(panel, new Vector2(24, -24), new Vector2(430, 112));

        Text nameText = MakeText("Name", panel, 24, TextAnchor.MiddleLeft);
        nameText.text = "HERO";
        nameText.fontStyle = FontStyle.Bold;
        TopLeft(nameText.rectTransform, new Vector2(120, -10), new Vector2(120, 34));

        levelText = MakeText("Level", panel, 22, TextAnchor.MiddleRight);
        levelText.color = new Color(1f, 0.85f, 0.3f);
        levelText.fontStyle = FontStyle.Bold;
        TopLeft(levelText.rectTransform, new Vector2(250, -10), new Vector2(165, 34));

        RectTransform livesRow = new GameObject("Lives", typeof(RectTransform)).GetComponent<RectTransform>();
        livesRow.SetParent(panel, false);
        TopLeft(livesRow, new Vector2(118, -48), new Vector2(300, 52));
        for (int i = 0; i < hearts.Length; i++)
        {
            RectTransform h = MakeImage("Heart" + i, livesRow, Color.white);
            TopLeft(h, new Vector2(i * 54, 0), new Vector2(50, 50));
            hearts[i] = h.GetComponent<Image>();
            hearts[i].sprite = heartFull;
        }
        livesText = MakeText("LivesText", panel, 20, TextAnchor.MiddleLeft);
        TopLeft(livesText.rectTransform, new Vector2(390, -56), new Vector2(60, 36));


        avatarRect = MakeImage("Avatar", panel, Color.white);
        TopLeft(avatarRect, new Vector2(-10, 8), new Vector2(124, 124));
        avatarHome = avatarRect.anchoredPosition;
        avatarImg = avatarRect.GetComponent<Image>();
        Texture2D tex = Resources.Load<Texture2D>("hero_avatar");
        if (tex != null)
            avatarImg.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }

    // Góc trên phải: số xu + tiến độ lên cấp aura
    void BuildCoinPanel(RectTransform safe)
    {
        RectTransform panel = MakeImage("CoinPanel", safe, new Color(0f, 0f, 0f, 0.45f));
        panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-24, -24);
        panel.sizeDelta = new Vector2(300, 112);

        RectTransform coinIcon = MakeImage("CoinIcon", panel, Color.white);
        TopLeft(coinIcon, new Vector2(14, -10), new Vector2(64, 64));
        coinIcon.GetComponent<Image>().sprite = coinSprite;

        coinText = MakeText("Coins", panel, 44, TextAnchor.MiddleLeft);
        coinText.fontStyle = FontStyle.Bold;
        coinText.color = new Color(1f, 0.85f, 0.3f);
        TopLeft(coinText.rectTransform, new Vector2(88, -10), new Vector2(200, 64));

        auraText = MakeText("Aura", panel, 18, TextAnchor.MiddleLeft);
        auraText.color = new Color(0.8f, 0.95f, 1f);
        TopLeft(auraText.rectTransform, new Vector2(16, -76), new Vector2(280, 28));
    }

    BossBar BuildBossBar(RectTransform safe, int index)
    {
        BossBar b = new BossBar();
        RectTransform root = new GameObject("BossBar" + index, typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(safe, false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = new Vector2(0, -26 - index * 92);
        root.sizeDelta = new Vector2(BossBarW + 10, 90);
        b.root = root.gameObject;

        b.name = MakeText("BossName", root, 30, TextAnchor.MiddleLeft);
        b.name.fontStyle = FontStyle.Bold;
        b.name.color = new Color(1f, 0.3f, 0.25f);
        TopLeft(b.name.rectTransform, new Vector2(5, 0), new Vector2(500, 38));

        b.form = MakeText("BossForm", root, 24, TextAnchor.MiddleRight);
        b.form.color = new Color(1f, 0.85f, 0.3f);
        TopLeft(b.form.rectTransform, new Vector2(BossBarW - 495, 0), new Vector2(500, 38));

        RectTransform border = MakeImage("Border", root, new Color(0.05f, 0.02f, 0.02f, 1f));
        TopLeft(border, new Vector2(0, -40), new Vector2(BossBarW + 10, BossBarH + 10));
        RectTransform inner = MakeImage("Bg", border, new Color(0.25f, 0.04f, 0.06f, 1f));
        TopLeft(inner, new Vector2(5, -5), new Vector2(BossBarW, BossBarH));
        b.trail = MakeImage("Trail", inner, new Color(1f, 0.9f, 0.7f, 1f));
        TopLeft(b.trail, Vector2.zero, new Vector2(BossBarW, BossBarH));
        b.fill = MakeImage("Fill", inner, new Color(0.85f, 0.1f, 0.15f, 1f));
        TopLeft(b.fill, Vector2.zero, new Vector2(BossBarW, BossBarH));
        b.fillImg = b.fill.GetComponent<Image>();
        RectTransform shine = MakeImage("Shine", b.fill, new Color(1f, 1f, 1f, 0.22f));
        shine.anchorMin = new Vector2(0, 0.55f); shine.anchorMax = new Vector2(1, 0.9f);
        shine.offsetMin = shine.offsetMax = Vector2.zero;

        // Vạch chia các dạng (bật theo số dạng của Boss)
        b.ticks = new RectTransform[2];
        for (int i = 0; i < b.ticks.Length; i++)
        {
            b.ticks[i] = MakeImage("Tick" + i, inner, new Color(0f, 0f, 0f, 0.9f));
            TopLeft(b.ticks[i], Vector2.zero, new Vector2(4, BossBarH));
        }
        b.root.SetActive(false);
        return b;
    }

    void BuildBanner(RectTransform safe)
    {
        RectTransform root = new GameObject("Banner", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
        root.SetParent(safe, false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.6f);
        root.sizeDelta = new Vector2(1400, 200);
        bannerGroup = root.GetComponent<CanvasGroup>();
        bannerGroup.alpha = 0f;

        RectTransform bg = MakeImage("Bg", root, new Color(0f, 0f, 0f, 0.5f));
        bg.anchorMin = new Vector2(0, 0.1f); bg.anchorMax = new Vector2(1, 0.9f);
        bg.offsetMin = bg.offsetMax = Vector2.zero;

        bannerTitle = MakeText("Title", root, 64, TextAnchor.MiddleCenter);
        bannerTitle.fontStyle = FontStyle.Bold;
        bannerTitle.color = new Color(1f, 0.85f, 0.3f);
        Stretch(bannerTitle.rectTransform, 0.45f, 0.9f);
        bannerTitle.gameObject.AddComponent<Outline>().effectColor = Color.black;

        bannerSub = MakeText("Sub", root, 30, TextAnchor.MiddleCenter);
        Stretch(bannerSub.rectTransform, 0.1f, 0.45f);
    }

    void BuildEndScreen(RectTransform safe)
    {
        RectTransform root = MakeImage("EndScreen", safe, new Color(0f, 0f, 0f, 0.6f));
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        endRoot = root.gameObject;

        endTitle = MakeText("Title", root, 110, TextAnchor.MiddleCenter);
        endTitle.fontStyle = FontStyle.Bold;
        Stretch(endTitle.rectTransform, 0.52f, 0.72f);
        Outline ol = endTitle.gameObject.AddComponent<Outline>(); ol.effectColor = Color.black; ol.effectDistance = new Vector2(3, -3);

        endInfo = MakeText("Info", root, 36, TextAnchor.MiddleCenter);
        Stretch(endInfo.rectTransform, 0.42f, 0.52f);

        Text s = MakeText("Hint", root, 30, TextAnchor.MiddleCenter);
        s.text = "Bấm R / Enter hoặc chạm màn hình để chơi lại từ Màn 1";
        Stretch(s.rectTransform, 0.32f, 0.40f);
        endRoot.SetActive(false);
    }

    // ------------------------------------------------------------------
    void Update()
    {
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) { player = p.GetComponent<PlayerController>(); Refresh(); }
        }

        // Thanh máu Boss (tối đa 2)
        for (int i = 0; i < bossBars.Length; i++)
        {
            BossController boss = i < GameSession.Bosses.Count ? GameSession.Bosses[i] : null;
            UpdateBossBar(bossBars[i], boss);
        }
    }

    void UpdateBossBar(BossBar b, BossController boss)
    {
        bool show = boss != null;
        if (b.root.activeSelf != show) b.root.SetActive(show);
        if (!show) { b.boss = null; return; }
        if (b.boss != boss)
        {
            b.boss = boss; b.trailValue = 1f;
            for (int t = 0; t < b.ticks.Length; t++)
            {
                bool on = t + 1 < boss.FormCount;
                b.ticks[t].gameObject.SetActive(on);
                if (on) b.ticks[t].anchoredPosition = new Vector2(BossBarW * (t + 1) / boss.FormCount - 2f, 0f);
            }
        }

        float ratio = (float)boss.Hp / Mathf.Max(1, boss.MaxHp);
        if (b.trailValue > ratio) b.trailValue = Mathf.MoveTowards(b.trailValue, ratio, Time.deltaTime * 0.35f);
        else b.trailValue = ratio;
        SetWidth(b.fill, ratio);
        SetWidth(b.trail, b.trailValue);
        b.name.text = boss.Name;
        b.form.text = boss.IsTransforming && boss.FormCount > 1 ? "ĐANG BIẾN HÌNH..."
                    : boss.FormCount > 1 ? "Dạng " + FormNumber(boss) + "/" + boss.FormCount + ": " + boss.FormName
                    : boss.FormName;
        b.fillImg.color = boss.CurrentForm == BossController.Form.OgreKing ? new Color(0.9f, 0.55f, 0.1f)
                        : boss.CurrentForm == BossController.Form.FireDragon ? new Color(0.9f, 0.12f, 0.15f)
                        : new Color(0.6f, 0.2f, 0.9f);
    }

    static int FormNumber(BossController boss)
    {
        float lost = 1f - (float)boss.Hp / Mathf.Max(1, boss.MaxHp);
        return Mathf.Min(boss.FormCount, (int)(lost * boss.FormCount) + 1);
    }

    void Refresh()
    {
        int lives = GameSession.Lives;
        for (int i = 0; i < hearts.Length; i++)
            hearts[i].sprite = i < lives ? heartFull : heartEmpty;
        livesText.text = "x" + lives;
        levelText.text = "MÀN " + GameSession.Level + "/" + GameSession.FinalLevel;
        coinText.text = GameSession.Coins.ToString();
        int tier = HeroAura.TierFor(GameSession.Coins);
        if (tier >= HeroAura.Thresholds.Length) auraText.text = "Aura MAX: " + HeroAura.TierNames[tier - 1];
        else auraText.text = (tier > 0 ? "Aura " + tier + " · " : "") + "còn " + (HeroAura.Thresholds[tier] - GameSession.Coins) + " xu lên cấp " + (tier + 1);

        if (lastLives >= 0 && lives < lastLives) StartCoroutine(HurtFlash());
        lastLives = lives;

        bool over = player != null && player.IsGameOver;
        bool win = GameSession.IsVictory;
        endRoot.SetActive(over || win);
        if (win)
        {
            endTitle.text = "PHÁ ĐẢO!";
            endTitle.color = new Color(1f, 0.85f, 0.25f);
            endInfo.text = "Đã hạ " + BossController.BossName + " · Xu: " + GameSession.Coins + " · Mạng còn: " + lives;
        }
        else if (over)
        {
            endTitle.text = "GAME OVER";
            endTitle.color = new Color(1f, 0.25f, 0.2f);
            endInfo.text = "Dừng ở Màn " + GameSession.Level + " · Xu: " + GameSession.Coins;
        }
        avatarImg.color = over ? new Color(0.4f, 0.4f, 0.4f) : Color.white;
    }

    IEnumerator HurtFlash()
    {
        for (int i = 0; i < 6; i++)
        {
            avatarImg.color = (i % 2 == 0) ? new Color(1f, 0.35f, 0.35f) : Color.white;
            avatarRect.anchoredPosition = avatarHome + Random.insideUnitCircle * 6f;
            yield return new WaitForSeconds(0.05f);
        }
        avatarRect.anchoredPosition = avatarHome;
        avatarImg.color = Color.white;
    }

    void ShowBanner(string title, string sub)
    {
        if (bannerRoutine != null) StopCoroutine(bannerRoutine);
        bannerRoutine = StartCoroutine(BannerRoutine(title, sub));
    }

    IEnumerator BannerRoutine(string title, string sub)
    {
        bannerTitle.text = title;
        bannerSub.text = sub;
        for (float t = 0; t < 0.25f; t += Time.deltaTime) { bannerGroup.alpha = t / 0.25f; yield return null; }
        bannerGroup.alpha = 1f;
        yield return new WaitForSeconds(2.2f);
        for (float t = 0; t < 0.5f; t += Time.deltaTime) { bannerGroup.alpha = 1f - t / 0.5f; yield return null; }
        bannerGroup.alpha = 0f;
    }

    // ------------------------------------------------------------------
    void SetWidth(RectTransform r, float ratio)
    {
        r.sizeDelta = new Vector2(BossBarW * Mathf.Clamp01(ratio), r.sizeDelta.y);
    }

    static void Stretch(RectTransform r, float yMin, float yMax)
    {
        r.anchorMin = new Vector2(0, yMin); r.anchorMax = new Vector2(1, yMax);
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    static RectTransform MakeImage(string n, Transform parent, Color c)
    {
        GameObject go = new GameObject(n, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return go.GetComponent<RectTransform>();
    }

    Text MakeText(string n, Transform parent, int size, TextAnchor align)
    {
        GameObject go = new GameObject(n, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text t = go.GetComponent<Text>();
        t.font = font; t.fontSize = size; t.alignment = align;
        t.color = Color.white; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        go.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);
        return t;
    }

    static void TopLeft(RectTransform r, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = new Vector2(0, 1);
        r.pivot = new Vector2(0, 1);
        r.anchoredPosition = pos;
        r.sizeDelta = size;
    }
}
