using System;
using System.IO;
using Sporeborn.Gameplay.Growing;
using UnityEngine;

namespace Sporeborn.Persistence
{
    [Serializable]
    public sealed class WorkshopSave
    {
        public const int CurrentVersion = 1;
        public int version;
        public int money;
        public int nextLabel;
        public string printerLabel;
        public GrowthCycle.Snapshot growth;
        public int scoop;
        public bool cupFull;
        public bool cultureLoaded;
        public int cropNumber;
        public ItemSave[] items;
        public BoxSave[] boxes;
        public Vector3 playerPosition;
        public float yaw;
        public float pitch;
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
            if (save == null || save.version != WorkshopSave.CurrentVersion)
            {
                throw new InvalidDataException("Версия сохранения не поддерживается.");
            }

            return save;
        }

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
