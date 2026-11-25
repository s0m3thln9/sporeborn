using System;
using System.IO;
using Sporeborn.Gameplay.Growing;
using UnityEngine;

namespace Sporeborn.Persistence
{
    [Serializable]
    public sealed class WorkshopSave
    {
        public const int CurrentVersion = 3;
        public const int RestockingVersion = 2;
        public const int LegacyVersion = 1;
        public int version;
        public int money;
        public int nextLabel;
        public int nextBox;
        public string printerLabel;
        public LegacyGrowthSnapshot growth;
        public GrowthCycle.Snapshot workshopGrowth;
        public int scoop;
        public bool cupFull;
        public bool cultureLoaded;
        public int cropNumber;
        public ItemSave[] items;
        public BoxSave[] boxes;
        public Vector3 playerPosition;
        public float yaw;
        public float pitch;

        public GrowthCycle.Snapshot GrowthSnapshot()
        {
            if (version == CurrentVersion)
            {
                return workshopGrowth;
            }

            if (growth == null || growth.doors?.Length != 3 || growth.finished?.Length != 3 ||
                growth.dock < -1 || growth.dock >= growth.doors.Length || cropNumber < 1 || cropNumber == int.MaxValue ||
                (growth.dock < 0 && growth.remaining != 0f))
            {
                return null;
            }

            var devices = new GrowthCycle.DeviceSnapshot[growth.doors.Length];
            for (int index = 0; index < devices.Length; index++)
            {
                bool loaded = growth.dock == index;
                devices[index] = new GrowthCycle.DeviceSnapshot
                {
                    open = growth.doors[index],
                    trayNumber = loaded ? 1 : 0,
                    remaining = loaded ? growth.remaining : 0f
                };
            }

            if (growth.dock >= 0)
            {
                TrayStage completed = (Station)growth.dock switch
                {
                    Station.Treatment => TrayStage.Treated,
                    Station.Incubation => TrayStage.Colonized,
                    _ => TrayStage.Harvest
                };
                if (growth.finished[growth.dock] != (growth.stage == completed))
                {
                    return null;
                }
            }

            bool hasCrop = growth.stage == TrayStage.Fruiting || growth.stage == TrayStage.Harvest;
            return new GrowthCycle.Snapshot
            {
                stock = growth.stock,
                mix = growth.mix,
                mixing = growth.mixing,
                jarOpen = growth.jarOpen,
                nextCrop = hasCrop ? cropNumber + 1 : cropNumber,
                trays = new[]
                {
                    new GrowthCycle.TraySnapshot { stage = growth.stage, lidClosed = growth.lidClosed, crop = hasCrop ? cropNumber : 0 }
                },
                devices = devices
            };
        }
    }

    [Serializable]
    public sealed class LegacyGrowthSnapshot
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

    public enum ItemLocation { Resting, Falling, Held, Stored, Rooted }

    [Serializable]
    public sealed class ItemSave
    {
        public string id;
        public string parent;
        public ItemLocation location;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 recoveryPosition;
        public Quaternion recoveryRotation;
    }

    [Serializable]
    public sealed class BoxSave
    {
        public string id;
        public int flaps;
        public bool confirmed;
    }

    public static class WorkshopSaveFile
    {
        public static WorkshopSave Read(string path)
        {
            WorkshopSave save = JsonUtility.FromJson<WorkshopSave>(File.ReadAllText(path));
            if (save == null || !IsSupportedVersion(save.version))
            {
                throw new InvalidDataException("Версия сохранения не поддерживается.");
            }

            return save;
        }

        public static bool IsSupportedVersion(int version) =>
            version is WorkshopSave.LegacyVersion or WorkshopSave.RestockingVersion or WorkshopSave.CurrentVersion;

        public static void Write(string path, WorkshopSave save)
        {
            string temporary = path + ".tmp";
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write))
            {
                using (var writer = new StreamWriter(file, System.Text.Encoding.UTF8, 4096, true))
                {
                    writer.Write(JsonUtility.ToJson(save));
                }

                file.Flush(true);
            }

            if (File.Exists(path))
            {
                File.Replace(temporary, path, path + ".bak");
            }
            else
            {
                File.Move(temporary, path);
            }
        }
    }
}
