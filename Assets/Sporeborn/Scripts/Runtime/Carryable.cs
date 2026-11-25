using System.Collections;
using Sporeborn.Persistence;
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
        public bool IsResting => _state == State.Available;
        public bool IsHeld => _state == State.Held;
        public bool CanPickUp => isActiveAndEnabled && !_covered && (_state == State.Available || _state == State.Falling);
        public float Radius { get; private set; } = 0.022f;
        public bool CanCut => NeedsCut && !_covered;
        internal bool NeedsCut => _state == State.Rooted;
        private bool _covered;
        private Vector3 _cutLanding;
        public string Id { get; private set; }
        public ItemKind Kind { get; private set; }
        public string ItemName => Kind switch
        {
            ItemKind.Knife => "нож",
            ItemKind.Box => "коробку",
            ItemKind.Label => "этикетку",
            ItemKind.Scoop => "совок",
            ItemKind.Cup => "кружку",
            ItemKind.Mixer => "мешалку",
            ItemKind.Bowl => "ёмкость",
            ItemKind.Tray => "лоток",
            ItemKind.Lid => "крышку",
            ItemKind.Inoculator => "мерку мицелия",
            _ => "гриб"
        };

        public enum ItemKind
        {
            Mushroom,
            Knife,
            Box,
            Label,
            Scoop,
            Cup,
            Mixer,
            Bowl,
            Tray,
            Lid,
            Inoculator
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
            Renderer highlightRenderer = null, string id = null)
        {
            Kind = kind;
            Id = id ?? kind.ToString();
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

        public void GrowOn(Transform tray, Vector3 position, bool ripe)
        {
            _state = State.Rooted;
            AttachTo(tray, position);
            transform.localScale = ripe ? Vector3.one : Vector3.one * 0.32f;
            SetCovered(!ripe);
        }

        public void SetCovered(bool covered)
        {
            _covered = covered;
            SetColliders(!covered && (_state == State.Rooted || _state == State.Available));
        }

        public bool TryCut(Vector3 landingPosition)
        {
            if (!CanCut)
            {
                return false;
            }

            _state = State.BeingCut;
            _cutLanding = landingPosition;
            SetColliders(false);
            StartCoroutine(MoveToPlatform(landingPosition));
            return true;
        }

        private IEnumerator MoveToPlatform(Vector3 landingPosition)
        {
            Vector3 start = transform.localPosition;
            const float duration = 0.24f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float progress = elapsed / duration;
                transform.localPosition = Vector3.Lerp(start, landingPosition, progress)
                    + Vector3.up * (0.08f * Mathf.Sin(progress * Mathf.PI));
                yield return null;
            }

            transform.localPosition = landingPosition;
            _state = State.Available;
            SetColliders(!_covered);
        }

        public bool TakeFromFixture(Transform hand)
        {
            if (_state != State.Stored)
            {
                return false;
            }

            _state = State.Held;
            AttachTo(hand, Vector3.zero);
            return true;
        }

        public void Place(Vector3 position)
        {
            Place(position, Quaternion.identity);
        }

        internal void Place(Vector3 position, Quaternion rotation)
        {
            transform.SetParent(null, true);
            _body.isKinematic = true;
            transform.SetPositionAndRotation(position, rotation);
            _body.position = position;
            _body.rotation = rotation;
            _state = State.Available;
            SetColliders(true);
        }

        internal ItemSave Capture(string parent)
        {
            bool cutting = _state == State.BeingCut;
            ItemLocation location = _state switch
            {
                State.Held => ItemLocation.Held,
                State.Stored => ItemLocation.Stored,
                State.Rooted => ItemLocation.Rooted,
                State.Falling => ItemLocation.Falling,
                _ => ItemLocation.Resting
            };
            return new ItemSave
            {
                id = Id,
                parent = parent,
                location = location,
                position = IsHeld ? Vector3.zero : cutting ? _cutLanding :
                    string.IsNullOrEmpty(parent) ? transform.position : transform.localPosition,
                rotation = IsHeld ? Quaternion.identity :
                    string.IsNullOrEmpty(parent) ? transform.rotation : transform.localRotation,
                recoveryPosition = _lastReachablePosition,
                recoveryRotation = _lastReachableRotation
            };
        }

        internal void RestoreSnapshot(ItemSave save, Transform parent, bool pins)
        {
            _lastReachablePosition = save.recoveryPosition;
            _lastReachableRotation = save.recoveryRotation;
            _body.isKinematic = true;
            transform.SetParent(parent, false);
            transform.SetLocalPositionAndRotation(save.position, save.rotation);
            _state = save.location switch
            {
                ItemLocation.Held => State.Held,
                ItemLocation.Stored => State.Stored,
                ItemLocation.Rooted => State.Rooted,
                _ => State.Available
            };
            transform.localScale = pins ? Vector3.one * 0.32f : Vector3.one;
            _covered = pins;
            SetColliders(!pins && save.location != ItemLocation.Falling &&
                (_state == State.Available || _state == State.Rooted));
        }

        internal void SettleRestoredDrop()
        {
            SetColliders(true);
            Physics.SyncTransforms();
            Bounds room = _roomBounds.bounds;
            float floor = float.NegativeInfinity;
            float bottom = transform.position.y;
            foreach (Collider itemCollider in _colliders)
            {
                bottom = Mathf.Min(bottom, itemCollider.bounds.min.y);
            }

            foreach (RaycastHit hit in Physics.RaycastAll(transform.position, Vector3.down, room.size.y,
                RoomLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody != _body && hit.normal.y > 0.55f && room.Contains(hit.point))
                {
                    floor = Mathf.Max(floor, hit.point.y);
                }
            }

            if (float.IsNegativeInfinity(floor))
            {
                Restore();
                return;
            }

            transform.position = new Vector3(transform.position.x, floor + transform.position.y - bottom,
                transform.position.z);
            Physics.SyncTransforms();
            if (!HasPickupPosition())
            {
                Restore();
            }
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

        internal bool TryGetPlacement(RaycastHit surface, Quaternion rotation, out Vector3 position, out Bounds footprint)
        {
            float lowest = float.PositiveInfinity;
            footprint = default;
            bool found = false;
            foreach (Collider collider in _colliders)
            {
                GetPlacementShape(collider, rotation, out Vector3 center, out Vector3 half, out Quaternion orientation);
                Vector3 extent = ProjectExtents(orientation, half);
                float bottom = center.y - extent.y;
                var bounds = new Bounds(center, extent * 2f);
                if (!found || bottom < lowest - 0.014f)
                {
                    footprint = bounds;
                    lowest = bottom;
                    found = true;
                }
                else if (bottom <= lowest + 0.014f)
                {
                    footprint.Encapsulate(bounds);
                    lowest = Mathf.Min(lowest, bottom);
                }
            }

            position = surface.point + Vector3.up * (0.002f - lowest);
            if (!found || !_roomBounds.bounds.Contains(position))
            {
                return false;
            }

            Vector3 span = footprint.extents * 0.78f;
            Vector3 supportCenter = surface.point + new Vector3(footprint.center.x, 0.065f, footprint.center.z);
            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 probe = supportCenter + new Vector3(x * span.x, 0f, z * span.z);
                    if (!Physics.Raycast(probe, Vector3.down, out RaycastHit support, 0.092f,
                        RoomLayerMask, QueryTriggerInteraction.Ignore) || support.normal.y < 0.85f ||
                        Mathf.Abs(support.point.y - surface.point.y) > 0.018f)
                    {
                        return false;
                    }
                }
            }

            foreach (Collider collider in _colliders)
            {
                GetPlacementShape(collider, rotation, out Vector3 center, out Vector3 half, out Quaternion orientation);
                var inset = new Vector3(Mathf.Max(0.001f, half.x - 0.003f), Mathf.Max(0.001f, half.y - 0.003f),
                    Mathf.Max(0.001f, half.z - 0.003f));
                if (Physics.CheckBox(position + center, inset, orientation, RoomLayerMask | (1 << 2),
                    QueryTriggerInteraction.Ignore))
                {
                    return false;
                }
            }

            return true;
        }

        private void GetPlacementShape(Collider collider, Quaternion rotation, out Vector3 center,
            out Vector3 half, out Quaternion orientation)
        {
            Transform shape = collider.transform;
            orientation = rotation * Quaternion.Inverse(transform.rotation) * shape.rotation;
            Vector3 sourceCenter = Vector3.zero;
            Vector3 size = Vector3.one * 0.044f;
            if (collider is BoxCollider box)
            {
                sourceCenter = box.center;
                size = box.size;
            }
            else if (collider is SphereCollider sphere)
            {
                sourceCenter = sphere.center;
                size = Vector3.one * (sphere.radius * 2f);
            }

            center = rotation * transform.InverseTransformPoint(shape.TransformPoint(sourceCenter));
            half = Vector3.Scale(size * 0.5f, shape.lossyScale);
        }

        private static Vector3 ProjectExtents(Quaternion orientation, Vector3 half)
        {
            Vector3 x = orientation * Vector3.right * half.x;
            Vector3 y = orientation * Vector3.up * half.y;
            Vector3 z = orientation * Vector3.forward * half.z;
            return new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        }

        public void Drop(CharacterController player)
        {
            transform.SetParent(null, true);
            _state = State.Falling;
            _body.position = transform.position;
            _body.rotation = transform.rotation;
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
            _body.isKinematic = true;
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
            transform.SetPositionAndRotation(_lastReachablePosition, _lastReachableRotation);
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
