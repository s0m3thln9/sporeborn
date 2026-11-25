using UnityEngine;

namespace Sporeborn.Runtime
{
    public sealed partial class WorkshopBatch : MonoBehaviour
    {
        private static readonly Quaternion TowardRoom = Quaternion.Euler(0f, 90f, 0f);
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        [SerializeField] private Carryable _mushroomTemplate;
        [SerializeField] private ShippingBox _boxTemplate;

        private readonly ShippingBox[] _boxes = new ShippingBox[3];
        private BoxCollider _roomBounds;
        private Material _material;
        private Carryable _printedLabel;
        private Vector3 _printerOutput;
        private int _nextLabel = 1;
        private WorkshopSign _scaleDisplay;
        private int _displayedWeight = -1;

        public WorkshopSession Session { get; private set; }
        public WorkshopGrowth Growth { get; private set; }
        public WorkshopSounds Sounds { get; private set; }
        public Collider Scale { get; private set; }
        public Collider Printer { get; private set; }
        public Collider Dispatch { get; private set; }
        public int Money { get; private set; }
        private bool IsOutputOccupied => _printedLabel != null && _printedLabel.CanPickUp &&
            (_printedLabel.transform.position - _printerOutput).sqrMagnitude < 0.045f * 0.045f;
        public string PrinterPrompt => IsOutputOccupied
            ? "Заберите напечатанную этикетку" : "ЛКМ   Напечатать этикетку";

        private void Start()
        {
            if (_mushroomTemplate == null || _boxTemplate == null || _mushroomTemplate.RoomBounds == null)
            {
                Debug.LogError("Assign the batch mushroom with room bounds, and the shipping box.", this);
                enabled = false;
                return;
            }

            _roomBounds = _mushroomTemplate.RoomBounds;
            _material = _boxTemplate.Material;
            Vector3 harvestPosition = _mushroomTemplate.transform.position;
            _mushroomTemplate.gameObject.SetActive(false);
            Sounds = new WorkshopSounds(gameObject.AddComponent<AudioSource>());
            Growth = new WorkshopGrowth(transform, _mushroomTemplate, _material, _roomBounds, harvestPosition, Sounds);
            BuildKnife(harvestPosition);
            BuildPackingStation();
            Session = gameObject.AddComponent<WorkshopSession>();
            Session.Initialize(this);
        }

        private void Update()
        {
            if (Session != null && Session.IsPlaying)
            {
                Growth.Tick(Time.deltaTime);
                UpdateScale();
            }
        }

        private void OnDestroy()
        {
            Sounds?.Dispose();
        }

        private void BuildKnife(Vector3 platform)
        {
            var knife = new GameObject("Harvest knife");
            knife.transform.position = platform + new Vector3(0.33f, -0.048f, -0.02f);
            Renderer blade = CreateBlock("Blade", knife.transform, new Vector3(0f, 0f, 0.06f),
                new Vector3(0.027f, 0.012f, 0.12f), _material, new Color(0.74f, 0.79f, 0.82f))
                .GetComponent<Renderer>();
            CreateBlock("Handle", knife.transform, new Vector3(0f, 0f, -0.045f),
                new Vector3(0.032f, 0.024f, 0.09f), _material, new Color(0.25f, 0.19f, 0.15f));
            knife.AddComponent<Carryable>().Configure(Carryable.ItemKind.Knife, 0.09f,
                _roomBounds, knife.transform.position, blade);
        }

        private void BuildPackingStation()
        {
            Vector3 boxPosition = _boxTemplate.transform.position;
            _boxes[0] = _boxTemplate;
            _boxes[1] = Instantiate(_boxTemplate, boxPosition + Vector3.left * 0.51f, Quaternion.identity);
            _boxes[2] = Instantiate(_boxTemplate, boxPosition + Vector3.forward * 0.42f, Quaternion.identity);
            for (int index = 0; index < _boxes.Length; index++)
            {
                _boxes[index].name = $"Shipping box {index + 1}";
                _boxes[index].Configure(_roomBounds, $"box/{index + 1}");
            }

            GameObject printer = CreateBlock("Label printer", transform, new Vector3(0.12f, 0.96f, 1.0f),
                new Vector3(0.24f, 0.12f, 0.21f), _material, new Color(0.25f, 0.33f, 0.40f));
            Printer = printer.GetComponent<Collider>();
            _printerOutput = printer.transform.position + new Vector3(0f, 0.063f, -0.048f);
            AddFrontText(transform, printer.transform.position + new Vector3(0f, 0.025f, -0.107f), "ПЕЧАТЬ", 0.017f, Color.white, new Vector2(0.21f, 0.045f));
            GameObject dispatch = CreateBlock("Dispatch zone", transform, new Vector3(2.70f, 0.40f, 0.56f),
                new Vector3(0.55f, 0.8f, 0.55f), _material, new Color(0.36f, 0.54f, 0.42f));
            Dispatch = dispatch.GetComponent<Collider>();
            dispatch.transform.rotation = TowardRoom;
            AddSign(transform, dispatch.transform.position + TowardRoom * new Vector3(0f, 0.31f, -0.277f),
                TowardRoom, "ОТПРАВКА", 0.025f, Color.white, new Vector2(0.47f, 0.060f));
            BuildScale();
        }

        private void BuildScale()
        {
            var position = new Vector3(2.70f, 0.4f, -0.55f);
            CreateBlock("Scale stand", transform, position, new Vector3(0.54f, 0.8f, 0.54f),
                _material, new Color(0.25f, 0.34f, 0.47f)).transform.rotation = TowardRoom;
            Scale = CreateBlock("Scale platform", transform, position + Vector3.up * 0.423f,
                new Vector3(0.51f, 0.046f, 0.49f), _material, new Color(0.62f, 0.72f, 0.78f)).GetComponent<Collider>();
            Scale.transform.rotation = TowardRoom;
            AddSign(transform, position + TowardRoom * new Vector3(0f, 0.33f, -0.272f),
                TowardRoom, "ВЕСЫ", 0.032f, Color.white, new Vector2(0.47f, 0.056f));
            _scaleDisplay = AddSign(transform, position + TowardRoom * new Vector3(0f, 0.25f, -0.272f),
                TowardRoom, "—", 0.024f,
                new Color(0.66f, 1f, 0.83f), new Vector2(0.47f, 0.046f));
        }

        public string ScaleReading
        {
            get
            {
                ShippingBox box = BoxOnScale();
                return box == null ? "Весы пусты" : $"{box.WeightGrams} г   ·   Цена: {box.Price}";
            }
        }

        public bool TryWeighBox(ShippingBox box, out string notice)
        {
            Bounds bounds = Scale.bounds;
            var position = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            if (Physics.CheckBox(position + Vector3.up * 0.07f, new Vector3(0.24f, 0.06f, 0.2f),
                Quaternion.identity, 1, QueryTriggerInteraction.Ignore))
            {
                notice = "Весы заняты";
                return false;
            }

            box.Item.Place(position);
            notice = $"{box.WeightGrams} г   ·   Цена: {box.Price}";
            Sounds.Play(WorkshopSounds.Cue.Pack);
            return true;
        }

        private ShippingBox BoxOnScale()
        {
            Bounds platform = Scale.bounds;
            foreach (ShippingBox box in _boxes)
            {
                if (box == null || !box.isActiveAndEnabled || !box.Item.IsResting)
                {
                    continue;
                }

                Vector3 position = box.transform.position;
                if (Mathf.Abs(position.y - platform.max.y) < 0.045f &&
                    Mathf.Abs(position.x - platform.center.x) < platform.extents.x &&
                    Mathf.Abs(position.z - platform.center.z) < platform.extents.z &&
                    Vector3.Dot(box.transform.up, Vector3.up) > 0.85f)
                {
                    return box;
                }
            }

            return null;
        }

        private void UpdateScale()
        {
            int weight = BoxOnScale()?.WeightGrams ?? -1;
            if (weight != _displayedWeight)
            {
                _displayedWeight = weight;
                _scaleDisplay.Text = weight < 0 ? "—" : ScaleReading;
            }
        }

        internal string NextStep(Carryable held)
        {
            string next = Growth.NextStep(held);
            if (next != null)
            {
                return next;
            }

            if (!Growth.HarvestReady)
            {
                return null;
            }

            if (Growth.HasUncutMushrooms)
            {
                return "Возьмите нож и срежьте грибы.";
            }

            if (Growth.HasMushroomsOnPlatform || held != null && held.Kind == Carryable.ItemKind.Mushroom)
            {
                return "Переложите грибы с площадки в открытую коробку.";
            }

            foreach (ShippingBox box in _boxes)
            {
                if (box == null || !box.isActiveAndEnabled || box.Price == 0)
                {
                    continue;
                }

                if (!box.IsClosed)
                {
                    return "Закройте клапаны, начиная с выделенного. Весы можно пропустить.";
                }

                if (!box.HasLabel)
                {
                    return "Напечатайте этикетку, возьмите её и наклейте на коробку.";
                }

                return "Поднимите коробку и поставьте её в зону отправки.";
            }

            return "Утилизируйте отработанный субстрат. Возьмите лоток и нажмите над баком.";
        }

        public bool TryCut(Carryable mushroom)
        {
            return Growth.TryCut(mushroom);
        }

        public ShippingBox FindReceivingBox(Ray aim, RaycastHit obstruction)
        {
            ShippingBox closest = null;
            float nearest = MushroomInteraction.RangeMeters;
            foreach (ShippingBox box in _boxes)
            {
                if (box != null && box.isActiveAndEnabled &&
                    box.CanReceive(aim, nearest, obstruction, out float distance))
                {
                    closest = box;
                    nearest = distance;
                }
            }

            return closest;
        }

        public void PrintLabel()
        {
            if (IsOutputOccupied)
            {
                return;
            }

            _printedLabel = MakeLabel($"label/{_nextLabel++}");
            Sounds.Play(WorkshopSounds.Cue.Printer);
        }

        private Carryable MakeLabel(string id)
        {
            var label = new GameObject("Printed label");
            label.transform.position = _printerOutput;
            Transform paper = CreateBlock("Paper", label.transform, Vector3.zero,
                new Vector3(0.085f, 0.006f, 0.055f), _material, new Color(0.98f, 0.96f, 0.86f)).transform;
            var item = label.AddComponent<Carryable>();
            item.Configure(Carryable.ItemKind.Label, 0.045f, _roomBounds, _printerOutput, paper.GetComponent<Renderer>(), id);
            return item;
        }

        public bool TryDispatchBox(ShippingBox box, out string notice)
        {
            string refusal = box.ShippingRefusal;
            if (box.TryShip(out int payment))
            {
                Money += payment;
                Sounds.Play(WorkshopSounds.Cue.Sale);
                notice = $"Продано за {payment}";
                return true;
            }

            Bounds zone = Dispatch.bounds;
            var position = new Vector3(zone.center.x, zone.max.y, zone.center.z);
            if (Physics.CheckBox(position + Vector3.up * 0.07f, new Vector3(0.24f, 0.06f, 0.2f),
                Quaternion.identity, 1, QueryTriggerInteraction.Ignore))
            {
                notice = "Зона отправки занята";
                return false;
            }

            box.Item.Place(position);
            notice = refusal;
            return true;
        }

        public static GameObject CreateBlock(string objectName, Transform parent, Vector3 position, Vector3 size,
            Material material, Color? color = null)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = objectName;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = size;
            Renderer renderer = block.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            if (color.HasValue)
            {
                var properties = new MaterialPropertyBlock();
                properties.SetColor(ColorProperty, color.Value);
                renderer.SetPropertyBlock(properties);
            }

            return block;
        }

        internal static WorkshopSign AddSign(Transform parent, Vector3 worldPosition, Quaternion worldRotation,
            string text, float size, Color color, Vector2 limits)
        {
            var caption = new GameObject("Caption");
            caption.transform.SetParent(parent, false);
            caption.transform.SetPositionAndRotation(worldPosition, worldRotation);
            var sign = caption.AddComponent<WorkshopSign>();
            sign.Configure(text, size, color, limits);
            return sign;
        }

        internal static WorkshopSign AddFrontText(Transform parent, Vector3 position, string text, float size,
            Color color, Vector2 limits)
        {
            return AddSign(parent, position, Quaternion.identity, text, size, color, limits);
        }

        internal Transform CreatePlacementMarker()
        {
            var root = new GameObject("Placement marker");
            root.transform.SetParent(transform, false);
            var markerColor = new Color(0.31f, 0.95f, 0.64f);
            for (int side = -1; side <= 1; side += 2)
            {
                CreateBlock("Horizontal", root.transform, Vector3.forward * (side * 0.5f),
                    new Vector3(1f, 0.003f, 0.045f), _material, markerColor).GetComponent<Collider>().enabled = false;
                CreateBlock("Vertical", root.transform, Vector3.right * (side * 0.5f),
                    new Vector3(0.045f, 0.003f, 1f), _material, markerColor).GetComponent<Collider>().enabled = false;
            }

            root.SetActive(false);
            return root.transform;
        }
    }
}
