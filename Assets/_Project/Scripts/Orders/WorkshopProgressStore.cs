using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace BORis
{
    [Serializable]
    public sealed class WorkshopProgress
    {
        public int version;
        public int balance;
        public bool hasSolderingStation;
        public int completedOrders;
        public string nextOrder;
        public string activeOrder;
        public string activeInstance;
        public int activeReward;
        public string lastDeliveredInstance;

        public bool IsValid => version == 1 && balance >= 0 && completedOrders >= 0 && activeReward >= 0
            && !string.IsNullOrWhiteSpace(nextOrder)
            && (string.IsNullOrEmpty(activeOrder) ? string.IsNullOrEmpty(activeInstance) && activeReward == 0
                : Guid.TryParseExact(activeInstance, "N", out _) && activeInstance != lastDeliveredInstance)
            && (string.IsNullOrEmpty(lastDeliveredInstance) || Guid.TryParseExact(lastDeliveredInstance, "N", out _));
    }

    public sealed class WorkshopProgressStore
    {
        public string FilePath { get; }
        bool recoveredBackup;

        public WorkshopProgressStore(string filePath) => FilePath = filePath;

        public static string DefaultPath => Environment.GetEnvironmentVariable("BORIS_SAVE_PATH")
            ?? Path.Combine(Application.persistentDataPath, "workshop.json");

        WorkshopProgress Read(string path)
        {
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Save file is too large");
            var data = JsonUtility.FromJson<WorkshopProgress>(File.ReadAllText(path));
            if (data != null && data.version > 1) throw new NotSupportedException("Newer save version");
            if (data == null || !data.IsValid) throw new InvalidDataException("Invalid workshop progress");
            return data;
        }

        public bool TryLoad(out WorkshopProgress data, out string message)
        {
            data = null;
            message = "";
            recoveredBackup = false;
            try
            {
                if (File.Exists(FilePath))
                {
                    try { data = Read(FilePath); return true; }
                    catch (NotSupportedException) { throw; }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException) { }
                }
                else if (!File.Exists(FilePath + ".bak")) return true;
                data = Read(FilePath + ".bak");
                recoveredBackup = true;
                message = "Восстановлена резервная копия. Последнее действие могло не сохраниться.";
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException
                || ex is ArgumentException || ex is NotSupportedException)
            {
                message = "Не удалось загрузить прогресс. На стойке можно повторить загрузку. Файл не изменён.";
                Debug.LogWarning($"Workshop load failed: {ex.Message}");
                return false;
            }
        }

        public bool TrySave(WorkshopProgress data, out string message)
        {
            message = "";
            try
            {
                if (data == null || !data.IsValid) throw new InvalidDataException("Invalid workshop progress");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath)));
                // Reject locked or read-only saves before preparing a replacement.
                if (File.Exists(FilePath))
                    using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data, true));
                using (var stream = new FileStream(FilePath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                // Keep the confirmed snapshot until the complete replacement is ready on the same filesystem.
                if (File.Exists(FilePath)) File.Replace(FilePath + ".tmp", FilePath, recoveredBackup ? null : FilePath + ".bak");
                else File.Move(FilePath + ".tmp", FilePath);
                recoveredBackup = false;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException
                || ex is ArgumentException || ex is NotSupportedException)
            {
                message = "Не удалось сохранить. Деньги и робот не изменены. Повторите действие.";
                Debug.LogWarning($"Workshop save failed: {ex.Message}");
                return false;
            }
        }
    }
}
