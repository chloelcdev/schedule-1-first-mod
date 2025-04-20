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
            MelonLogger.Msg($"--- ManorSetupHelper starting configuration for '{manorProperty.PropertyName}' ---");
            if (spawnedInstanceRoot == null || manorProperty == null)
            {
                MelonLogger.Error("ManorSetupHelper: Aborting due to null instance or property.");
                return;
            }

            manorProperty.Price = 150000;
            manorProperty.EmployeeCapacity = 25;

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

            // --- Find Template Save Point Materials ---
            Material[] templateSavePointMaterials = FindTemplateSavePointMaterials();
            // --- End Find Template Materials ---

            // --- Find and Configure Custom Loading Docks ---
            MelonLogger.Msg($"Finding LoadingDock components within '{parentTransform.name}'...");
            var foundDockComponents = parentTransform.GetComponentsInChildren<LoadingDock>(true);

            Il2CppSystem.Collections.Generic.List<LoadingDock> docksToAdd = new();

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
                manorProperty.LoadingDocks = new (docksToAdd.ToArray()); // lookie der
                MelonLogger.Msg($"Manor assigned {manorProperty.LoadingDocks.Length} LoadingDocks.");
            }
            else { MelonLogger.Warning("No valid LoadingDock components found in prefab to assign to Manor."); }

            // --- Apply Stolen Materials to Custom Save Point ---
            ApplyMaterialsToCustomSavePoint(parentTransform, templateSavePointMaterials);
            // --- End Apply Materials ---

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

            // --- ADDED: Configure Modular Switches ---
            MelonLogger.Msg("--- Configuring Modular Switches ---");
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
                    MelonLogger.Msg($"Assigned {manorProperty.Switches.Count} ModularSwitch components to Manor property.");

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
                    MelonLogger.Warning("No ModularSwitch components found within the spawned prefab instance.");
                    // Ensure the list is at least initialized if none are found
                    if (manorProperty.Switches == null)
                        manorProperty.Switches = new ();
                    else
                        manorProperty.Switches.Clear(); // Clear if list existed but no switches found now
                }
            }
            catch (System.Exception e)
            {
                 MelonLogger.Error($"Error configuring Modular Switches: {e.Message}");
            }
            MelonLogger.Msg("----------------------------------");
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

        // --- MODIFIED: Helper to find template Save Point materials WITH SHADER LOGGING ---
        private static Material[] FindTemplateSavePointMaterials()
        {
            MelonLogger.Msg($"--- Finding Template Save Point Materials ---");

            GameObject templateSavePointGO = GameObject.Find(TemplateSavePointGOName);
            if (templateSavePointGO == null) { MelonLogger.Error($"Could not find template Save Point GO: '{TemplateSavePointGOName}'"); return null; }

            Transform intermediateChild = templateSavePointGO.transform.Find(IntermediateGOName);
            if (intermediateChild == null) { MelonLogger.Error($"Could not find intermediate child '{IntermediateGOName}' under template '{templateSavePointGO.name}'!"); return null; }

            Transform lodChild = intermediateChild.Find(LodGOName);
            if (lodChild == null) { MelonLogger.Error($"Could not find LOD child '{LodGOName}' under intermediate '{intermediateChild.name}'!"); return null; }

            MeshRenderer templateRenderer = lodChild.GetComponent<MeshRenderer>();
            if (templateRenderer == null) { MelonLogger.Error($"No MeshRenderer on template LOD child '{lodChild.name}'!"); return null; }

            // Get the materials array (this is a copy)
            Material[] materials = templateRenderer.materials;
            if (materials == null || materials.Length == 0) { MelonLogger.Warning($"Template LOD child '{lodChild.name}' has no materials!"); return null; }

            MelonLogger.Msg($"Retrieved {materials.Length} materials from template '{lodChild.name}'.");

            // ***** ADDED SHADER NAME LOGGING *****
            MelonLogger.Msg("   Logging TEMPLATE material shaders:");
            for(int i=0; i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i].shader != null)
                {
                    // Log the shader name if both material and shader exist
                    MelonLogger.Msg($"     - Material {i} ('{materials[i].name}'): Shader = '{materials[i].shader.name}'");
                }
                else if (materials[i] != null)
                {
                    // Log if the material exists but the shader is null
                     MelonLogger.Msg($"     - Material {i} ('{materials[i].name}'): Shader = NULL");
                }
                else
                {
                    // Log if the material slot itself is null
                    MelonLogger.Msg($"     - Material {i}: NULL Material");
                }
            }
            MelonLogger.Msg("   Finished logging template shaders.");
            // ***** END ADDED LOGGING *****

            return materials; // Return the array of materials
        }

        // --- MODIFIED: Helper to apply materials to custom Save Point ---
        private static void ApplyMaterialsToCustomSavePoint(Transform prefabRoot, Material[] materialsToApply)
        {
            if (materialsToApply == null || materialsToApply.Length == 0)
            {
                MelonLogger.Warning("Skipping save point material application: No template materials.");
                return;
            }

            MelonLogger.Msg($"--- Applying Materials to Custom Save Point ---");

            Transform customSavePointTransform = FindDeepChild(prefabRoot, TargetSavePointGOName);
            if (customSavePointTransform == null) { MelonLogger.Error($"Could not find custom Save Point GO: '{TargetSavePointGOName}'"); return; }

            // Find the intermediate "Intercom" child under the custom save point
            Transform customIntermediateChild = customSavePointTransform.Find(IntermediateGOName);
             if (customIntermediateChild == null)
            {
                MelonLogger.Error($"Could not find intermediate child '{IntermediateGOName}' under custom Save Point '{customSavePointTransform.name}'!");
                return;
            }

            // Find the LOD child under the custom intermediate child
            Transform customLodChild = customIntermediateChild.Find(LodGOName);
            if (customLodChild == null)
            {
                MelonLogger.Error($"Could not find LOD child '{LodGOName}' under custom intermediate child '{customIntermediateChild.name}'!");
                return;
            }

            MeshRenderer customRenderer = customLodChild.GetComponent<MeshRenderer>();
            if (customRenderer == null) { MelonLogger.Error($"No MeshRenderer on custom LOD child '{customLodChild.name}'!"); return; }

            try
            {
                customRenderer.materials = materialsToApply;
                MelonLogger.Msg($"Applied {materialsToApply.Length} materials to '{customLodChild.name}'.");
            }
            catch (System.Exception e)
            {
                MelonLogger.Error($"Failed to apply materials to '{customLodChild.name}': {e.Message}");
            }
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

        // --- NEW: Combined Switch/Toggleable Logic ---
        private static void ConfigureSwitchesAndToggleables(Property manorProperty)
        {
             MelonLogger.Msg("--- Configuring Switches & Toggleables for Manor ---");
             // Search starting from the property's main transform to include everything
             Transform propertyTransform = manorProperty.transform;

             // Switches
             try
             {
                 var foundSwitches = propertyTransform.GetComponentsInChildren<ModularSwitch>(true)?.ToList();
                 if (foundSwitches != null) // Don't assume Length > 0, just assign if found
                 {
                     manorProperty.Switches = foundSwitches;
                     MelonLogger.Msg($"Assigned {manorProperty.Switches.Count} ModularSwitch components to Manor.");
                 } else { // Should not happen unless GetComponentsInChildren returns null
                      MelonLogger.Warning("GetComponentsInChildren<ModularSwitch> returned null unexpectedly.");
                 }
             }
             catch (System.Exception e) { MelonLogger.Error($"Error configuring Modular Switches: {e.Message}"); }

             // Toggleables
             try
             {
                  var foundToggleables = propertyTransform.GetComponentsInChildren<InteractableToggleable>(true).ToList();
                   if (foundToggleables != null)
                  {
                      manorProperty.Toggleables = foundToggleables;
                      MelonLogger.Msg($"Assigned {manorProperty.Toggleables.Count} InteractableToggleable components to Manor.");

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
                       MelonLogger.Msg($"Re-attached listeners for {manorProperty.Toggleables.Count} Toggleables.");
                  } else {
                       MelonLogger.Warning("GetComponentsInChildren<InteractableToggleable> returned null unexpectedly.");
                  }
             }
             catch (System.Exception e) { MelonLogger.Error($"Error configuring InteractableToggleables: {e.Message}"); }

             MelonLogger.Msg("--------------------------------------------------");
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
              MelonLogger.Msg($"Toggleable '{toggleable.name}' actioned. Marked Manor Property dirty.");
         }

        // --- NEW: Method to configure Employee Idle Points ---
        private static void ConfigureEmployeeIdlePoints(Transform prefabRoot, Property manorProperty)
        {
            MelonLogger.Msg("--- Configuring Employee Idle Points ---");

            // 1. Find the container for EXTRA points in the PREFAB
            // Assuming "Extra Employee Idle Points" is under "AtTheProperty" which is under the root
            Transform atTheProperty = prefabRoot.Find("AtTheProperty");
            Transform extraPointsContainer = null;
            if (atTheProperty != null) {
                extraPointsContainer = atTheProperty.Find(ExtraIdlePointsContainerName);
            } // Can also use FindDeepChild if hierarchy is complex/uncertain

            if (extraPointsContainer == null)
            {
                MelonLogger.Warning($"Could not find '{ExtraIdlePointsContainerName}' container within prefab. Skipping adding extra idle points.");
                return; // Nothing to add
            }
            MelonLogger.Msg($"Found '{ExtraIdlePointsContainerName}' container in prefab.");

            var combinedIdlePoints = manorProperty.EmployeeIdlePoints.ToList();

            // 3. Add the transforms of the CHILDREN of the extra points container
            for (int i = 0; i < extraPointsContainer.childCount; i++)
                combinedIdlePoints.Add(extraPointsContainer.GetChild(i));

            MelonLogger.Msg($"Added {extraPointsContainer.childCount} extra idle point transforms from '{ExtraIdlePointsContainerName}'.");


            // 4. Assign the combined list back to the Property's array
            manorProperty.EmployeeIdlePoints = combinedIdlePoints.ToReferenceArray();
            MelonLogger.Msg($"Set Manor EmployeeIdlePoints array. New total count: {manorProperty.EmployeeIdlePoints.Length}");
            MelonLogger.Msg("--------------------------------------");
        }

        // --- NEW METHOD for Realty Listing ---
        private static void ConfigureRealtyListing(GameObject prefabRoot)
        {
            MelonLogger.Msg("Attempting to configure realty listing...");
            Transform sourceListing = FindDeepChild(prefabRoot.transform, ListingPosterName);
            if (sourceListing == null)
            {
                MelonLogger.Warning($"Could not find realty listing object '{ListingPosterName}' in prefab.");
                return;
            }

            GameObject targetWhiteboard = GameObject.Find(WhiteboardPath);
            if (targetWhiteboard == null)
            {
                MelonLogger.Error($"Could not find target whiteboard object at path '{WhiteboardPath}'. Cannot reparent listing.");
                return;
            }

            MelonLogger.Msg($"Found source listing '{sourceListing.name}' and target whiteboard '{targetWhiteboard.name}'. Attempting reparent.");

            try
            {
                // Reparent the listing object to the whiteboard.
                sourceListing.SetParent(targetWhiteboard.transform, true);
                MelonLogger.Msg($"Successfully reparented '{sourceListing.name}' to '{targetWhiteboard.name}'.");
            }
            catch (System.Exception ex) // Qualified Exception
            {

                MelonLogger.Error($"Failed to reparent listing object: {ex.Message}");
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