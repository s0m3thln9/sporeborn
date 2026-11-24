using System.Collections;
using UnityEngine;

namespace Sporeborn.Runtime
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Carryable : MonoBehaviour
    {
        private const int RoomLayerMask = 1;
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly Color HighlightColor = new Color(1f, 0.9f, 0.76f);

        [SerializeField] private Renderer _highlightRenderer;
        [SerializeField] private BoxCollider _roomBounds;

        private Rigidbody _body;
        private Collider[] _colliders;
        private MaterialPropertyBlock _highlight;
        private MaterialPropertyBlock _originalAppearance;
        private State _state;
        private Vector3 _lastReachablePosition;
        private Quaternion _lastReachableRotation;

        public BoxCollider RoomBounds => _roomBounds;
        public bool IsHeld => _state == State.Held;
        public bool CanPickUp => isActiveAndEnabled && (_state == State.Available || _state == State.Falling);
        public float Radius { get; private set; } = 0.022f;
        public bool CanCut => _state == State.Rooted;
        public ItemKind Kind { get; private set; }
        public string ItemName => Kind switch
        {
            ItemKind.Knife => "нож",
            ItemKind.Box => "коробку",
            ItemKind.Label => "этикетку",
            _ => "гриб"
        };

        public enum ItemKind
        {
            Mushroom,
            Knife,
            Box,
            Label
        }

        private enum State
        {
            Available,
            Held,
            Falling,
            Stored,
            Rooted,
            BeingCut
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _highlight = new MaterialPropertyBlock();
            _highlight.SetColor(ColorProperty, HighlightColor);
            _colliders = GetComponentsInChildren<Collider>();
        }

        public void Configure(ItemKind kind, float radius, BoxCollider roomBounds, Vector3 recoveryPosition,
            Renderer highlightRenderer = null)
        {
            Kind = kind;
            Radius = radius;
            _roomBounds = roomBounds;
            _highlightRenderer = highlightRenderer != null ? highlightRenderer : _highlightRenderer;
            if (_highlightRenderer != null)
            {
                _originalAppearance = new MaterialPropertyBlock();
                _highlightRenderer.GetPropertyBlock(_originalAppearance);
            }

            _lastReachablePosition = recoveryPosition;
            _lastReachableRotation = Quaternion.identity;
        }

        public void SetRooted()
        {
            _state = State.Rooted;
        }

        public bool TryCut(Vector3 landingPosition)
        {
            if (!CanCut)
            {
                return false;
            }

            _state = State.BeingCut;
            SetColliders(false);
            StartCoroutine(MoveToPlatform(landingPosition));
            return true;
        }

        private IEnumerator MoveToPlatform(Vector3 landingPosition)
        {
            Vector3 start = transform.position;
            const float duration = 0.24f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float progress = elapsed / duration;
                transform.position = Vector3.Lerp(start, landingPosition, progress)
                    + Vector3.up * (0.08f * Mathf.Sin(progress * Mathf.PI));
                yield return null;
            }

            Place(landingPosition);
        }

        public void Place(Vector3 position)
        {
            transform.SetParent(null, true);
            _body.isKinematic = true;
            _body.position = position;
            _body.rotation = Quaternion.identity;
            _state = State.Available;
            SetColliders(true);
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
            if (_highlightRenderer != null)
            {
                _highlightRenderer.SetPropertyBlock(highlighted ? _highlight : _originalAppearance);
            }
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
            SetColliders(false);
            AttachTo(hand, Vector3.zero);
            return true;
        }

        public void Drop(CharacterController player)
        {
            transform.SetParent(null, true);
            _state = State.Falling;
            SetColliders(true);
            foreach (Collider itemCollider in _colliders)
            {
                Physics.IgnoreCollision(itemCollider, player);
            }
            _body.isKinematic = false;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.WakeUp();
        }

        public void Store(Transform box, Vector3 position)
        {
            _state = State.Stored;
            SetColliders(false);
            AttachTo(box, position);
        }

        private void SetColliders(bool enabled)
        {
            foreach (Collider itemCollider in _colliders)
            {
                itemCollider.enabled = enabled;
            }
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
            Vector3 target = _body.worldCenterOfMass;
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
                MushroomInteraction.RangeMeters, RoomLayerMask, QueryTriggerInteraction.Ignore) && hit.collider.attachedRigidbody == _body;
        }
    }
}
