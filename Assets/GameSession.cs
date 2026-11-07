using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Trạng thái của 1 lượt chơi: số mạng, xu, màn hiện tại, các Boss đang đánh, phá đảo.
// Sống qua các lần nạp lại scene (biến static), reset khi bấm Play hoặc chơi lại.
public static class GameSession
{
    public const int StartLives = 5;
    public const int FinalLevel = 6;   // Màn 6 là trận trùm cuối

    public static int Lives { get; private set; }
    public static int Level { get; private set; }
    public static int Coins { get; private set; }
    public static bool IsVictory { get; private set; }
    public static readonly List<BossController> Bosses = new List<BossController>();

    // Giữ tương thích: Boss đầu tiên còn sống (hoặc null)
    public static BossController CurrentBoss { get { return Bosses.Count > 0 ? Bosses[0] : null; } }

    public static event System.Action OnChanged;                 // mạng / xu / màn thay đổi -> HUD cập nhật
    public static event System.Action<string, string> OnBanner;  // (tiêu đề, phụ đề) hiện giữa màn hình

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        ResetRun();
        OnChanged = null; OnBanner = null;
    }

    static void ResetRun()
    {
        Lives = StartLives; Level = 1; Coins = 0; IsVictory = false;
        Bosses.Clear();
    }

    public static void LoseLife() { Lives = Mathf.Max(0, Lives - 1); Notify(); }
    public static void AddCoin() { Coins++; Notify(); }
    public static void NextLevel() { Level++; Notify(); }

    public static void AddBoss(BossController b) { if (b != null && !Bosses.Contains(b)) Bosses.Add(b); Notify(); }

    // Boss chết -> nếu là con cuối cùng trong đấu trường thì báo LevelGenerator mở đường / phá đảo
    public static void BossDied(BossController b)
    {
        Bosses.Remove(b);
        Notify();
        if (Bosses.Count == 0 && LevelGenerator.Instance != null) LevelGenerator.Instance.OnBossDefeated();
    }

    public static void Win()
    {
        IsVictory = true;
        Notify();
    }

    public static void Notify() { if (OnChanged != null) OnChanged(); }
    public static void Banner(string title, string sub) { if (OnBanner != null) OnBanner(title, sub); }

    // Hết mạng / phá đảo -> chơi lại từ Màn 1
    public static void Restart()
    {
        ResetRun();
        OnChanged = null; OnBanner = null;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}

// Gắn các script tự động cho Player / tạo HUD, nút cảm ứng, phông nền:
//  - Lúc bấm Play  : AfterSceneLoad (Unity đang tắt "Reload Scene" nên sceneLoaded KHÔNG chạy lúc này)
//  - Lúc chơi lại  : sceneLoaded (sau khi GameSession.Restart nạp lại scene)
// Các hàm Auto... đều tự kiểm tra trùng nên gọi nhiều lần cũng không sao.
public static class GameBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnPlay() { AttachAll(); }

    static void OnSceneLoaded(Scene s, LoadSceneMode mode) { AttachAll(); }

    static void AttachAll()
    {
        PlayerSpriteAnimator.AutoAttach();
        HeroSkills.AutoAttach();
        HeroAura.AutoAttach();
        PlayerHUD.AutoCreate();
        MobileControls.AutoCreate();
        BackgroundScroller.AutoCreate();
    }
}
