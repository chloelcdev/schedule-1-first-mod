using HarmonyLib;
using MelonLoader;
using Il2CppScheduleOne.Employees; 
using Il2CppScheduleOne.Management; // For CleanerConfiguration
using Il2CppScheduleOne.EntityFramework; // ADDED for BuildableItem
using Il2CppScheduleOne.ObjectScripts; // CORRECT namespace for TrashContainerItem
using Il2CppSystem.Collections.Generic; // Using this List specifically
using System.Text; // For StringBuilder

// Existing using statements for CleanerTaskDebugPatch can remain if in the same file

namespace ChloesManorMod // Use your mod's namespace
{
    // Patching CleanerConfiguration now
    [HarmonyPatch(typeof(CleanerConfiguration))]
    internal static class CleanerConfigurationDebugPatch
    {
        private static MelonLogger.Instance Logger => Melon<MainMod>.Logger;

        // Patch the method that updates the internal bin list when the UI changes
        [HarmonyPatch(nameof(CleanerConfiguration.AssignedBinsChanged))]
        [HarmonyPostfix]
        // Explicitly use Il2Cpp List<> and fully qualify BuildableItem
        static void AssignedBinsChanged_Postfix(CleanerConfiguration __instance, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.EntityFramework.BuildableItem> objects)
        {   
            try
            {
                if (__instance == null) { Logger.Warning("[CleanerConfigPatch] Postfix: Instance was null."); return; }

                // Build log message
                StringBuilder sb = new StringBuilder();
                string cleanerName = __instance.cleaner?.FirstName ?? "UnknownCleaner";
                sb.AppendLine($"[CleanerConfigPatch] AssignedBinsChanged Postfix for {cleanerName}'s Configuration:");

                // Log input from UI
                int inputCount = objects?.Count ?? 0;
                sb.AppendLine($"  - Input 'objects' count (from UI): {inputCount}");

                // Log resulting internal list
                int resultCount = __instance.binItems?.Count ?? 0;
                sb.AppendLine($"  - Resulting 'binItems' count: {resultCount}");

                // Log names in the resulting list
                if (__instance.binItems != null && resultCount > 0)
                {
                    sb.Append("  - Resulting binItems: [");
                    bool first = true;
                    // Use the simple type name now that the correct using is present
                    foreach (TrashContainerItem item in __instance.binItems)
                    {
                        if (!first) sb.Append(", ");
                        sb.Append(item?.name ?? "NULL_ITEM");
                        first = false;
                    }
                    sb.Append("]");
                }
                else if (resultCount == 0)
                {
                    sb.Append("  - Resulting binItems: []");
                }

                Logger.Msg(sb.ToString());
            }
            catch (System.Exception ex)
            {
                 Logger.Error($"[CleanerConfigPatch] AssignedBinsChanged Postfix Exception: {ex}");
            }
        }
    }
}

// --- You can keep the CleanerTaskDebugPatch class in the same file or separate it --- 
// namespace ChloesManorMod 
// {
//     [HarmonyPatch(typeof(Cleaner))] 
//     internal static class CleanerTaskDebugPatch 
//     { ... }
// } 