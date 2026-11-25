using Sporeborn.Gameplay.Growing;
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
        [SerializeField] private string _placePrompt = "ЛКМ   Положить в коробку";
        [SerializeField] private string _resumePrompt = "Щелчок   Вернуться к игре";

        private Carryable _held;
        private Carryable _aimed;
        private Carryable _guidanceTarget;
        private CharacterController _player;
        private string _prompt;
        private string _detail;
        private string _notice;
        private float _noticeUntil;
        private float _cutUntil;
        private Vector3 _handPosition;
        private GUIStyle _promptStyle;
        private GUIStyle _panelTitleStyle;
        private GUIStyle _actionStyle;
        private Transform _placementMarker;
        private string _placementPrompt;
        private bool _placementBlocked;
        private Texture2D _blockedReticle;

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
            _guidanceTarget = null;
            _prompt = null;
            _detail = null;
            _placementPrompt = null;
            _placementBlocked = false;
            if (_placementMarker != null && _placementMarker.gameObject.activeSelf)
            {
                _placementMarker.gameObject.SetActive(false);
            }
        }

        public void UpdateAim(bool interact, bool use, bool drop, float mixingSeconds = 0f, float scrollSteps = 0f)
        {
            if (!isActiveAndEnabled || !_batch.isActiveAndEnabled || _batch.Growth == null)
            {
                return;
            }

            var aim = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);
            bool hitSomething = Physics.Raycast(aim, out RaycastHit hit, RangeMeters, RoomLayerMask, QueryTriggerInteraction.Ignore);
            _placementPrompt = null;
            if (_held != null)
            {
                _held.RotateForPlacement(scrollSteps);
                UpdateHand(aim.origin);
                if (drop)
                {
                    _held.Drop(_player, aim.origin);
                    _held = null;
                    _noticeUntil = 0f;
                    ClearAim();
                    return;
                }

                _placementPrompt = "Колесо   Повернуть   ·   G   Бросить";
            }

            Carryable target = hitSomething && hit.rigidbody != null ? hit.rigidbody.GetComponent<Carryable>() : null;
            _guidanceTarget = target;
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
            bool mixingTarget = target != null && target.Kind == Carryable.ItemKind.Bowl &&
                (_held.Kind == Carryable.ItemKind.Scoop || _held.Kind == Carryable.ItemKind.Cup ||
                    _held.Kind == Carryable.ItemKind.Mixer);
            SetAimed((cuttingTarget || mixingTarget) && hit.distance <= ToolRangeMeters ? target : null);
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
                : weigh ? "ЛКМ   Поставить на весы" : null;

            if (_prompt == null)
            {
                TryPlaceOnSurface(hit, use);
                return;
            }

            SetPlacementPreview(default, Quaternion.identity, default, false, false);
            if (!use)
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
            else if (receiving != null && receiving.TryReceive(_held))
            {
                _batch.Sounds.Play(WorkshopSounds.Cue.Pack);
                _noticeUntil = 0f;
            }
            else
            {
                return;
            }

            _held = null;
            ClearAim();
        }

        private void TryPlaceOnSurface(RaycastHit hit, bool use)
        {
            bool aimedAtSurface = hit.collider != null && hit.normal.y >= 0.85f;
            Quaternion rotation = _held.PlacementRotation;
            Vector3 position = default;
            Bounds footprint = default;
            bool canPlace = aimedAtSurface && _held.TryGetPlacement(hit, rotation, out position, out footprint);
            SetPlacementPreview(hit.point, rotation, footprint, aimedAtSurface, canPlace);
            if (canPlace)
            {
                _prompt = "ЛКМ   Поставить";
                if (use)
                {
                    _held.Place(position, rotation);
                    _held = null;
                    _noticeUntil = 0f;
                    ClearAim();
                }
            }
            else if (aimedAtSurface)
            {
                _prompt = "Нужно свободное место с опорой";
            }
        }

        private void UpdateEmptyHand(Collider collider, Carryable target, ShippingBox box, bool interact, bool use)
        {
            SetAimed(target != null && target.CanPickUp ? target : null);
            if (_batch.AimAtOffer(collider, use, out string prompt, out string detail, out string notice))
            {
                _prompt = prompt;
                _detail = detail;
                if (notice != null)
                {
                    _notice = notice;
                    _noticeUntil = Time.time + 2.5f;
                }

                return;
            }

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
                _placementPrompt = pickup;
            }

            _prompt ??= flap ?? pickup;
            if (flap != null)
            {
                _placementPrompt = pickup;
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

        private void SetPlacementPreview(Vector3 surface, Quaternion rotation, Bounds footprint, bool aimedAtSurface, bool canPlace)
        {
            _placementBlocked = aimedAtSurface && !canPlace;
            if (canPlace)
            {
                if (_placementMarker == null)
                {
                    _placementMarker = _batch.CreatePlacementMarker();
                }

                _placementMarker.SetPositionAndRotation(surface + Vector3.up * 0.008f +
                    rotation * new Vector3(footprint.center.x, 0f, footprint.center.z), rotation);
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
            if (_held.Kind == Carryable.ItemKind.Knife)
            {
                float cutProgress = Mathf.Clamp01((_cutUntil - Time.time) / 0.24f);
                localOffset.y -= 0.10f * Mathf.Sin(cutProgress * Mathf.PI);
            }

            Vector3 offset = (large ? transform : _hand.parent).TransformVector(localOffset);
            float distance = offset.magnitude;
            Vector3 direction = offset.normalized;

            if (Physics.SphereCast(cameraPosition, _held.Radius + 0.015f, direction, out RaycastHit hit,
                distance, RoomLayerMask, QueryTriggerInteraction.Ignore))
            {
                distance = hit.distance;
            }

            _hand.SetPositionAndRotation(cameraPosition + direction * distance, _held.PlacementRotation);
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
                DrawActionHints(centerX, centerY, Mathf.Min(550f, width - 36f));
                var money = new Rect(width - 171f, 18f, 153f, 38f);
                DrawSurface(money, new Color(0.035f, 0.065f, 0.08f, 0.94f));
                GUI.Label(new Rect(money.x + 12f, money.y + 7f, money.width - 24f, 30f),
                    $"\u0414\u0435\u043d\u044c\u0433\u0438: {_batch.Money}", _promptStyle);
            }

            DrawGuidance(Mathf.Min(390f, width * 0.44f), isPlaying);
        }

        private void DrawGuidance(float panelWidth, bool isPlaying)
        {
            string next = isPlaying ? _batch.NextStep(_held, _guidanceTarget) : _resumePrompt;
            string notice = isPlaying && Time.time < _noticeUntil ? _notice : null;
            string detail = isPlaying ? _detail : null;
            string treatment = isPlaying ? _batch.Growth.TimerReading(Station.Treatment) : null;
            string incubation = isPlaying ? _batch.Growth.TimerReading(Station.Incubation) : null;
            string fruiting = isPlaying ? _batch.Growth.TimerReading(Station.Fruiting) : null;
            float textWidth = panelWidth - 28f;
            float height = 46f + RowHeight(next, textWidth) + RowHeight(detail, textWidth) + RowHeight(notice, textWidth) +
                RowHeight(treatment, textWidth) + RowHeight(incubation, textWidth) + RowHeight(fruiting, textWidth);

            DrawSurface(new Rect(18f, 18f, panelWidth, height), new Color(0.035f, 0.065f, 0.08f, 0.94f));
            GUI.Label(new Rect(32f, 29f, textWidth, 22f), "\u0421\u041b\u0415\u0414\u0423\u042e\u0429\u0418\u0419 \u0428\u0410\u0413", _panelTitleStyle);
            float y = 52f;
            DrawRow(next, textWidth, ref y);
            DrawRow(treatment, textWidth, ref y);
            DrawRow(incubation, textWidth, ref y);
            DrawRow(fruiting, textWidth, ref y);
            DrawRow(detail, textWidth, ref y);
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

        private void DrawActionHints(float x, float y, float maxWidth)
        {
            string first = _prompt;
            string second = _placementPrompt;
            if (string.IsNullOrEmpty(first) && string.IsNullOrEmpty(second))
            {
                return;
            }

            _actionStyle ??= new GUIStyle(_promptStyle) { alignment = TextAnchor.MiddleCenter };
            float textWidth = Mathf.Min(maxWidth - 24f,
                Mathf.Max(_actionStyle.CalcSize(new GUIContent(first)).x, _actionStyle.CalcSize(new GUIContent(second)).x) + 4f);
            float firstHeight = RowHeight(first, textWidth);
            float secondHeight = RowHeight(second, textWidth);
            var card = new Rect(x - textWidth * 0.5f - 12f, y + 24f, textWidth + 24f, firstHeight + secondHeight + 8f);
            DrawSurface(card, new Color(0.035f, 0.065f, 0.08f, 0.94f));
            if (firstHeight > 0f)
            {
                GUI.Label(new Rect(card.x + 12f, card.y + 4f, textWidth, firstHeight), first, _actionStyle);
            }

            if (secondHeight > 0f)
            {
                GUI.Label(new Rect(card.x + 12f, card.y + 4f + firstHeight, textWidth, secondHeight), second, _actionStyle);
            }
        }

        private void DrawReticle(float x, float y)
        {
            if (!_placementBlocked)
            {
                DrawSurface(new Rect(x - 2f, y - 2f, 4f, 4f), Color.white);
                return;
            }

            if (_blockedReticle == null)
            {
                _blockedReticle = CreateBlockedReticle();
            }

            GUI.DrawTexture(new Rect(x - 8f, y - 8f, 16f, 16f), _blockedReticle);
        }

        private static Texture2D CreateBlockedReticle()
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            var red = new Color(1f, 0.36f, 0.28f);
            var outline = new Color(0.05f, 0.06f, 0.07f);
            Vector2 diagonal = new Vector2(1f, 1f).normalized;
            Vector2 otherDiagonal = new Vector2(-diagonal.x, diagonal.y);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f) - Vector2.one * (size * 0.5f);
                    float distance = Mathf.Min(StrokeDistance(point, diagonal), StrokeDistance(point, otherDiagonal));
                    Color color = Color.Lerp(outline, red, Mathf.Clamp01(5.5f - distance));
                    color.a = Mathf.Clamp01(9f - distance);
                    pixels[y * size + x] = color;
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static float StrokeDistance(Vector2 point, Vector2 direction)
        {
            float projected = Mathf.Clamp(Vector2.Dot(point, direction), -24f, 24f);
            return (point - direction * projected).magnitude;
        }

        private void OnDestroy()
        {
            if (_blockedReticle != null)
            {
                Destroy(_blockedReticle);
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
