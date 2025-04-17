using MelonLoader;
using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;
using Newtonsoft.Json;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI; // Example if smuggling Sprites via Image
using ComponentRestoration.Data; // ADDED using for shared data structures

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
            var mapping = new ComponentTypeMapping {
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
        if(verboseLogging) MelonLogger.Msg($"[ComponentRestorer] Attempting to deserialize JSON content using Newtonsoft:\n{jsonContent}");
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
                    if(verboseLogging) MelonLogger.Warning($"    - No ApplyComponentAction defined for mapping type '{mapping.JsonTypeName}'.");
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
                 if(image != null) {
                     retrievedSprite = image.sprite;
                     if (verbose) MelonLogger.Msg($"          -> Found placeholder '{placeholderName}', retrieved Sprite '{retrievedSprite?.name ?? "null"}' from Image.");
                 } else {
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
            if (decalProjector == null) { // Check if AddComponent failed
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

        // Direct assignments (Add null checks for safety on data fields? Assumed valid from JSON for now)
        try { decalProjector.drawDistance = data.drawDistance; if(verbose) MelonLogger.Msg($"      - Set drawDistance = {data.drawDistance}"); } 
        catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting drawDistance: {e.Message}"); }
        
        try { decalProjector.fadeScale = data.fadeScale; if(verbose) MelonLogger.Msg($"      - Set fadeScale = {data.fadeScale}"); } 
        catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting fadeScale: {e.Message}"); }
        
        try { decalProjector.startAngleFade = data.startAngleFade; if(verbose) MelonLogger.Msg($"      - Set startAngleFade = {data.startAngleFade}"); } 
        catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting startAngleFade: {e.Message}"); }
        
        try { decalProjector.endAngleFade = data.endAngleFade; if(verbose) MelonLogger.Msg($"      - Set endAngleFade = {data.endAngleFade}"); } 
        catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting endAngleFade: {e.Message}"); }
        
        try { decalProjector.uvScale = data.uvScale; if(verbose) MelonLogger.Msg($"      - Set uvScale = {data.uvScale}"); } 
        catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting uvScale: {e.Message}"); }
        
        try { decalProjector.uvBias = data.uvBias; if(verbose) MelonLogger.Msg($"      - Set uvBias = {data.uvBias}"); } 
        catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting uvBias: {e.Message}"); }
        
        try { decalProjector.renderingLayerMask = data.renderingLayerMask; if(verbose) MelonLogger.Msg($"      - Set renderingLayerMask = {data.renderingLayerMask}"); } 
        catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting renderingLayerMask: {e.Message}"); }
        
        try { decalProjector.pivot = data.pivot; if(verbose) MelonLogger.Msg($"      - Set pivot = {data.pivot}"); } 
        catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting pivot: {e.Message}"); }
        
        try { decalProjector.size = data.size; if(verbose) MelonLogger.Msg($"      - Set size = {data.size}"); } 
        catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting size: {e.Message}"); }
        
        try { decalProjector.fadeFactor = data.fadeFactor; if(verbose) MelonLogger.Msg($"      - Set fadeFactor = {data.fadeFactor}"); } 
        catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting fadeFactor: {e.Message}"); }

        // Enum assignment
        try 
        {
            decalProjector.scaleMode = (DecalScaleMode)data.scaleMode; 
            if(verbose) MelonLogger.Msg($"      - Set scaleMode = {(DecalScaleMode)data.scaleMode} (from int {data.scaleMode})");
        } 
        catch (Exception e) { MelonLogger.Warning($"[ComponentRestorer] Failed setting scaleMode: {e.Message}"); }

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

    // ... (Smuggling Helpers, BuildPathLookup, FindTypeInLoadedAssemblies) ...
} 