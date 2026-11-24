using Sporeborn.Gameplay.Packaging;
using Sporeborn.Persistence;
using UnityEngine;

namespace Sporeborn.Runtime
{
    public sealed class ShippingBox : MonoBehaviour
    {
        [SerializeField] private Vector2 _openingSize = new Vector2(0.236f, 0.186f);
        [SerializeField] private float _openingHeight = 0.1f;
        [SerializeField] private float _mushroomCenterHeight = 0.032f;
        [SerializeField] private float _placementTolerance = 0.034f;

        private readonly ShippingBoxContents _contents = new ShippingBoxContents();
        private readonly Collider[] _flaps = new Collider[ShippingBoxContents.FlapCount];
        private bool _closingConfirmed;

        public string Summary => $"Грибы: {_contents.Count}/{ShippingBoxContents.Columns * ShippingBoxContents.Rows}   Цена: {_contents.Price}";
        public int WeightGrams => _contents.WeightGrams;
        public int Price => _contents.Price;
        public Carryable Item { get; private set; }
        public Material Material => transform.GetChild(0).GetComponent<Renderer>().sharedMaterial;
        public string LabelPrompt => _contents.HasLabel ? "На коробке уже есть этикетка"
            : _contents.IsClosed ? "ЛКМ   Наклеить этикетку" : "Сначала закройте коробку";
        public string ShippingPrompt => ShippingRefusal == null ? "E   Отправить коробку" : $"E   Поставить · {ShippingRefusal}";
        public string ShippingRefusal => _contents.Shipment switch
        {
            ShippingBoxContents.ShipmentStatus.Ready => null,
            ShippingBoxContents.ShipmentStatus.Empty => "Коробка пуста",
            ShippingBoxContents.ShipmentStatus.Open => "Коробка открыта",
            _ => "Нет этикетки"
        };

        public void Configure(BoxCollider roomBounds, string id)
        {
            Vector3[] positions =
            {
                new Vector3(0f, 0.102f, -0.15f), new Vector3(-0.185f, 0.102f, 0f),
                new Vector3(0f, 0.102f, 0.15f), new Vector3(0.185f, 0.102f, 0f)
            };

            Material material = Material;
            for (int index = 0; index < _flaps.Length; index++)
            {
                Vector3 size = index % 2 == 0 ? new Vector3(0.25f, 0.008f, 0.1f)
                    : new Vector3(0.12f, 0.008f, 0.19f);
                GameObject flap = WorkshopBatch.CreateBlock($"Flap {index + 1}", transform,
                    positions[index], size, material, index == 0 ? new Color(0.9f, 0.68f, 0.38f) : null);
                _flaps[index] = flap.GetComponent<Collider>();
            }

            Item = gameObject.AddComponent<Carryable>();
            Item.Configure(Carryable.ItemKind.Box, 0.16f, roomBounds, transform.position,
                transform.GetChild(0).GetComponent<Renderer>(), id);
        }

        public bool CanReceive(Ray aim, float rangeMeters, RaycastHit obstruction, out float distance)
        {
            distance = 0f;
            if (!_contents.HasSpace || !Item.CanPickUp || Vector3.Dot(aim.direction, transform.up) >= 0f)
            {
                return false;
            }

            var opening = new Plane(transform.up, transform.TransformPoint(0f, _openingHeight, 0f));
            if (!opening.Raycast(aim, out distance) || distance > rangeMeters)
            {
                return false;
            }

            if (obstruction.collider != null && distance > obstruction.distance &&
                (distance - obstruction.distance > _placementTolerance || System.Array.IndexOf(_flaps, obstruction.collider) < 0))
            {
                return false;
            }

            Vector3 point = transform.InverseTransformPoint(aim.GetPoint(distance));
            return Mathf.Abs(point.x) <= _openingSize.x * 0.5f + _placementTolerance
                && Mathf.Abs(point.z) <= _openingSize.y * 0.5f + _placementTolerance;
        }

        public bool TryReceive(Carryable mushroom)
        {
            if (mushroom.Kind != Carryable.ItemKind.Mushroom || !mushroom.IsHeld ||
                !_contents.TryAdd(out int column, out int row))
            {
                return false;
            }

            float x = ((column + 0.5f) / ShippingBoxContents.Columns - 0.5f) * _openingSize.x;
            float z = ((row + 0.5f) / ShippingBoxContents.Rows - 0.5f) * _openingSize.y;
            mushroom.Store(transform, new Vector3(x, _mushroomCenterHeight, z));
            return true;
        }

        public string FlapPrompt(Collider target)
        {
            int index = System.Array.IndexOf(_flaps, target);
            if (index < _contents.ClosedFlaps)
            {
                return null;
            }

            int next = _contents.ClosedFlaps;
            if (index != next)
            {
                return $"Сначала закройте клапан {next + 1}";
            }

            return next == 0 && !_closingConfirmed ? "ЛКМ   Закрыть · Открыть коробку будет нельзя"
                : $"ЛКМ   Закрыть клапан {next + 1}";
        }

        public void CloseFlap(Collider target)
        {
            int index = System.Array.IndexOf(_flaps, target);
            if (index == 0 && !_closingConfirmed)
            {
                _closingConfirmed = true;
                return;
            }

            if (!_contents.TryCloseFlap(index))
            {
                return;
            }

            Fold(index);
        }

        private void Fold(int index)
        {
            Transform flap = _flaps[index].transform;
            Vector3 position = flap.localPosition;
            position.x *= -0.35f;
            position.z *= -0.35f;
            position.y += (index + 1) * 0.008f;
            flap.localPosition = position;
        }

        public bool TryApplyLabel(Carryable label)
        {
            if (label.Kind != Carryable.ItemKind.Label || !label.IsHeld || !_contents.TryLabel())
            {
                return false;
            }

            label.Store(transform, new Vector3(0f, 0.14f, 0f));
            ShowLabel(label);
            return true;
        }

        private void ShowLabel(Carryable label)
        {
            WorkshopBatch.AddText(label.transform, $"{_contents.Count} шт.  ·  {_contents.Price}", 0.0021f, Color.black);
        }

        internal BoxSave Capture() => new BoxSave { id = Item.Id, flaps = _contents.ClosedFlaps, confirmed = _closingConfirmed };

        internal void RestoreContents(BoxSave save, int mushrooms, Carryable label)
        {
            _closingConfirmed = save.confirmed;
            for (int index = 0; index < mushrooms; index++)
            {
                _contents.TryAdd(out _, out _);
            }

            for (int index = 0; index < save.flaps; index++)
            {
                _contents.TryCloseFlap(index);
                Fold(index);
            }

            if (label != null && _contents.TryLabel())
            {
                ShowLabel(label);
            }
        }

        public bool TryShip(out int payment)
        {
            if (!_contents.TryShip(out payment))
            {
                return false;
            }

            gameObject.SetActive(false);
            Destroy(gameObject);
            return true;
        }
    }
}
