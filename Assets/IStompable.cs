// Quái có thể bị Player giẫm lên đầu / đánh trúng để trừ máu (Ogre, Rồng...).
// Chạm thân quái loại này chỉ bị đẩy lùi, chỉ dính đòn đánh mới chết.
public interface IStompable
{
    void TakeHit();

    // true = quái bay (chỉ Tia Lửa Xanh gây sát thương)
    // false = quái đi bộ (chỉ Kiếm gây sát thương)
    bool IsFlying { get; }
}
