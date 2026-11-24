using UnityEngine;

namespace Sporeborn.Runtime
{
    public sealed partial class WorkshopBatch : MonoBehaviour
    {
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        [SerializeField] private Carryable _mushroomTemplate;
        [SerializeField] private ShippingBox _boxTemplate;

        private readonly ShippingBox[] _boxes = new ShippingBox[3];
        private BoxCollider _roomBounds;
        private Material _material;
        private Carryable _printedLabel;
        private Vector3 _printerOutput;
        private int _nextLabel = 1;

        public WorkshopSession Session { get; private set; }
        public WorkshopGrowth Growth { get; private set; }
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
            Growth = new WorkshopGrowth(transform, _mushroomTemplate, _material, _roomBounds, harvestPosition);
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
            }
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
            AddStationText(transform, printer.transform.position + Vector3.up * 0.061f, "ПЕЧАТЬ");
            GameObject dispatch = CreateBlock("Dispatch zone", transform, new Vector3(1.88f, 0.40f, 0.56f),
                new Vector3(0.55f, 0.8f, 0.55f), _material, new Color(0.36f, 0.54f, 0.42f));
            Dispatch = dispatch.GetComponent<Collider>();
            AddStationText(transform, dispatch.transform.position + new Vector3(0f, 0.401f, -0.21f), "ОТПРАВКА");
        }

        public bool TryCut(Carryable mushroom)
        {
            return Growth.TryCut(mushroom);
        }

        public ShippingBox FindReceivingBox(Ray aim, float obstruction)
        {
            ShippingBox closest = null;
            float nearest = obstruction;
            foreach (ShippingBox box in _boxes)
            {
                if (box != null && box.isActiveAndEnabled &&
                    box.CanReceive(aim, MushroomInteraction.RangeMeters, nearest, out float distance))
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

        internal static void AddStationText(Transform parent, Vector3 position, string text, float size = 0.004f)
        {
            var sign = new GameObject(text);
            sign.transform.SetParent(parent, false);
            sign.transform.position = position;
            AddText(sign.transform, text, size, Color.white);
        }

        public static void AddText(Transform parent, string text, float size, Color color)
        {
            var caption = new GameObject("Caption");
            caption.transform.SetParent(parent, false);
            caption.transform.SetLocalPositionAndRotation(Vector3.up * 0.004f, Quaternion.Euler(90f, 0f, 0f));
            TextMesh mesh = caption.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontSize = 32;
            mesh.characterSize = size;
            mesh.color = color;
        }
    }
}
