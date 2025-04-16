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
        // --- Names for Save Point Stealing ---
        private const string TemplateSavePointGOName = "Intercom Save Point";
        private const string TargetSavePointGOName = "Intercom Save Point";
        private const string IntermediateGOName = "Intercom";
        private const string LodGOName = "Intercom_LOD0";

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

            MelonLogger.Msg($"--- ManorSetupHelper configuration FINISHED ---");

            MelonLogger.Msg($"--- Aligning Custom NavMesh Surface ---");
            // Find the GameObject holding the NavMeshSurface component within your prefab
            // (We could look this up by type, but what if we have more than one later on?)
            Transform navSurfaceHost = FindDeepChild(spawnedInstanceRoot.transform, "Extra Navigation"); // Or "AtTheProperty", whichever holds the NavMeshSurface

            if (navSurfaceHost != null)
            {
                // Check if it actually has the surface component
                var surface = navSurfaceHost.GetComponent<UnityEngine.AI.NavMeshSurface>(); // Make sure you have the right using for NavMeshSurface
                if (surface != null && surface.navMeshData != null)
                {
                    MelonLogger.Msg($"Found NavMeshSurface host: '{navSurfaceHost.name}' with NavMeshData '{surface.navMeshData.name}'.");

                    // --- THIS IS KEY ---
                    // Set the *world* position and rotation of the NavMeshSurface host
                    // to match the Manor property itself. This ensures the loaded NavMesh data
                    // aligns correctly with the Manor geometry in the world.
                    navSurfaceHost.position = manorProperty.transform.position;
                    navSurfaceHost.rotation = manorProperty.transform.rotation;
                    // --- END KEY PART ---

                    MelonLogger.Msg($"Aligned NavMeshSurface host to Manor property's world transform.");

                    // Optional: Force the NavMeshSurface to update/load if needed, though often automatic
                    // surface.BuildNavMesh(); // Might not be needed if data is just loaded

                }
                else
                {
                     MelonLogger.Error($"GameObject '{navSurfaceHost.name}' found, but missing NavMeshSurface component or NavMeshData assignment!");
                }
            }
            else
            {
                MelonLogger.Error("Could not find the 'Extra Navigation' (or NavMeshSurface host) GameObject within the spawned prefab!");
            }
             MelonLogger.Msg($"---------------------------------------");

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

        // --- MODIFIED: Helper to find template Save Point materials ---
        private static Material[] FindTemplateSavePointMaterials()
        {
            MelonLogger.Msg($"--- Finding Template Save Point Materials ---");

            GameObject templateSavePointGO = GameObject.Find(TemplateSavePointGOName);
            if (templateSavePointGO == null) { MelonLogger.Error($"Could not find template Save Point GO: '{TemplateSavePointGOName}'"); return null; }

            // Find the intermediate "Intercom" child
            Transform intermediateChild = templateSavePointGO.transform.Find(IntermediateGOName);
            if (intermediateChild == null)
            {
                MelonLogger.Error($"Could not find intermediate child '{IntermediateGOName}' under template Save Point '{templateSavePointGO.name}'!");
                return null;
            }

            // Find the LOD child under the intermediate child
            Transform lodChild = intermediateChild.Find(LodGOName);
            if (lodChild == null)
            {
                MelonLogger.Error($"Could not find LOD child '{LodGOName}' under intermediate child '{intermediateChild.name}'!");
                return null;
            }

            MeshRenderer templateRenderer = lodChild.GetComponent<MeshRenderer>();
            if (templateRenderer == null) { MelonLogger.Error($"No MeshRenderer on template LOD child '{lodChild.name}'!"); return null; }

            Material[] materials = templateRenderer.materials;
            if (materials == null || materials.Length == 0) { MelonLogger.Warning($"Template LOD child '{lodChild.name}' has no materials!"); return null; }

            MelonLogger.Msg($"Retrieved {materials.Length} materials from template '{lodChild.name}'.");
            return materials;
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

    } // End ManorSetupHelper
} // End namespace