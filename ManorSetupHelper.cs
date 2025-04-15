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
            MelonLogger.Msg($"--- ManorSetupHelper.ConfigureManorSetup START ---");
            if (spawnedInstanceRoot == null || manorProperty == null)
            {
                MelonLogger.Error("ConfigureManorSetup: Spawned instance root or Manor property is null. Aborting setup.");
                return;
            }
            Transform parentTransform = spawnedInstanceRoot.transform; // Root transform of your spawned prefab

            // --- 1. Find Template Projector ---
            GameObject templateProjectorGO = FindTemplateProjector();
            if (templateProjectorGO == null)
            {
                 MelonLogger.Error("Could not find template 'Projector' GameObject from Bungalow Loading Dock. Cannot copy projectors.");
                 // Decide if you want to continue without projectors or stop
                 // return; // Uncomment to stop if template is essential
            }
             else
            {
                 MelonLogger.Msg($"Found template Projector GameObject: '{templateProjectorGO.name}'");
            }
            // --- End Find Template Projector ---


            // --- 2. Find and Configure Custom Loading Docks (and copy projector) ---
            MelonLogger.Msg($"Finding LoadingDock components within '{parentTransform.name}'...");
            var foundDockComponents = parentTransform.GetComponentsInChildren<LoadingDock>(true); // Include inactive
            MelonLogger.Msg($"Found {foundDockComponents.Count} LoadingDock components in prefab children.");

            List<LoadingDock> docksToAdd = new List<LoadingDock>(); // Use a list to collect valid docks

            foreach (LoadingDock existingDockComp in foundDockComponents)
            {
                 MelonLogger.Msg($"--- ManorSetupHelper Processing Existing Dock ---");
                 MelonLogger.Msg($"   Dock GO Name: {existingDockComp.gameObject.name}");
                 MelonLogger.Msg($"   Dock Instance ID: {existingDockComp.GetInstanceID()}");

                 // Assign ParentProperty (if not already set)
                 existingDockComp.ParentProperty = manorProperty;
                 MelonLogger.Msg($"   Assigned ParentProperty: {existingDockComp.ParentProperty?.PropertyName ?? "NULL"}");

                 // Log the Parking reference *from the existing component*
                 MelonLogger.Msg($"   Existing Parking Lot Ref: {(existingDockComp.Parking != null ? existingDockComp.Parking.name : "NULL")}");
                 MelonLogger.Msg($"   Existing Parking Lot Instance ID: {existingDockComp.Parking?.GetInstanceID()}");

                 // Ensure GUID is set (Awake should handle this if BakedGUID is valid, log it)
                 MelonLogger.Msg($"   Dock BakedGUID: {GetProtectedStringField(existingDockComp, "BakedGUID") ?? "N/A"}");
                 MelonLogger.Msg($"   Dock Runtime GUID: {existingDockComp.GUID}");

                 // Add the existing, configured component to our list
                 if (!docksToAdd.Contains(existingDockComp))
                 {

                     docksToAdd.Add(existingDockComp);
                     MelonLogger.Msg($"   Added existing dock to list for property update.");
                 }

                // --- 3. Copy Projector if Template Exists ---
                if (templateProjectorGO != null)
                {
                    MelonLogger.Msg($"   Attempting to copy Projector to dock '{existingDockComp.gameObject.name}'...");
                    try
                    {
                        // Instantiate the template
                        GameObject projectorCopy = GameObject.Instantiate(templateProjectorGO);
                        projectorCopy.name = "Projector (Copied)"; // Rename for clarity

                        // Parent it to the current dock
                        projectorCopy.transform.SetParent(existingDockComp.transform, false); // worldPositionStays = false

                        // Set local position & rotation to match template's original local values
                        projectorCopy.transform.localPosition = templateProjectorGO.transform.localPosition;
                        projectorCopy.transform.localRotation = templateProjectorGO.transform.localRotation;

                        // Make it the first child
                        projectorCopy.transform.SetSiblingIndex(0);

                        MelonLogger.Msg($"   Successfully copied and configured Projector child.");
                    }
                    catch(System.Exception e)
                    {
                         MelonLogger.Error($"   Failed to copy Projector: {e.Message}");
                    }
                }
                else
                {
                    MelonLogger.Warning($"   Skipping Projector copy because template was not found.");
                }
                // --- End Copy Projector ---

                 MelonLogger.Msg($"-------------------------------------------");
            }
            // --- End Find/Configure Docks ---


            // --- 4. Update Manor Property's LoadingDocks Array ---
            if (docksToAdd.Count > 0)
            {
                // Combine with existing docks if necessary, or replace
                // For simplicity assuming replacement for now, adjust if needed
                MelonLogger.Msg($"Assigning {docksToAdd.Count} configured docks to Manor Property's LoadingDocks array.");
                manorProperty.LoadingDocks = docksToAdd.ToArray();
                // Log the final array state for confirmation
                 MelonLogger.Msg($"Manor Property now has {manorProperty.LoadingDocks.Length} docks.");
                 for(int i = 0; i < manorProperty.LoadingDocks.Length; i++)
                 {
                     MelonLogger.Msg($"   Index {i}: {manorProperty.LoadingDocks[i]?.gameObject.name ?? "NULL"}, Parking: {manorProperty.LoadingDocks[i]?.Parking?.name ?? "NULL"}");
                 }
            }
            else
            {
                MelonLogger.Warning("No valid LoadingDock components found in prefab to assign to Manor.");
            }
            // --- End Update Manor Property ---

            // --- 5. Configure NPC Spawn Point ---
            Transform npcSpawnPoint = FindDeepChild(parentTransform, NpcSpawnPointName);
            if (npcSpawnPoint != null)
            {
                if (manorProperty.NPCSpawnPoint == null)
                {
                    MelonLogger.Msg($"Assigning NPC Spawn Point '{npcSpawnPoint.name}' to Manor.");
                    manorProperty.NPCSpawnPoint = npcSpawnPoint;
                }
                else
                {
                    MelonLogger.Msg("Manor already has an NPC Spawn Point. Skipping assignment.");
                }
            }
            else { MelonLogger.Warning($"Could not find '{NpcSpawnPointName}' in prefab children."); }
            // --- End NPC Spawn Point ---

            // --- 6. Configure Listing Poster ---
            Transform listingPoster = FindDeepChild(parentTransform, ListingPosterName);
             if (listingPoster != null)
            {
                if (manorProperty.ListingPoster == null)
                {
                     MelonLogger.Msg($"Assigning Listing Poster '{listingPoster.name}' to Manor.");
                     manorProperty.ListingPoster = listingPoster;
                }
                else
                {
                     MelonLogger.Msg("Manor already has a Listing Poster. Skipping assignment.");
                }
            }
            else { MelonLogger.Warning($"Could not find '{ListingPosterName}' in prefab children."); }
            // --- End Listing Poster ---


            MelonLogger.Msg($"--- ManorSetupHelper.ConfigureManorSetup FINISHED ---");

            // Inside the method that handles onThisPropertyAcquired
            MelonLogger.Msg($"--- Gate Opening Handler ---");
            ManorGate theManorGate = GameObject.FindObjectOfType<ManorGate>(); // Or however you find it
            MelonLogger.Msg($"   Found ManorGate instance: {(theManorGate != null ? theManorGate.name : "NULL")}");
            if (theManorGate != null)
            {
                MelonLogger.Msg($"   Calling SetEnterable(true) on gate instance ID: {theManorGate.GetInstanceID()}");
                theManorGate.SetEnterable(true);
                 MelonLogger.Msg($"   SetEnterable call completed.");
            }
            else
            {
                MelonLogger.Error("   Could not find ManorGate instance to call SetEnterable!");
            }
             MelonLogger.Msg($"--------------------------");

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