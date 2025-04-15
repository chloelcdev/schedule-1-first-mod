// ManorSetupHelper.cs - Static helper class for configuration

using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime; // If needed for casting/helpers
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.Tiles;
using Il2CppSystem; // For Guid
using UnityEngine.Events;
using System.Reflection;
using MelonLoader;

// Use the same namespace as MainMod
namespace ChloesManorMod
{
    public static class ManorSetupHelper
    {
        // --- Constants for placeholder names (copy from MainMod) ---
        private const string AtThePropertyName = "AtTheProperty";
        private const string AtTheRealtyName = "AtTheRealty";
        private const string DockPlaceholderPrefix = "Loading Dock ";
        private const string NpcSpawnPlaceholderName = "NPCSpawn";
        private const string ListingPosterPlaceholderName = "PropertyListing Docks Manor";
        private const string SavePointPlaceholderName = "SavePoint";
        private const string ManorGatePlaceholderName = "ManorGatePos"; // todo: do this, it shouldn't be attached like this
        private const string NpcSpawnPointName = "NPC Spawn Point";
        private const string ListingPosterName = "Listing Poster";
        private const string BungalowPropertyCode = "bungalow"; // Property code for the bungalow

        /// <summary>
        /// Main entry point to configure the instantiated Manor setup structure.
        /// </summary>
        /// <param name="spawnedInstanceRoot">The root GameObject of the instantiated prefab structure.</param>
        /// <param name="manorProperty">The Il2Cpp Property object for the Manor.</param>
        public static void ConfigureManorSetup(GameObject spawnedInstanceRoot, Property manorProperty)
        {
            MelonLogger.Msg($"--- ManorSetupHelper starting configuration for '{manorProperty.PropertyName}' ---");
            if (spawnedInstanceRoot == null || manorProperty == null)
            {
                MelonLogger.Error("ManorSetupHelper: Aborting due to null instance or property.");
                return;
            }


            Transform parentTransform = spawnedInstanceRoot.transform;
            ManorGate manorGate = manorProperty.GetComponentInChildren<ManorGate>();

            manorProperty.onThisPropertyAcquired.AddListener(new System.Action(() => manorGate.SetEnterable(true)));


            // --- Grid Check ---
            Grid manorGrid = spawnedInstanceRoot.GetComponentInChildren<Grid>(true);
            if (manorGrid != null) { }
            else { MelonLogger.Error($"   !!! FAILED to find Grid component within spawned instance '{spawnedInstanceRoot.name}'! Building may fail."); }

            // --- Find Template Projector ---
            GameObject templateProjectorGO = FindTemplateProjector();
            if (templateProjectorGO == null) { MelonLogger.Warning("Could not find template 'Projector' from Bungalow. Projectors will not be added."); }

            // --- Find and Configure Custom Loading Docks ---
            MelonLogger.Msg($"Finding LoadingDock components within '{parentTransform.name}'...");
            var foundDockComponents = parentTransform.GetComponentsInChildren<LoadingDock>(true);

            List<LoadingDock> docksToAdd = new List<LoadingDock>();

            foreach (LoadingDock existingDockComp in foundDockComponents)
            {
                existingDockComp.ParentProperty = manorProperty;

                if (!docksToAdd.Contains(existingDockComp)) { docksToAdd.Add(existingDockComp); }

                // Copy Projector
                if (templateProjectorGO != null)
                {
                    try
                    {
                        GameObject projectorCopy = GameObject.Instantiate(templateProjectorGO);
                        projectorCopy.name = "Projector (Copied)";
                        projectorCopy.transform.SetParent(existingDockComp.transform, false);
                        projectorCopy.transform.localPosition = templateProjectorGO.transform.localPosition;
                        projectorCopy.transform.localRotation = templateProjectorGO.transform.localRotation;
                        projectorCopy.transform.SetSiblingIndex(0);
                    }
                    catch(System.Exception e) { MelonLogger.Error($"   Failed to copy Projector for dock '{existingDockComp.gameObject.name}': {e.Message}"); }
                }
            }

            // --- Update Manor Property's LoadingDocks Array ---
            if (docksToAdd.Count > 0)
            {
                MelonLogger.Msg($"Assigning {docksToAdd.Count} configured docks to Manor Property...");
                manorProperty.LoadingDocks = docksToAdd.ToArray();
                MelonLogger.Msg($"Manor assigned {manorProperty.LoadingDocks.Length} LoadingDocks.");
            }
            else { MelonLogger.Warning("No valid LoadingDock components found in prefab to assign to Manor."); }

            // --- Configure NPC Spawn Point ---
            Transform npcSpawnPoint = FindDeepChild(parentTransform, NpcSpawnPointName);
            if (npcSpawnPoint != null)
            {
                if (manorProperty.NPCSpawnPoint == null)
                {
                    MelonLogger.Msg($"Assigning NPC Spawn Point '{npcSpawnPoint.name}'.");
                    manorProperty.NPCSpawnPoint = npcSpawnPoint;
                }
            }
            else { MelonLogger.Warning($"Could not find '{NpcSpawnPointName}' in prefab children."); }

            // --- Configure Listing Poster ---
            Transform listingPoster = FindDeepChild(parentTransform, ListingPosterName);
             if (listingPoster != null)
            {
                if (manorProperty.ListingPoster == null)
                {
                    MelonLogger.Msg($"Assigning Listing Poster '{listingPoster.name}'.");
                    manorProperty.ListingPoster = listingPoster;
                }
            }
            else { MelonLogger.Warning($"Could not find '{ListingPosterName}' in prefab children."); }

            MelonLogger.Msg($"--- ManorSetupHelper configuration FINISHED ---");

        } // End ConfigureManorSetup

        // --- ADDED: Helper to find the template projector ---
        private static GameObject FindTemplateProjector()
        {
            MelonLogger.Msg($"--- Finding Template Projector from Bungalow ---");
            PropertyManager propManager = PropertyManager.Instance;
            if (propManager == null)
            {
                MelonLogger.Error("PropertyManager instance not found!");
                return null;
            }

            Property bungalowProp = propManager.GetProperty(BungalowPropertyCode);
            if (bungalowProp == null)
            {
                MelonLogger.Error($"Could not find Bungalow property (Code: '{BungalowPropertyCode}')!");
                return null;
            }
             MelonLogger.Msg($"Found Bungalow property: '{bungalowProp.PropertyName}'");


             if (bungalowProp.LoadingDocks == null || bungalowProp.LoadingDocks.Length == 0)
             {
                 MelonLogger.Error("Bungalow property has no LoadingDocks in its array!");
                 return null;
             }

            LoadingDock templateDock = bungalowProp.LoadingDocks[0];
             if (templateDock == null)
             {
                 MelonLogger.Error("Bungalow's LoadingDock at index 0 is null!");
                 return null;
             }
             MelonLogger.Msg($"Found template LoadingDock: '{templateDock.gameObject.name}'");


             Transform projectorTransform = templateDock.transform.Find("Projector");
              if (projectorTransform == null)
             {
                 MelonLogger.Error("Could not find child GameObject named 'Projector' under the template LoadingDock!");
                 return null;
             }

             return projectorTransform.gameObject; // Return the GameObject
        }

        // --- Helper Function to Find Deep Child ---
        private static Transform FindDeepChild(Transform parent, string childName)
        {
             // Existing FindDeepChild code... (keep as is)
            Transform child = parent.Find(childName);
            if (child != null) return child;
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true)) // Include inactive
            {
                if (t.gameObject.name == childName) return t;
            }
            return null;
        }

        // --- Helper Function to Read Protected Field (for logging) ---
        private static string GetProtectedStringField(Component target, string fieldName)
        {
             if (target == null) return null;
             FieldInfo fieldInfo = target.GetType().GetField(
                 fieldName,
                 System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
             );
             if (fieldInfo == null || fieldInfo.FieldType != typeof(string)) return null;
             try { return fieldInfo.GetValue(target) as string; } catch { return null; }
        }

    } // End ManorSetupHelper
} // End namespace