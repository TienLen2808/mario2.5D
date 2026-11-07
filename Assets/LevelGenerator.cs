using System.Collections.Generic;
using UnityEngine;

// Sinh map theo MÀN:
// - Mỗi màn gồm N mảnh map (Segment_1) random độ cao, quái (Ogre nhỏ, Ogre to, Rồng) và xu
// - Cuối màn là ĐẤU TRƯỜNG BOSS: 2 mảnh phẳng, vào là đóng tường, hạ Boss mới mở đường sang màn sau
// - Màn sau: thêm mảnh, quái nhiều hơn, Boss trâu hơn
// - Checkpoint: chết thì hồi sinh ở đầu mảnh map TRƯỚC mảnh đang đứng (trong đấu trường thì ở đầu đấu trường)
// - Object Pooling cho mảnh map, quái, xu
public class LevelGenerator : MonoBehaviour
{
    public static LevelGenerator Instance { get; private set; }

    [Header("Prefab")]
    [Tooltip("Mảnh map gốc (Segment_1). Chỉ cần có object Ground, Coin bên trong sẽ được dùng làm mẫu xu")]
    public GameObject segmentPrefab;
    [Tooltip("Để trống = tự lấy object 'Coin' bên trong segmentPrefab")]
    public GameObject coinPrefab;

    [Header("Tên object con trong Segment")]
    public string groundName = "Ground";
    public string coinName = "Coin";

    [Header("Cấu hình sinh map")]
    public Transform playerTransform;
    public int initialSegments = 5;          // Số mảnh sinh sẵn lúc bắt đầu
    public float safeDistance = 25f;         // Player còn cách cuối map bao nhiêu thì sinh thêm
    public float removeBehindDistance = 15f; // Mảnh nằm sau lưng Player quá khoảng này thì thu hồi
    public int safeStartSegments = 1;        // Số mảnh đầu bằng phẳng, không có quái
    public float fallbackSegmentLength = 20f;

    [Header("Màn chơi")]
    [Tooltip("Màn 1 có bao nhiêu mảnh trước khi tới Boss")]
    public int segmentsBeforeBoss = 14;
    [Tooltip("Mỗi màn sau thêm bao nhiêu mảnh")]
    public int extraSegmentsEachLevel = 3;
    public int arenaSegments = 2;            // Đấu trường Boss rộng bao nhiêu mảnh
    public float bossTriggerDistance = 6f;   // Đi vào đấu trường bao xa thì Boss xuất hiện

    [Header("Độ cao")]
    [Tooltip("Tỉ lệ an toàn so với độ cao nhảy tối đa (0.5 = chỉ dùng một nửa)")]
    [Range(0.2f, 0.9f)] public float jumpSafety = 0.5f;
    public float maxStepDown = 3f;           // Mảnh sau thấp hơn mảnh trước tối đa bao nhiêu
    public float minHeight = -2f;
    public float maxHeight = 6f;
    public float heightSnap = 0.5f;          // Làm tròn độ cao cho gọn (0 = không làm tròn)

    [Header("Quái nhỏ (Ogre mini)")]
    public int maxEnemiesStart = 1;          // Số quái tối đa ở đầu game
    public int maxEnemiesCap = 3;            // Số quái tối đa khi game đã khó
    public int segmentsPerDifficulty = 4;    // Cứ bao nhiêu mảnh thì tăng 1 quái tối đa
    public float enemyEdgeMargin = 2.5f;     // Cách mép mảnh
    public float enemyMinSpacing = 4f;       // Khoảng cách tối thiểu giữa 2 quái

    [Header("Quái khổng lồ (Ogre)")]
    [Range(0f, 1f)] public float ogreChance = 0.35f;  // Xác suất 1 mảnh có Ogre to (màn sau +5%)
    public int ogreStartSegment = 2;                   // Từ mảnh thứ mấy mới bắt đầu có Ogre to
    public float ogreEdgeMargin = 2.5f;                // Ogre đi tuần cách mép mảnh
    public float ogreClearance = 5f;                   // Quái nhỏ đứng cách Ogre to tối thiểu

    [Header("Rồng lửa (quái bay)")]
    [Range(0f, 1f)] public float dragonChance = 0.55f; // Xác suất 1 mảnh có Rồng (màn sau +5%)
    public int dragonStartSegment = 1;
    public int maxDragonsPerSegment = 2;
    public int segmentsPerExtraDragon = 8;
    public float dragonHoverMin = 3f;
    public float dragonHoverMax = 4.5f;

    [Header("Xu")]
    public int minCoins = 0;
    public int maxCoins = 6;
    public float coinEdgeMargin = 1f;
    public float coinMinHeight = 1f;
    public float coinMaxHeight = 3f;
    public float coinSpacing = 1.2f;
    public float coinAvoidEnemyDistance = 1.2f;

    // ---------------------------------------------------------------------

    private class Segment
    {
        public GameObject root;
        public Transform ground;
        public float minX, maxX, topY;
        public bool isArena;
        public SegmentSkin skin;
        public List<GameObject> items = new List<GameObject>();
    }

    private readonly List<Segment> activeSegments = new List<Segment>();
    private readonly Queue<Segment> segmentPool = new Queue<Segment>();
    private readonly Queue<GameObject> coinPool = new Queue<GameObject>();
    private readonly Queue<OgreController> ogrePool = new Queue<OgreController>();
    private readonly Queue<OgreController> miniPool = new Queue<OgreController>();
    private readonly Queue<DragonController> dragonPool = new Queue<DragonController>();

    private float spawnX;
    private float currentHeight = 0f;
    private float maxStepUp = 2.5f;
    private int segmentsSpawned = 0;         // tổng số mảnh (dùng cho độ khó)
    private int levelSegments = 0;           // số mảnh đã sinh trong màn hiện tại
    private Transform poolRoot;

    // Đấu trường Boss
    private bool arenaBuilt, bossStarted, waitingForBoss;
    private float arenaMinX, arenaMaxX, arenaY;
    private GameObject leftWall, rightWall;

    public bool BossFightActive { get { return bossStarted && waitingForBoss; } }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerTransform = player.transform;
        }

        if (segmentPrefab == null)
        {
            Debug.LogError("LevelGenerator: chưa gán segmentPrefab!");
            enabled = false;
            return;
        }

        if (segmentPrefab.scene.IsValid()) segmentPrefab.SetActive(false);
        if (coinPrefab == null) coinPrefab = FindChildObject(segmentPrefab.transform, coinName);

        poolRoot = new GameObject("LevelPool").transform;

        float jumpForce = 10f;
        if (playerTransform != null)
        {
            PlayerController pc = playerTransform.GetComponent<PlayerController>();
            if (pc != null) jumpForce = pc.jumpForce;
        }
        float g = Mathf.Abs(Physics.gravity.y);
        float maxJumpHeight = g > 0.01f ? (jumpForce * jumpForce) / (2f * g) : 3f;
        maxStepUp = maxJumpHeight * jumpSafety;

        spawnX = (playerTransform != null) ? playerTransform.position.x - 5f : 0f;

        for (int i = 0; i < initialSegments; i++) SpawnNext();

        GameSession.Banner("MÀN " + GameSession.Level + " / " + GameSession.FinalLevel, "Tiến lên và hạ Boss cuối màn!");
    }

    int SegmentsThisLevel { get { return segmentsBeforeBoss + extraSegmentsEachLevel * (GameSession.Level - 1); } }
    float LevelBonus { get { return 0.05f * (GameSession.Level - 1); } }

    void Update()
    {
        if (playerTransform == null) return;

        // Sinh thêm mảnh trước mặt (dừng lại khi đã có đấu trường Boss mà Boss chưa chết)
        while (!waitingForBoss && playerTransform.position.x + safeDistance > spawnX)
            SpawnNext();

        // Vào đấu trường -> đóng tường, gọi Boss
        if (arenaBuilt && !bossStarted && playerTransform.position.x > arenaMinX + bossTriggerDistance)
            StartBossFight();

        // Thu hồi mảnh ở xa sau lưng, nhưng luôn giữ lại 1 mảnh phía sau để làm checkpoint
        while (activeSegments.Count > 1 &&
               activeSegments[1].maxX < playerTransform.position.x - removeBehindDistance)
        {
            RecycleSegment(activeSegments[0]);
            activeSegments.RemoveAt(0);
        }
    }

    // ---------------------------------------------------------------------
    // Sinh mảnh map

    void SpawnNext()
    {
        if (levelSegments >= SegmentsThisLevel && !arenaBuilt)
        {
            BuildArena();
            return;
        }
        SpawnSegment(false);
    }

    Segment SpawnSegment(bool arena)
    {
        bool isSafe = segmentsSpawned < safeStartSegments || levelSegments == 0;

        // 1. Random độ cao (mảnh an toàn và đấu trường giữ nguyên độ cao)
        if (!isSafe && !arena)
        {
            float next = currentHeight + Random.Range(-maxStepDown, maxStepUp);
            if (heightSnap > 0f) next = Mathf.Round(next / heightSnap) * heightSnap;
            if (next - currentHeight > maxStepUp) next -= heightSnap;
            currentHeight = Mathf.Clamp(next, minHeight, maxHeight);
        }

        // 2. Lấy mảnh từ pool và đặt mép trái đúng spawnX
        Segment seg = GetSegment();
        seg.isArena = arena;
        seg.root.transform.position = new Vector3(0f, currentHeight, 0f);
        seg.root.SetActive(true);
        if (seg.skin == null) seg.skin = seg.root.AddComponent<SegmentSkin>();
        seg.skin.Build(seg.ground);   // chỉ dựng 1 lần cho mỗi mảnh trong pool
        MeasureSegment(seg);

        float offset = spawnX - seg.minX;
        seg.root.transform.position += new Vector3(offset, 0f, 0f);
        seg.minX += offset;
        seg.maxX += offset;

        // 3. Hoa lá cành + bục nhiều tầng
        List<SegmentSkin.PlatformInfo> plats = seg.skin.Randomize(arena);
        if (!arena) SpawnPlatformCoins(seg, plats);

        // 4. Random quái và xu (đấu trường thì để trống)
        if (!arena)
        {
            List<float> enemyXs = new List<float>();
            if (!isSafe)
            {
                if (segmentsSpawned >= ogreStartSegment && Random.value < ogreChance + LevelBonus) SpawnOgre(seg, enemyXs);
                SpawnMiniOgres(seg, enemyXs);
                if (segmentsSpawned >= dragonStartSegment) SpawnDragons(seg);
            }
            SpawnCoins(seg, enemyXs);
            levelSegments++;
        }

        activeSegments.Add(seg);
        spawnX = seg.maxX;
        segmentsSpawned++;
        return seg;
    }

    // ---------------------------------------------------------------------
    // Đấu trường Boss

    void BuildArena()
    {
        Segment first = null, last = null;
        for (int i = 0; i < arenaSegments; i++)
        {
            Segment s = SpawnSegment(true);
            if (first == null) first = s;
            last = s;
        }
        arenaMinX = first.minX;
        arenaMaxX = last.maxX;
        arenaY = first.topY;
        arenaBuilt = true;
        waitingForBoss = true;   // dừng sinh map cho tới khi hạ Boss

        rightWall = MakeWall("BossWall_Right", arenaMaxX - 0.5f);
        leftWall = MakeWall("BossWall_Left", arenaMinX + 0.5f);
        leftWall.SetActive(false);   // chỉ đóng khi trận đấu bắt đầu
    }

    GameObject MakeWall(string n, float x)
    {
        GameObject w = new GameObject(n);
        w.transform.position = new Vector3(x, arenaY + 8f, 0f);
        BoxCollider bc = w.AddComponent<BoxCollider>();
        bc.size = new Vector3(1f, 20f, 4f);

        // Màn chắn năng lượng mờ để Player biết là có tường
        GameObject vis = new GameObject("Barrier");
        vis.transform.SetParent(w.transform, false);
        vis.transform.localScale = new Vector3(0.6f, 20f, 1f);
        SpriteRenderer sr = vis.AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        sr.color = new Color(1f, 0.2f, 0.3f, 0.35f);
        sr.sortingOrder = 20;
        return w;
    }

    void StartBossFight()
    {
        bossStarted = true;
        leftWall.SetActive(true);

        // Boss của màn này (có màn 2 Boss cùng lúc)
        BossRoster.Entry[] list = BossRoster.ForLevel(GameSession.Level);
        for (int i = 0; i < list.Length; i++)
        {
            float t = list.Length == 1 ? 0.72f : (i == 0 ? 0.6f : 0.88f);
            float x = Mathf.Lerp(arenaMinX, arenaMaxX, t);
            BossController boss = BossController.Create(GameSession.Level, arenaMinX + 1f, arenaMaxX - 1f, arenaY, x,
                                                        list[i].name, list[i].forms, list[i].hp, i);
            GameSession.AddBoss(boss);
        }
        if (list.Length > 1)
            GameSession.Banner("CẢNH BÁO: " + list.Length + " BOSS!", list[0].name + " & " + list[1].name);
    }

    // BossController gọi khi Boss chết
    public void OnBossDefeated()
    {
        int finished = GameSession.Level;

        if (finished >= GameSession.FinalLevel)
        {
            // Hạ trùm cuối -> PHÁ ĐẢO
            GameSession.Banner("PHÁ ĐẢO!", "Bạn đã hạ " + BossController.BossName);
            GameSession.Win();
            if (rightWall != null) Destroy(rightWall);
            return;
        }

        GameSession.NextLevel();
        BossRoster.Entry[] next = BossRoster.ForLevel(GameSession.Level);
        string bossInfo = next.Length > 1 ? next[0].name + " & " + next[1].name : next[0].name;
        GameSession.Banner("HOÀN THÀNH MÀN " + finished + "!", "Màn " + GameSession.Level + " / " + GameSession.FinalLevel + " — Boss: " + bossInfo);

        if (leftWall != null) Destroy(leftWall);
        if (rightWall != null) Destroy(rightWall);
        arenaBuilt = bossStarted = waitingForBoss = false;
        levelSegments = 0;
    }

    // ---------------------------------------------------------------------
    // Checkpoint: đầu mảnh map TRƯỚC mảnh Player đang đứng
    public Vector3 GetCheckpoint(float playerX)
    {
        if (BossFightActive)
            return new Vector3(arenaMinX + 2.5f, arenaY + 1.5f, 0f);

        int idx = -1;
        for (int i = 0; i < activeSegments.Count; i++)
            if (playerX >= activeSegments[i].minX && playerX < activeSegments[i].maxX) { idx = i; break; }
        if (idx < 0)   // rơi ra ngoài -> lấy mảnh gần nhất
        {
            float best = float.MaxValue;
            for (int i = 0; i < activeSegments.Count; i++)
            {
                float c = (activeSegments[i].minX + activeSegments[i].maxX) * 0.5f;
                if (Mathf.Abs(c - playerX) < best) { best = Mathf.Abs(c - playerX); idx = i; }
            }
        }
        if (idx < 0) return new Vector3(0f, 2f, 0f);
        if (idx > 0) idx--;   // mảnh trước đó

        Segment s = activeSegments[idx];
        return new Vector3(s.minX + 1.5f, s.topY + 1.5f, 0f);
    }

    // ---------------------------------------------------------------------
    // Quái

    void SpawnOgre(Segment seg, List<float> enemyXs)
    {
        float left = seg.minX + ogreEdgeMargin;
        float right = seg.maxX - ogreEdgeMargin;
        if (right <= left) return;

        float x = (left + right) * 0.5f;
        OgreController ogre = GetOgre(ogrePool, false);
        PlaceOgre(ogre, seg, x, left, right);

        for (float k = -ogreClearance; k <= ogreClearance; k += 1f)
            enemyXs.Add(x + k);
    }

    void SpawnMiniOgres(Segment seg, List<float> enemyXs)
    {
        int difficulty = segmentsPerDifficulty > 0 ? (segmentsSpawned - safeStartSegments) / segmentsPerDifficulty : 0;
        int maxEnemies = Mathf.Min(maxEnemiesCap + (GameSession.Level - 1), maxEnemiesStart + Mathf.Max(0, difficulty));
        int count = Random.Range(0, maxEnemies + 1);

        float left = seg.minX + enemyEdgeMargin;
        float right = seg.maxX - enemyEdgeMargin;
        if (right <= left) return;

        for (int i = 0; i < count; i++)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                float x = Random.Range(left, right);
                if (!IsFarFromAll(x, enemyXs, enemyMinSpacing)) continue;

                enemyXs.Add(x);
                OgreController mini = GetOgre(miniPool, true);
                PlaceOgre(mini, seg, x, seg.minX + 1f, seg.maxX - 1f);
                break;
            }
        }
    }

    void SpawnDragons(Segment seg)
    {
        int extra = segmentsPerExtraDragon > 0 ? segmentsSpawned / segmentsPerExtraDragon : 0;
        int maxCount = Mathf.Clamp(1 + extra, 1, maxDragonsPerSegment);

        for (int i = 0; i < maxCount; i++)
        {
            if (Random.value >= dragonChance + LevelBonus) continue;

            DragonController d = null;
            while (dragonPool.Count > 0 && d == null) d = dragonPool.Dequeue();
            if (d == null) d = DragonController.Create();
            d.gameObject.SetActive(false);
            d.transform.SetParent(null);

            float x = Random.Range(seg.minX + 3f, seg.maxX - 3f);
            float h = Random.Range(dragonHoverMin, dragonHoverMax);
            d.ApplyLevel(GameSession.Level);
            d.minX = seg.minX + 1f;
            d.maxX = seg.maxX - 1f;
            d.groundY = seg.topY;
            d.hoverHeight = h;
            d.transform.SetPositionAndRotation(new Vector3(x, seg.topY + h, 0f), Quaternion.identity);
            d.gameObject.SetActive(true);
            seg.items.Add(d.gameObject);
        }
    }

    OgreController GetOgre(Queue<OgreController> pool, bool mini)
    {
        OgreController o = null;
        while (pool.Count > 0 && o == null) o = pool.Dequeue();
        if (o == null) o = OgreController.Create(mini);
        o.gameObject.SetActive(false);
        o.transform.SetParent(null);
        return o;
    }

    void PlaceOgre(OgreController o, Segment seg, float x, float minX, float maxX)
    {
        o.ApplyLevel(GameSession.Level);   // quái màn sau mạnh & khôn hơn
        o.minX = minX;
        o.maxX = maxX;
        o.transform.SetPositionAndRotation(new Vector3(x, seg.topY + 0.02f, 0f), Quaternion.identity);
        o.gameObject.SetActive(true);
        seg.items.Add(o.gameObject);
    }

    void SpawnCoins(Segment seg, List<float> enemyXs)
    {
        if (coinPrefab == null) return;

        int count = Random.Range(minCoins, maxCoins + 1);
        if (count <= 0) return;

        float left = seg.minX + coinEdgeMargin;
        float right = seg.maxX - coinEdgeMargin;
        float rowLength = (count - 1) * coinSpacing;
        if (right - left < rowLength) return;

        float startX = Random.Range(left, right - rowLength);
        float baseHeight = Random.Range(coinMinHeight, coinMaxHeight);
        bool arc = count >= 3 && Random.value < 0.4f;

        for (int i = 0; i < count; i++)
        {
            float x = startX + i * coinSpacing;
            float h = baseHeight;
            if (arc)
            {
                float t = (count == 1) ? 0f : (float)i / (count - 1);
                h = coinMinHeight + Mathf.Sin(t * Mathf.PI) * (coinMaxHeight - coinMinHeight);
            }
            if (!IsFarFromAll(x, enemyXs, coinAvoidEnemyDistance))
                h = Mathf.Max(h, coinMaxHeight);

            GameObject coin = GetCoin();
            coin.transform.position = new Vector3(x, seg.topY + h, coinPrefab.transform.localPosition.z);
            coin.SetActive(true);
            seg.items.Add(coin);
        }
    }

    // Xu trên mặt bục (thưởng cho người chịu leo lên tầng trên)
    void SpawnPlatformCoins(Segment seg, List<SegmentSkin.PlatformInfo> plats)
    {
        if (coinPrefab == null) return;
        foreach (SegmentSkin.PlatformInfo p in plats)
        {
            if (Random.value > 0.8f) continue;
            int count = Mathf.Max(1, Mathf.FloorToInt((p.width - 1f) / coinSpacing));
            float start = p.topCenter.x - (count - 1) * coinSpacing * 0.5f;
            for (int i = 0; i < count; i++)
            {
                GameObject coin = GetCoin();
                coin.transform.position = new Vector3(start + i * coinSpacing, p.topCenter.y + 1f, coinPrefab.transform.localPosition.z);
                coin.SetActive(true);
                seg.items.Add(coin);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Pool

    Segment GetSegment()
    {
        if (segmentPool.Count > 0) return segmentPool.Dequeue();

        Segment seg = new Segment();
        seg.root = Instantiate(segmentPrefab, poolRoot);
        seg.root.name = segmentPrefab.name;
        seg.root.transform.SetParent(null);

        List<GameObject> toRemove = new List<GameObject>();
        foreach (Transform t in seg.root.GetComponentsInChildren<Transform>(true))
        {
            if (t == seg.root.transform) continue;
            if (t.name == coinName) toRemove.Add(t.gameObject);
        }
        foreach (GameObject go in toRemove) { go.transform.SetParent(null); Destroy(go); }

        Transform g = FindChildRecursive(seg.root.transform, groundName);
        seg.ground = g != null ? g : seg.root.transform;
        return seg;
    }

    void RecycleSegment(Segment seg)
    {
        foreach (GameObject item in seg.items)
        {
            if (item == null) continue;
            item.SetActive(false);
            item.transform.SetParent(poolRoot);

            DragonController dc = item.GetComponent<DragonController>();
            OgreController o = item.GetComponent<OgreController>();
            if (dc != null) dragonPool.Enqueue(dc);
            else if (o != null)
            {
                if (o.isMini) miniPool.Enqueue(o);
                else ogrePool.Enqueue(o);
            }
            else coinPool.Enqueue(item);
        }
        seg.items.Clear();
        seg.root.SetActive(false);
        segmentPool.Enqueue(seg);
    }

    GameObject GetCoin()
    {
        GameObject go = null;
        while (coinPool.Count > 0 && go == null) go = coinPool.Dequeue();
        if (go == null)
        {
            go = Instantiate(coinPrefab, poolRoot);
            go.SetActive(false);
            go.name = coinName;
        }
        go.transform.SetParent(null);
        return go;
    }

    // ---------------------------------------------------------------------
    // Tiện ích

    void MeasureSegment(Segment seg)
    {
        Renderer[] rs = seg.ground.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0)
        {
            seg.minX = seg.root.transform.position.x - fallbackSegmentLength * 0.5f;
            seg.maxX = seg.minX + fallbackSegmentLength;
            seg.topY = seg.root.transform.position.y;
            return;
        }

        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        seg.minX = b.min.x;
        seg.maxX = b.max.x;
        seg.topY = b.max.y;
    }

    bool IsFarFromAll(float x, List<float> xs, float minDistance)
    {
        foreach (float other in xs)
            if (Mathf.Abs(other - x) < minDistance) return false;
        return true;
    }

    GameObject FindChildObject(Transform parent, string childName)
    {
        Transform t = FindChildRecursive(parent, childName);
        return t != null ? t.gameObject : null;
    }

    Transform FindChildRecursive(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;
            Transform found = FindChildRecursive(child, childName);
            if (found != null) return found;
        }
        return null;
    }
}
