using glFTPd_Commander.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace glFTPd_Commander.Utils
{
    public static class CustomCommandSlotStorage
    {
        // Save in user's AppData (recommended) or just app folder (simpler).
        private static readonly string SettingsFile =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CustomCommandSlots.json");


        public static void Save(IList<CustomCommandSlot> slots)
        {
            try
            {
                var folder = Path.GetDirectoryName(SettingsFile)!;
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                var persistList = new List<CustomCommandSlotData>();
                foreach (var slot in slots)
                    persistList.Add(new CustomCommandSlotData { ButtonText = slot.ButtonText, Command = slot.Command });

                var json = JsonSerializer.Serialize(persistList, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[CustomCmd] Error saving slots: " + ex);
            }
        }

        public static void Load(IList<CustomCommandSlot> slots)
        {
            try
            {
                if (!File.Exists(SettingsFile))
                    return;

                var json = File.ReadAllText(SettingsFile);
                var persistList = JsonSerializer.Deserialize<List<CustomCommandSlotData>>(json);

                // Repopulate your slots list
                for (int i = 0; i < Math.Min(slots.Count, persistList?.Count ?? 0); i++)
                {
                    slots[i].Command = persistList![i].Command;
                    slots[i].ButtonText = string.IsNullOrWhiteSpace(persistList[i]?.ButtonText ?? "")
                        ? "Configure Button"
                        : persistList[i]?.ButtonText ?? "Configure Button";

                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[CustomCmd] Error loading slots: " + ex);
            }
        }

        // Serializable POCO for storage (no events)
        private class CustomCommandSlotData
        {
            public string? ButtonText { get; set; }
            public string? Command { get; set; }
        }
    }
}
