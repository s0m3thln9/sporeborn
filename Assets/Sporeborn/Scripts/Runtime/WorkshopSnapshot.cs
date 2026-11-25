using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sporeborn.Gameplay.Growing;
using Sporeborn.Gameplay.Packaging;
using Sporeborn.Persistence;
using UnityEngine;
using Object = UnityEngine.Object;
using static Sporeborn.Runtime.WorkshopSnapshot;

namespace Sporeborn.Runtime
{
    public sealed partial class WorkshopBatch
    {
        internal WorkshopSave Capture(FirstPersonPlayer player)
        {
            var save = new WorkshopSave
            {
                version = WorkshopSave.CurrentVersion,
                money = Money,
                nextLabel = _nextLabel,
                nextBox = _nextBox,
                printerLabel = _printedLabel != null ? _printedLabel.Id : null,
                items = SceneItems().Values.Select(item => item.Capture(item.Kind == Carryable.ItemKind.Tray
                    ? Growth.DockIdFor(item) : item.transform.parent != null
                        ? item.transform.parent.GetComponent<Carryable>()?.Id : null)).ToArray(),
                boxes = _boxes.Values.Select(box => box.Capture()).ToArray()
            };
            Growth.Capture(save);
            player.CapturePose(save);
            return save;
        }

        internal void Resume(WorkshopSave save, FirstPersonPlayer player, MushroomInteraction interaction)
        {
            Dictionary<string, ItemSave> records = Validate(save, out GrowthCycle.Snapshot growth);
            Growth.Restore(save, growth, _supplyOutput);
            Dictionary<string, Carryable> items = SceneItems();
            ILookup<string, ItemSave> children = records.Values.ToLookup(item => item.parent, StringComparer.Ordinal);
            foreach (string id in _boxes.Keys.Where(id => !records.ContainsKey(id)).ToArray())
            {
                ShippingBox box = _boxes[id];
                items.Remove(id);
                _boxes.Remove(id);
                box.gameObject.SetActive(false);
                Destroy(box.gameObject);
            }

            foreach (BoxSave box in save.boxes)
            {
                if (!_boxes.ContainsKey(box.id))
                {
                    items.Add(box.id, SpawnBox(box.id, _supplyOutput, Quaternion.identity).Item);
                }
            }

            foreach (ItemSave record in records.Values)
            {
                if (TryMushroomId(record.id, out int crop, out int slot))
                {
                    items.Add(record.id, Growth.RestoreMushroom(record.id, crop, slot));
                }
                else if (TryLabelId(record.id, out _))
                {
                    items.Add(record.id, MakeLabel(record.id));
                }
            }

            Physics.SyncTransforms();
            player.RestorePose(save, _roomBounds.bounds);
            foreach (ItemSave record in records.Values)
            {
                Carryable item = items[record.id];
                Transform parent = record.location == ItemLocation.Held ? interaction.Hand
                    : !HasParent(record) ? null : Growth.ResolveDock(item) ?? items[record.parent].transform;
                item.RestoreSnapshot(record, parent, IsFruiting(record, growth));
                if (record.location == ItemLocation.Held)
                {
                    interaction.RestoreHeld(item);
                }
            }

            foreach (BoxSave box in save.boxes)
            {
                IEnumerable<ItemSave> contents = children[box.id];
                ItemSave label = contents.SingleOrDefault(item => TryLabelId(item.id, out _));
                _boxes[box.id].RestoreContents(box,
                    contents.Count(item => TryMushroomId(item.id, out _, out _)), label != null ? items[label.id] : null);
            }

            _nextBox = NextBoxId(save);
            _nextLabel = save.nextLabel;
            _printedLabel = string.IsNullOrEmpty(save.printerLabel) ? null : items[save.printerLabel];
            Money = save.money;
            Physics.SyncTransforms();
            foreach (ItemSave record in records.Values.Where(item => item.location == ItemLocation.Falling)
                .OrderBy(item => item.position.y))
            {
                items[record.id].SettleRestoredDrop();
            }

            Growth.RefreshCropAccess();
        }

        private Dictionary<string, Carryable> SceneItems()
        {
            return Object.FindObjectsByType<Carryable>(FindObjectsSortMode.None)
                .Where(item => item.gameObject.scene == gameObject.scene && !string.IsNullOrEmpty(item.Id))
                .ToDictionary(item => item.Id, StringComparer.Ordinal);
        }

    }

    internal static class WorkshopSnapshot
    {
        private static readonly string[] FixedIds = { "Knife", "Scoop", "Cup", "Mixer", "Inoculator", "Bowl" };

        internal static Dictionary<string, ItemSave> Validate(WorkshopSave save, out GrowthCycle.Snapshot growth)
        {
            growth = save?.GrowthSnapshot();
            if (save == null || !WorkshopSaveFile.IsSupportedVersion(save.version) || save.items == null || save.boxes == null ||
                save.money < 0 || save.money % 10 != 0 || NextBoxId(save) < WorkshopBatch.FirstPurchasedBox ||
                save.nextLabel < 1 || save.scoop < -1 || save.scoop > 2 || !Finite(save.playerPosition) ||
                !float.IsFinite(save.yaw) || !float.IsFinite(save.pitch) || Mathf.Abs(save.pitch) > 85f ||
                !GrowthCycle.IsValid(growth))
            {
                throw InvalidSave();
            }

            var boxes = new Dictionary<string, BoxSave>(StringComparer.Ordinal);
            int nextBox = NextBoxId(save);
            foreach (BoxSave box in save.boxes)
            {
                if (box == null || !TryBoxId(box.id, out int sequence) || sequence >= nextBox ||
                    box.flaps < 0 || box.flaps > ShippingBoxContents.FlapCount || !boxes.TryAdd(box.id, box))
                {
                    throw InvalidSave();
                }
            }

            var items = new Dictionary<string, ItemSave>(StringComparer.Ordinal);
            int hands = 0;
            foreach (ItemSave item in save.items)
            {
                if (item == null || string.IsNullOrEmpty(item.id) || !items.TryAdd(item.id, item) ||
                    !Finite(item.position) || !Finite(item.recoveryPosition) ||
                    !Rotation(item.rotation) || !Rotation(item.recoveryRotation) ||
                    !Enum.IsDefined(typeof(ItemLocation), item.location) ||
                    (item.location == ItemLocation.Held && ++hands > 1))
                {
                    throw InvalidSave();
                }
            }

            if (FixedIds.Any(id => !items.ContainsKey(id)) || boxes.Keys.Any(id => !items.ContainsKey(id)) ||
                (!string.IsNullOrEmpty(save.printerLabel) && (!items.ContainsKey(save.printerLabel) ||
                    !TryLabelId(save.printerLabel, out _))))
            {
                throw InvalidSave();
            }

            ILookup<string, ItemSave> children = items.Values.ToLookup(item => item.parent, StringComparer.Ordinal);
            var trays = new Dictionary<string, GrowthCycle.TraySnapshot>(StringComparer.Ordinal);
            var pairedItems = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < growth.trays.Length; index++)
            {
                int number = index + 1;
                GrowthCycle.TraySnapshot tray = growth.trays[index];
                string trayId = WorkshopGrowth.TrayId(number);
                string lidId = WorkshopGrowth.LidId(number);
                int dock = Array.FindIndex(growth.devices, device => device.trayNumber == number);
                ValidateFixtureItem(items, trayId, dock < 0 ? null : $"dock/{dock}");
                ValidateFixtureItem(items, lidId, tray.lidClosed ? trayId : null);
                trays.Add(trayId, tray);
                pairedItems.Add(trayId);
                pairedItems.Add(lidId);

                if (tray.stage == TrayStage.Fruiting &&
                    children[trayId].Count(item => item.location == ItemLocation.Rooted) != GrowthCycle.MushroomCount)
                {
                    throw InvalidSave();
                }
            }

            foreach (ItemSave item in items.Values)
            {
                if (pairedItems.Contains(item.id))
                {
                    continue;
                }

                bool mushroom = TryMushroomId(item.id, out int crop, out _);
                bool label = TryLabelId(item.id, out int labelNumber);
                if ((!mushroom && !label && !boxes.ContainsKey(item.id) && Array.IndexOf(FixedIds, item.id) < 0) ||
                    (label && labelNumber >= save.nextLabel) || (mushroom && crop >= growth.nextCrop))
                {
                    throw InvalidSave();
                }

                if (!HasParent(item))
                {
                    if (item.location == ItemLocation.Stored || item.location == ItemLocation.Rooted)
                    {
                        throw InvalidSave();
                    }

                    continue;
                }

                bool onTray = mushroom && trays.TryGetValue(item.parent, out GrowthCycle.TraySnapshot tray) &&
                    crop == tray.crop && ((tray.stage == TrayStage.Fruiting && item.location == ItemLocation.Rooted) ||
                        (tray.stage == TrayStage.Harvest &&
                            (item.location == ItemLocation.Resting || item.location == ItemLocation.Rooted)));
                bool packed = (mushroom || label) && item.location == ItemLocation.Stored && boxes.ContainsKey(item.parent);
                if (!onTray && !packed)
                {
                    throw InvalidSave();
                }
            }

            foreach (BoxSave box in boxes.Values)
            {
                IEnumerable<ItemSave> contents = children[box.id];
                int labels = contents.Count(item => TryLabelId(item.id, out _));
                if (contents.Count() - labels > ShippingBoxContents.Columns * ShippingBoxContents.Rows ||
                    labels > 1 || (labels > 0 && box.flaps != ShippingBoxContents.FlapCount))
                {
                    throw InvalidSave();
                }
            }

            return items;
        }

        private static void ValidateFixtureItem(Dictionary<string, ItemSave> items, string id, string owner)
        {
            if (!items.TryGetValue(id, out ItemSave item) || (owner == null
                ? HasParent(item) || item.location == ItemLocation.Stored || item.location == ItemLocation.Rooted
                : item.parent != owner || item.location != ItemLocation.Stored))
            {
                throw InvalidSave();
            }
        }

        internal static bool IsFruiting(ItemSave record, GrowthCycle.Snapshot growth)
        {
            if (record.location != ItemLocation.Rooted)
            {
                return false;
            }

            for (int index = 0; index < growth.trays.Length; index++)
            {
                if (record.parent == WorkshopGrowth.TrayId(index + 1))
                {
                    return growth.trays[index].stage == TrayStage.Fruiting;
                }
            }

            return false;
        }

        internal static bool HasParent(ItemSave item) => !string.IsNullOrEmpty(item.parent);
        internal static int NextBoxId(WorkshopSave save) =>
            save.version == WorkshopSave.LegacyVersion ? WorkshopBatch.FirstPurchasedBox : save.nextBox;

        private static bool TryBoxId(string id, out int sequence)
        {
            sequence = 0;
            return id != null && id.StartsWith("box/", StringComparison.Ordinal) &&
                int.TryParse(id.AsSpan(4), out sequence) && sequence > 0 && id == $"box/{sequence}";
        }

        internal static bool TryLabelId(string id, out int sequence)
        {
            sequence = 0;
            return id.StartsWith("label/", StringComparison.Ordinal) && int.TryParse(id.Substring(6), out sequence) &&
                sequence > 0 && id == $"label/{sequence}";
        }

        internal static bool TryMushroomId(string id, out int crop, out int slot)
        {
            crop = slot = 0;
            if (!id.StartsWith("mushroom/", StringComparison.Ordinal))
            {
                return false;
            }

            int separator = id.IndexOf('/', 9);
            return separator > 9 && int.TryParse(id.AsSpan(9, separator - 9), out crop) &&
                int.TryParse(id.AsSpan(separator + 1), out slot) && crop >= 1 && slot >= 1 && slot <= GrowthCycle.MushroomCount &&
                id == $"mushroom/{crop}/{slot}";
        }

        private static bool Finite(Vector3 vector) =>
            float.IsFinite(vector.x) && float.IsFinite(vector.y) && float.IsFinite(vector.z);

        private static bool Rotation(Quaternion rotation) =>
            Mathf.Abs(Quaternion.Dot(rotation, rotation) - 1f) < 0.01f;

        private static InvalidDataException InvalidSave() => new InvalidDataException("Данные сохранения повреждены.");
    }
}
