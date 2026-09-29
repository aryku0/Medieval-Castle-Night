using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A side-on door: only the mesh swings in 3D; all gameplay remains in 2D.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // Read PlayerMovement's velocity before the 2D solver stops it at the door.
public sealed class SideViewDoor : MonoBehaviour
{
    [Header("Hierarchy (children of the hinge root)")]
    [SerializeField] private Transform doorVisual;
    [SerializeField] private BoxCollider2D doorBlocker;
    [SerializeField] private BoxCollider2D doorSensor;

    [Header("Geometry in local units")]
    [SerializeField] private Vector2 doorwaySize = new Vector2(1f, 2.8f);
    [SerializeField, Min(0.01f)] private float panelThickness = 0.12f;
    [SerializeField, Min(1f)] private float visualWidthMultiplier = 4f;
    [SerializeField, Min(1f)] private float visualThicknessMultiplier = 3f;
    [SerializeField] private bool hingeOnRight;
    [SerializeField] private Vector2 sensorSize = new Vector2(2f, 3f);

    [Header("Swing (degrees / seconds)")]
    [SerializeField, Range(65f, 85f)] private float maximumAngle = 83f;
    [Tooltip("Angular acceleration per unit of player approach speed.")]
    [SerializeField, Min(0f)] private float pushStrength = 260f;
    [SerializeField, Min(0.01f)] private float damping = 7f;
    [SerializeField, Min(0f)] private float closingSpringStrength = 7f;
    [SerializeField, Min(1f)] private float maximumAngularSpeed = 220f;
    [SerializeField, Min(0f)] private float minimumPushSpeed = 0.05f;

    [Header("Safe passage")]
    [SerializeField, Range(55f, 65f)] private float passThroughAngle = 60f;
    [SerializeField, Range(0f, 30f)] private float solidAgainAngle = 10f;
    [SerializeField, Min(0.01f)] private float clearanceMargin = 0.12f;
    [Tooltip("Keep Default included for Player1. This does not change collider layers.")]
    [SerializeField] private LayerMask playerLayers = ~0;

    private readonly List<Collider2D> overlaps = new List<Collider2D>(16);
    private readonly HashSet<Rigidbody2D> checkedBodies = new HashSet<Rigidbody2D>();
    private float angle;
    private float angularVelocity;
    private bool released;
    private bool ready;
    private bool checkSpawnOverlap;

    public float OpeningAngle => angle;
    public float AngularVelocity => angularVelocity;
    public bool IsPassable => doorBlocker && !doorBlocker.enabled;

    private float HingeSign => hingeOnRight ? -1f : 1f;
    private Vector3 ClosedCenter => new Vector3(HingeSign * doorwaySize.x * 0.5f, 0f, 0f);

    private void Awake()
    {
        ready = doorVisual && doorBlocker && doorSensor
            && doorVisual.parent == transform && doorBlocker.transform.parent == transform
            && doorSensor.transform.parent == transform && doorBlocker != doorSensor;
        if (!ready)
        {
            Debug.LogError("SideViewDoor needs separate DoorVisual, DoorBlocker and DoorSensor children. Use the door setup menu.", this);
            enabled = false;
            return;
        }
        ClampSettings();
        ApplyGeometry();
        released = !doorBlocker.enabled;
        checkSpawnOverlap = true;
    }

    private void OnEnable() { checkSpawnOverlap = true; }

    private void OnValidate()
    {
        ClampSettings();
        if (!Application.isPlaying) ApplyGeometry();
    }

    private void ClampSettings()
    {
        doorwaySize.x = Mathf.Max(0.1f, doorwaySize.x);
        doorwaySize.y = Mathf.Max(0.1f, doorwaySize.y);
        sensorSize.x = Mathf.Max(doorwaySize.x + 0.2f, sensorSize.x);
        sensorSize.y = Mathf.Max(doorwaySize.y, sensorSize.y);
        panelThickness = Mathf.Max(0.01f, panelThickness);
        maximumAngle = Mathf.Clamp(maximumAngle, 65f, 85f);
        passThroughAngle = Mathf.Clamp(passThroughAngle, 55f, Mathf.Min(65f, maximumAngle - 1f));
        solidAgainAngle = Mathf.Clamp(solidAgainAngle, 0f, passThroughAngle - 5f);
        damping = Mathf.Max(0.01f, damping);
        closingSpringStrength = Mathf.Max(0f, closingSpringStrength);
        pushStrength = Mathf.Max(0f, pushStrength);
        maximumAngularSpeed = Mathf.Max(1f, maximumAngularSpeed);
        minimumPushSpeed = Mathf.Max(0f, minimumPushSpeed);
        clearanceMargin = Mathf.Max(0.01f, clearanceMargin);
    }

    private void ApplyGeometry()
    {
        ConfigureBox(doorBlocker, doorwaySize, false);
        ConfigureBox(doorSensor, sensorSize, true);
        ApplyVisual();
    }

    private void ConfigureBox(BoxCollider2D box, Vector2 size, bool trigger)
    {
        if (!box || box.transform.parent != transform) return;
        box.transform.localPosition = Vector3.zero;
        box.transform.localRotation = Quaternion.identity;
        box.transform.localScale = Vector3.one;
        box.offset = Vector2.zero;
        box.size = size;
        box.isTrigger = trigger;
    }

    private void ApplyVisual()
    {
        if (!doorVisual || doorVisual.parent != transform) return;
        // Keep the visual hinge on the far wall; 2D physics stays at the doorway.
        // The closed panel extends toward the viewer from behind the player.
        float width = doorwaySize.x * visualWidthMultiplier;
        float thickness = panelThickness * visualThicknessMultiplier;
        var swing = Quaternion.AngleAxis((90f - angle) * HingeSign, Vector3.up);
        doorVisual.localPosition = Vector3.forward * (width + thickness + 0.1f)
            + swing * (ClosedCenter * visualWidthMultiplier);
        doorVisual.localRotation = swing;
        doorVisual.localScale = new Vector3(width, doorwaySize.y, thickness);
        if (doorVisual.TryGetComponent<Renderer>(out var renderer)) renderer.sortingOrder = -1;
    }

    private void FixedUpdate()
    {
        if (!ready) return;
        float dt = Time.fixedDeltaTime;
        float push = GetPushSpeed();
        bool occupied = IsDoorwayOccupied(clearanceMargin);

        // Also handle spawning/reenabling with the player already inside the door.
        // Shrinking the query avoids mistaking ordinary closed-door contact for overlap.
        if (checkSpawnOverlap)
        {
            if (IsDoorwayOccupied(-0.04f)) released = true;
            checkSpawnOverlap = false;
        }

        // Hold an open door while somebody occupies it, including a stationary player.
        bool holding = released && occupied;
        if (holding && angularVelocity * angle < 0f) angularVelocity = 0f;
        float spring = holding ? 0f : -closingSpringStrength * angle;
        angularVelocity += (pushStrength * push * HingeSign + spring) * dt;
        angularVelocity *= Mathf.Exp(-damping * dt);
        angularVelocity = Mathf.Clamp(angularVelocity, -maximumAngularSpeed, maximumAngularSpeed);
        angle += angularVelocity * dt;

        if (Mathf.Abs(angle) >= maximumAngle)
        {
            angle = Mathf.Clamp(angle, -maximumAngle, maximumAngle);
            if (angularVelocity * angle > 0f) angularVelocity = 0f;
        }
        if (!holding && push == 0f && Mathf.Abs(angle) < 0.08f && Mathf.Abs(angularVelocity) < 0.3f)
        {
            angle = 0f;
            angularVelocity = 0f;
        }

        if (Mathf.Abs(angle) >= passThroughAngle) released = true;
        if (released && !occupied && push == 0f && Mathf.Abs(angle) <= solidAgainAngle)
            released = false;
        doorBlocker.enabled = !released;
        ApplyVisual();
    }

    private float GetPushSpeed()
    {
        QueryBox(doorSensor, 0f);
        checkedBodies.Clear();
        float total = 0f;
        Vector2 centre = doorBlocker.transform.TransformPoint(doorBlocker.offset);
        Vector2 right = transform.right;
        foreach (var hit in overlaps)
        {
            var body = PlayerBody(hit);
            if (!body || !checkedBodies.Add(body)) continue;
            float side = Vector2.Dot(body.worldCenterOfMass - centre, right);
            float speed = Vector2.Dot(body.linearVelocity, right);
            if (Mathf.Abs(speed) <= minimumPushSpeed || side * speed >= 0f) continue;
            // Once crossing an open door, do not flip it back across the player.
            float wantedSwing = speed * HingeSign;
            if (released && Mathf.Abs(angle) > solidAgainAngle && wantedSwing * angle < 0f) continue;
            total += speed;
        }
        return total;
    }

    private bool IsDoorwayOccupied(float margin)
    {
        // Query the shape explicitly: Collider2D.bounds is empty when disabled.
        QueryBox(doorBlocker, margin);
        foreach (var hit in overlaps)
            if (PlayerBody(hit)) return true;
        return false;
    }

    private void QueryBox(BoxCollider2D box, float margin)
    {
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(playerLayers);
        var scale = box.transform.lossyScale;
        Vector2 size = new Vector2(Mathf.Abs(scale.x) * box.size.x, Mathf.Abs(scale.y) * box.size.y);
        size += Vector2.one * (2f * margin);
        size.x = Mathf.Max(0.01f, size.x);
        size.y = Mathf.Max(0.01f, size.y);
        Vector2 centre = box.transform.TransformPoint(box.offset);
        gameObject.scene.GetPhysicsScene2D().OverlapBox(centre, size, box.transform.eulerAngles.z, filter, overlaps);
    }

    private static Rigidbody2D PlayerBody(Collider2D hit)
    {
        if (!hit || hit.isTrigger) return null;
        var body = hit.attachedRigidbody;
        return body && body.simulated && (body.CompareTag("Player") || hit.CompareTag("Player")) ? body : null;
    }
}
