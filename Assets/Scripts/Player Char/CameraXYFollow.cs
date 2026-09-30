using UnityEngine;

// Class name must match the file name (CameraXYFollow.cs) or Unity can't attach it.
public class CameraXYFollow : MonoBehaviour
{
    public Transform target;

    [Header("Follow")]
    public float smoothSpeed = 5f;

    [Header("Camera Boundaries")]
    public float minX;
    public float maxX;
    public float minY;
    public float maxY;

    // The limits the camera started with (the main castle floor).
    private Vector2 startMin, startMax;

    private void Awake()
    {
        startMin = new Vector2(minX, minY);
        startMax = new Vector2(maxX, maxY);
    }

    /// <summary>Switch to another area's limits (e.g. the cellar). Snap jumps straight there.</summary>
    public void SetBounds(Vector2 min, Vector2 max, bool snap)
    {
        minX = min.x; minY = min.y;
        maxX = max.x; maxY = max.y;
        if (snap && target != null)
        {
            transform.position = new Vector3(
                Mathf.Clamp(target.position.x, minX, maxX),
                Mathf.Clamp(target.position.y, minY, maxY),
                transform.position.z);
        }
    }

    /// <summary>Back to the limits the camera started with.</summary>
    public void ResetBounds() => SetBounds(startMin, startMax, snap: true);

    private void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 targetPosition = new Vector3(
            target.position.x,
            target.position.y,
            transform.position.z
        );

        Vector3 smoothPosition = Vector3.Lerp(
            transform.position,
            targetPosition,
            smoothSpeed * Time.deltaTime
        );

        smoothPosition.x = Mathf.Clamp(smoothPosition.x, minX, maxX);
        smoothPosition.y = Mathf.Clamp(smoothPosition.y, minY, maxY);

        transform.position = smoothPosition;
    }
}
