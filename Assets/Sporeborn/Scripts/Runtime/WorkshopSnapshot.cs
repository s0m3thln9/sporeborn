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
                printerLabel = _printedLabel != null ? _printedLabel.Id : null,
                items = SceneItems().Values.Select(item => item.Capture(item.Kind == Carryable.ItemKind.Tray
                    ? Growth.DockId : item.transform.parent != null
                        ? item.transform.parent.GetComponent<Carryable>()?.Id : null)).ToArray(),
                boxes = _boxes.Where(box => box != null && box.gameObject.activeSelf).Select(box => box.Capture()).ToArray()
            };
            Growth.Capture(save);
            player.CapturePose(save);
            return save;
        }

        internal void Resume(WorkshopSave save, FirstPersonPlayer player, MushroomInteraction interaction)
        {
            Dictionary<string, ItemSave> records = Validate(save);
            Dictionary<string, Carryable> items = SceneItems();
            Growth.Restore(save);
            foreach (ShippingBox box in _boxes)
            {
                if (!records.ContainsKey(box.Item.Id))
                {
                    items.Remove(box.Item.Id);
                    box.gameObject.SetActive(false);
                    Destroy(box.gameObject);
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

            player.RestorePose(save);
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
                ItemSave[] children = records.Values.Where(item => item.parent == box.id).ToArray();
                ItemSave label = children.SingleOrDefault(item => TryLabelId(item.id, out _));
                items[box.id].GetComponent<ShippingBox>().RestoreContents(box,
                    children.Count(item => TryMushroomId(item.id, out _, out _)), label != null ? items[label.id] : null);
            }

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
            if (save == null || save.version != WorkshopSave.CurrentVersion || save.items == null || save.boxes == null ||
                save.boxes.Length > 3 || save.money < 0 || save.money > 360 || save.money % 10 != 0 ||
                save.nextLabel < 1 || save.cropNumber < 1 || save.cropNumber > 4 ||
                save.scoop < -1 || save.scoop > 2 || !Finite(save.playerPosition) ||
                !float.IsFinite(save.yaw) || !float.IsFinite(save.pitch) || Mathf.Abs(save.pitch) > 85f ||
                !new GrowthCycle().TryRestore(save.growth))
            {
                throw InvalidSave();
            }

            var items = new Dictionary<string, ItemSave>(StringComparer.Ordinal);
            var boxes = new Dictionary<string, BoxSave>(StringComparer.Ordinal);
            foreach (BoxSave box in save.boxes)
            {
                if (box == null || !IsBoxId(box.id) || box.flaps < 0 || box.flaps > ShippingBoxContents.FlapCount ||
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

            foreach (BoxSave box in boxes.Values)
            {
                var contents = items.Values.Where(item => item.parent == box.id).ToArray();
                int labels = contents.Count(item => TryLabelId(item.id, out _));
                if (contents.Length - labels > ShippingBoxContents.Columns * ShippingBoxContents.Rows ||
                    labels > 1 || (labels > 0 && box.flaps != ShippingBoxContents.FlapCount))
                {
                    throw InvalidSave();
                }
            }

            return items;
        }

        private static bool HasParent(ItemSave item) => !string.IsNullOrEmpty(item.parent);
        private static bool IsBoxId(string id) => id == "box/1" || id == "box/2" || id == "box/3";

        private static bool TryLabelId(string id, out int sequence)
        {
            sequence = 0;
            return id.StartsWith("label/", StringComparison.Ordinal) && int.TryParse(id.Substring(6), out sequence) &&
                sequence > 0 && id == $"label/{sequence}";
        }

        private static bool TryMushroomId(string id, out int crop, out int slot)
        {
            crop = slot = 0;
            return id.StartsWith("mushroom/", StringComparison.Ordinal) && id.Length > 11 &&
                int.TryParse(id.Substring(9, 1), out crop) && int.TryParse(id.Substring(11), out slot) &&
                crop >= 1 && crop <= 3 && slot >= 1 && slot <= 12 && id == $"mushroom/{crop}/{slot}";
        }

        private static bool Finite(Vector3 vector) =>
            float.IsFinite(vector.x) && float.IsFinite(vector.y) && float.IsFinite(vector.z);

        private static bool Rotation(Quaternion rotation) =>
            Mathf.Abs(Quaternion.Dot(rotation, rotation) - 1f) < 0.01f;

        private static InvalidDataException InvalidSave() => new InvalidDataException("Данные сохранения повреждены.");
    }
}
