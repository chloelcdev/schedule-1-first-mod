using HarmonyLib;
using MelonLoader;
using Il2CppScheduleOne.Employees; // Use Il2Cpp namespace for Cleaner
using Il2CppScheduleOne.NPCs.Behaviour; // For Behaviour base class/properties
using Il2CppScheduleOne.Trash; // For TrashContainerItem, TrashItem
using Il2CppSystem.Collections.Generic; // Needed for Il2CppReferenceArray if patching methods returning lists
using Il2CppInterop.Runtime.InteropTypes.Arrays; // ADDED for Il2CppReferenceArray
using System.Reflection; // For accessing private fields/methods if needed
using System.Text; // For StringBuilder
using Il2CppScheduleOne.ObjectScripts; // Contains TrashContainerItem, assume WorkableObject too
using System; // For Exception, Type
using UnityEngine; // ADDED for Transform and Vector3

namespace ChloesManorMod // Use your mod's namespace
{
    [HarmonyPatch(typeof(Cleaner))] 
    internal static class CleanerTaskDebugPatch
    {
        private static MelonLogger.Instance Logger => Melon<MainMod>.Logger; // Cache logger

        // --- Reflection Helper Methods ---
        private static BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static T GetInstanceField<T>(object instance, string fieldName) where T : class
        {
            FieldInfo field = instance?.GetType().GetField(fieldName, instanceFlags);
            return field?.GetValue(instance) as T;
        }

        private static T InvokeInstanceMethod<T>(object instance, string methodName, params object[] parameters)
        {
            MethodInfo method = instance?.GetType().GetMethod(methodName, instanceFlags);
            object result = method?.Invoke(instance, parameters);
            // Handle potential null result or incorrect type conversion
            if (result == null) return default(T); 
            try { return (T)Convert.ChangeType(result, typeof(T)); }
            catch { return default(T); }
        }

        // ADDED: Helper for VALUE types (bool, int, float, structs etc.)
        private static T GetInstanceFieldValue<T>(object instance, string fieldName) where T : struct
        {
            FieldInfo field = instance?.GetType().GetField(fieldName, instanceFlags);
            object result = field?.GetValue(instance);
            if (result is T value) // Check if the result is directly the type we want
            {
                return value;
            }
            // Handle cases where it might be boxed or needs conversion (optional, can add later if needed)
            // For now, return default if direct cast fails
            return default(T); 
        }
        // --- End Reflection Helpers ---

        [HarmonyPatch(nameof(Cleaner.TryStartNewTask))]
        [HarmonyPostfix]
        static void TryStartNewTask_Postfix(Cleaner __instance) 
        {
            try
            {
                if (__instance == null) { Logger.Warning("[CleanerPatch_TryStartNewTask] Postfix: Instance was null."); return; }

                StringBuilder sb = new StringBuilder();
                string cleanerName = __instance.FirstName ?? "UnknownCleaner";
                sb.AppendLine($"--- [CleanerPatch_TryStartNewTask] Postfix for: {cleanerName} ---");
                
                // USE REFLECTION for Cleaner members
                object activeBehaviourObj = GetInstanceField<object>(__instance, "activeBehaviour");
                string currentBehaviourName = activeBehaviourObj?.GetType().Name ?? "None";
                sb.AppendLine($"  Current Active Behaviour (After Call): {currentBehaviourName}"); 
                sb.AppendLine($"  Is Waiting Outside: {__instance.IsWaitingOutside}");
                bool isWorking = InvokeInstanceMethod<bool>(__instance, "IsWorking");
                sb.AppendLine($"  Is Working: {isWorking}"); 

                // Determine if a task was likely set by checking the behaviour *after* the method ran
                // If it's NOT IdleBehaviour or RoamBehaviour, assume a task was found.
                // NOTE: This is an assumption, IdleBehaviour might have other names/types.
                bool taskLikelyFound = currentBehaviourName != "IdleBehaviour" && currentBehaviourName != "RoamBehaviour" && currentBehaviourName != "None";

                // Only log bin details if no task was likely found 
                if (!taskLikelyFound)
                {
                    sb.AppendLine("  No suitable task seems to have been set. Checking assigned bins for reasons...");
                    // Re-get the list to see what was checked (assuming GetTrashContainersOrderedByDistance is deterministic)
                    var orderedBins = __instance.GetTrashContainersOrderedByDistance();
                    int binCount = orderedBins?.Count ?? 0;
                    sb.AppendLine($"    Found {binCount} assigned bins ordered by distance (re-checked). Goint through reason...");
                    
                    if (orderedBins != null && binCount > 0)
                    {
                        int checkedBinIndex = 0;
                        foreach (var bin_raw in orderedBins) 
                        {
                            // Assume bin_raw is TrashContainerItem
                            TrashContainerItem bin = bin_raw as TrashContainerItem;
                            sb.AppendLine($"    Checking Bin #{checkedBinIndex + 1}: {(bin?.name ?? "NULL BIN")}");
                            if (bin == null)
                            {
                                sb.AppendLine("      - Bin is null or not a TrashContainerItem, skipping.");
                                checkedBinIndex++;
                                continue; 
                            }
                            
                            // Use DIRECT ACCESS for the public field
                            Transform[] accessPointsArray = bin.accessPoints; 
                            Transform targetAccessPoint = null;
                            
                            // Log the result of direct access
                            if (accessPointsArray == null)
                            {
                                sb.AppendLine("      - Direct access: bin.accessPoints is NULL.");
                            }
                            else if (accessPointsArray.Length == 0)
                            {
                                sb.AppendLine("      - Direct access: bin.accessPoints is EMPTY.");
                            }
                            else
                            {
                                // Use the first access point
                                targetAccessPoint = accessPointsArray[0];
                                sb.AppendLine($"      - Direct access: Found bin.accessPoints (Length: {accessPointsArray.Length}). Using first point: {targetAccessPoint?.name ?? "NULL"}");
                            }
                           
                            // --- Log State Relevant to CanWork --- 
                            // 1. UsableByCleaners flag (use VALUE type helper)
                            bool usableByCleaners = GetInstanceFieldValue<bool>(bin, "UsableByCleaners"); 
                            sb.AppendLine($"      - bin.UsableByCleaners: {usableByCleaners}");

                            // 2. HasTrash / CanEmpty state
                            bool hasTrash = InvokeInstanceMethod<bool>(bin, "HasTrash"); 
                            sb.AppendLine($"      - bin.HasTrash(): {hasTrash}");
                            bool canEmpty = false;
                            if (!hasTrash) 
                            {
                                canEmpty = InvokeInstanceMethod<bool>(bin, "CanEmpty"); 
                                sb.AppendLine($"      - bin.CanEmpty(): {canEmpty}");
                            }
                            else
                            {
                                 sb.AppendLine($"      - bin.CanEmpty(): (Skipped - Has Trash)");
                            }

                            // 3. TrashLevel from Container component
                            int trashLevel = -1; // Default invalid
                            if (bin.Container != null) 
                            {
                                // Try getting TrashLevel property via reflection
                                trashLevel = InvokeInstanceMethod<int>(bin.Container, "get_TrashLevel");
                                sb.AppendLine($"      - bin.Container.TrashLevel: {trashLevel}");
                            }
                            else
                            {
                                sb.AppendLine("      - bin.Container is NULL.");
                            }
                            // --- End State Logging ---

                            // Now check CanWork itself
                            bool canWork = InvokeInstanceMethod<bool>(bin, "CanWork"); 
                            sb.AppendLine($"      - bin.CanWork() Result: {canWork}"); // Clarify this is the result

                            // CanAccess check remains the same...
                            bool canAccess = false; 
                            if (targetAccessPoint != null) 
                            {
                                Vector3 nodePos = targetAccessPoint.position; 
                                canAccess = InvokeInstanceMethod<bool>(__instance, "CanAccess", nodePos); 
                                sb.AppendLine($"      - CanAccess(accessPoint[0].position): {canAccess} (Node Pos: {nodePos})");
                            }
                            else
                            {
                                sb.AppendLine("      - Cannot check CanAccess: No valid access point found/retrieved.");
                            }

                            // Log final outcome remains the same...
                            if (canWork && canAccess)
                            {
                                sb.AppendLine("      - Outcome: Should be able to work on this bin."); 
                            }
                            else
                            {
                                sb.AppendLine("      - Outcome: Cannot work on this bin.");
                            }
                            
                            checkedBinIndex++;
                        }
                    }
                    else
                    {
                        sb.AppendLine("    No assigned bins found or list was null.");
                    }
                    sb.AppendLine("  Finished checking bins.");
                }
                else 
                {
                     sb.AppendLine("  Task was likely set (Behaviour changed). Skipping detailed bin check.");
                }

                sb.AppendLine("--- End Postfix Log ---");
                Logger.Msg(sb.ToString());
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[CleanerPatch_TryStartNewTask] Postfix Exception: {ex}");
            }
        }

        // --- NEW PATCH for GetTrashContainersOrderedByDistance --- 
        [HarmonyPatch("GetTrashContainersOrderedByDistance")] 
        [HarmonyPostfix]
        // Use System.Object for __result and reflection to get Count
        static void GetTrashContainers_Postfix(Cleaner __instance, object __result) 
        {
             try
            {
                if (__instance == null) { Logger.Warning("[CleanerPatch_GetBins] Postfix: Instance was null."); return; }
                
                int count = 0;
                if (__result != null) {
                    // Use reflection to get the Count property, avoids compile-time type issues
                    try {
                        PropertyInfo countProp = __result.GetType().GetProperty("Count");
                        if (countProp != null) {
                             // Make sure the property returns an int before casting
                             if (countProp.PropertyType == typeof(int)) {
                                 count = (int)countProp.GetValue(__result);
                             } else {
                                  Logger.Warning($"[CleanerPatch_GetBins] Postfix for: {__instance.FirstName} - Result object has 'Count' property but it's not an int (Type: {countProp.PropertyType.Name}).");
                             }
                        } else {
                            Logger.Warning($"[CleanerPatch_GetBins] Postfix for: {__instance.FirstName} - Result object (Type: {__result.GetType().FullName}) does not have a 'Count' property.");
                        }
                    } catch (System.Exception reflectEx) {
                         Logger.Error($"[CleanerPatch_GetBins] Postfix reflection error getting Count: {reflectEx.Message}");
                    }
                }
                // else: __result was null, count remains 0
                
                Logger.Msg($"[CleanerPatch_GetBins] Postfix for: {__instance.FirstName} - Found {count} assigned bin(s).");
            }
            catch (System.Exception ex) { Logger.Error($"[CleanerPatch_GetBins] Postfix Exception: {ex}"); }
        }
        // --- END NEW PATCH --- 

        // TODO: Add reflection helpers here if needed
    }
} 