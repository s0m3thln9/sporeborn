using Sporeborn.Persistence;
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

        internal float Sensitivity
        {
            get => _lookSensitivity;
            set => _lookSensitivity = value;
        }

        internal void CapturePose(WorkshopSave save)
        {
            save.playerPosition = transform.position;
            save.yaw = transform.eulerAngles.y;
            save.pitch = _pitch;
        }

        internal void RestorePose(WorkshopSave save)
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(save.playerPosition, Quaternion.Euler(0f, save.yaw, 0f));
            _controller.enabled = true;
            _verticalSpeed = 0f;
            _pitch = save.pitch;
            _playerCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

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
            WorkshopSession session = _interaction.Session;
            if (session == null || !session.IsPlaying)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                session.Pause();
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

            float mouseX = Input.GetAxisRaw("Mouse X");
            float mouseY = Input.GetAxisRaw("Mouse Y");
            transform.Rotate(0f, mouseX * _lookSensitivity, 0f);
            _pitch = Mathf.Clamp(_pitch - mouseY * _lookSensitivity, -85f, 85f);
            _playerCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            Vector2 input = new Vector2(
                KeyValue(KeyCode.D) - KeyValue(KeyCode.A),
                KeyValue(KeyCode.W) - KeyValue(KeyCode.S));
            input = Vector2.ClampMagnitude(input, 1f);
            Vector3 velocity = (transform.right * input.x + transform.forward * input.y) * (_walkingSpeed * _interaction.WalkingMultiplier);
            _verticalSpeed = _controller.isGrounded ? -2f : _verticalSpeed + Physics.gravity.y * Time.deltaTime;
            velocity.y = _verticalSpeed;
            _controller.Move(velocity * Time.deltaTime);

            float mixingSeconds = mouseX != 0f || mouseY != 0f ? Time.deltaTime : 0f;
            bool forceDrop = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            _interaction.UpdateAim(Input.GetKeyDown(KeyCode.E), Input.GetMouseButtonDown(0),
                Input.GetKeyDown(KeyCode.G), mixingSeconds, forceDrop);
        }

        private static int KeyValue(KeyCode key)
        {
            return Input.GetKey(key) ? 1 : 0;
        }
    }
}
