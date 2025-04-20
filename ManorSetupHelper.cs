// ManorSetupHelper.cs - Static helper class for configuration

using Il2CppSystem.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.Tiles;
using Il2CppSystem;
using UnityEngine.Events;
using System.Reflection;
using MelonLoader;
using Il2CppScheduleOne.Misc;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Employees;


// Use the same namespace as MainMod
namespace ChloesManorMod
{
    public static class ManorSetupHelper
    {
        // --- Constants for placeholder names (copy from MainMod) ---
        private const string AtThePropertyName = "AtTheProperty";
        private const string AtTheRealtyName = "AtTheRealty";
        private const string DockPlaceholderPrefix = "Loading Dock ";
        private const string SavePointPlaceholderName = "SavePoint";
        private const string NpcSpawnPointName = "NPCSpawn";
        private const string BungalowPropertyCode = "bungalow"; // Property code for the bungalow
        // --- Names for Save Point Stealing ---
        private const string TemplateSavePointGOName = "Intercom Save Point";
        private const string TargetSavePointGOName = "Intercom Save Point";

        private const string IntermediateGOName = "Intercom";
        private const string LodGOName = "Intercom_LOD0";
        private const string ExtraIdlePointsContainerName = "Extra Employee Idle Points"; // Name of your container
        private const string PropertyIdlePointsGOName = "EmployeeIdlePoints"; // Name of the GO under Property
        private const string ListingPosterName = "PropertyListing Hilltop Manor"; // Name of your listing object in the prefab
        private const string WhiteboardPath = "/Map/Container/RE Office/Interior/Whiteboard"; // Path to the whiteboard

        /// <summary>
        /// Main entry point to configure the instantiated Manor setup structure.
        /// </summary>
        /// <param name="spawnedInstanceRoot">The root GameObject of the instantiated prefab structure.</param>
        /// <param name="manorProperty">The Il2Cpp Property object for the Manor.</param>
        public static void ConfigureManorSetup(GameObject spawnedInstanceRoot, Property manorProperty)
        {
            MelonLogger.Msg($"ManorSetupHelper: Starting configuration for '{manorProperty.PropertyName}' ---");
            if (spawnedInstanceRoot == null || manorProperty == null)
            {
                MelonLogger.Error($"ManorSetupHelper: Aborting due to null instance or property.");
                return;
            }

            manorProperty.Price = 150000;
            manorProperty.EmployeeCapacity = 25;

            Transform parentTransform = spawnedInstanceRoot.transform;
            ManorGate manorGate = manorProperty.GetComponentInChildren<ManorGate>();

            manorProperty.onThisPropertyAcquired.AddListener(new System.Action(() => manorGate.SetEnterable(true)));


            // --- Grid Check ---
            Grid manorGrid = spawnedInstanceRoot.GetComponentInChildren<Grid>(true);
            if (manorGrid == null)
            {
                MelonLogger.Error($"ManorSetupHelper: FAILED to find Grid component within spawned instance '{spawnedInstanceRoot.name}'! Building may fail.");
            }

            // --- Find and Configure Custom Loading Docks ---
            MelonLogger.Msg($"ManorSetupHelper: Finding LoadingDock components within '{parentTransform.name}'...");
            var foundDockComponents = parentTransform.GetComponentsInChildren<LoadingDock>(true);

            Il2CppSystem.Collections.Generic.List<LoadingDock> docksToAdd = new();

            foreach (LoadingDock existingDockComp in foundDockComponents)
            {
                existingDockComp.ParentProperty = manorProperty;

                if (!docksToAdd.Contains(existingDockComp)) { docksToAdd.Add(existingDockComp); }
            }

            // --- Update Manor Property's LoadingDocks Array ---
            if (docksToAdd.Count > 0)
            {
                MelonLogger.Msg($"ManorSetupHelper: Assigning {docksToAdd.Count} configured docks to Manor Property...");
                manorProperty.LoadingDocks = new (docksToAdd.ToArray());
                MelonLogger.Msg($"ManorSetupHelper: Manor assigned {manorProperty.LoadingDocks.Length} LoadingDocks.");
            }
            else { MelonLogger.Warning($"ManorSetupHelper: No valid LoadingDock components found in prefab to assign to Manor."); }

            // --- Configure NPC Spawn Point ---
            Transform npcSpawnPoint = FindDeepChild(parentTransform, NpcSpawnPointName);
            if (npcSpawnPoint != null)
            {
                if (manorProperty.NPCSpawnPoint == null)
                {
                    MelonLogger.Msg($"ManorSetupHelper: Assigning NPC Spawn Point '{npcSpawnPoint.name}'.");
                    manorProperty.NPCSpawnPoint = npcSpawnPoint;
                }
            }
            else { MelonLogger.Warning($"ManorSetupHelper: Could not find '{NpcSpawnPointName}' in prefab children."); }

            // --- Configure Listing Poster ---
            Transform listingPoster = FindDeepChild(parentTransform, ListingPosterName);
             if (listingPoster != null)
            {
                if (manorProperty.ListingPoster == null)
                {
                    MelonLogger.Msg($"ManorSetupHelper: Assigning Listing Poster '{listingPoster.name}'.");
                    manorProperty.ListingPoster = listingPoster;
                }
            }
            else { MelonLogger.Warning($"ManorSetupHelper: Could not find '{ListingPosterName}' in prefab children."); }

            // --- ADDED: Configure Modular Switches ---
            MelonLogger.Msg($"ManorSetupHelper: --- Configuring Modular Switches ---");
            try
            {
                // find all the switches in a format we can use

                // Damn, the extension functions like .ToList and AsEnumerable and such all kinda use System no matter what so we can't use the Il2CppSystem versions which we need to in this case.
                // later on we can make a function to do this more easily *sigh*
                Il2CppSystem.Collections.Generic.List<ModularSwitch> foundSwitches = new();
                foreach (var MSwitch in spawnedInstanceRoot.GetComponentsInChildren<ModularSwitch>(true)) {
                    foundSwitches.Add(MSwitch);
                }

                if (foundSwitches != null && foundSwitches.Count > 0)
                {
                    // Assign the found switches directly to the Manor's list
                    manorProperty.Switches = foundSwitches;
                    MelonLogger.Msg($"ManorSetupHelper: Assigned {manorProperty.Switches.Count} ModularSwitch components to Manor property.");

                    // Optional: Add listener for changes if needed (Property.cs does this, maybe redundant)
                    // foreach (ModularSwitch sw in manorProperty.Switches) {
                    //     if (sw != null) {
                    //         // Ensure listener setup is correct based on Property.cs logic if you replicate it
                    //         sw.onToggled = (ModularSwitch.ButtonChange)Delegate.Combine(sw.onToggled, (ModularSwitch.ButtonChange)delegate {
                    //             manorProperty.HasChanged = true; // Example: Mark property dirty on toggle
                    //             // MelonLogger.Msg($"Switch '{sw.name}' toggled, marking property dirty.");
                    //         });
                    //     }
                    // }
                }
                else
                {
                    MelonLogger.Warning($"ManorSetupHelper: No ModularSwitch components found within the spawned prefab instance.");
                    // Ensure the list is at least initialized if none are found
                    if (manorProperty.Switches == null)
                        manorProperty.Switches = new ();
                    else
                        manorProperty.Switches.Clear(); // Clear if list existed but no switches found now
                }
            }
            catch (System.Exception e)
            {
                 MelonLogger.Error($"ManorSetupHelper: Error configuring Modular Switches: {e.Message}");
            }
            MelonLogger.Msg($"ManorSetupHelper: ----------------------------------");
            // --- END Configure Modular Switches ---

            // --- Configure Switches and Toggleables AFTER prefab setup is complete ---
            // --- Search from the MAIN MANOR PROPERTY transform now ---
            ConfigureSwitchesAndToggleables(manorProperty);
            // --- End Switch/Toggleable config ---

            // --- ADD/MERGE EMPLOYEE IDLE POINTS ---
            ConfigureEmployeeIdlePoints(parentTransform, manorProperty);
            // --- END IDLE POINTS ---

            // --- NEW: Configure the listing at the Realty Office ---
            ConfigureRealtyListing(spawnedInstanceRoot); // Call the new method

            MelonLogger.Msg($"ManorSetupHelper: --- Configuration FINISHED ---");

        } // End ConfigureManorSetup

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

        // --- NEW: Combined Switch/Toggleable Logic ---
        private static void ConfigureSwitchesAndToggleables(Property manorProperty)
        {
             MelonLogger.Msg($"ManorSetupHelper: --- Configuring Switches & Toggleables for Manor ---");
             // Search starting from the property's main transform to include everything
             Transform propertyTransform = manorProperty.transform;

             // Switches
             try
             {
                 var foundSwitches = propertyTransform.GetComponentsInChildren<ModularSwitch>(true)?.ToList();
                 if (foundSwitches != null) // Don't assume Length > 0, just assign if found
                 {
                     manorProperty.Switches = foundSwitches;
                     MelonLogger.Msg($"ManorSetupHelper: Assigned {manorProperty.Switches.Count} ModularSwitch components to Manor.");
                 } else { // Should not happen unless GetComponentsInChildren returns null
                      MelonLogger.Warning($"ManorSetupHelper: GetComponentsInChildren<ModularSwitch> returned null unexpectedly.");
                 }
             }
             catch (System.Exception e) { MelonLogger.Error($"ManorSetupHelper: Error configuring Modular Switches: {e.Message}"); }

             // Toggleables
             try
             {
                  var foundToggleables = propertyTransform.GetComponentsInChildren<InteractableToggleable>(true).ToList();
                   if (foundToggleables != null)
                  {
                      manorProperty.Toggleables = foundToggleables;
                      MelonLogger.Msg($"ManorSetupHelper: Assigned {manorProperty.Toggleables.Count} InteractableToggleable components to Manor.");

                      // Re-attach listeners AFTER the list is populated, mimicking Property.Awake logic
                      foreach (InteractableToggleable toggleable in manorProperty.Toggleables)
                      {
                          if (toggleable != null)
                          {
                               // Use a local function to capture the correct variable instance
                               void ToggleAction() => PropertyToggleableActioned(manorProperty, toggleable);
                               // Remove listener first to prevent duplicates if setup runs again
                               toggleable.onToggle.RemoveListener((UnityEngine.Events.UnityAction)ToggleAction);
                               toggleable.onToggle.AddListener((UnityEngine.Events.UnityAction)ToggleAction);
                          }
                      }
                       MelonLogger.Msg($"ManorSetupHelper: Re-attached listeners for {manorProperty.Toggleables.Count} Toggleables.");
                  } else {
                       MelonLogger.Warning($"ManorSetupHelper: GetComponentsInChildren<InteractableToggleable> returned null unexpectedly.");
                  }
             }
             catch (System.Exception e) { MelonLogger.Error($"ManorSetupHelper: Error configuring InteractableToggleables: {e.Message}"); }

             MelonLogger.Msg($"ManorSetupHelper: --------------------------------------------------");
        }

         // --- NEW: Static helper mirroring Property's internal ToggleableActioned ---
         // We need this because the original is an instance method
         private static void PropertyToggleableActioned(Property propertyInstance, InteractableToggleable toggleable)
         {
            if(propertyInstance == null || toggleable == null) return;
            propertyInstance.HasChanged = true; // Mark dirty
             // The Property script handles sending the state via RPC itself when initialized,
             // but we might need to manually trigger the RPC if state changes *after* initial spawn?
             // For now, just marking dirty might be enough for saving.
             // Let's test this first before adding RPC calls from here.
         }

        // --- NEW: Method to configure Employee Idle Points ---
        private static void ConfigureEmployeeIdlePoints(Transform prefabRoot, Property manorProperty)
        {
            MelonLogger.Msg($"ManorSetupHelper: --- Configuring Employee Idle Points ---");

            // 1. Find the container for EXTRA points in the PREFAB
            // Assuming "Extra Employee Idle Points" is under "AtTheProperty" which is under the root
            Transform atTheProperty = prefabRoot.Find("AtTheProperty");
            Transform extraPointsContainer = null;
            if (atTheProperty != null) {
                extraPointsContainer = atTheProperty.Find(ExtraIdlePointsContainerName);
            } // Can also use FindDeepChild if hierarchy is complex/uncertain

            if (extraPointsContainer == null)
            {
                MelonLogger.Warning($"ManorSetupHelper: Could not find '{ExtraIdlePointsContainerName}' container within prefab. Skipping adding extra idle points.");
                return; // Nothing to add
            }
            MelonLogger.Msg($"ManorSetupHelper: Found '{ExtraIdlePointsContainerName}' container in prefab.");

            var combinedIdlePoints = manorProperty.EmployeeIdlePoints.ToList();

            // 3. Add the transforms of the CHILDREN of the extra points container
            for (int i = 0; i < extraPointsContainer.childCount; i++)
                combinedIdlePoints.Add(extraPointsContainer.GetChild(i));

            MelonLogger.Msg($"ManorSetupHelper: Added {extraPointsContainer.childCount} extra idle point transforms from '{ExtraIdlePointsContainerName}'.");


            // 4. Assign the combined list back to the Property's array
            manorProperty.EmployeeIdlePoints = combinedIdlePoints.ToReferenceArray();
            MelonLogger.Msg($"ManorSetupHelper: Set Manor EmployeeIdlePoints array. New total count: {manorProperty.EmployeeIdlePoints.Length}");
            MelonLogger.Msg($"ManorSetupHelper: --------------------------------------");
        }

        // --- NEW METHOD for Realty Listing ---
        private static void ConfigureRealtyListing(GameObject prefabRoot)
        {
            MelonLogger.Msg($"ManorSetupHelper: Attempting to configure realty listing...");
            Transform sourceListing = FindDeepChild(prefabRoot.transform, ListingPosterName);
            if (sourceListing == null)
            {
                MelonLogger.Warning($"ManorSetupHelper: Could not find realty listing object '{ListingPosterName}' in prefab.");
                return;
            }

            GameObject targetWhiteboard = GameObject.Find(WhiteboardPath);
            if (targetWhiteboard == null)
            {
                MelonLogger.Error($"ManorSetupHelper: Could not find target whiteboard object at path '{WhiteboardPath}'. Cannot reparent listing.");
                return;
            }

            MelonLogger.Msg($"ManorSetupHelper: Found source listing '{sourceListing.name}' and target whiteboard '{targetWhiteboard.name}'. Attempting reparent.");

            try
            {
                // Reparent the listing object to the whiteboard.
                sourceListing.SetParent(targetWhiteboard.transform, true);
                MelonLogger.Msg($"ManorSetupHelper: Successfully reparented '{sourceListing.name}' to '{targetWhiteboard.name}'.");
            }
            catch (System.Exception ex) // Qualified Exception
            {

                MelonLogger.Error($"ManorSetupHelper: Failed to reparent listing object: {ex.Message}");
                MelonLogger.Error(ex); // Use MelonLoader's exception logging
            }
        }

    } // End ManorSetupHelper



    // --- Extension method to convert Il2CppArrayBase<T> to List<T>. Useful for when you do GetComponentsInChildren and you're trying to set some in-game variable---
    public static class ToListExtension
    {
        // tolist to convert Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<T> to Il2CppSystem.Collections.Generic.List<T>
        public static Il2CppSystem.Collections.Generic.List<T> ToList<T>(this Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<T> array)
        {
            if (array == null) return null;
            var list = new Il2CppSystem.Collections.Generic.List<T>(array.Length);
            for (int i = 0; i < array.Length; i++)
            {
                list.Add(array[i]);
            }
            return list;
        }

        // toarray to convert Il2CppSystem.Collections.Generic.List<T> to Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<T>
        public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<T> ToReferenceArray<T>(this Il2CppSystem.Collections.Generic.List<T> list) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        {
            if (list == null) return null;
            var array = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<T>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                array[i] = list[i];
            }
            return array;
        }
    }
} // End namespace