using UnityEngine;

namespace Sporeborn.Runtime
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonPlayer : MonoBehaviour
    {
        [SerializeField] private Camera _playerCamera;
        [SerializeField] private MushroomInteraction _interaction;
        [SerializeField] private float _walkingSpeed = 3f;
        [SerializeField] private float _lookSensitivity = 2f;

        private CharacterController _controller;
        private float _pitch;
        private float _verticalSpeed;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            if (_playerCamera == null || _interaction == null)
            {
                Debug.LogError("Assign the player camera and mushroom interaction.", this);
                enabled = false;
            }
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _interaction.ClearAim();
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                _interaction.ClearAim();

                if (Input.GetMouseButtonDown(0))
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }

                return;
            }

            transform.Rotate(0f, Input.GetAxisRaw("Mouse X") * _lookSensitivity, 0f);
            _pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * _lookSensitivity, -85f, 85f);
            _playerCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            Vector2 input = new Vector2(
                KeyValue(KeyCode.D) - KeyValue(KeyCode.A),
                KeyValue(KeyCode.W) - KeyValue(KeyCode.S));
            input = Vector2.ClampMagnitude(input, 1f);
            Vector3 velocity = (transform.right * input.x + transform.forward * input.y) * _walkingSpeed;
            _verticalSpeed = _controller.isGrounded ? -2f : _verticalSpeed + Physics.gravity.y * Time.deltaTime;
            velocity.y = _verticalSpeed;
            _controller.Move(velocity * Time.deltaTime);

            _interaction.UpdateAim(Input.GetKeyDown(KeyCode.E));
        }

        private static int KeyValue(KeyCode key)
        {
            return Input.GetKey(key) ? 1 : 0;
        }
    }
}
