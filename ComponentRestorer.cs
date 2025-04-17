using MelonLoader;
using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using Newtonsoft.Json;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI; // Example if smuggling Sprites via Image

// --- NEW: Configuration Structures ---
public class PropertyMap
{
    public string JsonPropertyName { get; set; } // Name of field in ComponentData
    public string RuntimePropertyName { get; set; } // Name of property on the actual runtime Component
}

public class ComponentTypeMapping
{
    public string JsonTypeName { get; set; } // Full type name from JSON
    public System.Type RuntimeType { get; set; } // Actual System.Type at runtime
    public List<PropertyMap> PropertyMappings { get; set; } = new List<PropertyMap>();
    public Func<GameObject, Component> AddComponentAction { get; set; } // Action to add the component
}
// --- END: Configuration Structures ---

// Define simple structures to hold the JSON data
// NOTE: JsonUtility might require fields to be public or marked with [SerializeField]
// Consider using Newtonsoft.Json (via NuGet in your mod project) for more flexibility if JsonUtility is too limited.
[Serializable]
public class ComponentData
{
    public string typeFullName; // e.g., "UnityEngine.Rendering.Universal.DecalProjector"

    // --- Standard Properties ONLY --- (No materialPath, no SmuggleInfo)
    public float drawDistance;
    public float fadeScale;
    public float startAngleFade;
    public float endAngleFade;
    public Vector2 uvScale;
    public Vector2 uvBias;
    public uint renderingLayerMask;
    public int scaleMode; // Store enum as int
    public Vector3 pivot;
    public Vector3 size;
    public float fadeFactor;
    // Add other properties as needed
}

[Serializable]
public class GameObjectData
{
    public string name;
    public string path; // Path relative to prefab root
    public List<ComponentData> components = new List<ComponentData>();
}

[Serializable]
public class PrefabHierarchyData
{
    public List<GameObjectData> gameObjects = new List<GameObjectData>();
}

public static class ComponentRestorer
{
    // --- NEW: Configuration Table ---
    private static List<ComponentTypeMapping> _componentMappings = new List<ComponentTypeMapping>();
    private static bool _mappingsInitialized = false;

    // --- Updated Helper to add mappings in a config-like way ---
    private static void AddMapping(bool verboseLogging, string jsonTypeName,
                                   List<PropertyMap> propertyMaps,
                                   Func<GameObject, Component> addAction)
    {
        System.Type runtimeType = FindTypeInLoadedAssemblies(jsonTypeName);
        if (runtimeType != null)
        {
            if (verboseLogging) MelonLogger.Msg($"    - Found runtime type for {jsonTypeName}: {runtimeType.AssemblyQualifiedName}");
            var mapping = new ComponentTypeMapping {
                JsonTypeName = jsonTypeName,
                RuntimeType = runtimeType,
                PropertyMappings = propertyMaps, // Assign the property maps
                AddComponentAction = addAction // Assign the add action
            };
            _componentMappings.Add(mapping);
            if (verboseLogging) MelonLogger.Msg($"    - Added mapping for {jsonTypeName} with {mapping.PropertyMappings.Count} property maps.");
        }
        else
        {
            MelonLogger.Error($"[ComponentRestorer] Could not find runtime System.Type for '{jsonTypeName}'. Mapping initialization failed for this type.");
        }
    }
    // --- END Helper ---

    private static void InitializeMappings(bool verboseLogging)
    {
        if (_mappingsInitialized) return;
        if (verboseLogging) MelonLogger.Msg("[ComponentRestorer] Initializing component type mappings...");
        _componentMappings.Clear();

        // DecalProjector Mapping (Simplified Property Map)
        AddMapping(verboseLogging, "UnityEngine.Rendering.Universal.DecalProjector",
        /* Property Maps: */ new List<PropertyMap>
        {
            // Map JSON fields -> Runtime Property Names
            // **NOTE: No entry for "material" here - it will be handled by type dispatch**
            new PropertyMap { JsonPropertyName = "drawDistance", RuntimePropertyName = "drawDistance" },
            new PropertyMap { JsonPropertyName = "fadeScale", RuntimePropertyName = "fadeScale" },
            new PropertyMap { JsonPropertyName = "startAngleFade", RuntimePropertyName = "startAngleFade" },
            new PropertyMap { JsonPropertyName = "endAngleFade", RuntimePropertyName = "endAngleFade" },
            new PropertyMap { JsonPropertyName = "uvScale", RuntimePropertyName = "uvScale" },
            new PropertyMap { JsonPropertyName = "uvBias", RuntimePropertyName = "uvBias" },
            new PropertyMap { JsonPropertyName = "renderingLayerMask", RuntimePropertyName = "renderingLayerMask" },
            new PropertyMap { JsonPropertyName = "pivot", RuntimePropertyName = "pivot" },
            new PropertyMap { JsonPropertyName = "size", RuntimePropertyName = "size" },
            new PropertyMap { JsonPropertyName = "fadeFactor", RuntimePropertyName = "fadeFactor" },
            new PropertyMap { JsonPropertyName = "scaleMode", RuntimePropertyName = "scaleMode" }
        },
        /* Add Action: */ (go) => go.AddComponent<DecalProjector>()
        );

        // --- Add mappings for other types here using AddMapping() ---
        /* Example: Rigidbody
        AddMapping(verboseLogging, "UnityEngine.Rigidbody",
        new List<PropertyMap> {
             new PropertyMap { JsonPropertyName = "mass", RuntimePropertyName = "mass" },
             new PropertyMap { JsonPropertyName = "useGravity", RuntimePropertyName = "useGravity" },
             // ... other Rigidbody properties ...
        },
        (go) => go.AddComponent<Rigidbody>()
        );
        */

        _mappingsInitialized = true;
        if (verboseLogging) MelonLogger.Msg("[ComponentRestorer] Component type mappings initialized.");
    }
    // --- END: Configuration Table ---

    public static void RestoreComponentsFromJSON(GameObject instantiatedRoot, string jsonContent, bool verboseLogging = false)
    {
        // --- Initialize Mappings --- 
        InitializeMappings(verboseLogging); // Ensure mappings are ready
        // --- 

        if (string.IsNullOrEmpty(jsonContent))
        {
            MelonLogger.Error("[ComponentRestorer] JSON content is null or empty. Cannot restore components.");
            return;
        }

        PrefabHierarchyData hierarchyData;
        try
        {
            // Use Newtonsoft.Json for deserialization
            hierarchyData = JsonConvert.DeserializeObject<PrefabHierarchyData>(jsonContent);
            if (hierarchyData == null || hierarchyData.gameObjects == null)
            {
                MelonLogger.Error("[ComponentRestorer] Failed to deserialize JSON using Newtonsoft.Json or data is invalid.");
                return;
            }
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"[ComponentRestorer] Error deserializing JSON using Newtonsoft.Json: {ex.Message}");
            return;
        }

        MelonLogger.Msg($"[ComponentRestorer] Successfully deserialized hierarchy data with {hierarchyData.gameObjects.Count} GameObject entries.");

        // Build a lookup for instantiated objects by path, starting from children
        Dictionary<string, GameObject> instantiatedObjects = new Dictionary<string, GameObject>();
        // Start lookup from CHILDREN using an indexed loop for Il2Cpp safety
        int rootChildCount = instantiatedRoot.transform.childCount;
        for (int i = 0; i < rootChildCount; i++)
        {
            Transform child = instantiatedRoot.transform.GetChild(i);
            if (child != null) 
            {
                BuildPathLookup(child, "", instantiatedObjects);
            }
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
            // Find the target GameObject using the path from JSON
            if (!instantiatedObjects.TryGetValue(goData.path, out GameObject targetGO))
            {
                 // Optional: Log only if we *expected* to find it based on components
                 bool expectedTarget = goData.components.Exists(c => _componentMappings.Exists(m => m.JsonTypeName == c.typeFullName));
                 if (expectedTarget && verboseLogging) 
                    MelonLogger.Warning($"[ComponentRestorer] Could not find instantiated GameObject at path relevant for mapped components: '{goData.path}'. Skipping.");
                continue;
            }

            // --- Process components for this GO using the new mapping logic --- 
            foreach (ComponentData compData in goData.components)
            {
                // Find the mapping for this component type
                ComponentTypeMapping mapping = _componentMappings.Find(m => m.JsonTypeName == compData.typeFullName);

                // If no mapping exists for this type, skip it
                if (mapping == null)
                {
                    // Optional: Log unmapped types if verbose
                    // if (verboseLogging) MelonLogger.Msg($"[ComponentRestorer] No mapping found for JSON type '{compData.typeFullName}' on '{targetGO.name}'. Skipping.");
                    continue;
                }

                // --- Stricter Check: Iterate through existing components and compare exact Type ---
                bool exactRuntimeTypeExists = false;
                Component[] existingComponents = targetGO.GetComponents<Component>(); // Get all components
                foreach(var existingComp in existingComponents)
                {
                    if (existingComp != null && existingComp.GetType().AssemblyQualifiedName == mapping.RuntimeType.AssemblyQualifiedName)
                    {
                        exactRuntimeTypeExists = true;
                        if (verboseLogging) MelonLogger.Msg($"    - Found existing component with exact runtime type '{mapping.RuntimeType.AssemblyQualifiedName}' on '{targetGO.name}'. Skipping add.");
                        break; // Found the exact type we want, no need to add
                    }
                    // Optional: Log if we find one with the same name but different assembly
                    else if (verboseLogging && existingComp != null && existingComp.GetType().FullName == mapping.JsonTypeName)
                    {
                         MelonLogger.Msg($"    - Found existing component '{existingComp.GetType().AssemblyQualifiedName}' which matches JsonTypeName '{mapping.JsonTypeName}' but NOT the target RuntimeType AQN. Will proceed with adding correct type.");
                    }
                }
                // --- End Stricter Check ---

                // Proceed only if the *exact* runtime type wasn't found
                if (exactRuntimeTypeExists)
                {
                    continue; // Skip to the next component in the JSON data
                }

                // --- Component is MISSING (the exact runtime type) - Try to add it using the mapping --- 
                if (verboseLogging) MelonLogger.Msg($"[ComponentRestorer] Mapped component type '{mapping.RuntimeType.Name}' missing on '{targetGO.name}'. Attempting AddComponent via mapped action...");

                try
                {
                    // --- Use the stored AddComponentAction --- 
                    if (mapping.AddComponentAction == null)
                    {
                        MelonLogger.Error($"[ComponentRestorer] Mapping for '{mapping.JsonTypeName}' is missing the AddComponentAction delegate on '{targetGO.name}'. Cannot add component.");
                        continue;
                    }

                    Component addedComponent = mapping.AddComponentAction(targetGO);
                    // --- 

                    if (addedComponent != null)
                    {
                        string addedTypeName = addedComponent.GetType().FullName;
                        if (verboseLogging) MelonLogger.Msg($"[ComponentRestorer] Successfully added component '{addedTypeName}' (using mapped type '{mapping.RuntimeType.Name}') to '{targetGO.name}'. Applying properties...");
                        
                        // Apply properties using the mapping
                        ApplyPropertiesFromMapping(addedComponent, compData, mapping, verboseLogging);
                        restoredCount++;
                    }
                    else
                    {
                        // AddComponent returning null is also a failure scenario
                         MelonLogger.Error($"[ComponentRestorer] AddComponent(Il2CppType.From('{mapping.RuntimeType.Name}')) returned NULL for '{targetGO.name}'. Restoration failed for this component.");
                         // Consider falling back to template instantiation here if needed for specific types
                         // if (mapping.JsonTypeName == "UnityEngine.Rendering.Universal.DecalProjector" && _foundDecalTemplateComponent != null) { /* try template logic */ }
                    }
                }
                catch (System.Exception ex)
                {
                     // Catch errors during AddComponent itself
                     MelonLogger.Error($"[ComponentRestorer] AddComponent failed for type '{mapping.RuntimeType.Name}' on '{targetGO.name}': {ex.Message}");
                     // Consider fallback here too
                }
            }
        }
        MelonLogger.Msg($"[ComponentRestorer] Finished component restoration using mappings. Added {restoredCount} missing components.");
    }

    // --- Updated ApplyPropertiesFromMapping using Type Dispatch ---
    private static void ApplyPropertiesFromMapping(Component target, ComponentData data, ComponentTypeMapping mapping, bool verbose)
    {
        if (verbose) MelonLogger.Msg($"    - Applying properties using mapping for {mapping.JsonTypeName}...");
        System.Type targetRuntimeType = mapping.RuntimeType;
        System.Type jsonDataType = typeof(ComponentData);

        // First, handle SMUGGLED properties by checking the TARGET component's properties
        PropertyInfo[] allRuntimeProps = targetRuntimeType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (PropertyInfo runtimeProp in allRuntimeProps)
        {
            if (!runtimeProp.CanWrite) continue;

            // --- Dispatch based on Property Type for Smuggled Assets ---
            try
            {
                if (runtimeProp.PropertyType == typeof(Material))
                {
                    Material mat = RetrieveSmuggledMaterial(target, runtimeProp.Name, verbose);
                    if (mat != null)
                    {
                         runtimeProp.SetValue(target, mat);
                         if (verbose) MelonLogger.Msg($"      - Set Smuggled Material '{runtimeProp.Name}' successfully.");
                    }
                    // else: If RetrieveSmuggledMaterial returned null, means placeholder wasn't found or was invalid.
                    //       Property remains unset (default/null).
                }
                else if (runtimeProp.PropertyType == typeof(Sprite)) // Example for Sprites
                {
                    Sprite sprite = RetrieveSmuggledSprite(target, runtimeProp.Name, verbose);
                     if (sprite != null)
                     {
                          runtimeProp.SetValue(target, sprite);
                          if (verbose) MelonLogger.Msg($"      - Set Smuggled Sprite '{runtimeProp.Name}' successfully.");
                     }
                }
                // Add other 'else if' blocks here for other smuggled types (Texture2D, AudioClip, etc.)

            }
            catch (System.Exception ex)
            {
                 MelonLogger.Warning($"[ComponentRestorer] Error setting smuggled property '{runtimeProp.Name}': {ex.Message}");
            }
        }

        // Second, handle properties explicitly listed in the mapping (JSON values)
        foreach (PropertyMap propMap in mapping.PropertyMappings)
        {
            try
            {
                PropertyInfo runtimeProp = targetRuntimeType.GetProperty(propMap.RuntimePropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (runtimeProp == null || !runtimeProp.CanWrite)
                {
                    // Warning logged in previous loop if null, only check CanWrite here if needed
                    if (runtimeProp != null && !runtimeProp.CanWrite && verbose) MelonLogger.Msg($"      - Skip JSON property '{propMap.RuntimePropertyName}': Target not writable (Might be handled by smuggling).");
                    continue;
                }

                // Skip properties handled by smuggling (check again to be safe)
                if (runtimeProp.PropertyType == typeof(Material) || runtimeProp.PropertyType == typeof(Sprite)) continue;

                // Get the source field from JSON data
                FieldInfo jsonField = jsonDataType.GetField(propMap.JsonPropertyName, BindingFlags.Instance | BindingFlags.Public);
                if (jsonField == null)
                {
                    if (verbose) MelonLogger.Warning($"      - Skip '{propMap.JsonPropertyName}': Source field not found in ComponentData class definition for property '{propMap.RuntimePropertyName}'.");
                    continue;
                }
                object sourceValue = jsonField.GetValue(data);
                if (sourceValue == null) 
                {
                    if (verbose) MelonLogger.Msg($"      - Skip '{propMap.JsonPropertyName}': Source value from JSON is null.");
                    continue;
                }

                // Handle Enums
                if (runtimeProp.PropertyType.IsEnum)
                {
                    try
                    {
                        object enumValue = System.Enum.ToObject(runtimeProp.PropertyType, sourceValue);
                        runtimeProp.SetValue(target, enumValue);
                        if (verbose) MelonLogger.Msg($"      - Set Enum '{propMap.RuntimePropertyName}' to {enumValue} (from JSON value {sourceValue})");
                    }
                    catch (System.Exception ex) { MelonLogger.Warning($"[ComponentRestorer] Failed to set Enum property '{propMap.RuntimePropertyName}': {ex.Message}"); }
                }
                // Handle standard types
                else
                {
                    try
                    {
                        runtimeProp.SetValue(target, sourceValue);
                        if (verbose) MelonLogger.Msg($"      - Set '{propMap.RuntimePropertyName}' from JSON to {sourceValue?.ToString() ?? "null"}.");
                    }
                    catch (System.Exception ex) { MelonLogger.Warning($"[ComponentRestorer] Failed to set property '{propMap.RuntimePropertyName}' (value: {sourceValue?.ToString() ?? "null"}) from JSON: {ex.Message}"); }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[ComponentRestorer] Error processing property mapping for Json:'{propMap.JsonPropertyName}' -> Runtime:'{propMap.RuntimePropertyName}': {ex.Message}");
            }
        }
        if (verbose) MelonLogger.Msg($"    - Finished applying properties for {mapping.JsonTypeName}.");
    }

    // --- NEW Smuggling Helper Functions ---
    private static Material RetrieveSmuggledMaterial(Component target, string runtimePropName, bool verbose)
    {
        string placeholderName = $"[ComponentRestoration] [{runtimePropName}]";
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
        string placeholderName = $"[ComponentRestoration] [{runtimePropName}]";
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
    private static void BuildPathLookup(Transform current, string currentPath, Dictionary<string, GameObject> lookup)
    {
        // Safety check for the current transform itself
        if (current == null) return;

        string path = string.IsNullOrEmpty(currentPath) ? current.name : currentPath + "/" + current.name;
        lookup[path] = current.gameObject;

        // Iterate using index instead of foreach for Il2Cpp safety
        int childCount = current.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform child = current.GetChild(i);
            if (child != null) // Add null check for child just in case
            {
                BuildPathLookup(child, path, lookup);
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
} 