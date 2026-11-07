using UnityEngine;

// Co RectTransform vào vùng an toàn của màn hình (tránh tai thỏ / camera đục lỗ / thanh điều hướng)
[RequireComponent(typeof(RectTransform))]
public class SafeArea : MonoBehaviour
{
    RectTransform rt;
    Rect lastSafe;
    Vector2Int lastSize;

    public static RectTransform Create(Transform canvas)
    {
        GameObject go = new GameObject("SafeArea", typeof(RectTransform));
        go.transform.SetParent(canvas, false);
        go.AddComponent<SafeArea>();
        return go.GetComponent<RectTransform>();
    }

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        Apply();
    }

    void Update()
    {
        if (Screen.safeArea != lastSafe || Screen.width != lastSize.x || Screen.height != lastSize.y) Apply();
    }

    void Apply()
    {
        lastSafe = Screen.safeArea;
        lastSize = new Vector2Int(Screen.width, Screen.height);
        if (Screen.width <= 0 || Screen.height <= 0) return;
        Vector2 min = lastSafe.position, max = lastSafe.position + lastSafe.size;
        min.x /= Screen.width; min.y /= Screen.height;
        max.x /= Screen.width; max.y /= Screen.height;
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
