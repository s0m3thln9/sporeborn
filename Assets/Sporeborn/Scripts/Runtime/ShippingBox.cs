using Sporeborn.Gameplay.Packaging;
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

        public bool CanReceive(Ray aim, float rangeMeters, float obstructionDistance)
        {
            if (!_contents.HasSpace || Vector3.Dot(aim.direction, transform.up) >= 0f)
            {
                return false;
            }

            var opening = new Plane(transform.up, transform.TransformPoint(0f, _openingHeight, 0f));

            if (!opening.Raycast(aim, out float distance) || distance > rangeMeters || distance > obstructionDistance)
            {
                return false;
            }

            Vector3 point = transform.InverseTransformPoint(aim.GetPoint(distance));
            return Mathf.Abs(point.x) <= _openingSize.x * 0.5f + _placementTolerance
                && Mathf.Abs(point.z) <= _openingSize.y * 0.5f + _placementTolerance;
        }

        public bool TryReceive(LooseMushroom mushroom)
        {
            if (!mushroom.IsHeld || !_contents.TryAdd(out int column, out int row))
            {
                return false;
            }

            float x = ((column + 0.5f) / ShippingBoxContents.Columns - 0.5f) * _openingSize.x;
            float z = ((row + 0.5f) / ShippingBoxContents.Rows - 0.5f) * _openingSize.y;
            mushroom.Store(transform, new Vector3(x, _mushroomCenterHeight, z));
            return true;
        }
    }
}
