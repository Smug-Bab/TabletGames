using UnityEngine;
using UnityEngine.InputSystem;

public class carmovement : MonoBehaviour
{
    [SerializeField] private WheelCollider[] FrontWheel;
    [SerializeField] private WheelCollider[] RearWheels;
    [SerializeField] private Rigidbody carRigidbody;
    [SerializeField] private InputAction controls;

    [SerializeField] private float torque = 10f;
    [SerializeField] private float maxSteerAngle = 30f;
    [SerializeField] private float steeringRate = 5f;
    [SerializeField] private float lateralDamping = 3f;
    [SerializeField] private float airDrag = 0.15f;

    private void OnEnable() => controls.Enable();
    private void OnDisable() => controls.Disable();

    private void FixedUpdate()
    {
        var input = controls.ReadValue<Vector2>().normalized;

        foreach (var wheel in FrontWheel)
        {
            wheel.steerAngle = Mathf.Lerp(wheel.steerAngle, input.x * maxSteerAngle, Time.fixedDeltaTime * steeringRate);
        }

        foreach (var wheel in RearWheels)
        {
            wheel.motorTorque = input.y * torque;
        }

        bool grounded = false;
        foreach (var wheel in FrontWheel)
        {
            if (wheel.isGrounded) grounded = true;
        }
        foreach (var wheel in RearWheels)
        {
            if (wheel.isGrounded) grounded = true;
        }

        if (grounded)
        {
            var localVel = transform.InverseTransformDirection(carRigidbody.linearVelocity);
            localVel.x = Mathf.Lerp(localVel.x, 0f, Time.fixedDeltaTime * lateralDamping);
            carRigidbody.linearVelocity = transform.TransformDirection(localVel);
        }
        else
        {
            var localVel = transform.InverseTransformDirection(carRigidbody.linearVelocity);
            localVel.x *= 1f - airDrag * Time.fixedDeltaTime;
            localVel.z *= 1f - airDrag * Time.fixedDeltaTime;
            carRigidbody.linearVelocity = transform.TransformDirection(localVel);

            var roll = input.x * Mathf.Clamp01(carRigidbody.linearVelocity.magnitude) * 2f;
            carRigidbody.angularVelocity = new Vector3(0f, carRigidbody.angularVelocity.y, Mathf.Lerp(carRigidbody.angularVelocity.z, roll, Time.fixedDeltaTime * 2f));
        }
    }
}

