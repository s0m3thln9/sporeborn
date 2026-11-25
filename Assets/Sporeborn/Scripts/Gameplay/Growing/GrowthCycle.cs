using System;

namespace Sporeborn.Gameplay.Growing
{
    public enum Portion { Base, Straw, Premix, Water, Culture }
    public enum Station { Treatment, Incubation, Fruiting }
    public enum TrayStage { Empty, Mixed, Treated, Inoculated, Colonized, Fruiting, Harvest }

    public sealed class GrowthCycle
    {
        private static readonly int[] Recipe = { 4, 1, 1, 2 };
        private static readonly float[] Durations = { 10f, 30f, 45f };
        private static readonly float[] SavedDurationLimits = { 10f, 60f, 120f };
        private static readonly TrayStage[] RequiredStages = { TrayStage.Mixed, TrayStage.Inoculated, TrayStage.Colonized };
        private static readonly TrayStage[] CompletedStages = { TrayStage.Treated, TrayStage.Colonized, TrayStage.Harvest };

        private readonly int[] _stock = { 12, 3, 3, 6, 3 };
        private readonly int[] _mix = new int[Recipe.Length];
        private readonly Tray[] _trays = { new Tray(1), null };
        private readonly Device[] _devices = { new Device(), new Device(), new Device() };
        private int _nextCrop = 1;

        private sealed class Device
        {
            public bool IsOpen;
            public Tray Tray;
            public float Remaining;
        }

        public sealed class Tray
        {
            internal Tray(int number)
            {
                Number = number;
            }

            public int Number { get; }
            public TrayStage Stage { get; internal set; }
            public bool LidClosed { get; internal set; }
            public int Crop { get; internal set; }
        }

        public const float MixingSeconds = 5f;
        public const int MaxTrays = 2;
        public const int MushroomCount = 12;
        public float Mixing { get; private set; }
        public bool JarOpen { get; private set; }
        public bool HasMix => Array.Exists(_mix, count => count > 0);
        public bool MixReady => Mixing >= MixingSeconds && _mix[0] == Recipe[0] &&
            _mix[1] == Recipe[1] && _mix[2] == Recipe[2] && _mix[3] == Recipe[3];
        public bool CanRestock
        {
            get
            {
                for (int index = 0; index < _stock.Length; index++)
                {
                    if (_stock[index] > int.MaxValue - BundleAmount(index))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public int Stock(Portion portion) => _stock[(int)portion];
        public int Count(Portion portion) => _mix[(int)portion];
        public Tray TrayAt(int number) => _trays[number - 1];
        public Tray LoadedTray(Station station) => _devices[(int)station].Tray;
        public float Remaining(Station station) => _devices[(int)station].Remaining;
        public bool IsOpen(Station station) => _devices[(int)station].IsOpen;
        public bool IsWorking(Station station) => Remaining(station) > 0f;
        public bool IsDone(Station station) => LoadedTray(station)?.Stage == CompletedStages[(int)station];

        public Station? DockOf(Tray tray)
        {
            if (tray == null)
            {
                return null;
            }

            for (int index = 0; index < _devices.Length; index++)
            {
                if (_devices[index].Tray == tray)
                {
                    return (Station)index;
                }
            }

            return null;
        }

        public Tray TryAddSecondTray()
        {
            if (_trays[1] != null)
            {
                return null;
            }

            _trays[1] = new Tray(2) { LidClosed = true };
            return _trays[1];
        }

        public bool TryRestock()
        {
            if (!CanRestock)
            {
                return false;
            }

            for (int index = 0; index < _stock.Length; index++)
            {
                _stock[index] += BundleAmount(index);
            }

            return true;
        }

        private static int BundleAmount(int index) => index == (int)Portion.Culture ? 1 : Recipe[index];
        private bool Owns(Tray tray) => tray != null && TrayAt(tray.Number) == tray;

        public bool TryAdd(Portion portion)
        {
            int index = (int)portion;
            if (portion == Portion.Culture || _stock[index] == 0 || _mix[index] >= Recipe[index])
            {
                return false;
            }

            _stock[index]--;
            _mix[index]++;
            Mixing = 0f;
            return true;
        }

        public void Mix(float seconds)
        {
            if (HasMix && seconds > 0f)
            {
                Mixing = Math.Min(MixingSeconds, Mixing + seconds);
            }
        }

        public void DiscardMix()
        {
            Array.Clear(_mix, 0, _mix.Length);
            Mixing = 0f;
        }

        public bool TryFillTray(Tray tray)
        {
            if (!Owns(tray) || !MixReady || tray.Stage != TrayStage.Empty || tray.LidClosed || DockOf(tray).HasValue)
            {
                return false;
            }

            DiscardMix();
            tray.Stage = TrayStage.Mixed;
            return true;
        }

        public bool TrySetLid(Tray tray, bool closed)
        {
            if (!Owns(tray) || tray.LidClosed == closed || DockOf(tray).HasValue)
            {
                return false;
            }

            tray.LidClosed = closed;
            return true;
        }

        public void ToggleJar() => JarOpen = !JarOpen;

        public bool TryInoculate(Tray tray)
        {
            if (!Owns(tray) || tray.Stage != TrayStage.Treated || tray.LidClosed || DockOf(tray).HasValue ||
                Stock(Portion.Culture) == 0)
            {
                return false;
            }

            _stock[(int)Portion.Culture]--;
            tray.Stage = TrayStage.Inoculated;
            return true;
        }

        public bool TryToggleDoor(Station station)
        {
            Device device = _devices[(int)station];
            if (device.Remaining > 0f)
            {
                return false;
            }

            device.IsOpen = !device.IsOpen;
            return true;
        }

        public bool HasCorrectTray(Station station, Tray tray) =>
            Owns(tray) && tray.Stage == RequiredStages[(int)station] && tray.LidClosed == (station != Station.Fruiting);

        public bool TryLoad(Station station, Tray tray)
        {
            Device device = _devices[(int)station];
            if (device.Tray != null || !device.IsOpen || !HasCorrectTray(station, tray) || DockOf(tray).HasValue)
            {
                return false;
            }

            device.Tray = tray;
            return true;
        }

        public bool TryUnload(Station station)
        {
            Device device = _devices[(int)station];
            if (device.Tray == null || !device.IsOpen || device.Remaining > 0f)
            {
                return false;
            }

            device.Tray = null;
            return true;
        }

        public bool TryStart(Station station)
        {
            Device device = _devices[(int)station];
            if (device.IsOpen || device.Remaining > 0f || !HasCorrectTray(station, device.Tray) ||
                (station == Station.Fruiting && _nextCrop == int.MaxValue))
            {
                return false;
            }

            device.Remaining = Durations[(int)station];
            if (station == Station.Fruiting)
            {
                device.Tray.Stage = TrayStage.Fruiting;
                device.Tray.Crop = _nextCrop++;
            }

            return true;
        }

        public void Tick(float seconds)
        {
            if (!(seconds > 0f))
            {
                return;
            }

            for (int index = 0; index < _devices.Length; index++)
            {
                Device device = _devices[index];
                if (device.Remaining <= 0f)
                {
                    continue;
                }

                device.Remaining = Math.Max(0f, device.Remaining - seconds);
                if (device.Remaining == 0f)
                {
                    device.Tray.Stage = CompletedStages[index];
                }
            }
        }

        public bool TryDiscardTray(Tray tray)
        {
            if (!Owns(tray) || tray.Stage != TrayStage.Harvest || DockOf(tray).HasValue || tray.LidClosed)
            {
                return false;
            }

            tray.Stage = TrayStage.Empty;
            tray.Crop = 0;
            return true;
        }

        [Serializable]
        public sealed class Snapshot
        {
            public int[] stock;
            public int[] mix;
            public TraySnapshot[] trays;
            public DeviceSnapshot[] devices;
            public int nextCrop;
            public float mixing;
            public bool jarOpen;
        }

        [Serializable]
        public sealed class TraySnapshot
        {
            public TrayStage stage;
            public bool lidClosed;
            public int crop;
        }

        [Serializable]
        public sealed class DeviceSnapshot
        {
            public bool open;
            public int trayNumber;
            public float remaining;
        }

        public Snapshot Capture()
        {
            var snapshot = new Snapshot
            {
                stock = (int[])_stock.Clone(),
                mix = (int[])_mix.Clone(),
                trays = new TraySnapshot[_trays[1] == null ? 1 : MaxTrays],
                devices = new DeviceSnapshot[_devices.Length],
                nextCrop = _nextCrop,
                mixing = Mixing,
                jarOpen = JarOpen
            };

            for (int index = 0; index < snapshot.trays.Length; index++)
            {
                Tray tray = _trays[index];
                snapshot.trays[index] = new TraySnapshot { stage = tray.Stage, lidClosed = tray.LidClosed, crop = tray.Crop };
            }

            for (int index = 0; index < _devices.Length; index++)
            {
                Device device = _devices[index];
                snapshot.devices[index] = new DeviceSnapshot
                {
                    open = device.IsOpen, trayNumber = device.Tray?.Number ?? 0, remaining = device.Remaining
                };
            }

            return snapshot;
        }

        public bool TryRestore(Snapshot snapshot)
        {
            if (!IsValid(snapshot))
            {
                return false;
            }

            Array.Copy(snapshot.stock, _stock, _stock.Length);
            Array.Copy(snapshot.mix, _mix, _mix.Length);
            Array.Clear(_trays, 0, _trays.Length);
            for (int index = 0; index < snapshot.trays.Length; index++)
            {
                TraySnapshot tray = snapshot.trays[index];
                _trays[index] = new Tray(index + 1) { Stage = tray.stage, LidClosed = tray.lidClosed, Crop = tray.crop };
            }

            for (int index = 0; index < _devices.Length; index++)
            {
                DeviceSnapshot device = snapshot.devices[index];
                _devices[index].IsOpen = device.open;
                _devices[index].Tray = device.trayNumber == 0 ? null : TrayAt(device.trayNumber);
                _devices[index].Remaining = device.remaining;
            }

            _nextCrop = snapshot.nextCrop;
            Mixing = snapshot.mixing;
            JarOpen = snapshot.jarOpen;
            return true;
        }

        public static bool IsValid(Snapshot snapshot)
        {
            if (snapshot == null || snapshot.stock?.Length != Recipe.Length + 1 || snapshot.mix?.Length != Recipe.Length ||
                snapshot.trays == null || snapshot.trays.Length < 1 || snapshot.trays.Length > MaxTrays ||
                snapshot.devices?.Length != Durations.Length || snapshot.nextCrop < 1 ||
                !(snapshot.mixing >= 0f && snapshot.mixing <= MixingSeconds))
            {
                return false;
            }

            for (int index = 0; index < snapshot.stock.Length; index++)
            {
                if (snapshot.stock[index] < 0 ||
                    (index < Recipe.Length && (snapshot.mix[index] < 0 || snapshot.mix[index] > Recipe[index])))
                {
                    return false;
                }
            }

            for (int index = 0; index < snapshot.trays.Length; index++)
            {
                TraySnapshot tray = snapshot.trays[index];
                if (tray == null || !Enum.IsDefined(typeof(TrayStage), tray.stage) ||
                    (tray.stage >= TrayStage.Fruiting ? tray.crop < 1 || tray.crop >= snapshot.nextCrop : tray.crop != 0) ||
                    (tray.crop != 0 && index > 0 && snapshot.trays[0].crop == tray.crop))
                {
                    return false;
                }
            }

            var docked = new bool[snapshot.trays.Length];
            for (int index = 0; index < snapshot.devices.Length; index++)
            {
                DeviceSnapshot device = snapshot.devices[index];
                if (device == null || device.trayNumber < 0 || device.trayNumber > snapshot.trays.Length ||
                    !(device.remaining >= 0f && device.remaining <= SavedDurationLimits[index]))
                {
                    return false;
                }

                if (device.trayNumber == 0)
                {
                    if (device.remaining != 0f)
                    {
                        return false;
                    }

                    continue;
                }

                int trayIndex = device.trayNumber - 1;
                TraySnapshot tray = snapshot.trays[trayIndex];
                bool active = device.remaining > 0f;
                bool fruitingDevice = index == (int)Station.Fruiting;
                TrayStage expected = active && fruitingDevice ? TrayStage.Fruiting : RequiredStages[index];
                if (docked[trayIndex] || tray.lidClosed == fruitingDevice || (active && device.open) ||
                    (tray.stage != expected && (active || tray.stage != CompletedStages[index])))
                {
                    return false;
                }

                docked[trayIndex] = true;
            }

            for (int index = 0; index < docked.Length; index++)
            {
                if (!docked[index] && snapshot.trays[index].stage == TrayStage.Fruiting)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
