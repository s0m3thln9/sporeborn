namespace Sporeborn.Gameplay.Packaging
{
    public sealed class ShippingBoxContents
    {
        public const int Columns = 5;
        public const int Rows = 4;
        public const int FlapCount = 4;
        private const int PricePerMushroom = 10;
        private const int GramsPerMushroom = 25;

        public int Count { get; private set; }
        public int ClosedFlaps { get; private set; }
        public int Price => Count * PricePerMushroom;
        public int WeightGrams => Count * GramsPerMushroom;
        public bool IsClosed => ClosedFlaps == FlapCount;
        public bool HasLabel { get; private set; }
        public bool IsShipped { get; private set; }
        public bool HasSpace => ClosedFlaps == 0 && Count < Columns * Rows;

        public bool TryAdd(out int column, out int row)
        {
            column = Count % Columns;
            row = Count / Columns;

            if (!HasSpace)
            {
                return false;
            }

            Count++;
            return true;
        }

        public bool TryCloseFlap(int flap)
        {
            if (IsClosed || flap != ClosedFlaps)
            {
                return false;
            }

            ClosedFlaps++;
            return true;
        }

        public bool TryLabel()
        {
            if (!IsClosed || HasLabel)
            {
                return false;
            }

            HasLabel = true;
            return true;
        }

        public ShipmentStatus Shipment => IsShipped ? ShipmentStatus.AlreadyShipped
            : Count == 0 ? ShipmentStatus.Empty
            : !IsClosed ? ShipmentStatus.Open
            : !HasLabel ? ShipmentStatus.Unlabeled
            : ShipmentStatus.Ready;

        public bool TryShip(out int payment)
        {
            payment = 0;
            if (Shipment != ShipmentStatus.Ready)
            {
                return false;
            }

            IsShipped = true;
            payment = Price;
            return true;
        }

        public enum ShipmentStatus
        {
            Ready,
            Empty,
            Open,
            Unlabeled,
            AlreadyShipped
        }
    }
}
