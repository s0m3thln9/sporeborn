using System.Collections.Generic;
using Sporeborn.Gameplay.Growing;
using Sporeborn.Persistence;
using UnityEngine;
using Kind = Sporeborn.Runtime.Carryable.ItemKind;

namespace Sporeborn.Runtime
{
    public sealed class WorkshopGrowth
    {
        private const int MushroomCount = 12;
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
        private readonly Carryable[] _crop = new Carryable[MushroomCount];
        private readonly Carryable _template;
        private readonly Transform _room;
        private readonly BoxCollider _bounds;
        private readonly Material _material;
        private readonly Carryable _tray;
        private readonly Carryable _lid;
        private readonly GameObject _mixture;
        private readonly GameObject _substrate;
        private readonly Collider _waste;
        private readonly Transform _jarLid;
        private Portion? _scoop;
        private bool _cupFull;
        private bool _cultureLoaded;
        private bool _cropCovered;
        private int _cropNumber = 1;

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
                new Vector3(0.27f, 0.074f, 0.25f), new Color(0.32f, 0.37f, 0.43f));
            _mixture = Surface("Mixture", bowl.transform, Vector3.up * 0.039f, new Vector3(0.24f, 0.012f, 0.22f));

            Vector3 trayPosition = platform + new Vector3(0f, -0.022f, 0.11f);
            _tray = MakeTray(trayPosition);
            _substrate = Surface("Substrate", _tray.transform, new Vector3(0f, 0.046f, 0.11f),
                new Vector3(0.36f, 0.027f, 0.26f));
            _lid = MakeItem(Kind.Lid, new Vector3(-0.75f, 0.915f, 0.95f), new Vector3(0.41f, 0.025f, 0.31f),
                new Color(0.60f, 0.64f, 0.68f));
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
            TrayStage before = _cycle.Stage;
            _cycle.Tick(seconds);
            if (before != _cycle.Stage)
            {
                RefreshTray();
                _sounds.Play(WorkshopSounds.Cue.Finished);
            }

            UpdateDeviceDisplays();

            RefreshCropAccess();
        }

        internal void RefreshCropAccess()
        {
            bool covered = !_tray.IsResting || _cycle.LidClosed || _cycle.Dock.HasValue || _cycle.Stage != TrayStage.Harvest;
            if (_cropCovered == covered)
            {
                return;
            }

            _cropCovered = covered;
            foreach (Carryable mushroom in _crop)
            {
                if (mushroom != null && mushroom.transform.parent == _tray.transform)
                {
                    mushroom.SetCovered(covered);
                }
            }
        }

        public bool IsIncubating => _cycle.IsWorking(Station.Incubation);

        public string FirstStep => _cycle.Stage == TrayStage.Empty && !_cycle.HasMix && _cropNumber == 1
            ? "Начните со стола подготовки слева" : null;

        public bool TryCut(Carryable mushroom)
        {
            if (_cycle.Stage != TrayStage.Harvest || _cycle.Dock.HasValue || _cycle.LidClosed ||
                mushroom.transform.parent != _tray.transform)
            {
                return false;
            }

            int index = System.Array.IndexOf(_crop, mushroom);
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
            else if (target == _tray)
            {
                detail = TrayStatus();
                if (_cycle.Dock == null)
                {
                    prompt = AimAtTray(hand, ref held, click && (held == null || nearTool));
                }
                else if (held == null)
                {
                    prompt = DevicePrompt(_cycle.Dock.Value, false, false, hand, ref held, click);
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

        private string AimAtTray(Transform hand, ref Carryable held, bool click)
        {
            if (held == null && _cycle.LidClosed)
            {
                if (click && _cycle.TrySetLid(false) && _lid.TakeFromFixture(hand))
                {
                    held = _lid;
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
                    if (click && _cycle.TrySetLid(true))
                    {
                        _lid.Store(_tray.transform, LidPosition);
                        held = null;
                    }

                    return "ЛКМ   Закрыть лоток";
                case Kind.Bowl:
                    if (_cycle.LidClosed || _cycle.Stage != TrayStage.Empty || !_cycle.MixReady)
                    {
                        return _cycle.LidClosed ? "Снимите крышку" : _cycle.Stage != TrayStage.Empty
                            ? "Лоток уже заполнен" : "Смесь ещё не готова";
                    }

                    if (click && _cycle.TryFillTray())
                    {
                        RefreshMix();
                        RefreshTray();
                        _sounds.Play(WorkshopSounds.Cue.Portion);
                    }

                    return "ЛКМ   Переложить смесь";
                case Kind.Inoculator:
                    if (!_cultureLoaded || _cycle.LidClosed || _cycle.Stage != TrayStage.Treated)
                    {
                        return !_cultureLoaded ? "Наберите мицелий" : _cycle.LidClosed
                            ? "Снимите крышку" : "Нужен обработанный субстрат";
                    }

                    if (click && _cycle.TryInoculate())
                    {
                        _cultureLoaded = false;
                        RefreshTray();
                        _sounds.Play(WorkshopSounds.Cue.Portion);
                    }

                    return "ЛКМ   Внести мицелий";
                default:
                    return null;
            }
        }

        private string DevicePrompt(Station station, bool door, bool start, Transform hand, ref Carryable held, bool click)
        {
            if (_cycle.IsWorking(station))
            {
                return "Устройство работает · дверца заблокирована";
            }

            if (held != null && held != _tray)
            {
                return "Освободите руки";
            }

            if (start)
            {
                if (_cycle.Dock != station || _cycle.IsOpen(station) || !_cycle.HasCorrectTray(station))
                {
                    return _cycle.IsDone(station) ? "Готово · откройте дверцу" : "Для запуска загрузите лоток и закройте дверцу";
                }

                if (click && _cycle.TryStart(station))
                {
                    RefreshTray();
                    _sounds.Play(WorkshopSounds.Cue.Start);
                }

                return "ЛКМ   Старт";
            }

            if (held == _tray)
            {
                if (!_cycle.IsOpen(station))
                {
                    return "Поставьте лоток и откройте дверцу";
                }

                if (!_cycle.HasCorrectTray(station))
                {
                    return station switch
                    {
                        Station.Treatment => "Нужен закрытый лоток с готовой смесью",
                        Station.Incubation => "Нужен закрытый лоток с мицелием",
                        _ => "Нужен заросший лоток без крышки"
                    };
                }

                if (click && _cycle.TryLoad(station))
                {
                    _tray.Store(_docks[(int)station], Vector3.zero);
                    held = null;
                }

                return "ЛКМ   Загрузить лоток";
            }

            if (_cycle.IsOpen(station) && _cycle.Dock == station && !door)
            {
                if (click && _cycle.TryUnload(station) && _tray.TakeFromFixture(hand))
                {
                    held = _tray;
                }

                return "ЛКМ   Вынуть лоток";
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

            if (held != _tray)
            {
                return null;
            }

            foreach (Carryable mushroom in _crop)
            {
                if (mushroom != null && mushroom.transform.parent == _tray.transform)
                {
                    return "Срежьте и уберите все грибы с площадки";
                }
            }

            if (_cycle.Stage != TrayStage.Harvest || _cycle.LidClosed)
            {
                return "Нужен открытый лоток с отработанным субстратом";
            }

            if (click && _cycle.TryDiscardTray())
            {
                System.Array.Clear(_crop, 0, _crop.Length);
                _cropNumber++;
                RefreshTray();
            }

            return "ЛКМ   Утилизировать субстрат";
        }

        private string TrayStatus()
        {
            string state = _cycle.Stage switch
            {
                TrayStage.Empty => "Лоток пуст",
                TrayStage.Mixed => "Смесь загружена",
                TrayStage.Treated => "Субстрат обработан",
                TrayStage.Inoculated => "Мицелий внесён",
                TrayStage.Colonized => "Субстрат зарос",
                TrayStage.Fruiting => "Плодоношение",
                _ => "Готово к сбору"
            };
            return _cycle.LidClosed ? $"{state} · Крышка закрыта" : state;
        }

        private string DeviceStatus(Station station) => $"{Stations[(int)station]} · {DeviceReading(station)}";

        private string DeviceReading(Station station) => _cycle.IsWorking(station) ? $"{Mathf.CeilToInt(_cycle.Remaining)} с"
            : _cycle.Dock != station ? "Пусто" : _cycle.IsDone(station) ? "Готово" : "Лоток загружен";

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

        private void RefreshTray(bool spawnMushrooms = true)
        {
            _substrate.SetActive(_cycle.Stage != TrayStage.Empty);
            Tint(_substrate, _cycle.Stage >= TrayStage.Colonized ? new Color(0.72f, 0.70f, 0.59f)
                : _cycle.Stage == TrayStage.Inoculated ? new Color(0.49f, 0.40f, 0.27f) : Soil);
            if (spawnMushrooms && (_cycle.Stage == TrayStage.Fruiting || _cycle.Stage == TrayStage.Harvest))
            {
                bool ripe = _cycle.Stage == TrayStage.Harvest;
                for (int index = 0; index < _crop.Length; index++)
                {
                    if (_crop[index] == null)
                    {
                        _crop[index] = MakeMushroom($"mushroom/{_cropNumber}/{index + 1}", index);
                    }

                    _crop[index].GrowOn(_tray.transform, MushroomOffset(index), ripe);
                    _crop[index].SetCovered(true);
                }
            }
        }

        internal void Capture(WorkshopSave save)
        {
            save.growth = _cycle.Capture();
            save.scoop = _scoop.HasValue ? (int)_scoop.Value : -1;
            save.cupFull = _cupFull;
            save.cultureLoaded = _cultureLoaded;
            save.cropNumber = _cropNumber;
        }

        internal void Restore(WorkshopSave save)
        {
            _cycle.TryRestore(save.growth);
            _scoop = save.scoop < 0 ? null : (Portion)save.scoop;
            _cupFull = save.cupFull;
            _cultureLoaded = save.cultureLoaded;
            _cropNumber = save.cropNumber;
            _jarLid.localPosition = new Vector3(-2.19f, 1.048f, _cycle.JarOpen ? 0.535f : 0.4f);
            for (int index = 0; index < _doorPanels.Length; index++)
            {
                _doorPanels[index].localPosition = DoorPosition(_cycle.IsOpen((Station)index));
            }

            RefreshMix();
            RefreshTray(false);
            UpdateDeviceDisplays();
        }

        internal string DockId => _cycle.Dock.HasValue ? $"dock/{(int)_cycle.Dock.Value}" : null;
        internal Transform ResolveDock() => _cycle.Dock.HasValue ? _docks[(int)_cycle.Dock.Value] : null;

        internal Carryable RestoreMushroom(string id, int crop, int slot)
        {
            Carryable mushroom = MakeMushroom(id, slot - 1);
            if (crop == _cropNumber)
            {
                _crop[slot - 1] = mushroom;
            }

            return mushroom;
        }

        private Carryable MakeMushroom(string id, int index)
        {
            Carryable mushroom = Object.Instantiate(_template);
            mushroom.name = $"Mushroom {index + 1}";
            mushroom.gameObject.SetActive(true);
            mushroom.Configure(Kind.Mushroom, 0.022f, _bounds,
                _tray.transform.TransformPoint(MushroomOffset(index)), id: id);
            return mushroom;
        }

        private static Vector3 MushroomOffset(int index)
        {
            return new Vector3((index % 4 - 1.5f) * 0.083f, 0.080f, 0.11f + (index / 4 - 1f) * 0.07f);
        }

        private Carryable MakeTray(Vector3 position)
        {
            var root = new GameObject("Growing tray");
            root.transform.position = position;
            var color = new Color(0.34f, 0.38f, 0.41f);
            GameObject floor = Block("Tray base", root.transform, new Vector3(0f, 0.018f, 0.11f),
                new Vector3(0.4f, 0.04f, 0.3f), color);
            Block("Harvest platform", root.transform, new Vector3(0f, 0.005f, -0.145f),
                new Vector3(0.4f, 0.015f, 0.21f), color);
            return ConfigureItem(root, Kind.Tray, 0.28f, floor.GetComponent<Renderer>());
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

        private Carryable MakeItem(Kind kind, Vector3 worldPosition, Vector3 size, Color color)
        {
            var item = new GameObject(kind.ToString());
            item.transform.position = worldPosition;
            Renderer renderer = Block(kind.ToString(), item.transform, Vector3.zero, size, color).GetComponent<Renderer>();
            return ConfigureItem(item, kind, Mathf.Max(size.x, size.z) * 0.55f, renderer);
        }

        private Carryable ConfigureItem(GameObject root, Kind kind, float radius, Renderer renderer)
        {
            Carryable item = root.AddComponent<Carryable>();
            item.Configure(kind, radius, _bounds, root.transform.position, renderer);
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
