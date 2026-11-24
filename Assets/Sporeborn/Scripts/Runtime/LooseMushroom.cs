using UnityEngine;

namespace Sporeborn.Runtime
{
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class LooseMushroom : MonoBehaviour
    {
        private const int RoomLayerMask = 1;
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly Color HighlightColor = new Color(1f, 0.9f, 0.76f);

        [SerializeField] private Renderer _cap;
        [SerializeField] private BoxCollider _roomBounds;
        [SerializeField] private Transform _worktableRecoveryPoint;

        private Rigidbody _body;
        private SphereCollider _collider;
        private MaterialPropertyBlock _highlight;
        private State _state;
        private Vector3 _lastReachablePosition;
        private Quaternion _lastReachableRotation;

        public bool IsHeld => _state == State.Held;
        public bool CanPickUp => isActiveAndEnabled && (_state == State.Available || _state == State.Falling);
        public float Radius => _collider.radius;

        private enum State
        {
            Available,
            Held,
            Falling,
            Stored
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _collider = GetComponent<SphereCollider>();
            _highlight = new MaterialPropertyBlock();
            _highlight.SetColor(ColorProperty, HighlightColor);

            if (_cap == null || _roomBounds == null || _worktableRecoveryPoint == null)
            {
                Debug.LogError("Assign the mushroom cap, room bounds, and worktable recovery point.", this);
                enabled = false;
                return;
            }

            _lastReachablePosition = _worktableRecoveryPoint.position;
            _lastReachableRotation = _worktableRecoveryPoint.rotation;
        }

        private void FixedUpdate()
        {
            if (_state != State.Falling)
            {
                return;
            }

            if (!_roomBounds.bounds.Contains(_body.position))
            {
                Restore();
                return;
            }

            if (!_body.IsSleeping())
            {
                return;
            }

            _body.isKinematic = true;
            _state = State.Available;

            if (!HasPickupPosition())
            {
                Restore();
            }
        }

        public void SetHighlighted(bool highlighted)
        {
            _cap.SetPropertyBlock(highlighted ? _highlight : null);
        }

        public bool TryPickUp(Transform hand)
        {
            if (!CanPickUp)
            {
                return false;
            }

            if (_state == State.Available)
            {
                _lastReachablePosition = transform.position;
                _lastReachableRotation = transform.rotation;
            }

            _state = State.Held;
            _body.isKinematic = true;
            _collider.enabled = false;
            AttachTo(hand, Vector3.zero);
            return true;
        }

        public void Drop(CharacterController player)
        {
            transform.SetParent(null, true);
            _state = State.Falling;
            _collider.enabled = true;
            Physics.IgnoreCollision(_collider, player);
            _body.isKinematic = false;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.WakeUp();
        }

        public void Store(Transform box, Vector3 position)
        {
            _state = State.Stored;
            AttachTo(box, position);
        }

        private void AttachTo(Transform parent, Vector3 position)
        {
            transform.SetParent(parent, false);
            transform.SetLocalPositionAndRotation(position, Quaternion.identity);
        }

        private void Restore()
        {
            _body.isKinematic = true;
            _body.position = _lastReachablePosition;
            _body.rotation = _lastReachableRotation;
            _state = State.Available;
        }

        private bool HasPickupPosition()
        {
            Vector3 target = _body.position;
            Bounds room = _roomBounds.bounds;

            for (int direction = 0; direction < 8; direction++)
            {
                float angle = direction * 45f * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                if (CanReachFrom(target, room, offset * 0.45f) || CanReachFrom(target, room, offset * 0.95f))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanReachFrom(Vector3 target, Bounds room, Vector3 offset)
        {
            var feet = new Vector3(target.x + offset.x, room.min.y + 0.32f, target.z + offset.z);
            Vector3 eyes = feet + Vector3.up * (MushroomInteraction.EyeHeightMeters - 0.32f);

            if (!room.Contains(eyes) || Physics.CheckCapsule(feet, eyes, 0.29f, RoomLayerMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            Vector3 toMushroom = target - eyes;
            return Physics.Raycast(eyes, toMushroom.normalized, out RaycastHit hit,
                MushroomInteraction.RangeMeters, RoomLayerMask, QueryTriggerInteraction.Ignore) && hit.collider == _collider;
        }
    }
}
