using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sporeborn.Gameplay.Growing;
using Sporeborn.Gameplay.Packaging;
using Sporeborn.Persistence;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sporeborn.Runtime
{
    public sealed partial class WorkshopBatch
    {
        private static readonly string[] FixedIds =
            { "Knife", "Scoop", "Cup", "Mixer", "Inoculator", "Bowl", "Tray", "Lid" };

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
                    ? Growth.DockId : item.transform.parent != null
                        ? item.transform.parent.GetComponent<Carryable>()?.Id : null)).ToArray(),
                boxes = _boxes.Values.Select(box => box.Capture()).ToArray()
            };
            Growth.Capture(save);
            player.CapturePose(save);
            return save;
        }

        internal void Resume(WorkshopSave save, FirstPersonPlayer player, MushroomInteraction interaction)
        {
            Dictionary<string, ItemSave> records = Validate(save);
            Dictionary<string, Carryable> items = SceneItems();
            ILookup<string, ItemSave> children = records.Values.ToLookup(item => item.parent, StringComparer.Ordinal);
            Growth.Restore(save);
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
                    items.Add(box.id, SpawnBox(box.id, _boxOutput, Quaternion.identity).Item);
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
                    : !HasParent(record) ? null : record.parent == Growth.DockId ? Growth.ResolveDock()
                    : items[record.parent].transform;
                item.RestoreSnapshot(record, parent, save.growth.stage == TrayStage.Fruiting && record.location == ItemLocation.Rooted);
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

        private static Dictionary<string, ItemSave> Validate(WorkshopSave save)
        {
            if (save == null || !WorkshopSaveFile.IsSupportedVersion(save.version) || save.items == null || save.boxes == null ||
                save.money < 0 || save.money % 10 != 0 || NextBoxId(save) < FirstPurchasedBox ||
                save.nextLabel < 1 || save.cropNumber < 1 ||
                save.scoop < -1 || save.scoop > 2 || !Finite(save.playerPosition) ||
                !float.IsFinite(save.yaw) || !float.IsFinite(save.pitch) || Mathf.Abs(save.pitch) > 85f ||
                !new GrowthCycle().TryRestore(save.growth))
            {
                throw InvalidSave();
            }

            var items = new Dictionary<string, ItemSave>(StringComparer.Ordinal);
            var boxes = new Dictionary<string, BoxSave>(StringComparer.Ordinal);
            int nextBox = NextBoxId(save);
            foreach (BoxSave box in save.boxes)
            {
                if (box == null || !TryBoxId(box.id, out int sequence) || sequence >= nextBox ||
                    box.flaps < 0 || box.flaps > ShippingBoxContents.FlapCount ||
                    !boxes.TryAdd(box.id, box))
                {
                    throw InvalidSave();
                }
            }

            int hands = 0;
            foreach (ItemSave item in save.items)
            {
                if (item == null || string.IsNullOrEmpty(item.id) || !items.TryAdd(item.id, item) ||
                    !Finite(item.position) || !Finite(item.recoveryPosition) ||
                    !Rotation(item.rotation) || !Rotation(item.recoveryRotation) ||
                    !Enum.IsDefined(typeof(ItemLocation), item.location))
                {
                    throw InvalidSave();
                }

                if (item.location == ItemLocation.Held && ++hands > 1)
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

            int cropOnTray = 0;
            string dockId = save.growth.dock < 0 ? null : $"dock/{save.growth.dock}";
            foreach (ItemSave item in items.Values)
            {
                bool mushroom = TryMushroomId(item.id, out int crop, out _);
                bool label = TryLabelId(item.id, out int labelNumber);
                if ((!mushroom && !label && !boxes.ContainsKey(item.id) && Array.IndexOf(FixedIds, item.id) < 0) ||
                    (label && labelNumber >= save.nextLabel) || (mushroom && crop > save.cropNumber))
                {
                    throw InvalidSave();
                }

                if ((item.id == "Tray" && (item.location == ItemLocation.Stored) != (dockId != null)) ||
                    (item.id == "Lid" && (item.location == ItemLocation.Stored) != save.growth.lidClosed))
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

                bool docked = item.id == "Tray" && item.parent == dockId && item.location == ItemLocation.Stored;
                bool lid = item.id == "Lid" && item.parent == "Tray" && item.location == ItemLocation.Stored;
                bool onTray = mushroom && item.parent == "Tray" && crop == save.cropNumber &&
                    ((item.location == ItemLocation.Rooted && save.growth.stage == TrayStage.Fruiting) ||
                        ((item.location == ItemLocation.Resting || item.location == ItemLocation.Rooted) &&
                            save.growth.stage == TrayStage.Harvest));
                bool packed = (mushroom || label) && item.location == ItemLocation.Stored && boxes.ContainsKey(item.parent);
                if (!docked && !lid && !onTray && !packed)
                {
                    throw InvalidSave();
                }

                if (onTray)
                {
                    cropOnTray++;
                }
            }

            if (save.growth.stage == TrayStage.Fruiting && cropOnTray != 12)
            {
                throw InvalidSave();
            }

            ILookup<string, ItemSave> children = items.Values.ToLookup(item => item.parent, StringComparer.Ordinal);
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

        private static bool HasParent(ItemSave item) => !string.IsNullOrEmpty(item.parent);
        private static int NextBoxId(WorkshopSave save) =>
            save.version == WorkshopSave.LegacyVersion ? FirstPurchasedBox : save.nextBox;

        private static bool TryBoxId(string id, out int sequence)
        {
            sequence = 0;
            return id != null && id.StartsWith("box/", StringComparison.Ordinal) &&
                int.TryParse(id.AsSpan(4), out sequence) && sequence > 0 && id == $"box/{sequence}";
        }

        private static bool TryLabelId(string id, out int sequence)
        {
            sequence = 0;
            return id.StartsWith("label/", StringComparison.Ordinal) && int.TryParse(id.Substring(6), out sequence) &&
                sequence > 0 && id == $"label/{sequence}";
        }

        private static bool TryMushroomId(string id, out int crop, out int slot)
        {
            crop = slot = 0;
            if (!id.StartsWith("mushroom/", StringComparison.Ordinal))
            {
                return false;
            }

            int separator = id.IndexOf('/', 9);
            return separator > 9 && int.TryParse(id.AsSpan(9, separator - 9), out crop) &&
                int.TryParse(id.AsSpan(separator + 1), out slot) && crop >= 1 && slot >= 1 && slot <= 12 &&
                id == $"mushroom/{crop}/{slot}";
        }

        private static bool Finite(Vector3 vector) =>
            float.IsFinite(vector.x) && float.IsFinite(vector.y) && float.IsFinite(vector.z);

        private static bool Rotation(Quaternion rotation) =>
            Mathf.Abs(Quaternion.Dot(rotation, rotation) - 1f) < 0.01f;

        private static InvalidDataException InvalidSave() => new InvalidDataException("Данные сохранения повреждены.");
    }
}
