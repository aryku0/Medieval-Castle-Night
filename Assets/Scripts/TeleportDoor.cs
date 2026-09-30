using UnityEngine;

/// <summary>
/// A doorway in the back wall that moves you to its linked door elsewhere
/// (e.g. the prison door upstairs and the stairs door in the cellar).
/// Also switches the camera to the area you arrive in.
/// </summary>
public class TeleportDoor : Interactable
{
    [Header("Where it leads")]
    public TeleportDoor destination;
    [Tooltip("What the on-screen prompt says.")]
    public string prompt = "[E] Go through";

    [Header("Camera limits for the area you arrive in")]
    public Vector2 cameraMin;
    public Vector2 cameraMax;

    public override string Prompt => prompt;
    public override bool CanInteract => base.CanInteract && destination;

    public override void Interact(GameObject player)
    {
        if (!destination) return;

        // Arrive standing in front of the other door, just above its floor.
        Vector3 arrival = destination.transform.position;
        arrival.z = player.transform.position.z;

        if (player.TryGetComponent<Rigidbody2D>(out var body))
        {
            body.position = arrival;
            body.linearVelocity = Vector2.zero;
        }
        player.transform.position = arrival;

        var cam = Camera.main ? Camera.main.GetComponent<CameraXYFollow>() : null;
        if (cam) cam.SetBounds(cameraMin, cameraMax, snap: true);
    }
}
