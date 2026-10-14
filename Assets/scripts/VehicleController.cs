using UnityEngine;
using UnityEngine.InputSystem;

public class VehicleController : MonoBehaviour
{
    [Header("Wheel Colliders")]
    public WheelCollider frontLeftCollider;
    public WheelCollider frontRightCollider;
    public WheelCollider rearLeftCollider;
    public WheelCollider rearRightCollider;

    [Header("Motor Settings")]
    public float motorForce = 1200f;
    public float steeringAngle = 30f;
    public float brakeForce = 3000f;

    [Header("New Input System")]
    public InputAction moveAction = new InputAction("Move", binding: "<Gamepad>/leftStick");
    public InputAction brakeAction = new InputAction("Brake", binding: "<Gamepad>/buttonSouth");

    private void OnEnable()
    {
        moveAction.Enable();
        brakeAction.Enable();
    }

    private void OnDisable()
    {
        moveAction.Disable();
        brakeAction.Disable();
    }

    private void FixedUpdate()
    {
        // 1. Read standard input (Keyboard / Gamepad)
        Vector2 moveInput = moveAction.ReadValue<Vector2>();
        float steerInput = moveInput.x;
        float throttleInput = moveInput.y;

        // 2. Override or combine with Mobile D-Pad inputs if active
        if (MobileDpad.horizontalInput != 0f) steerInput = MobileDpad.horizontalInput;
        if (MobileDpad.verticalInput != 0f) throttleInput = MobileDpad.verticalInput;

        bool isBraking = brakeAction.IsPressed();

        // 3. Apply physics forces
        float currentMotorForce = motorForce * throttleInput;
        float currentBrakeForce = isBraking ? brakeForce : 0f;

        frontLeftCollider.motorTorque = currentMotorForce;
        frontRightCollider.motorTorque = currentMotorForce;

        frontLeftCollider.brakeTorque = currentBrakeForce;
        frontRightCollider.brakeTorque = currentBrakeForce;
        rearLeftCollider.brakeTorque = currentBrakeForce;
        rearRightCollider.brakeTorque = currentBrakeForce;

        float currentSteeringAngle = steeringAngle * steerInput;
        frontLeftCollider.steerAngle = currentSteeringAngle;
        frontRightCollider.steerAngle = currentSteeringAngle;

        // Update visual wheels
        UpdateWheelVisual(frontLeftCollider);
        UpdateWheelVisual(frontRightCollider);
        UpdateWheelVisual(rearLeftCollider);
        UpdateWheelVisual(rearRightCollider);
    }

    void UpdateWheelVisual(WheelCollider col)
    {
        if (col.transform.childCount > 0)
        {
            Transform visualWheel = col.transform.GetChild(0);
            col.GetWorldPose(out Vector3 pos, out Quaternion rot);
            visualWheel.position = pos;
            visualWheel.rotation = rot;
        }
    }
}
