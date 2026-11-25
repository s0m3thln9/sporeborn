using System.Collections.Generic;
using Sporeborn.Gameplay.Growing;
using Sporeborn.Persistence;
using UnityEngine;
using Kind = Sporeborn.Runtime.Carryable.ItemKind;

namespace Sporeborn.Runtime
{
    public sealed class WorkshopGrowth
    {
        private static readonly string[] Portions = { "Основа", "Солома", "Премикс", "Вода", "Мицелий" };
        private static readonly string[] Stations = { "Обработка", "Инкубатор", "Камера роста" };
        private static readonly Color Soil = new Color(0.25f, 0.16f, 0.08f);
        private static readonly Vector3 LidPosition = new Vector3(0f, 0.105f, 0.11f);

        private readonly GrowthCycle _cycle = new GrowthCycle();
        private readonly Dictionary<Collider, Portion> _sources = new Dictionary<Collider, Portion>();
        private readonly Dictionary<Collider, Station> _doors = new Dictionary<Collider, Station>();
        private readonly Dictionary<Collider, Station> _slots = new Dictionary<Collider, Station>();
        private readonly Dictionary<Collider, Station> _starts = new Dictionary<Collider, Station>();
        private readonly Transform[] _doorPanels = new Transform[3];
        private readonly WorkshopSign[] _deviceDisplays = new WorkshopSign[3];
        private readonly WorkshopSounds _sounds;
        private readonly Transform[] _docks = new Transform[3];
        private readonly Carryable _template;
        private readonly Transform _room;
        private readonly BoxCollider _bounds;
        private readonly Material _material;
        private readonly GrowingTray[] _trays = new GrowingTray[GrowthCycle.MaxTrays];
        private readonly GameObject _mixture;
        private readonly Collider _waste;
        private readonly Transform _jarLid;
        private Portion? _scoop;
        private bool _cupFull;
        private bool _cultureLoaded;

        private sealed class GrowingTray
        {
            public GrowthCycle.Tray State;
            public Carryable Item;
            public Carryable Lid;
            public GameObject Substrate;
            public bool? CropCovered;
            public readonly Carryable[] Crop = new Carryable[GrowthCycle.MushroomCount];

            public int Number => State.Number;
            public string Name => $"Лоток №{Number}";
            public bool HasUncutMushrooms => System.Array.Exists(Crop, mushroom => mushroom != null && mushroom.NeedsCut);
            public bool HasMushroomsOnPlatform => System.Array.Exists(Crop,
                mushroom => mushroom != null && mushroom.transform.parent == Item.transform);
        }

        public WorkshopGrowth(Transform room, Carryable template, Material material, BoxCollider bounds, Vector3 platform, WorkshopSounds sounds)
        {
            _room = room;
            _template = template;
            _material = material;
            _bounds = bounds;
            _sounds = sounds;

            Block("Preparation bench", room, new Vector3(-1.97f, 0.44f, -0.27f),
                new Vector3(0.9f, 0.88f, 1.79f), new Color(0.38f, 0.30f, 0.24f));
            WorkshopBatch.AddSign(room, new Vector3(-1.518f, 0.71f, -0.27f), Quaternion.Euler(0f, -90f, 0f),
                "ПОДГОТОВКА", 0.038f, Color.white, new Vector2(0.76f, 0.11f));
            for (int index = 0; index < Portions.Length; index++)
            {
                float z = -1f + index * 0.35f;
                var source = Block(Portions[index], room, new Vector3(-2.19f, 0.96f, z),
                    new Vector3(0.26f, 0.16f, 0.24f),
                    index == 3 ? new Color(0.36f, 0.63f, 0.79f) : new Color(0.62f, 0.52f, 0.36f));
                _sources.Add(source.GetComponent<Collider>(), (Portion)index);
                WorkshopBatch.AddSign(room, new Vector3(-2.058f, 0.96f, z), Quaternion.Euler(0f, -90f, 0f),
                    Portions[index], 0.023f, Color.black, new Vector2(0.206f, 0.066f));
            }

            _jarLid = Block("Culture jar lid", room, new Vector3(-2.19f, 1.048f, 0.4f),
                new Vector3(0.27f, 0.016f, 0.25f), new Color(0.37f, 0.29f, 0.23f)).transform;
            _sources.Add(_jarLid.GetComponent<Collider>(), Portion.Culture);

            MakeItem(Kind.Scoop, new Vector3(-1.75f, 0.900f, -0.92f), new Vector3(0.08f, 0.04f, 0.20f),
                new Color(0.68f, 0.69f, 0.72f));
            MakeItem(Kind.Cup, new Vector3(-1.75f, 0.924f, -0.63f), new Vector3(0.095f, 0.088f, 0.095f),
                new Color(0.33f, 0.64f, 0.77f));
            MakeItem(Kind.Mixer, new Vector3(-1.73f, 0.901f, 0.48f), new Vector3(0.035f, 0.042f, 0.22f),
                new Color(0.73f, 0.48f, 0.27f));
            MakeItem(Kind.Inoculator, new Vector3(-1.74f, 0.904f, 0.19f), new Vector3(0.044f, 0.048f, 0.16f),
                new Color(0.80f, 0.77f, 0.53f));
            Carryable bowl = MakeItem(Kind.Bowl, new Vector3(-1.76f, 0.917f, -0.23f),
                new Vector3(0.27f, 0.074f, 0.25f), new Color(0.18f, 0.42f, 0.35f));
            WorkshopBatch.AddSign(bowl.transform, bowl.transform.TransformPoint(0.137f, 0f, 0f),
                Quaternion.Euler(0f, -90f, 0f), "СМЕСЬ", 0.021f, Color.white, new Vector2(0.215f, 0.045f));
            _mixture = Surface("Mixture", bowl.transform, Vector3.up * 0.039f, new Vector3(0.24f, 0.012f, 0.22f));

            Vector3 trayPosition = platform + new Vector3(0f, -0.022f, 0.11f);
            BuildTrayPair(_cycle.TrayAt(1), trayPosition, new Vector3(-0.75f, 0.915f, 0.95f));
            _waste = Block("Disposal", room, new Vector3(-2.04f, 0.37f, 1.16f),
                new Vector3(0.48f, 0.74f, 0.40f), new Color(0.32f, 0.29f, 0.28f)).GetComponent<Collider>();
            WorkshopBatch.AddFrontText(room, new Vector3(-2.04f, 0.66f, 0.958f),
                "Утилизация", 0.026f, Color.white, new Vector2(0.43f, 0.067f));
            for (int index = 0; index < Stations.Length; index++)
            {
                BuildDevice((Station)index, new Vector3(-1.57f + index * 1.57f, 0f, 2.69f));
            }
        }

        public void Tick(float seconds)
        {
            TrayStage firstStage = _trays[0].State.Stage;
            TrayStage? secondStage = _trays[1]?.State.Stage;
            _cycle.Tick(seconds);
            UpdateTrayAfterTick(_trays[0], firstStage);
            if (secondStage.HasValue)
            {
                UpdateTrayAfterTick(_trays[1], secondStage.Value);
            }

            UpdateDeviceDisplays();
            RefreshCropAccess();
        }

        private void UpdateTrayAfterTick(GrowingTray tray, TrayStage before)
        {
            if (before != tray.State.Stage)
            {
                RefreshTray(tray);
                _sounds.Play(WorkshopSounds.Cue.Finished);
            }
        }

        internal void RefreshCropAccess()
        {
            foreach (GrowingTray tray in _trays)
            {
                if (tray == null || tray.State.Crop == 0)
                {
                    continue;
                }

                bool covered = !tray.Item.IsResting || tray.State.LidClosed || tray.State.Stage != TrayStage.Harvest;
                if (tray.CropCovered == covered)
                {
                    continue;
                }

                tray.CropCovered = covered;
                foreach (Carryable mushroom in tray.Crop)
                {
                    if (mushroom != null && mushroom.transform.parent == tray.Item.transform)
                    {
                        mushroom.SetCovered(covered);
                    }
                }
            }
        }

        internal bool CanRestock => _cycle.CanRestock;
        internal bool TryRestock() => _cycle.TryRestock();
        internal bool HasSecondTray => _cycle.TrayAt(2) != null;
        internal bool HasUncutMushrooms(int number) => _trays[number - 1].HasUncutMushrooms;
        internal bool HasMushroomsOnPlatform(int number) => _trays[number - 1].HasMushroomsOnPlatform;

        internal bool TryAddSecondTray(Vector3 position)
        {
            GrowthCycle.Tray tray = _cycle.TryAddSecondTray();
            if (tray == null)
            {
                return false;
            }

            BuildTrayPair(tray, position, position + LidPosition);
            return true;
        }

        internal static string TrayId(int number) => number == 1 ? "Tray" : $"tray/{number}";
        internal static string LidId(int number) => number == 1 ? "Lid" : $"lid/{number}";
        private static string DockId(Station station) => $"dock/{(int)station}";

        internal string DockIdFor(Carryable item)
        {
            GrowingTray tray = FindTray(item);
            return tray != null && _cycle.DockOf(tray.State) is Station station ? DockId(station) : null;
        }

        internal Transform ResolveDock(Carryable item)
        {
            if (item.Kind != Kind.Tray)
            {
                return null;
            }

            GrowingTray tray = FindTray(item);
            return tray != null && _cycle.DockOf(tray.State) is Station station ? _docks[(int)station] : null;
        }

        internal string TimerReading(Station station) => _cycle.IsWorking(station)
            ? $"{Stations[(int)station]} · лоток №{_cycle.LoadedTray(station).Number} · {Mathf.CeilToInt(_cycle.Remaining(station))} с"
            : null;

        internal string NextStep(Carryable held, Carryable aimed, out int harvestTray)
        {
            harvestTray = 0;
            GrowingTray tray = FindTray(held) ?? FindTray(aimed) ?? FocusTray();
            if (_cycle.DockOf(tray.State) is Station station)
            {
                string device = Stations[(int)station];
                if (_cycle.IsWorking(station))
                {
                    return station == Station.Incubation
                        ? $"{tray.Name} · Поднесите открытую коробку и напечатайте этикетку."
                        : $"{tray.Name} · Дождитесь окончания работы.";
                }

                if (_cycle.IsDone(station))
                {
                    return _cycle.IsOpen(station) ? $"{tray.Name} · выньте лоток с полки."
                        : $"{device} · откройте дверцу лотка №{tray.Number}.";
                }

                return _cycle.IsOpen(station) ? $"{device} · закройте дверцу."
                    : $"{device} · нажмите зелёную кнопку.";
            }

            string next = tray.State.Stage switch
            {
                TrayStage.Empty => PreparationStep(tray, held),
                TrayStage.Mixed => LoadStep(tray, Station.Treatment, true, held),
                TrayStage.Treated => CultureStep(tray, held),
                TrayStage.Inoculated => LoadStep(tray, Station.Incubation, true, held),
                TrayStage.Colonized => LoadStep(tray, Station.Fruiting, false, held),
                TrayStage.Harvest when tray.State.LidClosed => LidStep(tray, false, held),
                TrayStage.Harvest when !tray.Item.IsResting && tray.HasMushroomsOnPlatform => "Поставьте лоток на стол.",
                _ => null
            };
            if (next == null)
            {
                harvestTray = tray.Number;
                return null;
            }

            return $"{tray.Name} · {next}";
        }

        private GrowingTray FocusTray()
        {
            GrowingTray best = _trays[0];
            if (_trays[1] != null && StepPriority(_trays[1]) > StepPriority(best))
            {
                best = _trays[1];
            }

            return best;
        }

        private int StepPriority(GrowingTray tray)
        {
            if (_cycle.DockOf(tray.State) is Station station)
            {
                return _cycle.IsWorking(station) ? 0 : 3;
            }

            return tray.State.Stage == TrayStage.Empty ? 1 : 2;
        }

        private GrowingTray FindTray(Carryable item)
        {
            if (item == null)
            {
                return null;
            }

            foreach (GrowingTray tray in _trays)
            {
                if (tray != null && (item == tray.Item || item == tray.Lid ||
                    (item.Kind == Kind.Mushroom && item.transform.parent == tray.Item.transform)))
                {
                    return tray;
                }
            }

            return null;
        }

        private string PreparationStep(GrowingTray tray, Carryable held)
        {
            if (_cycle.Stock(Portion.Base) < 4 - _cycle.Count(Portion.Base) ||
                _cycle.Stock(Portion.Straw) < 1 - _cycle.Count(Portion.Straw) ||
                _cycle.Stock(Portion.Premix) < 1 - _cycle.Count(Portion.Premix) ||
                _cycle.Stock(Portion.Water) < 2 - _cycle.Count(Portion.Water) ||
                _cycle.Stock(Portion.Culture) == 0)
            {
                return "Запасов для новой партии не хватает. Комплект материалов можно купить на стенде.";
            }

            if (tray.State.LidClosed)
            {
                return LidStep(tray, false, held);
            }

            if (_cycle.MixReady)
            {
                return "Готовая смесь. Возьмите ёмкость и переложите смесь в открытый лоток.";
            }

            if (_cycle.Count(Portion.Base) < 4 || _cycle.Count(Portion.Straw) < 1 || _cycle.Count(Portion.Premix) < 1)
            {
                return "Стол подготовки слева. Возьмите совок. Наберите порции и перенесите их в ёмкость «СМЕСЬ»: 4 основы, 1 соломы и 1 премикса.";
            }

            if (_cycle.Count(Portion.Water) < 2)
            {
                return "Возьмите кружку и добавьте в ёмкость «СМЕСЬ» две порции воды.";
            }

            return "Возьмите мешалку и двигайте мышью над ёмкостью «СМЕСЬ» 5 секунд.";
        }

        private string CultureStep(GrowingTray tray, Carryable held)
        {
            if (tray.State.LidClosed)
            {
                return LidStep(tray, false, held);
            }

            if (!_cycle.JarOpen && !_cultureLoaded)
            {
                return "Положите крышку на стол и откройте банку мицелия.";
            }

            return _cultureLoaded ? "Внесите мицелий в открытый лоток."
                : "Возьмите мерку и наберите мицелий из банки.";
        }

        private string LoadStep(GrowingTray tray, Station station, bool needsLid, Carryable held)
        {
            if (tray.State.LidClosed != needsLid)
            {
                return LidStep(tray, needsLid, held);
            }

            string device = Stations[(int)station];
            if (_cycle.LoadedTray(station) is GrowthCycle.Tray loaded)
            {
                return $"{device} · сначала выньте лоток №{loaded.Number}.";
            }

            if (!_cycle.IsOpen(station))
            {
                return held == tray.Item ? $"Поставьте лоток и откройте дверцу. Дальше · {device}."
                    : $"{device} · откройте дверцу.";
            }

            return $"{device} · возьмите лоток и загрузите его на полке.";
        }

        private string LidStep(GrowingTray tray, bool close, Carryable held)
        {
            if (held == tray.Item)
            {
                return "Поставьте лоток, чтобы получить доступ к крышке.";
            }

            return close ? "Возьмите крышку и закройте лоток."
                : "Наведите прицел на лоток и снимите крышку свободными руками.";
        }

        public bool TryCut(Carryable mushroom)
        {
            GrowingTray tray = FindTray(mushroom);
            if (tray == null || tray.State.Stage != TrayStage.Harvest || _cycle.DockOf(tray.State).HasValue ||
                tray.State.LidClosed)
            {
                return false;
            }

            int index = System.Array.IndexOf(tray.Crop, mushroom);
            return index >= 0 && mushroom.TryCut(MushroomOffset(index) + new Vector3(0f, -0.048f, -0.255f));
        }

        public void Aim(RaycastHit hit, Carryable target, Transform hand, ref Carryable held, bool click,
            float mixingSeconds, out string prompt, out string detail)
        {
            prompt = null;
            detail = null;
            Collider collider = hit.collider;
            if (collider == null)
            {
                return;
            }

            bool nearTool = hit.distance <= MushroomInteraction.ToolRangeMeters;
            if (_sources.TryGetValue(collider, out Portion portion))
            {
                detail = $"{Portions[(int)portion]} · Остаток: {_cycle.Stock(portion)}";
                prompt = SourcePrompt(portion, held, click && (held == null || nearTool));
            }
            else if (target != null && target.Kind == Kind.Bowl)
            {
                detail = $"Основа {_cycle.Count(Portion.Base)}/4 · Солома {_cycle.Count(Portion.Straw)}/1 · Премикс {_cycle.Count(Portion.Premix)}/1 · Вода {_cycle.Count(Portion.Water)}/2";
                prompt = AimAtBowl(held, click && nearTool, nearTool ? mixingSeconds : 0f);
            }
            else if (FindTray(target) is GrowingTray tray)
            {
                if (target == tray.Lid)
                {
                    detail = $"Крышка лотка №{tray.Number}";
                }
                else if (_cycle.DockOf(tray.State) is Station dock)
                {
                    detail = DeviceStatus(dock);
                    prompt = DevicePrompt(dock, false, false, hand, ref held, click);
                }
                else
                {
                    detail = TrayStatus(tray);
                    if (target == tray.Item)
                    {
                        prompt = AimAtTray(tray, hand, ref held, click && (held == null || nearTool));
                    }
                }
            }
            else if (_doors.TryGetValue(collider, out Station station))
            {
                detail = DeviceStatus(station);
                prompt = DevicePrompt(station, true, false, hand, ref held, click);
            }
            else if (_slots.TryGetValue(collider, out station))
            {
                detail = DeviceStatus(station);
                prompt = DevicePrompt(station, false, false, hand, ref held, click);
            }
            else if (_starts.TryGetValue(collider, out station))
            {
                detail = DeviceStatus(station);
                prompt = DevicePrompt(station, false, true, hand, ref held, click);
            }
            else if (collider == _waste)
            {
                prompt = AimAtWaste(held, click && nearTool);
            }

            if (held != null && !nearTool && prompt != null && !_doors.ContainsKey(collider) &&
                !_slots.ContainsKey(collider) && !_starts.ContainsKey(collider))
            {
                prompt = "Подойдите ближе";
            }
        }

        private string SourcePrompt(Portion portion, Carryable held, bool click)
        {
            if (portion == Portion.Culture)
            {
                if (held == null)
                {
                    if (click)
                    {
                        _cycle.ToggleJar();
                        _jarLid.localPosition = new Vector3(-2.19f, 1.048f, _cycle.JarOpen ? 0.535f : 0.4f);
                    }

                    return _cycle.JarOpen ? "ЛКМ   Закрыть банку" : "ЛКМ   Открыть банку";
                }

                if (held.Kind != Kind.Inoculator)
                {
                    return "Для мицелия нужна отдельная мерка";
                }

                if (!_cycle.JarOpen || _cycle.Stock(portion) == 0 || _cultureLoaded)
                {
                    return !_cycle.JarOpen ? "Откройте банку" : _cultureLoaded ? "Мерка уже наполнена" : "Мицелий закончился";
                }

                if (click)
                {
                    _cultureLoaded = true;
                    _sounds.Play(WorkshopSounds.Cue.Portion);
                }

                return "ЛКМ   Набрать порцию мицелия";
            }

            if (held == null || held.Kind != (portion == Portion.Water ? Kind.Cup : Kind.Scoop))
            {
                return portion == Portion.Water ? "Для воды нужна кружка" : "Для сухих компонентов нужен совок";
            }

            Portion? loaded = ToolPortion(held);
            if (loaded.HasValue)
            {
                if (loaded != portion)
                {
                    return $"Совок наполнен: {Portions[(int)loaded.Value]}";
                }

                if (click)
                {
                    EmptyTool(held);
                }

                return "ЛКМ   Вернуть порцию";
            }

            if (_cycle.Stock(portion) == 0)
            {
                return "Запас закончился";
            }

            if (click)
            {
                if (held.Kind == Kind.Cup)
                {
                    _cupFull = true;
                }
                else
                {
                    _scoop = portion;
                }

                _sounds.Play(WorkshopSounds.Cue.Portion);
            }

            return "ЛКМ   Набрать порцию";
        }

        private string AimAtBowl(Carryable held, bool click, float mixingSeconds)
        {
            if (held == null)
            {
                return _cycle.MixReady ? "Смесь готова" : "Рабочая ёмкость";
            }

            if (held.Kind == Kind.Mixer)
            {
                bool wasReady = _cycle.MixReady;
                _cycle.Mix(mixingSeconds);
                if (!wasReady && _cycle.MixReady)
                {
                    RefreshMix();
                    _sounds.Play(WorkshopSounds.Cue.Finished);
                }

                return _cycle.MixReady ? "Смесь готова" : !_cycle.HasMix ? "Сначала добавьте компоненты"
                    : $"Двигайте мышью над смесью · {_cycle.Mixing:0.0}/5 с";
            }

            if (held.Kind != Kind.Scoop && held.Kind != Kind.Cup)
            {
                return null;
            }

            Portion? portion = ToolPortion(held);
            if (!portion.HasValue)
            {
                return "Сначала наберите порцию";
            }

            int required = portion == Portion.Base ? 4 : portion == Portion.Water ? 2 : 1;
            if (_cycle.Count(portion.Value) >= required)
            {
                return $"{Portions[(int)portion.Value]}: нужное количество уже добавлено";
            }

            if (click && _cycle.TryAdd(portion.Value))
            {
                EmptyTool(held);
                RefreshMix();
                _sounds.Play(WorkshopSounds.Cue.Portion);
            }

            return "ЛКМ   Добавить порцию";
        }

        private Portion? ToolPortion(Carryable item) => item.Kind == Kind.Cup ? (_cupFull ? Portion.Water : null) : _scoop;

        private void EmptyTool(Carryable item)
        {
            if (item.Kind == Kind.Cup)
            {
                _cupFull = false;
            }
            else
            {
                _scoop = null;
            }
        }

        private string AimAtTray(GrowingTray tray, Transform hand, ref Carryable held, bool click)
        {
            if (held == null && tray.State.LidClosed)
            {
                if (click && _cycle.TrySetLid(tray.State, false) && tray.Lid.TakeFromFixture(hand))
                {
                    held = tray.Lid;
                }

                return "ЛКМ   Снять крышку";
            }

            if (held == null)
            {
                return null;
            }

            switch (held.Kind)
            {
                case Kind.Lid:
                    if (held != tray.Lid)
                    {
                        return $"Нужна крышка для лотка №{tray.Number}";
                    }

                    if (click && _cycle.TrySetLid(tray.State, true))
                    {
                        tray.Lid.Store(tray.Item.transform, LidPosition);
                        held = null;
                    }

                    return tray.State.LidClosed ? "Лоток уже закрыт" : "ЛКМ   Закрыть лоток";
                case Kind.Bowl:
                    if (tray.State.LidClosed || tray.State.Stage != TrayStage.Empty || !_cycle.MixReady)
                    {
                        return tray.State.LidClosed ? "Снимите крышку" : tray.State.Stage != TrayStage.Empty
                            ? "Лоток уже заполнен" : "Смесь ещё не готова";
                    }

                    if (click && _cycle.TryFillTray(tray.State))
                    {
                        RefreshMix();
                        RefreshTray(tray);
                        _sounds.Play(WorkshopSounds.Cue.Portion);
                    }

                    return "ЛКМ   Переложить смесь";
                case Kind.Inoculator:
                    if (!_cultureLoaded || tray.State.LidClosed || tray.State.Stage != TrayStage.Treated)
                    {
                        return !_cultureLoaded ? "Наберите мицелий" : tray.State.LidClosed
                            ? "Снимите крышку" : "Нужен обработанный субстрат";
                    }

                    if (click && _cycle.TryInoculate(tray.State))
                    {
                        _cultureLoaded = false;
                        RefreshTray(tray);
                        _sounds.Play(WorkshopSounds.Cue.Portion);
                    }

                    return "ЛКМ   Внести мицелий";
                default:
                    return null;
            }
        }

        private string DevicePrompt(Station station, bool door, bool start, Transform hand, ref Carryable held, bool click)
        {
            GrowthCycle.Tray loaded = _cycle.LoadedTray(station);
            if (_cycle.IsWorking(station))
            {
                return $"Лоток №{loaded.Number} в работе · дверца заблокирована";
            }

            GrowingTray carrying = FindTray(held);
            bool carryingTray = carrying != null && held == carrying.Item;
            if (held != null && !carryingTray)
            {
                return "Освободите руки";
            }

            if (start)
            {
                if (loaded == null || _cycle.IsOpen(station) || !_cycle.HasCorrectTray(station, loaded))
                {
                    return _cycle.IsDone(station) ? "Готово · откройте дверцу" : "Для запуска загрузите лоток и закройте дверцу";
                }

                if (click && _cycle.TryStart(station))
                {
                    RefreshTray(_trays[loaded.Number - 1]);
                    _sounds.Play(WorkshopSounds.Cue.Start);
                }

                return "ЛКМ   Старт";
            }

            if (carryingTray)
            {
                if (loaded != null)
                {
                    return $"Устройство занято · лоток №{loaded.Number}";
                }

                if (!_cycle.IsOpen(station))
                {
                    return "Поставьте лоток и откройте дверцу";
                }

                if (!_cycle.HasCorrectTray(station, carrying.State))
                {
                    return station switch
                    {
                        Station.Treatment => "Нужен закрытый лоток с готовой смесью",
                        Station.Incubation => "Нужен закрытый лоток с мицелием",
                        _ => "Нужен заросший лоток без крышки"
                    };
                }

                if (click && _cycle.TryLoad(station, carrying.State))
                {
                    carrying.Item.Store(_docks[(int)station], Vector3.zero);
                    held = null;
                }

                return $"ЛКМ   Загрузить лоток №{carrying.Number}";
            }

            if (_cycle.IsOpen(station) && loaded != null && !door)
            {
                GrowingTray tray = _trays[loaded.Number - 1];
                if (click && _cycle.TryUnload(station) && tray.Item.TakeFromFixture(hand))
                {
                    held = tray.Item;
                }

                return $"ЛКМ   Вынуть лоток №{loaded.Number}";
            }

            if (click && _cycle.TryToggleDoor(station))
            {
                _doorPanels[(int)station].localPosition = DoorPosition(_cycle.IsOpen(station));
            }

            return _cycle.IsOpen(station) ? "ЛКМ   Закрыть дверцу" : "ЛКМ   Открыть дверцу";
        }

        private string AimAtWaste(Carryable held, bool click)
        {
            if (held == null)
            {
                return "Утилизация смеси и субстрата";
            }

            if (held.Kind == Kind.Bowl)
            {
                if (click)
                {
                    _cycle.DiscardMix();
                    RefreshMix();
                }

                return "ЛКМ   Утилизировать смесь";
            }

            if (held.Kind != Kind.Tray)
            {
                return null;
            }

            GrowingTray tray = FindTray(held);
            if (tray.HasMushroomsOnPlatform)
            {
                return "Срежьте и уберите все грибы с площадки";
            }

            if (tray.State.Stage != TrayStage.Harvest || tray.State.LidClosed)
            {
                return "Нужен открытый лоток с отработанным субстратом";
            }

            if (click && _cycle.TryDiscardTray(tray.State))
            {
                System.Array.Clear(tray.Crop, 0, tray.Crop.Length);
                RefreshTray(tray);
            }

            return $"ЛКМ   Утилизировать субстрат · лоток №{tray.Number}";
        }

        private static string TrayStatus(GrowingTray tray)
        {
            string state = tray.State.Stage switch
            {
                TrayStage.Empty => "Пустой",
                TrayStage.Mixed => "Смесь загружена",
                TrayStage.Treated => "Субстрат обработан",
                TrayStage.Inoculated => "Мицелий внесён",
                TrayStage.Colonized => "Субстрат зарос",
                TrayStage.Fruiting => "Плодоношение",
                _ => "Готово к сбору"
            };
            return tray.State.LidClosed ? $"{tray.Name} · {state} · крышка закрыта" : $"{tray.Name} · {state}";
        }

        private string DeviceStatus(Station station) => _cycle.IsWorking(station)
            ? null
            : $"{Stations[(int)station]} · {DeviceReading(station)}";

        private string DeviceReading(Station station)
        {
            GrowthCycle.Tray tray = _cycle.LoadedTray(station);
            if (tray == null)
            {
                return "Пусто";
            }

            string status = _cycle.IsWorking(station) ? $"{Mathf.CeilToInt(_cycle.Remaining(station))} с"
                : _cycle.IsDone(station) ? "Готово" : "Загружен";
            return $"№{tray.Number} · {status}";
        }

        private void UpdateDeviceDisplays()
        {
            for (int index = 0; index < _deviceDisplays.Length; index++)
            {
                string status = DeviceReading((Station)index);
                if (_deviceDisplays[index].Text != status)
                {
                    _deviceDisplays[index].Text = status;
                }
            }
        }

        private void RefreshMix()
        {
            _mixture.SetActive(_cycle.HasMix);
            Tint(_mixture, _cycle.MixReady ? new Color(0.36f, 0.26f, 0.14f) : Soil);
        }

        private void RefreshTray(GrowingTray tray, bool spawnMushrooms = true)
        {
            TrayStage stage = tray.State.Stage;
            tray.Substrate.SetActive(stage != TrayStage.Empty);
            Tint(tray.Substrate, stage >= TrayStage.Colonized ? new Color(0.72f, 0.70f, 0.59f)
                : stage == TrayStage.Inoculated ? new Color(0.49f, 0.40f, 0.27f) : Soil);
            if (spawnMushrooms && (stage == TrayStage.Fruiting || stage == TrayStage.Harvest))
            {
                bool ripe = stage == TrayStage.Harvest;
                for (int index = 0; index < tray.Crop.Length; index++)
                {
                    if (tray.Crop[index] == null)
                    {
                        tray.Crop[index] = MakeMushroom(tray, $"mushroom/{tray.State.Crop}/{index + 1}", index);
                    }

                    tray.Crop[index].GrowOn(tray.Item.transform, MushroomOffset(index), ripe);
                    tray.Crop[index].SetCovered(true);
                }
            }
        }

        internal void Capture(WorkshopSave save)
        {
            save.workshopGrowth = _cycle.Capture();
            save.scoop = _scoop.HasValue ? (int)_scoop.Value : -1;
            save.cupFull = _cupFull;
            save.cultureLoaded = _cultureLoaded;
        }

        internal void Restore(WorkshopSave save, GrowthCycle.Snapshot growth, Vector3 secondTrayPosition)
        {
            _cycle.TryRestore(growth);
            _trays[0].State = _cycle.TrayAt(1);
            if (_cycle.TrayAt(2) is GrowthCycle.Tray second)
            {
                BuildTrayPair(second, secondTrayPosition, secondTrayPosition + LidPosition);
            }

            _scoop = save.scoop < 0 ? null : (Portion)save.scoop;
            _cupFull = save.cupFull;
            _cultureLoaded = save.cultureLoaded;
            _jarLid.localPosition = new Vector3(-2.19f, 1.048f, _cycle.JarOpen ? 0.535f : 0.4f);
            for (int index = 0; index < _doorPanels.Length; index++)
            {
                _doorPanels[index].localPosition = DoorPosition(_cycle.IsOpen((Station)index));
            }

            RefreshMix();
            foreach (GrowingTray tray in _trays)
            {
                if (tray != null)
                {
                    RefreshTray(tray, false);
                }
            }

            UpdateDeviceDisplays();
        }

        internal Carryable RestoreMushroom(string id, int crop, int slot)
        {
            GrowingTray tray = System.Array.Find(_trays, pair => pair != null && pair.State.Crop == crop);
            Carryable mushroom = MakeMushroom(tray ?? _trays[0], id, slot - 1);
            if (tray != null)
            {
                tray.Crop[slot - 1] = mushroom;
            }

            return mushroom;
        }

        private Carryable MakeMushroom(GrowingTray tray, string id, int index)
        {
            Carryable mushroom = Object.Instantiate(_template);
            mushroom.name = $"Mushroom {index + 1}";
            mushroom.gameObject.SetActive(true);
            mushroom.Configure(Kind.Mushroom, 0.022f, _bounds,
                tray.Item.transform.TransformPoint(MushroomOffset(index)), id: id);
            return mushroom;
        }

        private void BuildTrayPair(GrowthCycle.Tray state, Vector3 trayPosition, Vector3 lidPosition)
        {
            Carryable item = MakeTray(trayPosition, TrayId(state.Number));
            Carryable lid = MakeItem(Kind.Lid, lidPosition, new Vector3(0.41f, 0.025f, 0.31f),
                state.Number == 1 ? new Color(0.60f, 0.64f, 0.68f) : new Color(0.46f, 0.64f, 0.59f),
                LidId(state.Number));
            var tray = new GrowingTray
            {
                State = state, Item = item, Lid = lid,
                Substrate = Surface("Substrate", item.transform, new Vector3(0f, 0.046f, 0.11f),
                    new Vector3(0.36f, 0.027f, 0.26f))
            };
            _trays[state.Number - 1] = tray;
            if (state.LidClosed)
            {
                lid.Store(item.transform, LidPosition);
            }

            RefreshTray(tray, false);
        }

        private static Vector3 MushroomOffset(int index)
        {
            return new Vector3((index % 4 - 1.5f) * 0.083f, 0.080f, 0.11f + (index / 4 - 1f) * 0.07f);
        }

        private Carryable MakeTray(Vector3 position, string id)
        {
            var root = new GameObject("Growing tray");
            root.transform.position = position;
            var color = new Color(0.34f, 0.38f, 0.41f);
            GameObject floor = Block("Tray base", root.transform, new Vector3(0f, 0.018f, 0.11f),
                new Vector3(0.4f, 0.04f, 0.3f), color);
            Block("Harvest platform", root.transform, new Vector3(0f, 0.005f, -0.145f),
                new Vector3(0.4f, 0.015f, 0.21f), color);
            return ConfigureItem(root, Kind.Tray, 0.28f, floor.GetComponent<Renderer>(), id);
        }

        private void BuildDevice(Station station, Vector3 position)
        {
            int index = (int)station;
            var device = new GameObject(Stations[index]);
            device.transform.SetParent(_room, false);
            device.transform.localPosition = position;
            Transform root = device.transform;
            var frame = new Color(0.29f, 0.37f, 0.40f);
            Block("Stand", root, new Vector3(0f, 0.40f, 0f), new Vector3(0.89f, 0.8f, 0.78f), frame);
            _slots.Add(Block("Interior", root, new Vector3(0f, 1.005f, 0.29f),
                new Vector3(0.82f, 0.39f, 0.10f), frame).GetComponent<Collider>(), station);
            _slots.Add(Block("Shelf", root, new Vector3(0f, 0.82f, 0f),
                new Vector3(0.84f, 0.04f, 0.70f), frame).GetComponent<Collider>(), station);
            Block("Left wall", root, new Vector3(-0.42f, 1.03f, 0f), new Vector3(0.07f, 0.43f, 0.7f), frame);
            Block("Right wall", root, new Vector3(0.42f, 1.03f, 0f), new Vector3(0.07f, 0.43f, 0.7f), frame);
            Block("Top", root, new Vector3(0f, 1.25f, 0f), new Vector3(0.91f, 0.05f, 0.7f), frame);
            var doorRoot = new GameObject("Door");
            doorRoot.transform.SetParent(root, false);
            doorRoot.transform.localPosition = DoorPosition(false);
            var doorColor = new Color(0.51f, 0.61f, 0.64f);
            GameObject door = Block("Panel", doorRoot.transform, Vector3.zero, new Vector3(0.87f, 0.41f, 0.035f), doorColor);
            if (station == Station.Fruiting)
            {
                door.GetComponent<Renderer>().enabled = false;
                BuildViewingFrame(doorRoot.transform, doorColor);
            }

            _doors.Add(door.GetComponent<Collider>(), station);
            _doorPanels[index] = doorRoot.transform;
            _starts.Add(Block("Start button", root, new Vector3(0.48f, 0.97f, -0.29f),
                new Vector3(0.11f, 0.13f, 0.12f), new Color(0.52f, 0.72f, 0.31f)).GetComponent<Collider>(), station);
            var badge = Block("Status panel", root, new Vector3(0f, 1.36f, -0.365f),
                new Vector3(0.78f, 0.17f, 0.02f), new Color(0.14f, 0.23f, 0.28f));
            badge.GetComponent<Collider>().enabled = false;
            WorkshopBatch.AddFrontText(root, root.TransformPoint(0f, 1.393f, -0.377f), Stations[index], 0.024f, Color.white, new Vector2(0.70f, 0.049f));
            _deviceDisplays[index] = WorkshopBatch.AddFrontText(root, root.TransformPoint(0f, 1.325f, -0.377f),
                "Пусто", 0.020f, new Color(0.66f, 1f, 0.83f), new Vector2(0.70f, 0.045f));
            var dock = new GameObject("Tray dock");
            dock.transform.SetParent(root, false);
            dock.transform.localPosition = new Vector3(0f, 0.84f, -0.04f);
            _docks[index] = dock.transform;
        }

        private void BuildViewingFrame(Transform door, Color color)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject vertical = Block("Window jamb", door, Vector3.right * (side * 0.39f),
                    new Vector3(0.09f, 0.41f, 0.036f), color);
                vertical.GetComponent<Collider>().enabled = false;
                GameObject horizontal = Block("Window rail", door, Vector3.up * (side * 0.17f),
                    new Vector3(0.69f, 0.07f, 0.036f), color);
                horizontal.GetComponent<Collider>().enabled = false;
            }
        }

        private static Vector3 DoorPosition(bool open) => new Vector3(0f, open ? 1.45f : 1.035f, -0.34f);

        private Carryable MakeItem(Kind kind, Vector3 worldPosition, Vector3 size, Color color, string id = null)
        {
            var item = new GameObject(kind.ToString());
            item.transform.position = worldPosition;
            Renderer renderer = Block(kind.ToString(), item.transform, Vector3.zero, size, color).GetComponent<Renderer>();
            return ConfigureItem(item, kind, Mathf.Max(size.x, size.z) * 0.55f, renderer, id);
        }

        private Carryable ConfigureItem(GameObject root, Kind kind, float radius, Renderer renderer, string id = null)
        {
            Carryable item = root.AddComponent<Carryable>();
            item.Configure(kind, radius, _bounds, root.transform.position, renderer, id);
            return item;
        }

        private GameObject Surface(string name, Transform parent, Vector3 position, Vector3 size)
        {
            GameObject surface = Block(name, parent, position, size, Soil);
            surface.GetComponent<Collider>().enabled = false;
            surface.SetActive(false);
            return surface;
        }

        private GameObject Block(string name, Transform parent, Vector3 position, Vector3 size, Color color)
        {
            return WorkshopBatch.CreateBlock(name, parent, position, size, _material, color);
        }

        private static void Tint(GameObject block, Color color)
        {
            Renderer renderer = block.GetComponent<Renderer>();
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
        }
    }
}
