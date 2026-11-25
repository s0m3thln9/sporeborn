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

        internal void RestorePose(WorkshopSave save, Bounds room)
        {
            _controller.enabled = false;
            Vector3 position = FindFreePosition(save.playerPosition, room);
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, save.yaw, 0f));
            _controller.enabled = true;
            _verticalSpeed = 0f;
            _pitch = save.pitch;
            _playerCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private Vector3 FindFreePosition(Vector3 saved, Bounds room)
        {
            if (CanStand(saved, room))
            {
                return saved;
            }

            for (int ring = 1; ring <= 4; ring++)
            {
                for (int direction = 0; direction < 8; direction++)
                {
                    float radians = direction * Mathf.PI / 4f;
                    var offset = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * (ring * 0.18f);
                    Vector3 candidate = saved + offset;
                    if (CanStand(candidate, room))
                    {
                        return candidate;
                    }
                }
            }

            return transform.position;
        }

        private bool CanStand(Vector3 position, Bounds room)
        {
            Vector3 middle = position + _controller.center;
            float radius = _controller.radius;
            Vector3 offset = Vector3.up * Mathf.Max(0f, _controller.height * 0.5f - radius);
            return room.Contains(middle) && !Physics.CheckCapsule(middle - offset, middle + offset,
                radius - 0.01f, 1, QueryTriggerInteraction.Ignore);
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
            _interaction.UpdateAim(Input.GetKeyDown(KeyCode.E), Input.GetMouseButtonDown(0),
                Input.GetKeyDown(KeyCode.G), mixingSeconds);
        }

        private static int KeyValue(KeyCode key)
        {
            return Input.GetKey(key) ? 1 : 0;
        }
    }
}
