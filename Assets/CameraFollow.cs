using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // Nhân vật Player
    public Vector3 offset = new Vector3(0f, 2f, -10f); // Khoảng cách giữa Camera và Player
    public float smoothSpeed = 5f; // Tốc độ di chuyển mượt của Camera

    void LateUpdate()
    {
        if (target != null)
        {
            // Vị trí mục tiêu mà Camera cần tới
            Vector3 desiredPosition = target.position + offset;
            
            // Nội suy vị trí giúp Camera di chuyển mượt mà
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
            
            // Cập nhật vị trí Camera
            transform.position = smoothedPosition;
        }
    }
}