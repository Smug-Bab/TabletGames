using System;
using UnityEngine;

/// <summary>
/// Capsule pedestrian: roams around its town (walk to a random spot, pause, repeat)
/// and gets knocked down when the vehicle hits it. Created and configured by NpcGenerator.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class NpcController : MonoBehaviour
{
    public static int RunOverCount { get; private set; }
    public static event Action<NpcController> OnRunOver;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        RunOverCount = 0;
        OnRunOver = null;
    }

    private const float Acceleration = 12f;
    private const float TurnSpeed = 10f;

    private Rigidbody rb;
    private Renderer rend;

    // Roaming
    private Vector2 home;
    private float roamRadius;
    private float walkSpeed;
    private Vector2 pauseTime;
    private bool waitingForGround = true;   // frozen until terrain collision exists underneath
    private bool resting = true;
    private float restTimer;
    private Vector3 target;
    private float stuckTimer;
    private Vector3 lastPos;

    // Getting hit
    private float hitSpeedThreshold;
    private float removeDelay;
    private Color hitColor;
    private bool dead;
    private float deadTime;
    private Vector3 baseScale;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rend = GetComponent<Renderer>();

        rb.mass = 70f;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.isKinematic = true;
    }

    public void Init(Vector2 homeXZ, float radius, float speed, Vector2 pause,
                     float hitSpeed, float vanishDelay, Color tint)
    {
        home = homeXZ;
        roamRadius = radius;
        walkSpeed = speed;
        pauseTime = pause;
        hitSpeedThreshold = hitSpeed;
        removeDelay = vanishDelay;
        hitColor = tint;

        restTimer = UnityEngine.Random.Range(0f, pause.y);
        lastPos = transform.position;
    }

    // ------------------------------------------------------------------
    // Roaming
    // ------------------------------------------------------------------

    private void FixedUpdate()
    {
        if (dead) return;

        // Don't fall through the world if the ground hasn't been built yet
        // (the ray starts inside our own collider, which Unity ignores)
        if (waitingForGround)
        {
            if (Physics.Raycast(transform.position, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                waitingForGround = false;
                rb.isKinematic = false;
            }
            return;
        }

        float dt = Time.fixedDeltaTime;

        if (resting)
        {
            Move(Vector3.zero, dt);
            restTimer -= dt;
            if (restTimer <= 0f) PickTarget();
            return;
        }

        Vector3 toTarget = target - transform.position;
        toTarget.y = 0f;

        if (toTarget.magnitude < 0.8f)
        {
            Rest();
            return;
        }

        Move(toTarget.normalized, dt);

        // Blocked by a wall? Pick somewhere else.
        stuckTimer += dt;
        if (stuckTimer > 1f)
        {
            if ((transform.position - lastPos).sqrMagnitude < 0.09f) PickTarget();
            lastPos = transform.position;
            stuckTimer = 0f;
        }
    }

    private void Move(Vector3 direction, float dt)
    {
        Vector3 vel = rb.linearVelocity;   // use rb.velocity on Unity 2022 and older
        Vector3 horizontal = new Vector3(vel.x, 0f, vel.z);
        horizontal = Vector3.MoveTowards(horizontal, direction * walkSpeed, Acceleration * dt);
        rb.linearVelocity = new Vector3(horizontal.x, vel.y, horizontal.z);

        if (direction.sqrMagnitude > 0.01f)
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(direction), TurnSpeed * dt));
    }

    private void Rest()
    {
        resting = true;
        restTimer = UnityEngine.Random.Range(pauseTime.x, pauseTime.y);
    }

    private void PickTarget()
    {
        for (int i = 0; i < 8; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * roamRadius;
            Vector3 from = new Vector3(home.x + offset.x, transform.position.y + 60f, home.y + offset.y);

            // Skip spots on top of buildings (their colliders are boxes)
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 120f, ~0, QueryTriggerInteraction.Ignore)
                && !(hit.collider is BoxCollider))
            {
                target = hit.point;
                resting = false;
                stuckTimer = 0f;
                lastPos = transform.position;
                return;
            }
        }

        Rest();
    }

    // ------------------------------------------------------------------
    // Getting hit
    // ------------------------------------------------------------------

    private void OnCollisionEnter(Collision c)
    {
        if (dead || waitingForGround) return;
        if (c.relativeVelocity.magnitude < hitSpeedThreshold) return;
        if (c.collider.GetComponentInParent<VehicleController>() == null) return;

        KnockDown();
    }

    private void KnockDown()
    {
        dead = true;
        deadTime = Time.time;
        baseScale = transform.localScale;

        // Let physics tumble the body
        rb.constraints = RigidbodyConstraints.None;
        rb.AddTorque(UnityEngine.Random.onUnitSphere * 4f, ForceMode.VelocityChange);

        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", hitColor);        // Built-in pipeline
        block.SetColor("_BaseColor", hitColor);    // URP / HDRP
        rend.SetPropertyBlock(block);

        RunOverCount++;
        OnRunOver?.Invoke(this);
    }

    private void Update()
    {
        if (transform.position.y < -200f) { Destroy(gameObject); return; }
        if (!dead) return;

        // Stay a while, then shrink away
        float t = Time.time - deadTime;
        if (t > removeDelay)
        {
            float k = Mathf.Clamp01(t - removeDelay);
            transform.localScale = baseScale * (1f - k);
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
