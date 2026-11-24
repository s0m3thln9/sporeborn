using UnityEngine;

namespace Sporeborn.Runtime
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class MushroomInteraction : MonoBehaviour
    {
        public const float RangeMeters = 1.5f;
        public const float EyeHeightMeters = 1.4f;
        public const float ToolRangeMeters = 1f;
        private const int RoomLayerMask = 1;

        [SerializeField] private Camera _playerCamera;
        [SerializeField] private Transform _hand;
        [SerializeField] private WorkshopBatch _batch;
        [SerializeField] private string _pickupPrompt = "E   Поднять гриб";
        [SerializeField] private string _placePrompt = "E   Положить в коробку";
        [SerializeField] private string _resumePrompt = "Щелчок   Вернуться к игре";

        private Carryable _held;
        private Carryable _aimed;
        private CharacterController _player;
        private string _prompt;
        private string _detail;
        private string _notice;
        private float _noticeUntil;
        private float _cutUntil;
        private Vector3 _handPosition;
        private GUIStyle _promptStyle;

        public float WalkingMultiplier => _held != null && _held.Kind == Carryable.ItemKind.Box ? 0.82f : 1f;

        internal WorkshopSession Session => _batch != null ? _batch.Session : null;
        internal Transform Hand => _hand;

        internal void RestoreHeld(Carryable item)
        {
            _held = item;
            UpdateHand(_playerCamera.transform.position);
        }

        private void Awake()
        {
            if (_playerCamera == null || _hand == null || _hand.parent == null || _batch == null)
            {
                Debug.LogError("Assign the interaction camera, hand, and workshop batch.", this);
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
            _detail = null;
        }

        public void UpdateAim(bool interact, bool use, bool drop, float mixingSeconds = 0f)
        {
            if (!isActiveAndEnabled || !_batch.isActiveAndEnabled || _batch.Growth == null)
            {
                return;
            }

            if (drop && _held != null)
            {
                UpdateHand(_playerCamera.transform.position);
                _held.Drop(_player);
                _held = null;
                _noticeUntil = 0f;
                ClearAim();
                return;
            }

            var aim = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);
            bool hitSomething = Physics.Raycast(aim, out RaycastHit hit, RangeMeters, RoomLayerMask, QueryTriggerInteraction.Ignore);
            Carryable target = hitSomething && hit.rigidbody != null ? hit.rigidbody.GetComponent<Carryable>() : null;
            ShippingBox targetBox = target != null && target.Kind == Carryable.ItemKind.Box
                ? target.GetComponent<ShippingBox>() : null;

            Carryable previous = _held;
            _batch.Growth.Aim(hit, target, _hand, ref _held, use, mixingSeconds, out _prompt, out _detail);
            _detail ??= targetBox != null ? targetBox.Summary : null;
            if (_held != previous)
            {
                ClearAim();
                if (_held != null)
                {
                    UpdateHand(aim.origin);
                }

                return;
            }

            if (_held == null)
            {
                UpdateEmptyHand(hit.collider, target, targetBox, interact, use);
                return;
            }

            UpdateHand(aim.origin);
            bool cuttingTarget = _held.Kind == Carryable.ItemKind.Knife && target != null && target.CanCut;
            SetAimed(cuttingTarget && hit.distance <= ToolRangeMeters ? target : null);
            if (cuttingTarget)
            {
                _prompt = hit.distance <= ToolRangeMeters ? "ЛКМ   Срезать гриб" : "Подойдите ближе, чтобы срезать гриб";
                if (use && hit.distance <= ToolRangeMeters && _batch.TryCut(target))
                {
                    _cutUntil = Time.time + 0.24f;
                }
            }
            else if (_held.Kind == Carryable.ItemKind.Label && targetBox != null)
            {
                _prompt = hit.distance <= ToolRangeMeters ? targetBox.LabelPrompt : "Подойдите ближе, чтобы наклеить этикетку";
                if (use && hit.distance <= ToolRangeMeters && targetBox.TryApplyLabel(_held))
                {
                    _held = null;
                    ClearAim();
                    return;
                }
            }

            ShippingBox receiving = _held.Kind == Carryable.ItemKind.Mushroom
                ? _batch.FindReceivingBox(aim, hit) : null;
            bool dispatch = _held.Kind == Carryable.ItemKind.Box && hit.collider == _batch.Dispatch && hit.normal.y > 0.9f;
            _prompt ??= receiving != null ? _placePrompt
                : dispatch ? _held.GetComponent<ShippingBox>().ShippingPrompt : null;

            if (!interact)
            {
                return;
            }

            if (dispatch)
            {
                bool released = _batch.TryDispatchBox(_held.GetComponent<ShippingBox>(), out _notice);
                _noticeUntil = Time.time + 2.5f;
                if (!released)
                {
                    return;
                }
            }
            else
            {
                if (receiving == null || !receiving.TryReceive(_held))
                {
                    if (_held.Kind == Carryable.ItemKind.Mushroom)
                    {
                        _notice = "Наведите прицел на открытую коробку со свободным местом";
                        _noticeUntil = Time.time + 1.5f;
                    }

                    return;
                }

                _noticeUntil = 0f;
            }

            _held = null;
            ClearAim();
        }

        private void UpdateEmptyHand(Collider collider, Carryable target, ShippingBox box, bool interact, bool use)
        {
            SetAimed(target != null && target.CanPickUp ? target : null);
            if (collider == _batch.Printer)
            {
                _prompt = _batch.PrinterPrompt;
                if (use)
                {
                    _batch.PrintLabel();
                }

                return;
            }

            if (target == null)
            {
                return;
            }

            if (target.CanCut)
            {
                _prompt = "Для сбора нужен нож";
                return;
            }

            if (!target.CanPickUp)
            {
                return;
            }

            string pickup = target.Kind == Carryable.ItemKind.Mushroom ? _pickupPrompt : $"E   Поднять {target.ItemName}";
            string flap = box != null ? box.FlapPrompt(collider) : null;
            if (_prompt != null)
            {
                _detail = _detail == null ? pickup : $"{_detail}   ·   {pickup}";
            }

            _prompt ??= flap ?? pickup;
            if (flap != null)
            {
                _detail = $"{_detail}   ·   {pickup}";
                if (use)
                {
                    box.CloseFlap(collider);
                }
            }

            if (interact && target.TryPickUp(_hand))
            {
                _held = target;
                UpdateHand(_playerCamera.transform.position);
                ClearAim();
            }
        }

        private void SetAimed(Carryable item)
        {
            if (_aimed == item)
            {
                return;
            }

            if (_aimed != null)
            {
                _aimed.SetHighlighted(false);
            }

            _aimed = item;
            if (_aimed != null)
            {
                _aimed.SetHighlighted(true);
            }
        }

        private void UpdateHand(Vector3 cameraPosition)
        {
            bool large = _held.Kind == Carryable.ItemKind.Box || _held.Kind == Carryable.ItemKind.Tray;
            Vector3 localOffset = large ? new Vector3(0.08f, -0.27f, 0.66f) : _handPosition;
            Vector3 offset = _hand.parent.TransformVector(localOffset);
            float distance = offset.magnitude;
            Vector3 direction = offset.normalized;

            if (Physics.SphereCast(cameraPosition, _held.Radius + 0.015f, direction, out RaycastHit hit,
                distance, RoomLayerMask, QueryTriggerInteraction.Ignore))
            {
                distance = hit.distance;
            }

            _hand.position = cameraPosition + direction * distance;
            float cutProgress = Mathf.Clamp01((_cutUntil - Time.time) / 0.24f);
            if (large)
            {
                _hand.rotation = transform.rotation;
            }
            else
            {
                _hand.localRotation = _held.Kind == Carryable.ItemKind.Knife
                    ? Quaternion.Euler(42f * Mathf.Sin(cutProgress * Mathf.PI), 0f, 0f)
                    : Quaternion.identity;
            }
        }

        private void OnGUI()
        {
            if (Session == null || !Session.IsPlaying || Event.current.type != EventType.Repaint)
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
                DrawLabel(new Rect(Screen.width - 172f, 18f, 154f, 32f), $"Деньги: {_batch.Money}");
                DrawLabel(new Rect(centerX - 245f, centerY + 56f, 490f, 30f), _detail);
                float nextRow = centerY + 91f;
                if (_held != null)
                {
                    DrawLabel(new Rect(centerX - 245f, nextRow, 490f, 32f), $"G   Отпустить {_held.ItemName}");
                    nextRow += 34f;
                }

                if (Time.time < _noticeUntil)
                {
                    DrawLabel(new Rect(centerX - 245f, nextRow, 490f, 32f), _notice);
                }
            }

            DrawLabel(new Rect(centerX - 245f, centerY + 22f, 490f, 34f), isPlaying ? _prompt : _resumePrompt);
        }

        private void DrawLabel(Rect rect, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            GUI.Box(rect, GUIContent.none);
            GUI.Label(rect, text, _promptStyle);
        }
    }
}
