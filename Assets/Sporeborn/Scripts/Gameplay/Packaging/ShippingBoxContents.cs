namespace Sporeborn.Gameplay.Packaging
{
    public sealed class ShippingBoxContents
    {
        public const int Columns = 5;
        public const int Rows = 4;

        private int _count;

        public bool HasSpace => _count < Columns * Rows;

        public bool TryAdd(out int column, out int row)
        {
            column = _count % Columns;
            row = _count / Columns;

            if (!HasSpace)
            {
                return false;
            }

            _count++;
            return true;
        }
    }
}
