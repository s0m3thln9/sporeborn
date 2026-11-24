using UnityEngine;

namespace Sporeborn.Runtime
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class MushroomInteraction : MonoBehaviour
    {
        public const float RangeMeters = 1.5f;
        public const float EyeHeightMeters = 1.4f;
        private const int RoomLayerMask = 1;

        [SerializeField] private Camera _playerCamera;
        [SerializeField] private Transform _hand;
        [SerializeField] private ShippingBox _shippingBox;
        [SerializeField] private string _pickupPrompt = "E   Поднять гриб";
        [SerializeField] private string _placePrompt = "E   Положить в коробку";
        [SerializeField] private string _dropPrompt = "E   Отпустить гриб";
        [SerializeField] private string _resumePrompt = "Щелчок   Вернуться к игре";

        private LooseMushroom _held;
        private LooseMushroom _aimed;
        private CharacterController _player;
        private string _prompt;
        private Vector3 _handPosition;
        private GUIStyle _promptStyle;

        private void Awake()
        {
            if (_playerCamera == null || _hand == null || _hand.parent == null || _shippingBox == null)
            {
                Debug.LogError("Assign the interaction camera, hand, and shipping box.", this);
                enabled = false;
                return;
            }

            _player = GetComponent<CharacterController>();
            _handPosition = _hand.localPosition;
        }

        public void ClearAim()
        {
            SetAimed(null);
            _prompt = null;
        }

        public void UpdateAim(bool interact)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            var aim = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);
            bool hitSomething = Physics.Raycast(aim, out RaycastHit hit, RangeMeters, RoomLayerMask, QueryTriggerInteraction.Ignore);

            if (_held == null)
            {
                AimAtMushroom(hitSomething ? hit.collider : null, interact);
                return;
            }

            UpdateHand(aim.origin, _held.Radius);
            bool canPlace = _shippingBox.CanReceive(aim, RangeMeters, hitSomething ? hit.distance : RangeMeters);
            _prompt = canPlace ? _placePrompt : _dropPrompt;

            if (!interact)
            {
                return;
            }

            if (!canPlace || !_shippingBox.TryReceive(_held))
            {
                _held.Drop(_player);
            }

            _held = null;
            _prompt = null;
        }

        private void AimAtMushroom(Collider target, bool interact)
        {
            LooseMushroom mushroom = null;

            if (target != null && target.TryGetComponent(out LooseMushroom candidate) && candidate.CanPickUp)
            {
                mushroom = candidate;
            }

            SetAimed(mushroom);
            _prompt = mushroom != null ? _pickupPrompt : null;

            if (!interact || mushroom == null)
            {
                return;
            }

            if (mushroom.TryPickUp(_hand))
            {
                _held = mushroom;
                UpdateHand(_playerCamera.transform.position, mushroom.Radius);
                ClearAim();
            }
        }

        private void SetAimed(LooseMushroom mushroom)
        {
            if (_aimed == mushroom)
            {
                return;
            }

            if (_aimed != null)
            {
                _aimed.SetHighlighted(false);
            }

            _aimed = mushroom;

            if (_aimed != null)
            {
                _aimed.SetHighlighted(true);
            }
        }

        private void UpdateHand(Vector3 cameraPosition, float radius)
        {
            Vector3 offset = _hand.parent.TransformVector(_handPosition);
            float distance = offset.magnitude;
            Vector3 direction = offset.normalized;

            if (Physics.SphereCast(cameraPosition, radius + 0.015f, direction, out RaycastHit hit,
                distance, RoomLayerMask, QueryTriggerInteraction.Ignore))
            {
                distance = hit.distance;
            }

            _hand.position = cameraPosition + direction * distance;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            _promptStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 17
            };

            float centerX = Screen.width * 0.5f;
            float centerY = Screen.height * 0.5f;
            bool isPlaying = Cursor.lockState == CursorLockMode.Locked;

            if (isPlaying)
            {
                GUI.DrawTexture(new Rect(centerX - 2f, centerY - 2f, 4f, 4f), Texture2D.whiteTexture);
            }

            string prompt = isPlaying ? _prompt : _resumePrompt;

            if (!string.IsNullOrEmpty(prompt))
            {
                var label = new Rect(centerX - 140f, centerY + 22f, 280f, 34f);
                GUI.Box(label, GUIContent.none);
                GUI.Label(label, prompt, _promptStyle);
            }
        }
    }
}
