using System;

namespace Sporeborn.Gameplay.Growing
{
    public enum Portion { Base, Straw, Premix, Water, Culture }
    public enum Station { Treatment, Incubation, Fruiting }
    public enum TrayStage { Empty, Mixed, Treated, Inoculated, Colonized, Fruiting, Harvest }

    public sealed class GrowthCycle
    {
        private const float TreatmentSeconds = 10f;
        private const float IncubationSeconds = 30f;
        private const float FruitingSeconds = 45f;
        private const float LegacyIncubationSeconds = 60f;
        private const float LegacyFruitingSeconds = 120f;

        private static readonly int[] Recipe = { 4, 1, 1, 2 };
        private readonly int[] _stock = { 12, 3, 3, 6, 3 };
        private readonly int[] _mix = new int[Recipe.Length];
        private readonly bool[] _doors = new bool[3];
        private readonly bool[] _finished = new bool[3];

        public const float MixingSeconds = 5f;
        public TrayStage Stage { get; private set; }
        public Station? Dock { get; private set; }
        public float Remaining { get; private set; }
        public float Mixing { get; private set; }
        public bool LidClosed { get; private set; }
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
        public bool IsOpen(Station station) => _doors[(int)station];
        public bool IsWorking(Station station) => Dock == station && Remaining > 0f;
        public bool IsDone(Station station) => _finished[(int)station];

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

        [Serializable]
        public sealed class Snapshot
        {
            public int[] stock;
            public int[] mix;
            public bool[] doors;
            public bool[] finished;
            public TrayStage stage;
            public int dock;
            public float remaining;
            public float mixing;
            public bool lidClosed;
            public bool jarOpen;
        }

        public Snapshot Capture() => new Snapshot
        {
            stock = (int[])_stock.Clone(),
            mix = (int[])_mix.Clone(),
            doors = (bool[])_doors.Clone(),
            finished = (bool[])_finished.Clone(),
            stage = Stage,
            dock = Dock.HasValue ? (int)Dock.Value : -1,
            remaining = Remaining,
            mixing = Mixing,
            lidClosed = LidClosed,
            jarOpen = JarOpen
        };

        public bool TryRestore(Snapshot state)
        {
            if (state == null || state.stock?.Length != _stock.Length || state.mix?.Length != _mix.Length ||
                state.doors?.Length != _doors.Length || state.finished?.Length != _finished.Length ||
                !Enum.IsDefined(typeof(TrayStage), state.stage) || state.dock < -1 || state.dock >= _doors.Length ||
                !(state.remaining >= 0f && state.remaining <= LegacyFruitingSeconds) ||
                !(state.mixing >= 0f && state.mixing <= MixingSeconds))
            {
                return false;
            }

            for (int index = 0; index < state.stock.Length; index++)
            {
                if (state.stock[index] < 0 ||
                    (index < Recipe.Length && (state.mix[index] < 0 || state.mix[index] > Recipe[index])))
                {
                    return false;
                }
            }

            if (state.dock < 0)
            {
                if (state.remaining > 0f || state.stage == TrayStage.Fruiting)
                {
                    return false;
                }
            }
            else
            {
                int dock = state.dock;
                TrayStage expected = dock == 0 ? TrayStage.Mixed : dock == 1 ? TrayStage.Inoculated
                    : state.remaining > 0f ? TrayStage.Fruiting : TrayStage.Colonized;
                TrayStage completed = dock == 0 ? TrayStage.Treated : dock == 1 ? TrayStage.Colonized : TrayStage.Harvest;
                float maxSavedDuration = (Station)dock switch
                {
                    Station.Treatment => TreatmentSeconds,
                    Station.Incubation => LegacyIncubationSeconds,
                    _ => LegacyFruitingSeconds
                };
                if (state.stage != (state.finished[dock] ? completed : expected) || state.lidClosed != (dock != 2) ||
                    (state.remaining > 0f && (state.doors[dock] || state.finished[dock] || state.remaining > maxSavedDuration)))
                {
                    return false;
                }
            }

            Array.Copy(state.stock, _stock, _stock.Length);
            Array.Copy(state.mix, _mix, _mix.Length);
            Array.Copy(state.doors, _doors, _doors.Length);
            Array.Copy(state.finished, _finished, _finished.Length);
            Stage = state.stage;
            Dock = state.dock < 0 ? null : (Station)state.dock;
            Remaining = state.remaining;
            Mixing = state.mixing;
            LidClosed = state.lidClosed;
            JarOpen = state.jarOpen;
            return true;
        }

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

        public bool TryFillTray()
        {
            if (!MixReady || Stage != TrayStage.Empty || LidClosed || Dock.HasValue)
            {
                return false;
            }

            DiscardMix();
            Stage = TrayStage.Mixed;
            return true;
        }

        public bool TrySetLid(bool closed)
        {
            if (LidClosed == closed || Dock.HasValue)
            {
                return false;
            }

            LidClosed = closed;
            return true;
        }

        public void ToggleJar() => JarOpen = !JarOpen;

        public bool TryInoculate()
        {
            if (Stage != TrayStage.Treated || LidClosed || Dock.HasValue || Stock(Portion.Culture) == 0)
            {
                return false;
            }

            _stock[(int)Portion.Culture]--;
            Stage = TrayStage.Inoculated;
            return true;
        }

        public bool TryToggleDoor(Station station)
        {
            if (IsWorking(station))
            {
                return false;
            }

            _doors[(int)station] = !IsOpen(station);
            return true;
        }

        public bool HasCorrectTray(Station station)
        {
            return station switch
            {
                Station.Treatment => Stage == TrayStage.Mixed && LidClosed,
                Station.Incubation => Stage == TrayStage.Inoculated && LidClosed,
                Station.Fruiting => Stage == TrayStage.Colonized && !LidClosed,
                _ => false
            };
        }

        public bool TryLoad(Station station)
        {
            if (Dock.HasValue || !IsOpen(station) || !HasCorrectTray(station))
            {
                return false;
            }

            Dock = station;
            _finished[(int)station] = false;
            return true;
        }

        public bool TryUnload(Station station)
        {
            if (Dock != station || !IsOpen(station) || IsWorking(station))
            {
                return false;
            }

            Dock = null;
            return true;
        }

        public bool TryStart(Station station)
        {
            if (Dock != station || IsOpen(station) || Remaining > 0f || !HasCorrectTray(station))
            {
                return false;
            }

            Remaining = station switch
            {
                Station.Treatment => TreatmentSeconds,
                Station.Incubation => IncubationSeconds,
                _ => FruitingSeconds
            };
            if (station == Station.Fruiting)
            {
                Stage = TrayStage.Fruiting;
            }

            return true;
        }

        public void Tick(float seconds)
        {
            if (!Dock.HasValue || Remaining <= 0f || seconds <= 0f)
            {
                return;
            }

            Remaining = Math.Max(0f, Remaining - seconds);
            if (Remaining > 0f)
            {
                return;
            }

            var station = Dock.Value;
            _finished[(int)station] = true;
            Stage = station switch
            {
                Station.Treatment => TrayStage.Treated,
                Station.Incubation => TrayStage.Colonized,
                _ => TrayStage.Harvest
            };
        }

        public bool TryDiscardTray()
        {
            if (Stage != TrayStage.Harvest || Dock.HasValue || LidClosed)
            {
                return false;
            }

            Stage = TrayStage.Empty;
            return true;
        }
    }
}
