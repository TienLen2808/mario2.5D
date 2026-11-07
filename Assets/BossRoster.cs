using UnityEngine;

// Danh sách Boss của từng màn (6 màn). Màn 6 = trùm cuối, hạ xong là PHÁ ĐẢO.
//   Màn 1: VUA OGRE                         (đi bộ)                 -> KIẾM
//   Màn 2: HỎA LONG                         (bay)                   -> CHƯỞNG / nhảy lên chém
//   Màn 3: VUA OGRE + HỎA LONG cùng lúc
//   Màn 4: HẮC LONG                         (bay, lửa tím, đáp đất choáng)
//   Màn 5: SONG LONG: HỎA LONG + HẮC LONG cùng lúc
//   Màn 6: MA VƯƠNG 3 dạng (Ogre Vương -> Hỏa Long -> Hắc Long)
public static class BossRoster
{
    public struct Entry
    {
        public string name;
        public BossController.Form[] forms;
        public int hp;
        public Entry(string n, int h, params BossController.Form[] f) { name = n; hp = h; forms = f; }
    }

    const BossController.Form O = BossController.Form.OgreKing;
    const BossController.Form F = BossController.Form.FireDragon;
    const BossController.Form D = BossController.Form.DarkDragon;

    public static Entry[] ForLevel(int level)
    {
        switch (Mathf.Clamp(level, 1, GameSession.FinalLevel))
        {
            case 1: return new[] { new Entry("VUA OGRE", 24, O) };
            case 2: return new[] { new Entry("HỎA LONG", 28, F) };
            case 3: return new[] { new Entry("VUA OGRE", 20, O), new Entry("HỎA LONG", 20, F) };
            case 4: return new[] { new Entry("HẮC LONG", 34, D) };
            case 5: return new[] { new Entry("HỎA LONG", 24, F), new Entry("HẮC LONG", 28, D) };
            default: return new[] { new Entry(BossController.BossName, 60, O, F, D) };
        }
    }
}
