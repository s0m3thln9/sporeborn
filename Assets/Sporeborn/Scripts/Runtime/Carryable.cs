using System;
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

            Bounds room = _roomBounds.bounds;
            Vector3 position = _body.position;
            if (position.y < room.min.y && position.x >= room.min.x && position.x <= room.max.x &&
                position.z >= room.min.z && position.z <= room.max.z)
            {
                _body.isKinematic = true;
                transform.position = new Vector3(position.x, room.min.y + 0.18f, position.z);
                _state = State.Available;
                SettleRestoredDrop();
                return;
            }

            if (!room.Contains(position))
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
            Quaternion local = Quaternion.Inverse(rotation);
            foreach (Collider collider in _colliders)
            {
                GetPlacementShape(collider, rotation, out Vector3 center, out Vector3 half, out Quaternion orientation);
                Vector3 extent = ProjectExtents(local * orientation, half);
                var bounds = new Bounds(local * center, extent * 2f);
                float bottom = bounds.min.y;
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
            if (!found || !_roomBounds.bounds.Contains(position) || !HasEnoughSupport(surface, rotation, footprint))
            {
                return false;
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

        private static bool HasEnoughSupport(RaycastHit surface, Quaternion rotation, Bounds footprint)
        {
            if (surface.collider is BoxCollider support && Vector3.Dot(support.transform.up, Vector3.up) > 0.999f)
            {
                return OverlapsMostOfFootprint(surface.point, rotation, footprint, support);
            }

            const int grid = 8;
            int supported = 0;
            Vector3 corner = footprint.min;
            for (int x = 0; x < grid; x++)
            {
                for (int z = 0; z < grid; z++)
                {
                    var point = new Vector3(corner.x + footprint.size.x * (x + 0.5f) / grid, 0.035f,
                        corner.z + footprint.size.z * (z + 0.5f) / grid);
                    Vector3 probe = surface.point + rotation * point;
                    if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 0.055f,
                        RoomLayerMask, QueryTriggerInteraction.Ignore) && hit.collider == surface.collider &&
                        hit.normal.y >= 0.85f && Mathf.Abs(hit.point.y - surface.point.y) <= 0.018f)
                    {
                        supported++;
                    }
                }
            }

            return supported > grid * grid / 2;
        }

        private static bool OverlapsMostOfFootprint(Vector3 origin, Quaternion rotation, Bounds footprint, BoxCollider support)
        {
            Vector3 min = footprint.min;
            Vector3 max = footprint.max;
            Span<Vector2> polygon = stackalloc Vector2[12];
            Span<Vector2> buffer = stackalloc Vector2[12];
            polygon[0] = SupportLocal(support, origin + rotation * new Vector3(min.x, 0f, min.z));
            polygon[1] = SupportLocal(support, origin + rotation * new Vector3(max.x, 0f, min.z));
            polygon[2] = SupportLocal(support, origin + rotation * new Vector3(max.x, 0f, max.z));
            polygon[3] = SupportLocal(support, origin + rotation * new Vector3(min.x, 0f, max.z));

            float fullArea = PolygonArea(polygon, 4);
            Vector3 lower = support.center - support.size * 0.5f;
            Vector3 upper = support.center + support.size * 0.5f;
            int count = ClipEdge(polygon, 4, buffer, 0, lower.x, 1);
            count = ClipEdge(buffer, count, polygon, 0, upper.x, -1);
            count = ClipEdge(polygon, count, buffer, 1, lower.z, 1);
            count = ClipEdge(buffer, count, polygon, 1, upper.z, -1);
            return fullArea > 0f && PolygonArea(polygon, count) > fullArea * 0.500001f;
        }

        private static Vector2 SupportLocal(BoxCollider support, Vector3 world)
        {
            Vector3 point = support.transform.InverseTransformPoint(world);
            return new Vector2(point.x, point.z);
        }

        private static int ClipEdge(Span<Vector2> input, int count, Span<Vector2> output, int axis, float edge, int sign)
        {
            if (count == 0)
            {
                return 0;
            }

            int written = 0;
            Vector2 previous = input[count - 1];
            float from = (previous[axis] - edge) * sign;
            for (int index = 0; index < count; index++)
            {
                Vector2 current = input[index];
                float to = (current[axis] - edge) * sign;
                if ((from < 0f) != (to < 0f))
                {
                    output[written++] = Vector2.LerpUnclamped(previous, current, from / (from - to));
                }

                if (to >= 0f)
                {
                    output[written++] = current;
                }

                previous = current;
                from = to;
            }

            return written;
        }

        private static float PolygonArea(Span<Vector2> polygon, int count)
        {
            if (count < 3)
            {
                return 0f;
            }

            Vector2 origin = polygon[0];
            float sum = 0f;
            for (int index = 1; index < count - 1; index++)
            {
                Vector2 left = polygon[index] - origin;
                Vector2 right = polygon[index + 1] - origin;
                sum += left.x * right.y - left.y * right.x;
            }

            return Mathf.Abs(sum) * 0.5f;
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

        public void Drop(CharacterController player, Vector3 cameraPosition)
        {
            Vector3 heldPosition = transform.position;
            Quaternion rotation = transform.rotation;
            transform.SetParent(null, true);
            if (!TryFindDropPosition(heldPosition, cameraPosition, rotation, out Vector3 releasePosition))
            {
                SetColliders(true);
                Restore();
                return;
            }

            transform.position = releasePosition;
            _state = State.Falling;
            _body.position = releasePosition;
            _body.rotation = transform.rotation;
            SetColliders(true);
            foreach (Collider itemCollider in _colliders)
            {
                Physics.IgnoreCollision(itemCollider, player);
            }
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _body.isKinematic = false;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.WakeUp();
        }

        private bool TryFindDropPosition(Vector3 held, Vector3 cameraPosition, Quaternion rotation, out Vector3 release)
        {
            Bounds room = _roomBounds.bounds;
            for (int step = 0; step <= 8; step++)
            {
                release = Vector3.Lerp(held, cameraPosition, step / 8f);
                if (room.Contains(release) && !OverlapsSolid(release, rotation))
                {
                    return true;
                }
            }

            release = default;
            return false;
        }

        private bool OverlapsSolid(Vector3 position, Quaternion rotation)
        {
            foreach (Collider collider in _colliders)
            {
                GetPlacementShape(collider, rotation, out Vector3 center, out Vector3 half, out Quaternion orientation);
                Vector3 inset = Vector3.Max(Vector3.one * 0.001f, half - Vector3.one * 0.003f);
                if (Physics.CheckBox(position + center, inset, orientation, RoomLayerMask, QueryTriggerInteraction.Ignore))
                {
                    return true;
                }
            }

            return false;
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
