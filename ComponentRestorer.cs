using MelonLoader;
using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;

// Define simple structures to hold the JSON data
// NOTE: JsonUtility might require fields to be public or marked with [SerializeField]
// Consider using Newtonsoft.Json (via NuGet in your mod project) for more flexibility if JsonUtility is too limited.
[Serializable]
public class ComponentData
{
    public string typeFullName; // e.g., "UnityEngine.Rendering.Universal.DecalProjector"
    // Store properties as basic types or strings where possible
    public string materialPath; // Store material by Resources path or bundle path if applicable
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
    // Add other properties as needed, matching the Editor script output
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
    private static Dictionary<string, Il2CppSystem.Type> _targetTypeCache = new Dictionary<string, Il2CppSystem.Type>();

    public static void RestoreComponentsFromJSON(GameObject instantiatedRoot, string jsonContent, bool verboseLogging = false)
    {
        if (string.IsNullOrEmpty(jsonContent))
        {
            MelonLogger.Error("[ComponentRestorer] JSON content is null or empty. Cannot restore components.");
            return;
        }

        PrefabHierarchyData hierarchyData;
        try
        {
            // Use Unity's JsonUtility - requires matching public fields or [SerializeField]
            hierarchyData = UnityEngine.JsonUtility.FromJson<PrefabHierarchyData>(jsonContent);
            if (hierarchyData == null || hierarchyData.gameObjects == null)
            {
                MelonLogger.Error("[ComponentRestorer] Failed to deserialize JSON or data is invalid.");
                return;
            }
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"[ComponentRestorer] Error deserializing JSON: {ex.Message}");
            return;
        }

        MelonLogger.Msg($"[ComponentRestorer] Successfully deserialized hierarchy data with {hierarchyData.gameObjects.Count} GameObject entries.");

        // Build a lookup for instantiated objects by path
        Dictionary<string, GameObject> instantiatedObjects = new Dictionary<string, GameObject>();
        BuildPathLookup(instantiatedRoot.transform, "", instantiatedObjects);

        int restoredCount = 0;

        // Iterate through the data from JSON
        foreach (GameObjectData goData in hierarchyData.gameObjects)
        {
            if (!instantiatedObjects.TryGetValue(goData.path, out GameObject targetGO))
            {
                if (verboseLogging) MelonLogger.Warning($"[ComponentRestorer] Could not find instantiated GameObject at path: '{goData.path}'. Skipping components.");
                continue;
            }

            foreach (ComponentData compData in goData.components)
            {
                // Check if the component *already exists* (maybe it loaded correctly?)
                // Note: GetComponent(string) might be less reliable than checking type list
                bool alreadyExists = false;
                Component[] existingComponents = targetGO.GetComponents<Component>();
                foreach(var existingComp in existingComponents)
                {
                    if (existingComp != null && existingComp.GetType().FullName == compData.typeFullName)
                    {
                        alreadyExists = true;
                        break;
                    }
                }

                if (!alreadyExists)
                {
                    if (verboseLogging) MelonLogger.Msg($"[ComponentRestorer] Component type '{compData.typeFullName}' missing on '{targetGO.name}' (Path: {goData.path}). Attempting restoration...");

                    // Find the target Il2CppType (cache it)
                    if (!_targetTypeCache.TryGetValue(compData.typeFullName, out Il2CppSystem.Type targetIl2CppType))
                    {
                        System.Type targetSysType = FindTypeInLoadedAssemblies(compData.typeFullName);
                        if (targetSysType != null)
                        {
                            targetIl2CppType = Il2CppType.From(targetSysType);
                            if (targetIl2CppType != null)
                            {
                                _targetTypeCache[compData.typeFullName] = targetIl2CppType;
                            }
                        }
                        
                        if (targetIl2CppType == null)
                        {
                            MelonLogger.Error($"[ComponentRestorer] Could not find or convert target type '{compData.typeFullName}'. Cannot add component.");
                            continue; // Skip this component
                        }
                    }

                    // Add the component
                    try
                    {
                        Component newComponent = targetGO.AddComponent(targetIl2CppType);
                        if (newComponent != null)
                        {
                            restoredCount++;
                             MelonLogger.Msg($"[ComponentRestorer] Added component '{newComponent.GetType().FullName}' to '{targetGO.name}'. Applying properties...");
                            // Apply properties from JSON data
                            ApplyDecalPropertiesFromData(newComponent, compData, verboseLogging);
                        }
                        else
                        {
                            MelonLogger.Error($"[ComponentRestorer] AddComponent returned null for type '{targetIl2CppType.FullName}' on '{targetGO.name}'.");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Error($"[ComponentRestorer] Error adding component '{compData.typeFullName}' to '{targetGO.name}': {ex.Message}");
                    }
                }
                 else if (verboseLogging)
                 {
                      MelonLogger.Msg($"[ComponentRestorer] Component type '{compData.typeFullName}' already exists on '{targetGO.name}' (Path: {goData.path}). Skipping restoration.");
                 }
            }
        }
        MelonLogger.Msg($"[ComponentRestorer] Finished component restoration. Added {restoredCount} missing components.");
    }

    // Recursive helper to build path lookup
    private static void BuildPathLookup(Transform current, string currentPath, Dictionary<string, GameObject> lookup)
    {
        string path = string.IsNullOrEmpty(currentPath) ? current.name : currentPath + "/" + current.name;
        lookup[path] = current.gameObject;
        foreach (Transform child in current)
        {
            BuildPathLookup(child, path, lookup);
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

    // Specific method to apply Decal properties from JSON data
    private static void ApplyDecalPropertiesFromData(Component target, ComponentData data, bool verbose)
    {
        System.Type targetType = target.GetType();
        if (verbose) MelonLogger.Msg($"    - Applying properties to {targetType.FullName}:");

        // Apply basic value types directly
        SetProperty(target, targetType, "drawDistance", data.drawDistance, verbose);
        SetProperty(target, targetType, "fadeScale", data.fadeScale, verbose);
        SetProperty(target, targetType, "startAngleFade", data.startAngleFade, verbose);
        SetProperty(target, targetType, "endAngleFade", data.endAngleFade, verbose);
        SetProperty(target, targetType, "uvScale", data.uvScale, verbose);
        SetProperty(target, targetType, "uvBias", data.uvBias, verbose);
        SetProperty(target, targetType, "renderingLayerMask", data.renderingLayerMask, verbose);
        SetProperty(target, targetType, "pivot", data.pivot, verbose);
        SetProperty(target, targetType, "size", data.size, verbose);
        SetProperty(target, targetType, "fadeFactor", data.fadeFactor, verbose);

        // Apply Enum (ScaleMode)
        try
        {
            PropertyInfo scaleModeProp = targetType.GetProperty("scaleMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (scaleModeProp != null && scaleModeProp.CanWrite)
            {
                // Assuming DecalScaleMode enum underlying type is int
                object enumValue = System.Enum.ToObject(scaleModeProp.PropertyType, data.scaleMode);
                scaleModeProp.SetValue(target, enumValue);
                if (verbose) MelonLogger.Msg($"      - Set 'scaleMode' to {(UnityEngine.Rendering.Universal.DecalScaleMode)enumValue}");
            }
            else if (verbose) MelonLogger.Msg($"      - Skip 'scaleMode': Target property not found or not writable.");
        }
        catch (System.Exception ex) { MelonLogger.Warning($"[ComponentRestorer] Failed to set property 'scaleMode': {ex.Message}"); }

        // Apply Material
        try
        {
            PropertyInfo matProp = targetType.GetProperty("material", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (matProp != null && matProp.CanWrite)
            {
                Material mat = null;
                if (!string.IsNullOrEmpty(data.materialPath))
                {
                    // Attempt to load material - THIS IS THE TRICKY PART
                    // Option 1: Resources.Load (if material is in a Resources folder in *your* bundle)
                     mat = Resources.Load<Material>(data.materialPath); 
                     if (mat == null && verbose) MelonLogger.Warning($"     - Could not load material via Resources.Load: '{data.materialPath}'");

                    // Option 2: Load from the bundle directly (if you know its exact asset name/path)
                    // if (mat == null && YourModMain.il2cppCustomAssetsBundle != null) { 
                    //    mat = YourModMain.il2cppCustomAssetsBundle.LoadAsset<Material>(data.materialPath);
                    //    if (mat == null && verbose) MelonLogger.Warning($"     - Could not load material via Bundle.LoadAsset: '{data.materialPath}'");
                    // }
                }
                matProp.SetValue(target, mat); // Set even if null
                if (verbose) MelonLogger.Msg($"      - Set 'material' to '{mat?.name ?? "null"}' (Loaded from: '{data.materialPath ?? "N/A"}')");
            }
             else if (verbose) MelonLogger.Msg($"      - Skip 'material': Target property not found or not writable.");
        }
        catch (System.Exception ex) { MelonLogger.Warning($"[ComponentRestorer] Failed to set property 'material': {ex.Message}"); }
    }

    // Generic helper to set a property via reflection
    private static void SetProperty(Component target, System.Type targetType, string propName, object value, bool verbose)
    {
        PropertyInfo prop = targetType.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null && prop.CanWrite)
        {
            try
            {
                prop.SetValue(target, value);
                if (verbose) MelonLogger.Msg($"      - Set '{propName}' to {value?.ToString() ?? "null"}.");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[ComponentRestorer] Failed to set property '{propName}': {ex.Message}");
            }
        }
         else if (verbose) MelonLogger.Msg($"      - Skip '{propName}': Target property not found or not writable.");
    }
} 