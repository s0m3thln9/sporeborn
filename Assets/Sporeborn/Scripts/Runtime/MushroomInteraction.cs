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
        private GUIStyle _panelTitleStyle;
        private Transform _placementMarker;
        private string _placementPrompt;
        private bool _placing;
        private bool _validPlacement;

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
            _placing = false;
            _validPlacement = false;
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
                Bounds footprint = default;
                bool canPlace = wantsPlacement && _held.TryGetPlacement(hit, rotation, out position, out footprint);
                UpdatePlacementPreview(hit.point, footprint, wantsPlacement, canPlace);
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
                _prompt ??= collider == _batch.Scale ? _batch.ScaleReading : null;
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

        private void UpdatePlacementPreview(Vector3 surface, Bounds footprint, bool wantsPlacement, bool canPlace)
        {
            _placing = wantsPlacement;
            _validPlacement = canPlace;
            _placementPrompt = canPlace ? "G   Поставить   ·   Shift+G   Бросить"
                : wantsPlacement ? "Место занято или узко   ·   Shift+G   Бросить"
                : $"G   Бросить {_held.ItemName}";
            if (canPlace)
            {
                if (_placementMarker == null)
                {
                    _placementMarker = _batch.CreatePlacementMarker();
                }

                _placementMarker.position = surface + new Vector3(footprint.center.x, 0.008f, footprint.center.z);
                _placementMarker.localScale = new Vector3(Mathf.Max(0.075f, footprint.size.x), 1f,
                    Mathf.Max(0.075f, footprint.size.z));
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
            Vector3 localOffset = large ? new Vector3(0.34f, -0.47f, 0.54f) : _handPosition + new Vector3(0.08f, -0.07f, 0f);
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
                alignment = TextAnchor.UpperLeft,
                fontSize = 18,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
            _panelTitleStyle ??= new GUIStyle(_promptStyle)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.58f, 0.92f, 0.81f) }
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
            bool isPlaying = Cursor.lockState == CursorLockMode.Locked;
            if (isPlaying)
            {
                DrawReticle(centerX, centerY);
                var money = new Rect(width - 171f, 18f, 153f, 38f);
                DrawSurface(money, new Color(0.035f, 0.065f, 0.08f, 0.94f));
                GUI.Label(new Rect(money.x + 12f, money.y + 7f, money.width - 24f, 30f),
                    $"\u0414\u0435\u043d\u044c\u0433\u0438: {_batch.Money}", _promptStyle);
            }

            DrawGuidance(Mathf.Min(390f, width * 0.44f), isPlaying);
        }

        private void DrawGuidance(float panelWidth, bool isPlaying)
        {
            string next = isPlaying ? _batch.NextStep(_held) : _resumePrompt;
            string notice = isPlaying && Time.time < _noticeUntil ? _notice : null;
            string context = isPlaying ? _prompt : null;
            string detail = isPlaying ? _detail : null;
            string placement = isPlaying && _held != null ? _placementPrompt : null;
            float textWidth = panelWidth - 28f;
            float height = 46f + RowHeight(next, textWidth) + RowHeight(context, textWidth) +
                RowHeight(detail, textWidth) + RowHeight(placement, textWidth) + RowHeight(notice, textWidth);

            DrawSurface(new Rect(18f, 18f, panelWidth, height), new Color(0.035f, 0.065f, 0.08f, 0.94f));
            GUI.Label(new Rect(32f, 29f, textWidth, 22f), "\u0421\u041b\u0415\u0414\u0423\u042e\u0429\u0418\u0419 \u0428\u0410\u0413", _panelTitleStyle);
            float y = 52f;
            DrawRow(next, textWidth, ref y);
            DrawRow(context, textWidth, ref y);
            DrawRow(detail, textWidth, ref y);
            DrawRow(placement, textWidth, ref y);
            DrawRow(notice, textWidth, ref y);
        }

        private float RowHeight(string text, float width)
        {
            return string.IsNullOrEmpty(text) ? 0f : _promptStyle.CalcHeight(new GUIContent(text), width) + 6f;
        }

        private void DrawRow(string text, float width, ref float y)
        {
            float row = RowHeight(text, width);
            if (row > 0f)
            {
                GUI.Label(new Rect(32f, y, width, row), text, _promptStyle);
                y += row;
            }
        }

        private void DrawReticle(float x, float y)
        {
            if (!_placing)
            {
                DrawSurface(new Rect(x - 2f, y - 2f, 4f, 4f), Color.white);
                return;
            }

            Color color = _validPlacement ? new Color(0.31f, 1f, 0.64f) : new Color(1f, 0.55f, 0.42f);
            for (int side = -1; side <= 1; side += 2)
            {
                DrawSurface(new Rect(x + side * 12f - 2f, y - 8f, 4f, 16f), new Color(0.04f, 0.08f, 0.08f));
                DrawSurface(new Rect(x + side * 12f - 1f, y - 7f, 2f, 14f), color);
            }
        }

        private static void DrawSurface(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
