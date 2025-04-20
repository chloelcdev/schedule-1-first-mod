using MelonLoader;
using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI; // Example if smuggling Sprites via Image
using ComponentRestoration.Data; // ADDED using for shared data structures

using Unity.AI.Navigation; // <-- ADDED
using UnityEngine.AI;

namespace ChloesManorMod
{
    // --- Configuration Structures --- 
    public class ComponentTypeMapping
    {
        public string JsonTypeName { get; set; } // Full type name from JSON
                                                 // public System.Type RuntimeType { get; set; } // REMOVED - Not needed if action handles type
                                                 // public Func<GameObject, Component> AddComponentAction { get; set; } // REMOVED

        // NEW Combined Action: Handles finding/adding AND applying properties for a specific type
        public Action<GameObject, ComponentData, bool> ApplyComponentAction { get; set; }
    }
    // --- END: Configuration Structures ---

    public static class ComponentRestorer
    {
        private static List<ComponentTypeMapping> _componentMappings = new List<ComponentTypeMapping>();
        private static bool _mappingsInitialized = false;

        // --- Updated Helper signature --- 
        private static void AddMapping(bool verboseLogging, string jsonTypeName,
                                       Action<GameObject, ComponentData, bool> applyAction) // Only takes the combined action
        {
            // We don't necessarily need the System.Type here anymore if the action handles everything
            // System.Type runtimeType = FindTypeInLoadedAssemblies(jsonTypeName);
            // if (runtimeType != null) {
            var mapping = new ComponentTypeMapping
            {
                JsonTypeName = jsonTypeName,
                // RuntimeType = runtimeType, // REMOVED
                ApplyComponentAction = applyAction // Assign the combined action
            };
            _componentMappings.Add(mapping);
            if (verboseLogging) MelonLogger.Msg($"    - Added mapping for {jsonTypeName} with combined Apply action.");
            // } else { ... Error Log ... }
        }
        // --- END Helper ---

        private static void InitializeMappings(bool verboseLogging)
        {
            if (_mappingsInitialized) return;
            if (verboseLogging) MelonLogger.Msg("[ComponentRestorer] Initializing component type mappings...");
            _componentMappings.Clear();

            // DecalProjector Mapping - Point to the new combined action function
            AddMapping(verboseLogging,
                "UnityEngine.Rendering.Universal.DecalProjector", // JSON Type Name
                ApplyDecalProjector // NEW combined action function
            );

            // --- Add mappings for other types here using AddMapping() --- 
            // NavMeshSurface Mapping
            AddMapping(verboseLogging,
                "Unity.AI.Navigation.NavMeshSurface", // JSON Type Name
                ApplyNavMeshSurface // NEW combined action function
            );

            /* Example: Rigidbody
            AddMapping(verboseLogging, 
                "UnityEngine.Rigidbody", 
                ApplyRigidbody // Assuming you create this combined function
            );
            */

            _mappingsInitialized = true;
            if (verboseLogging) MelonLogger.Msg("[ComponentRestorer] Component type mappings initialized.");
        }
        // --- END: Configuration Table ---

        public static void RestoreComponentsFromJSON(GameObject instantiatedRoot, string jsonContent, bool verboseLogging = false)
        {
            InitializeMappings(verboseLogging);
            // --- Initialize Mappings --- 
            if (string.IsNullOrEmpty(jsonContent))
            {
                MelonLogger.Error("[ComponentRestorer] JSON content is null or empty. Cannot restore components.");
                return;
            }

            // --- Log the raw JSON content before attempting deserialization ---
            if (verboseLogging) MelonLogger.Msg($"[ComponentRestorer] Attempting to deserialize JSON content using Newtonsoft:\n{jsonContent}");
            // --- 

            PrefabHierarchyData hierarchyData;
            try
            {
                // --- Switch back to Newtonsoft.Json, initially WITHOUT specific converters ---
                hierarchyData = JsonConvert.DeserializeObject<PrefabHierarchyData>(jsonContent);

                if (hierarchyData == null || hierarchyData.gameObjects == null)
                {
                    MelonLogger.Error("[ComponentRestorer] Failed to deserialize JSON using Newtonsoft.Json or data is invalid (returned null).");
                    return;
                }
            }
            catch (System.Exception ex)
            {
                // Catch potential exceptions from Newtonsoft
                MelonLogger.Error($"[ComponentRestorer] Error deserializing JSON using Newtonsoft.Json: {ex.ToString()}");
                return;
            }

            MelonLogger.Msg($"[ComponentRestorer] Successfully deserialized hierarchy data with {hierarchyData.gameObjects.Count} GameObject entries using Newtonsoft.Json.");

            // Build a lookup for instantiated objects by path
            Dictionary<string, GameObject> instantiatedObjects = new Dictionary<string, GameObject>();

            // --- Get expected root name from JSON data --- 
            string expectedJsonRootName = null;
            if (hierarchyData.gameObjects.Count > 0 && hierarchyData.gameObjects[0].path.Contains("/"))
            {
                expectedJsonRootName = hierarchyData.gameObjects[0].path.Split('/')[0];
            }
            else if (hierarchyData.gameObjects.Count > 0)
            {
                expectedJsonRootName = hierarchyData.gameObjects[0].path; // Path IS the root name
            }

            if (string.IsNullOrEmpty(expectedJsonRootName))
            {
                MelonLogger.Error("[ComponentRestorer] Could not determine expected root name from JSON data!");
                // Decide if we should proceed with potentially mismatched paths or return
                // return;
            }
            else if (verboseLogging)
            {
                MelonLogger.Msg($"[ComponentRestorer] Expected root name from JSON: {expectedJsonRootName}");
            }
            // --- 

            if (instantiatedRoot != null)
            {
                // Pass the expected root name to the lookup function
                BuildPathLookup(instantiatedRoot.transform, "", expectedJsonRootName, instantiatedObjects);
            }

            // --- REMOVED OLD DEBUG LOGGING of PathDebug --- 
            // foreach (var go_path in instantiatedObjects.Keys)
            // {
            //     if (go_path.ToLower().Contains("walltag"))
            //         MelonLogger.Msg($"[ComponentRestorer] PathDebug: '{go_path}'");
            // }

            int restoredCount = 0;

            if (verboseLogging) MelonLogger.Msg($"[ComponentRestorer] Built path lookup with {instantiatedObjects.Count} entries. Comparing against JSON...");

            // Iterate through the data from JSON
            foreach (GameObjectData goData in hierarchyData.gameObjects)
            {
                if (!instantiatedObjects.TryGetValue(goData.path, out GameObject targetGO))
                {
                    // Optional: Log only if we *expected* to find it based on components
                    bool expectedTarget = goData.components.Exists(c => _componentMappings.Exists(m => m.JsonTypeName == c.typeFullName));
                    if (expectedTarget && verboseLogging)
                        MelonLogger.Warning($"[ComponentRestorer] Could not find instantiated GameObject at path relevant for mapped components: '{goData.path}'. Skipping.");
                    continue;
                }

                // --- Process components for this GO --- 
                foreach (ComponentData compData in goData.components)
                {
                    ComponentTypeMapping mapping = _componentMappings.Find(m => m.JsonTypeName == compData.typeFullName);
                    if (mapping == null) continue;

                    // --- Directly Call the Combined Action --- 
                    if (mapping.ApplyComponentAction != null)
                    {
                        try
                        {
                            // The action now handles finding/adding/applying
                            mapping.ApplyComponentAction(targetGO, compData, verboseLogging);
                            // We don't get the component back here, so can't increment restoredCount easily
                        }
                        catch (Exception applyEx)
                        {
                            MelonLogger.Error($"[ComponentRestorer] ApplyComponentAction failed for '{mapping.JsonTypeName}': {applyEx.Message}");
                        }
                    }
                    else
                    {
                        if (verboseLogging) MelonLogger.Warning($"    - No ApplyComponentAction defined for mapping type '{mapping.JsonTypeName}'.");
                    }
                }
            }
            // Removed restoredCount from the final log as we don't track additions easily this way
            MelonLogger.Msg($"[ComponentRestorer] Finished component restoration using combined action mappings.");
        }

        // --- Smuggling Helper Functions ---
        private static Material RetrieveSmuggledMaterial(Component target, string runtimePropName, bool verbose)
        {
            // --- UPDATE Prefix ---
            string placeholderName = $"[PropertySmuggler] [{runtimePropName}]"; // Use the new prefix
            Transform smugglerTransform = target.transform.Find(placeholderName);
            Material retrievedMaterial = null;

            if (smugglerTransform != null)
            {
                MeshRenderer meshRenderer = smugglerTransform.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    retrievedMaterial = meshRenderer.sharedMaterial;
                    if (verbose) MelonLogger.Msg($"          -> Found placeholder '{placeholderName}', retrieved Material '{retrievedMaterial?.name ?? "null"}' from MeshRenderer.");
                }
                else if (verbose) MelonLogger.Warning($"          -> Found placeholder '{placeholderName}' but it missing MeshRenderer component.");

                UnityEngine.Object.Destroy(smugglerTransform.gameObject); // Clean up placeholder
            }
            else if (verbose) MelonLogger.Msg($"      - No placeholder found for smuggled material property '{runtimePropName}' (looked for '{placeholderName}').");

            return retrievedMaterial;
        }

        private static Sprite RetrieveSmuggledSprite(Component target, string runtimePropName, bool verbose) // Example for Sprite
        {
            // --- UPDATE Prefix ---
            string placeholderName = $"[PropertySmuggler] [{runtimePropName}]"; // Use the new prefix
            Transform smugglerTransform = target.transform.Find(placeholderName);
            Sprite retrievedSprite = null;

            if (smugglerTransform != null)
            {
                // Option 1: SpriteRenderer
                SpriteRenderer spriteRenderer = smugglerTransform.GetComponent<SpriteRenderer>();
                if (spriteRenderer != null)
                {
                    retrievedSprite = spriteRenderer.sprite;
                    if (verbose) MelonLogger.Msg($"          -> Found placeholder '{placeholderName}', retrieved Sprite '{retrievedSprite?.name ?? "null"}' from SpriteRenderer.");
                }
                else
                {
                    // Option 2: Image (UI)
                    Image image = smugglerTransform.GetComponent<Image>();
                    if (image != null)
                    {
                        retrievedSprite = image.sprite;
                        if (verbose) MelonLogger.Msg($"          -> Found placeholder '{placeholderName}', retrieved Sprite '{retrievedSprite?.name ?? "null"}' from Image.");
                    }
                    else
                    {
                        if (verbose) MelonLogger.Warning($"          -> Found placeholder '{placeholderName}' but it missing SpriteRenderer or Image component.");
                    }
                }

                UnityEngine.Object.Destroy(smugglerTransform.gameObject); // Clean up placeholder
            }
            else if (verbose) MelonLogger.Msg($"      - No placeholder found for smuggled sprite property '{runtimePropName}' (looked for '{placeholderName}').");

            return retrievedSprite;
        }

        // Recursive helper to build path lookup
        private static void BuildPathLookup(Transform current, string currentPath, string expectedRootName, Dictionary<string, GameObject> lookup)
        {
            // Safety check for the current transform itself
            if (current == null) return;

            string currentName = current.name;
            string path;
            // If currentPath is empty, this is the root node being processed.
            // Use the expectedJsonRootName for the path instead of the instantiated name.
            if (string.IsNullOrEmpty(currentPath))
            {
                path = expectedRootName ?? currentName; // Use expected name, fallback to actual name if expected is null
            }
            else
            {
                // For children, append the actual name
                path = currentPath + "/" + currentName;
            }

            lookup[path] = current.gameObject;

            // Recurse through children
            int childCount = current.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = current.GetChild(i);
                if (child != null) // Add null check for child just in case
                {
                    // Pass the *constructed* path (which might use expectedRootName or actual name) down
                    BuildPathLookup(child, path, expectedRootName, lookup);
                }
            }
        }

        // Helper copy-pasted from URPShaderFix - needed here too
        private static System.Type FindTypeInLoadedAssemblies(string typeFullName)
        {
            System.Type foundType = null;
            foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try { foundType = assembly.GetType(typeFullName); if (foundType != null) return foundType; }
                catch { /* Ignore */ }
            }
            return null;
        }

        // --- NEW Helper Function for Property Retrieval and Conversion ---
        private static T GetValue<T>(Dictionary<string, object> properties, string key, bool verbose, T defaultValue = default)
        {
            if (properties == null)
            {
                if (verbose) MelonLogger.Warning($"[GetValue] Properties dictionary is null. Cannot get key '{key}'. Returning default.");
                return defaultValue;
            }

            if (properties.TryGetValue(key, out object valueObj))
            {
                try
                {
                    // Handle common Newtonsoft numeric types
                    if (typeof(T) == typeof(float) && valueObj is double) return (T)(object)Convert.ToSingle(valueObj);
                    if (typeof(T) == typeof(double) && valueObj is double) return (T)(object)valueObj; // Already double
                    if (typeof(T) == typeof(int) && valueObj is long) return (T)(object)Convert.ToInt32(valueObj);
                    if (typeof(T) == typeof(uint) && valueObj is long) return (T)(object)Convert.ToUInt32(valueObj);
                    if (typeof(T) == typeof(long) && valueObj is long) return (T)(object)valueObj; // Already long

                    // Handle Vectors (might come as JObject)
                    if (typeof(T) == typeof(Vector2) && valueObj is JObject v2JObj)
                        return (T)(object)new Vector2(v2JObj["x"].Value<float>(), v2JObj["y"].Value<float>());
                    if (typeof(T) == typeof(Vector3) && valueObj is JObject v3JObj)
                        return (T)(object)new Vector3(v3JObj["x"].Value<float>(), v3JObj["y"].Value<float>(), v3JObj["z"].Value<float>());

                    // LayerMask handling (stored as int/long)
                    if (typeof(T) == typeof(LayerMask))
                    {
                        if (valueObj is long lmLong) return (T)(object)(LayerMask)Convert.ToInt32(lmLong);
                        if (valueObj is int lmInt) return (T)(object)(LayerMask)lmInt;
                    }

                    // Default: Use Convert.ChangeType for enums (stored as long/int) or other direct conversions
                    // Note: Enums stored as int/long by Extractor will be converted correctly here.
                    return (T)Convert.ChangeType(valueObj, typeof(T));
                }
                catch (Exception e)
                {
                    // Improved error logging
                    MelonLogger.Warning($"[GetValue] Failed converting property '{key}' to {typeof(T).Name}. Exception: {e.Message}. (Value: '{valueObj}', Type: {valueObj?.GetType().Name})");
                    return defaultValue;
                }
            }
            // Only log if verbose and key not found
            if (verbose) MelonLogger.Warning($"[GetValue] Property '{key}' not found in JSON data.");
            return defaultValue;
        }
        // --- END Helper Function ---

        // --- NEW Combined Apply function for DecalProjector --- 
        private static void ApplyDecalProjector(GameObject targetGO, ComponentData data, bool verbose)
        {
            if (targetGO == null) return;

            // Find or Add the component
            DecalProjector decalProjector = targetGO.GetComponent<DecalProjector>();
            if (decalProjector == null)
            {
                if (verbose) MelonLogger.Msg($"    - DecalProjector not found on '{targetGO.name}'. Adding...");
                decalProjector = targetGO.AddComponent<DecalProjector>();
                if (decalProjector == null)
                { // Check if AddComponent failed
                    MelonLogger.Error($"[ComponentRestorer] Failed to add DecalProjector component to '{targetGO.name}'.");
                    return;
                }
            }
            else
            {
                if (verbose) MelonLogger.Msg($"    - Found existing DecalProjector on '{targetGO.name}'.");
            }

            // Apply properties directly
            if (verbose) MelonLogger.Msg($"    - Applying DecalProjector specific properties...");

            // --- Check if Properties dictionary exists ---
            if (data.Properties == null)
            {
                if (verbose) MelonLogger.Warning($"    - No 'Properties' dictionary found in JSON data for DecalProjector on '{targetGO.name}'. Skipping property application.");
                return; // Can't apply properties if the dictionary is missing
            }

            // --- Apply properties from dictionary using GetValue helper ---
            try { decalProjector.drawDistance = GetValue<float>(data.Properties, "drawDistance", verbose); if (verbose) MelonLogger.Msg($"      - Set drawDistance = {decalProjector.drawDistance}"); }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Error applying drawDistance: {e.Message}"); }

            try { decalProjector.fadeScale = GetValue<float>(data.Properties, "fadeScale", verbose); if (verbose) MelonLogger.Msg($"      - Set fadeScale = {decalProjector.fadeScale}"); }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Error applying fadeScale: {e.Message}"); }

            try { decalProjector.startAngleFade = GetValue<float>(data.Properties, "startAngleFade", verbose); if (verbose) MelonLogger.Msg($"      - Set startAngleFade = {decalProjector.startAngleFade}"); }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Error applying startAngleFade: {e.Message}"); }

            try { decalProjector.endAngleFade = GetValue<float>(data.Properties, "endAngleFade", verbose); if (verbose) MelonLogger.Msg($"      - Set endAngleFade = {decalProjector.endAngleFade}"); }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Error applying endAngleFade: {e.Message}"); }

            try { decalProjector.uvScale = GetValue<Vector2>(data.Properties, "uvScale", verbose); if (verbose) MelonLogger.Msg($"      - Set uvScale = {decalProjector.uvScale}"); }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Error applying uvScale: {e.Message}"); }

            try { decalProjector.uvBias = GetValue<Vector2>(data.Properties, "uvBias", verbose); if (verbose) MelonLogger.Msg($"      - Set uvBias = {decalProjector.uvBias}"); }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Error applying uvBias: {e.Message}"); }

            try { decalProjector.renderingLayerMask = GetValue<uint>(data.Properties, "renderingLayerMask", verbose); if (verbose) MelonLogger.Msg($"      - Set renderingLayerMask = {decalProjector.renderingLayerMask}"); }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Error applying renderingLayerMask: {e.Message}"); }

            try { decalProjector.pivot = GetValue<Vector3>(data.Properties, "pivot", verbose); if (verbose) MelonLogger.Msg($"      - Set pivot = {decalProjector.pivot}"); }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Error applying pivot: {e.Message}"); }

            try { decalProjector.size = GetValue<Vector3>(data.Properties, "size", verbose); if (verbose) MelonLogger.Msg($"      - Set size = {decalProjector.size}"); }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Error applying size: {e.Message}"); }

            try { decalProjector.fadeFactor = GetValue<float>(data.Properties, "fadeFactor", verbose); if (verbose) MelonLogger.Msg($"      - Set fadeFactor = {decalProjector.fadeFactor}"); }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Error applying fadeFactor: {e.Message}"); }

            // Enum assignment
            try
            {
                int scaleModeInt = GetValue<int>(data.Properties, "scaleMode", verbose);
                decalProjector.scaleMode = (DecalScaleMode)scaleModeInt;
                if (verbose) MelonLogger.Msg($"      - Set scaleMode = {(DecalScaleMode)scaleModeInt} (from int {scaleModeInt})");
            }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting scaleMode: {e.Message}"); } // Removed value logging as it's complex

            // Smuggled material assignment
            try
            {
                // Pass the specific component instance to the smuggler helper
                Material mat = RetrieveSmuggledMaterial(decalProjector, "material", verbose);
                if (mat != null)
                {
                    decalProjector.material = mat;
                    if (verbose) MelonLogger.Msg($"      - Set material = {mat.name} (from smuggler)");
                }
            }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting material from smuggler: {e.Message}"); }

            if (verbose) MelonLogger.Msg($"    - Finished applying DecalProjector specific properties.");
        }

        // --- NEW Combined Apply function for NavMeshSurface ---
        private static void ApplyNavMeshSurface(GameObject targetGO, ComponentData data, bool verbose)
        {
            if (targetGO == null) return;

            // Find or Add the component
            NavMeshSurface navMeshSurface = targetGO.GetComponent<NavMeshSurface>();
            bool added = false;
            if (navMeshSurface == null)
            {
                if (verbose) MelonLogger.Msg($"    - NavMeshSurface not found on '{targetGO.name}'. Adding...");
                navMeshSurface = targetGO.AddComponent<NavMeshSurface>();
                if (navMeshSurface == null)
                { // Check if AddComponent failed
                    MelonLogger.Error($"[ComponentRestorer] Failed to add NavMeshSurface component to '{targetGO.name}'.");
                    return;
                }
                added = true;
            }
            else
            {
                if (verbose) MelonLogger.Msg($"    - Found existing NavMeshSurface on '{targetGO.name}'.");
            }

            // Apply properties directly from ComponentData fields
            if (verbose) MelonLogger.Msg($"    - Applying NavMeshSurface specific properties from ComponentData fields...");

            // --- Check if Properties dictionary exists ---
            if (data.Properties == null)
            {
                if (verbose) MelonLogger.Warning($"    - No 'Properties' dictionary found in JSON data for NavMeshSurface on '{targetGO.name}'. Skipping property application.");
                return; // Can't apply properties if the dictionary is missing
            }

            // --- Apply properties from dictionary using GetValue helper ---
            try
            {
                int agentTypeId = GetValue<int>(data.Properties, "agentTypeID", verbose);
                navMeshSurface.agentTypeID = agentTypeId;
                if (verbose) MelonLogger.Msg($"      - Set agentTypeID = {agentTypeId}");
            }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting agentTypeID: {e.Message}"); }

            try
            {
                int collectObjectsInt = GetValue<int>(data.Properties, "collectObjects", verbose);
                navMeshSurface.collectObjects = (CollectObjects)collectObjectsInt;
                if (verbose) MelonLogger.Msg($"      - Set collectObjects = {(CollectObjects)collectObjectsInt} (from int {collectObjectsInt})");
            }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting collectObjects: {e.Message}"); }

            try
            {
                LayerMask layerMaskValue = GetValue<LayerMask>(data.Properties, "layerMask", verbose);
                navMeshSurface.layerMask = layerMaskValue;
                if (verbose) MelonLogger.Msg($"      - Set layerMask = {layerMaskValue.value} (int value)");
            }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting layerMask: {e.Message}"); }

            try
            {
                int useGeometryInt = GetValue<int>(data.Properties, "useGeometry", verbose);
                navMeshSurface.useGeometry = (NavMeshCollectGeometry)useGeometryInt;
                if (verbose) MelonLogger.Msg($"      - Set useGeometry = {(NavMeshCollectGeometry)useGeometryInt} (from int {useGeometryInt})");
            }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting useGeometry: {e.Message}"); }

            try
            {
                int defaultAreaInt = GetValue<int>(data.Properties, "defaultArea", verbose);
                navMeshSurface.defaultArea = defaultAreaInt;
                if (verbose) MelonLogger.Msg($"      - Set defaultArea = {defaultAreaInt}");
            }
            catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting defaultArea: {e.Message}"); }


            if (verbose) MelonLogger.Msg($"    - Finished applying NavMeshSurface specific properties.");

            // IMPORTANT: We only restore the settings. The actual baking needs to be triggered elsewhere.
            MelonLogger.Warning($"[ComponentRestorer] Restored NavMeshSurface settings on '{targetGO.name}'. BuildNavMesh() was NOT called. Runtime logic must trigger baking if needed.");
        }

        // ... (Smuggling Helpers, BuildPathLookup, FindTypeInLoadedAssemblies) ...
    }
}