using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class VehicleController : MonoBehaviour
{
    [Header("Wheels")]
    public RaycastWheel[] wheels;           // assign all four (set Steered / Driven on each)

    [Header("Motor")]
    public float motorForce = 1500f;        // N per driven wheel
    public float brakeForce = 3000f;        // N per wheel
    public float dragCoefficient = 0f;      // 0 = no top speed; try 0.05 for a soft cap

    [Header("Speed-Based Handling (X axis = speed in m/s)")]
    [Tooltip("Max front wheel angle in degrees")]
    public AnimationCurve steerAngleBySpeed = new AnimationCurve(
        new Keyframe(0f, 35f), new Keyframe(10f, 25f), new Keyframe(25f, 12f),
                                                                 new Keyframe(50f, 6f), new Keyframe(100f, 3f));
    [Tooltip("How fast steering input ramps toward the stick/key value (per second)")]
    public AnimationCurve steerRateBySpeed = new AnimationCurve(
        new Keyframe(0f, 8f), new Keyframe(30f, 5f), new Keyframe(80f, 3f));
    [Tooltip("Multiplier on sideways tire grip")]
    public AnimationCurve gripBySpeed = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(20f, 1.5f), new Keyframe(60f, 3f), new Keyframe(120f, 5f));
    [Tooltip("How strongly the car is pulled toward the yaw rate your steering asks for (1/s)")]
    public AnimationCurve yawAssistBySpeed = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(5f, 1f), new Keyframe(30f, 4f), new Keyframe(80f, 6f));
    public float maxYawRate = 2.5f;         // rad/s cap on the assisted yaw target
    public float defaultWheelbase = 2.5f;   // used if wheels can't tell front from rear

    [Header("Stability")]
    public float downforce = 50f;
    [Range(0f, 1f)]
    [Tooltip("1 = acceleration/cornering can't lift the nose or roll the car; 0 = fully realistic")]
    public float tireForceAtCom = 0.8f;
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.4f, 0f);

    [Header("Auto Flip")]
    [Tooltip("Automatically turns the car back onto its wheels when it's stuck upside down or on its side.")]
    public bool autoFlip = true;
    [Tooltip("Seconds the car must be stuck overturned and nearly stationary before it flips back.")]
    public float flipDelay = 2f;
    [Range(-1f, 1f), Tooltip("How upright the car must be to count as OK (1 = level, 0 = on its side, -1 = upside down).")]
    public float flipUprightThreshold = 0.4f;
    [Tooltip("Only flips back when slower than this (m/s), so it never interrupts a tumble.")]
    public float flipMaxSpeed = 3f;
    [Tooltip("How far the car is lifted while it rights itself (m).")]
    public float flipLift = 1.5f;
    public float flipDuration = 0.6f;

    [Header("Engine Sound")]
    [Tooltip("A loopable engine sound. Leave empty for no engine audio.")]
    public AudioClip engineClip;
    [Tooltip("Speed (m/s) at which each gear tops out, in ascending order. The engine pitch climbs through each gear, then drops on the shift.")]
    public float[] gearTopSpeeds = { 15f, 30f, 50f, 80f, 120f };
    public float idlePitch = 0.7f;
    public float maxPitch = 1.8f;
    [Range(0f, 1f)] public float idleVolume = 0.35f;
    [Range(0f, 1f)] public float loadVolume = 0.9f;
    [Tooltip("How quickly the engine revs follow the target (higher = snappier)")]
    public float rpmResponse = 6f;

    [Header("Radio")]
    [Tooltip("Songs for the radio. Switching it on picks a random one; when a song ends the next in order starts, looping back to the first after the last.")]
    public AudioClip[] radioTracks;
    public bool radioOnAtStart = false;
    [Tooltip("Join each track partway through, like tuning into a real radio.")]
    public bool randomStartTime = true;
    [Range(0f, 1f)] public float radioVolume = 0.6f;
    public bool showRadioLabel = true;
    public float radioLabelSeconds = 3f;

    [Header("Skid & Impact Sounds")]
    [Tooltip("A loopable tire screech. Leave empty for no skid audio.")]
    public AudioClip skidClip;
    [Tooltip("Cornering strength (speed x turn rate, in m/s^2) where the screech starts to fade in.")]
    public float skidStartCornering = 10f;
    [Tooltip("Cornering strength where the screech reaches full volume. Lower = easier to screech.")]
    public float skidFullCornering = 22f;
    [Range(0f, 1f)] public float skidVolume = 0.8f;
    [Tooltip("Crash sounds for hitting an NPC, building or tree. One is picked at random per impact. Leave empty for no impact audio.")]
    public AudioClip[] hitClips;
    [Tooltip("Impacts slower than this (m/s) make no sound.")]
    public float hitMinSpeed = 3f;
    [Tooltip("Impact speed (m/s) that plays the hit at full volume.")]
    public float hitFullSpeed = 25f;
    [Range(0f, 1f)] public float hitVolume = 1f;

    [Header("Input (rebind in inspector)")]
    public InputAction moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
    public InputAction radioToggleAction = new InputAction("RadioToggle", InputActionType.Button);

    private Rigidbody rb;
    private float steer;
    private float wheelbase;

    // Auto flip
    private bool flipping;
    private float flipTimer;
    private float flipProgress;
    private Vector3 flipStartPos;
    private Quaternion flipStartRot;
    private Quaternion flipTargetRot;
    private float throttle;                 // latest throttle input, shared with the audio code

    private AudioSource engineSource;
    private AudioSource radioSource;
    private AudioSource skidSource;
    private AudioSource hitSource;
    private float lastHitTime;
    private float rpm;                      // smoothed 0..1 engine revs
    private bool radioOn;
    private int trackIndex = -1;           // -1 = nothing played yet
    private string radioLabel = "";
    private float radioLabelHideTime;
    private GUIStyle labelStyle;

    private void Reset() => AddDefaultBindings();

    private void AddDefaultBindings()
    {
        if (moveAction.bindings.Count == 0)
        {
            moveAction.AddCompositeBinding("2DVector(mode=1)")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        }
        if (radioToggleAction.bindings.Count == 0)   radioToggleAction.AddBinding("<Keyboard>/r");
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass += centerOfMassOffset;
        wheelbase = ComputeWheelbase();
        AddDefaultBindings();

        engineSource = CreateSource("EngineAudio", engineClip, idleVolume);
        radioSource = CreateSource("RadioAudio", null, radioVolume);
        radioSource.loop = false;               // we advance tracks ourselves

        skidSource = CreateSource("SkidAudio", skidClip, 0f);
        hitSource = CreateSource("HitAudio", null, 1f);
        hitSource.loop = false;

        if (engineClip != null) engineSource.Play();
        if (skidClip != null) skidSource.Play();    // loops silently, volume follows the slip
    }

    private void Start()
    {
        if (radioOnAtStart) SetRadio(true);
    }

    private AudioSource CreateSource(string objectName, AudioClip clip, float volume)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;                  // 2D: the player always hears their own car
        src.volume = volume;
        return src;
    }

    private float ComputeWheelbase()
    {
        float frontZ = 0f, rearZ = 0f;
        int f = 0, r = 0;
        foreach (var w in wheels)
        {
            float z = transform.InverseTransformPoint(w.transform.position).z;
            if (w.steered) { frontZ += z; f++; }
            else           { rearZ += z;  r++; }
        }
        if (f == 0 || r == 0) return defaultWheelbase;
        float wb = frontZ / f - rearZ / r;
        return wb > 0.1f ? wb : defaultWheelbase;
    }

    private void OnEnable()
    {
        moveAction.Enable();
        radioToggleAction.Enable();
    }

    private void OnDisable()
    {
        moveAction.Disable();
        radioToggleAction.Disable();
    }

    private void FixedUpdate()
    {
        if (UpdateAutoFlip()) return;   // wheels and driving are paused while righting itself

        float dt = Time.fixedDeltaTime;
        Vector2 move = moveAction.ReadValue<Vector2>();
        float steerInput = move.x;
        float throttleInput = move.y;
        throttle = throttleInput;
        float speed = rb.linearVelocity.magnitude;   // use rb.velocity on Unity 2022 and older
        float fwdSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);

        // Input opposing current motion = brake; once nearly stopped it drives the other way
        bool opposing = throttleInput * fwdSpeed < -0.5f;

        float drive = opposing ? 0f : motorForce * throttleInput;
        float brake = opposing ? brakeForce * Mathf.Abs(throttleInput) : 0f;

        // Speed-sensitive steering: smaller angle and slower ramp as you go faster
        steer = Mathf.MoveTowards(steer, steerInput, steerRateBySpeed.Evaluate(speed) * dt);
        float angle = steerAngleBySpeed.Evaluate(speed) * steer;

        // Speed-sensitive sideways grip
        float gripScale = gripBySpeed.Evaluate(speed);

        foreach (var w in wheels)
        {
            w.gripScale = gripScale;
            w.Step(rb, w.driven ? drive : 0f, brake, angle, tireForceAtCom);
        }

        // Yaw assist: pull the car toward the turn rate the steering angle asks for
        // (bicycle model). Keeps the car pointing where you steer and kills random spins.
        float desiredYaw = Mathf.Clamp(
            fwdSpeed * Mathf.Tan(angle * Mathf.Deg2Rad) / wheelbase, -maxYawRate, maxYawRate);
        float yawRate = Vector3.Dot(rb.angularVelocity, transform.up);
        rb.AddTorque(transform.up * (desiredYaw - yawRate) * yawAssistBySpeed.Evaluate(speed),
                     ForceMode.Acceleration);

        // Downforce for grip at speed
        rb.AddForce(-transform.up * downforce * speed);

        // Optional air drag (0 = off)
        rb.AddForce(-rb.linearVelocity * speed * dragCoefficient);
    }

    private void Update()
    {
        UpdateEngineSound();
        UpdateSkidSound();
        UpdateRadio();
    }

    // ------------------------------------------------------------------
    // Auto flip
    // ------------------------------------------------------------------

    /// <summary>Detects an overturned car and smoothly rights it. Returns true while the flip is in progress.</summary>
    private bool UpdateAutoFlip()
    {
        float dt = Time.fixedDeltaTime;

        if (flipping)
        {
            flipProgress += dt / Mathf.Max(0.05f, flipDuration);
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(flipProgress));

            rb.MovePosition(Vector3.Lerp(flipStartPos, flipStartPos + Vector3.up * flipLift, t));
            rb.MoveRotation(Quaternion.Slerp(flipStartRot, flipTargetRot, t));
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            if (flipProgress >= 1f) flipping = false;
            return true;
        }

        if (!autoFlip) return false;

        bool overturned = Vector3.Dot(transform.up, Vector3.up) < flipUprightThreshold;
        if (overturned && rb.linearVelocity.magnitude < flipMaxSpeed) flipTimer += dt;
        else flipTimer = 0f;

        if (flipTimer >= flipDelay)
        {
            flipTimer = 0f;
            flipping = true;
            flipProgress = 0f;
            flipStartPos = rb.position;
            flipStartRot = rb.rotation;

            // Keep facing the same way, just level out
            Vector3 heading = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (heading.sqrMagnitude < 0.01f) heading = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            if (heading.sqrMagnitude < 0.01f) heading = Vector3.forward;
            flipTargetRot = Quaternion.LookRotation(heading.normalized, Vector3.up);
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Engine sound
    // ------------------------------------------------------------------

    private void UpdateEngineSound()
    {
        if (engineClip == null) return;

        float speed = rb.linearVelocity.magnitude;
        float load = Mathf.Abs(throttle);

        // Revving at a standstill when you press the throttle, otherwise follow the gear
        float targetRpm = Mathf.Max(GearRpm(speed), load * 0.3f);
        rpm = Mathf.Lerp(rpm, targetRpm, 1f - Mathf.Exp(-rpmResponse * Time.deltaTime));

        engineSource.pitch = Mathf.Lerp(idlePitch, maxPitch, rpm);
        engineSource.volume = Mathf.Lerp(idleVolume, loadVolume, Mathf.Max(load, rpm * 0.5f));
    }

    /// <summary>0..1 position within the current gear. Drops back to ~0 when the next gear starts.</summary>
    private float GearRpm(float speed)
    {
        if (gearTopSpeeds == null || gearTopSpeeds.Length == 0)
            return Mathf.Clamp01(speed / 60f);

        float lower = 0f;
        for (int i = 0; i < gearTopSpeeds.Length; i++)
        {
            float top = gearTopSpeeds[i];
            if (speed <= top || i == gearTopSpeeds.Length - 1)
                return Mathf.Clamp01(Mathf.InverseLerp(lower, top, speed));
            lower = top;
        }
        return 1f;
    }

    // ------------------------------------------------------------------
    // Skid & impact sounds
    // ------------------------------------------------------------------

    private void UpdateSkidSound()
    {
        if (skidClip == null) return;

        float speed = rb.linearVelocity.magnitude;

        bool grounded = false;
        foreach (var w in wheels)
        {
            if (w.IsGrounded) { grounded = true; break; }
        }

        // Sharp turn = high lateral acceleration (speed x how fast the car is rotating)
        float yawRate = Mathf.Abs(Vector3.Dot(rb.angularVelocity, transform.up));
        float cornering = yawRate * speed;
        float intensity = grounded ? Mathf.InverseLerp(skidStartCornering, skidFullCornering, cornering) : 0f;

        float target = intensity * skidVolume;
        skidSource.volume = Mathf.MoveTowards(skidSource.volume, target, 6f * Time.deltaTime);
        skidSource.pitch = Mathf.Lerp(0.9f, 1.15f, Mathf.Clamp01(speed / 40f));
    }

    private void OnCollisionEnter(Collision c)
    {
        if (hitClips == null || hitClips.Length == 0) return;
        if (Time.time - lastHitTime < 0.1f) return;    // one crash can report several contacts

        // Only NPCs and solid obstacles make a crash sound: buildings (box colliders)
        // and tree trunks (named "Trunk" by TreeGenerator). Ground and roads stay silent.
        bool hitNpc = c.collider.GetComponentInParent<NpcController>() != null;
        bool hitWall = c.collider is BoxCollider || c.collider.name == "Trunk";
        if (!hitNpc && !hitWall) return;

        float impact = c.relativeVelocity.magnitude;
        if (impact < hitMinSpeed) return;

        AudioClip clip = hitClips[Random.Range(0, hitClips.Length)];
        if (clip == null) return;

        lastHitTime = Time.time;
        hitSource.pitch = Random.Range(0.9f, 1.1f);
        hitSource.PlayOneShot(clip, hitVolume * Mathf.Lerp(0.3f, 1f, Mathf.InverseLerp(hitMinSpeed, hitFullSpeed, impact)));
    }

    // ------------------------------------------------------------------
    // Radio
    // ------------------------------------------------------------------

    private void UpdateRadio()
    {
        radioSource.volume = radioVolume;

        if (radioToggleAction.WasPressedThisFrame()) SetRadio(!radioOn);

        // Song finished: roll on to the next one (loops back to the first after the last)
        if (radioOn && radioSource.clip != null && !radioSource.isPlaying)
            PlayTrack(trackIndex + 1, false);
    }

    private void SetRadio(bool on)
    {
        if (on)
        {
            radioOn = true;
            PlayTrack(RandomTrack(), randomStartTime);   // a fresh random song every time it's switched on
        }
        else
        {
            radioOn = false;
            radioSource.Stop();
            ShowLabel("Radio off");
        }
    }

    /// <summary>Random non-empty slot other than the last song played (that one only if it's the sole choice).</summary>
    private int RandomTrack()
    {
        if (radioTracks == null || radioTracks.Length == 0) return -1;

        int candidates = 0;
        for (int i = 0; i < radioTracks.Length; i++)
        {
            if (radioTracks[i] != null && i != trackIndex) candidates++;
        }
        if (candidates == 0) return trackIndex;

        int pick = Random.Range(0, candidates);
        for (int i = 0; i < radioTracks.Length; i++)
        {
            if (radioTracks[i] == null || i == trackIndex) continue;
            if (pick == 0) return i;
            pick--;
        }
        return trackIndex;
    }

    private void PlayTrack(int startIndex, bool randomStart)
    {
        int index = FindValidTrack(startIndex);
        if (index < 0)
        {
            radioOn = false;                    // empty array or all slots unassigned
            radioSource.Stop();
            return;
        }

        trackIndex = index;
        AudioClip clip = radioTracks[index];
        radioSource.clip = clip;
        radioSource.Play();
        if (randomStart) radioSource.time = Random.Range(0f, clip.length * 0.7f);

        ShowLabel("Radio: " + clip.name);
    }

    /// <summary>First non-empty slot at or after 'from', wrapping around the array. -1 if none.</summary>
    private int FindValidTrack(int from)
    {
        if (radioTracks == null || radioTracks.Length == 0) return -1;

        int n = radioTracks.Length;
        for (int k = 0; k < n; k++)
        {
            int i = (((from + k) % n) + n) % n;
            if (radioTracks[i] != null) return i;
        }
        return -1;
    }

    private void ShowLabel(string text)
    {
        radioLabel = text;
        radioLabelHideTime = Time.time + radioLabelSeconds;
    }

    private void OnGUI()
    {
        if (!showRadioLabel || Time.time > radioLabelHideTime || string.IsNullOrEmpty(radioLabel)) return;

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            labelStyle.normal.textColor = Color.white;
        }
        GUI.Label(new Rect(24f, Screen.height - 56f, 700f, 36f), radioLabel, labelStyle);
    }
}
