using UnityEngine;

namespace Sporeborn.Runtime
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class MushroomInteraction : MonoBehaviour
    {
        public const float RangeMeters = 1.75f;
        public const float EyeHeightMeters = 1.4f;
        public const float ToolRangeMeters = 1.2f;
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
        private Transform _placementMarker;
        private string _placementPrompt;

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
            _placementPrompt = null;
            if (_placementMarker != null && _placementMarker.gameObject.activeSelf)
            {
                _placementMarker.gameObject.SetActive(false);
            }
        }

        public void UpdateAim(bool interact, bool use, bool drop, float mixingSeconds = 0f, bool forceDrop = false)
        {
            if (!isActiveAndEnabled || !_batch.isActiveAndEnabled || _batch.Growth == null)
            {
                return;
            }

            var aim = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);
            bool hitSomething = Physics.Raycast(aim, out RaycastHit hit, RangeMeters, RoomLayerMask, QueryTriggerInteraction.Ignore);
            if (_held != null)
            {
                UpdateHand(aim.origin);
                bool wantsPlacement = !forceDrop && hitSomething && hit.normal.y >= 0.85f;
                var rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                Vector3 position = default;
                bool canPlace = wantsPlacement && _held.TryGetPlacement(hit, rotation, out position);
                UpdatePlacementPreview(hit.point, wantsPlacement, canPlace);
                if (drop)
                {
                    if (wantsPlacement && !canPlace)
                    {
                        _notice = "Найдите ровное свободное место";
                        _noticeUntil = Time.time + 1.5f;
                        return;
                    }

                    if (canPlace)
                    {
                        _held.Place(position, rotation);
                    }
                    else
                    {
                        _held.Drop(_player);
                    }

                    _held = null;
                    _noticeUntil = 0f;
                    ClearAim();
                    return;
                }
            }

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

            bool cuttingTarget = _held.Kind == Carryable.ItemKind.Knife && target != null && target.CanCut;
            SetAimed(cuttingTarget && hit.distance <= ToolRangeMeters ? target : null);
            if (cuttingTarget)
            {
                _prompt = hit.distance <= ToolRangeMeters ? "ЛКМ   Срезать гриб" : "Подойдите ближе, чтобы срезать гриб";
                if (use && hit.distance <= ToolRangeMeters && _batch.TryCut(target))
                {
                    _cutUntil = Time.time + 0.24f;
                    _batch.Sounds.Play(WorkshopSounds.Cue.Cut);
                }
            }
            else if (_held.Kind == Carryable.ItemKind.Label && targetBox != null)
            {
                _prompt = hit.distance <= ToolRangeMeters ? targetBox.LabelPrompt : "Подойдите ближе, чтобы наклеить этикетку";
                if (use && hit.distance <= ToolRangeMeters && targetBox.TryApplyLabel(_held))
                {
                    _batch.Sounds.Play(WorkshopSounds.Cue.Pack);
                    _held = null;
                    ClearAim();
                    return;
                }
            }

            ShippingBox receiving = _held.Kind == Carryable.ItemKind.Mushroom
                ? _batch.FindReceivingBox(aim, hit) : null;
            bool boxOnStand = _held.Kind == Carryable.ItemKind.Box && hit.normal.y > 0.9f;
            bool dispatch = boxOnStand && hit.collider == _batch.Dispatch;
            bool weigh = boxOnStand && hit.collider == _batch.Scale;
            _prompt ??= receiving != null ? _placePrompt
                : dispatch ? _held.GetComponent<ShippingBox>().ShippingPrompt
                : weigh ? "E   Поставить на весы" : null;

            if (!interact)
            {
                return;
            }

            if (dispatch || weigh)
            {
                ShippingBox box = _held.GetComponent<ShippingBox>();
                bool released = dispatch ? _batch.TryDispatchBox(box, out _notice) : _batch.TryWeighBox(box, out _notice);
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

                _batch.Sounds.Play(WorkshopSounds.Cue.Pack);
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
                _prompt ??= collider == _batch.Scale ? _batch.ScaleReading : _batch.Growth.FirstStep;
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

        private void UpdatePlacementPreview(Vector3 surface, bool wantsPlacement, bool canPlace)
        {
            _placementPrompt = canPlace ? "G   Поставить   ·   Shift+G   Бросить"
                : wantsPlacement ? "Место занято или узко   ·   Shift+G   Бросить"
                : $"G   Бросить {_held.ItemName}";
            if (canPlace)
            {
                if (_placementMarker == null)
                {
                    _placementMarker = _batch.CreatePlacementMarker();
                }

                _placementMarker.position = surface + Vector3.up * 0.005f;
                _placementMarker.gameObject.SetActive(true);
            }
            else if (_placementMarker != null)
            {
                _placementMarker.gameObject.SetActive(false);
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
            Vector3 localOffset = large ? new Vector3(0.12f, -0.39f, 0.56f) : _handPosition;
            Vector3 offset = (large ? transform : _hand.parent).TransformVector(localOffset);
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
                fontSize = 18,
                normal = { textColor = Color.white }
            };

            float scale = WorkshopGui.Scale;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = previous * Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                DrawHud(Screen.width / scale, Screen.height / scale);
            }
            finally
            {
                GUI.matrix = previous;
            }
        }

        private void DrawHud(float width, float height)
        {
            float centerX = width * 0.5f;
            float centerY = height * 0.5f;
            float hintWidth = Mathf.Min(635f, width - 24f);
            float hintLeft = centerX - hintWidth * 0.5f;
            bool isPlaying = Cursor.lockState == CursorLockMode.Locked;
            if (isPlaying)
            {
                GUI.DrawTexture(new Rect(centerX - 2f, centerY - 2f, 4f, 4f), Texture2D.whiteTexture);
                if (_batch.Growth.IsIncubating)
                {
                    DrawLabel(new Rect(hintLeft, height - 62f, hintWidth, 34f),
                        "Пока идёт инкубация, поднесите коробку и напечатайте этикетку");
                }

                DrawLabel(new Rect(width - 172f, 18f, 154f, 34f), $"Деньги: {_batch.Money}");
                DrawLabel(new Rect(hintLeft, centerY + 59f, hintWidth, 32f), _detail);
                float nextRow = centerY + 95f;
                if (_held != null)
                {
                    DrawLabel(new Rect(hintLeft, nextRow, hintWidth, 32f), _placementPrompt);
                    nextRow += 36f;
                }

                if (Time.time < _noticeUntil)
                {
                    DrawLabel(new Rect(hintLeft, nextRow, hintWidth, 34f), _notice);
                }
            }

            DrawLabel(new Rect(hintLeft, centerY + 22f, hintWidth, 34f), isPlaying ? _prompt : _resumePrompt);
        }

        private void DrawLabel(Rect rect, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            float labelWidth = _promptStyle.CalcSize(new GUIContent(text)).x + 24f;
            float actualWidth = Mathf.Min(labelWidth, rect.width);
            rect.x += (rect.width - actualWidth) * 0.5f;
            rect.width = actualWidth;
            Color previous = GUI.color;
            GUI.color = new Color(0.035f, 0.065f, 0.08f, 0.94f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(rect, text, _promptStyle);
        }
    }
}
