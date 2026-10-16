using UnityEngine;

/// <summary>
/// Custom raycast wheel: spring/damper suspension + arcade tire model.
/// Put this on an empty GameObject at the suspension mount point (forward = vehicle forward).
/// The visual mesh goes in the "visual" slot (can be a child of this object).
/// Forces are applied by VehicleController calling Step() from FixedUpdate.
/// </summary>
public class RaycastWheel : MonoBehaviour
{
    [Header("Geometry")]
    public float radius = 0.35f;
    [Tooltip("Distance from mount to wheel centre at full droop (m)")]
    public float suspensionLength = 0.4f;
    public Transform visual;
    public LayerMask groundMask = ~0;      // exclude the vehicle's own layer if it has one

    [Header("Suspension")]
    public float spring = 30000f;          // N/m  (~ mass/4 * g / desired sag)
    public float damper = 3000f;           // N*s/m

    [Header("Tire")]
    public float friction = 1.2f;          // peak friction coefficient (sideways grip)
    [Tooltip("Grip multiplier vs slip (0 = rolling, 1 = pure sideways slide)")]
    public AnimationCurve gripBySlip = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.7f));
    public float rollingResistance = 0.015f;
    [Tooltip("Off = arcade: drive/brake force ignores tire grip. On = realistic friction circle.")]
    public bool limitDriveByGrip = false;

    [Header("Role")]
    public bool steered;
    public bool driven;

    [HideInInspector] public float gripScale = 1f;   // set by VehicleController each frame

    public bool IsGrounded { get; private set; }
    public float Load { get; private set; }   // current suspension force (N)

    private static readonly RaycastHit[] hits = new RaycastHit[8];
    private Quaternion baseLocalRot;
    private float length;                      // current wheel-centre distance from mount
    private float spin;                        // degrees

    private void Awake()
    {
        baseLocalRot = transform.localRotation;
        length = suspensionLength;
    }

    /// <param name="comBlend">0 = tire forces act at the wheel (realistic squat/dive),
    /// 1 = at centre-of-mass height (no nose-lift or roll from drive/cornering forces).</param>
    public void Step(Rigidbody rb, float driveForce, float brakeForce, float steerAngle, float comBlend)
    {
        if (steered) transform.localRotation = baseLocalRot * Quaternion.Euler(0f, steerAngle, 0f);

        float dt = Time.fixedDeltaTime;
        Vector3 up = transform.up;
        Vector3 origin = transform.position;

        IsGrounded = Cast(rb, origin, -up, suspensionLength + radius, out RaycastHit hit);

        if (!IsGrounded)
        {
            length = Mathf.MoveTowards(length, suspensionLength, 2f * dt);
            Load = 0f;
            return;
        }

        length = Mathf.Clamp(hit.distance - radius, 0f, suspensionLength);
        Vector3 wheelPos = origin - up * length;

        // --- Suspension ---
        float compression = suspensionLength - length;
        float suspVel = Vector3.Dot(rb.GetPointVelocity(origin), up);
        float normalForce = Mathf.Max(0f, spring * compression - damper * suspVel);
        Load = normalForce;

        // --- Tire frame (projected on the ground) ---
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized;
        Vector3 right = Vector3.Cross(hit.normal, fwd);

        Vector3 v = rb.GetPointVelocity(hit.point);
        float vLong = Vector3.Dot(v, fwd);
        float vLat = Vector3.Dot(v, right);
        spin += vLong / radius * Mathf.Rad2Deg * dt;

        float loadMass = normalForce / Physics.gravity.magnitude;   // mass this wheel is carrying

        // Lateral: cancel sideways velocity, scaled down as the tire starts to slide
        float slip = Mathf.Abs(vLat) / Mathf.Max(Mathf.Abs(vLong), Mathf.Abs(vLat), 1f);
        float fy = -vLat * loadMass / dt * gripBySlip.Evaluate(slip);

        // Longitudinal: drive + brake + rolling resistance
        float brake = -Mathf.Sign(vLong) * Mathf.Min(brakeForce, Mathf.Abs(vLong) * loadMass / dt);
        float rolling = -Mathf.Clamp(vLong, -1f, 1f) * rollingResistance * normalForce;
        float fx = driveForce + brake + rolling;

        // Grip limit
        float maxF = friction * normalForce * gripScale;
        float fxOut = fx, fyOut = fy;
        if (limitDriveByGrip)
        {
            Vector2 f = new Vector2(fx, fy);
            if (f.magnitude > maxF) f *= maxF / f.magnitude;
            fxOut = f.x;
            fyOut = f.y;
        }
        else
        {
            fyOut = Mathf.Clamp(fy, -maxF, maxF);   // only sideways grip is limited
        }

        // Suspension acts at the wheel; tire forces optionally act at centre-of-mass height
        Vector3 tirePoint = Vector3.Lerp(wheelPos,
                                         wheelPos + up * Vector3.Dot(rb.worldCenterOfMass - wheelPos, up), comBlend);

        rb.AddForceAtPosition(up * normalForce, wheelPos);
        rb.AddForceAtPosition(fwd * fxOut + right * fyOut, tirePoint);
    }

    private bool Cast(Rigidbody rb, Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(origin, dir, hits, dist, groundMask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            if (hits[i].rigidbody == rb) continue;      // ignore our own colliders
            if (hits[i].distance < nearest) { nearest = hits[i].distance; best = hits[i]; found = true; }
        }
        return found;
    }

    private void LateUpdate()
    {
        if (!visual) return;
        visual.localPosition = Vector3.down * length;
        visual.localRotation = Quaternion.Euler(spin, 0f, 0f);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 bottom = transform.position - transform.up * suspensionLength;
        Gizmos.DrawLine(transform.position, bottom);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(bottom, radius);
    }
}
