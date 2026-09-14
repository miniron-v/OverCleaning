using System;

namespace OverCleaning.InGame
{
    public sealed class DustBin
    {
        public int Capacity { get; }
        public int StoredCount { get; private set; }
        public int ReservedCount { get; private set; }
        public bool IsFull => StoredCount >= Capacity;
        public bool HasSpace => StoredCount + ReservedCount < Capacity;

        public DustBin(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
        }

        public bool TryReserve()
        {
            if (!HasSpace)
                return false;
            ReservedCount++;
            return true;
        }

        public void CancelReservation()
        {
            if (ReservedCount > 0)
                ReservedCount--;
        }

        public void CompleteReservation()
        {
            if (ReservedCount == 0)
                return;
            ReservedCount--;
            StoredCount++;
        }

        public void Empty()
        {
            StoredCount = 0;
            ReservedCount = 0;
        }
    }
}
