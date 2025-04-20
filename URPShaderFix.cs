using MelonLoader;
using UnityEngine;
using System.Reflection;

namespace ChloesManorMod
{

    public static class URPShaderFix
    {
        // --- Configuration Structure ---
        private class ShaderFixConfig
        {
            public string ProblematicShaderName; // Name of the shader to replace
            public string TargetShaderName;      // Name of the shader to find and apply
            public Dictionary<string, string> TexturePropertyMappings; // Key=Original Property Name, Value=Target Property Name
            public Dictionary<string, string> ColorPropertyMappings;   // Key=Original Property Name, Value=Target Property Name
            public Dictionary<string, string> FloatPropertyMappings;   // Key=Original Property Name, Value=Target Property Name

            public ShaderFixConfig(string problematic, string target,
                                   Dictionary<string, string> textureMappings,
                                   Dictionary<string, string> colorMappings = null, // Optional
                                   Dictionary<string, string> floatMappings = null) // Optional
            {
                ProblematicShaderName = problematic;
                TargetShaderName = target;
                TexturePropertyMappings = textureMappings ?? new Dictionary<string, string>(); // Ensure dictionary exists
                ColorPropertyMappings = colorMappings ?? new Dictionary<string, string>();     // Ensure dictionary exists
                FloatPropertyMappings = floatMappings ?? new Dictionary<string, string>();       // Ensure dictionary exists
            }
        }

        // --- Define Fix Configurations ---
        private static readonly List<ShaderFixConfig> FixConfigs = new List<ShaderFixConfig>
    {
        // Fix for standard Lit shader
        new ShaderFixConfig(
            problematic: "Universal Render Pipeline/Lit",
            target: "Universal Render Pipeline/Lit", // Find the game's version
            textureMappings: new Dictionary<string, string> {
                // Map original property names to target property names
                // Assuming they are the same unless proven otherwise
                { "_BaseMap", "_BaseMap" },         // Standard color/albedo texture
                { "_MainTex", "_MainTex" },         // Often alias for _BaseMap, copy anyway
                { "_BumpMap", "_BumpMap" },         // Normal map
                { "_EmissionMap", "_EmissionMap" }, // Emission map
                { "_MetallicGlossMap", "_MetallicGlossMap" }, // Metallic/Smoothness map (if used)
                { "_OcclusionMap", "_OcclusionMap" },      // Ambient Occlusion map (if used)
                // Add other common URP/Lit texture properties if needed
            }
        ),
        // Fix for the simple lit shader
        new ShaderFixConfig(
            problematic: "Universal Render Pipeline/Simple Lit",
            target: "Universal Render Pipeline/Simple Lit", // Find the game's version
            textureMappings: new Dictionary<string, string> {
                { "_BaseMap", "_BaseMap" },
                { "_MainTex", "_MainTex" },
                { "_EmissionMap", "_EmissionMap" },
                { "_BumpMap", "_BumpMap" },
            }
        ),
        // Fix for the Worldspace UV shader
        new ShaderFixConfig(
            problematic: "Shader Graphs/WorldspaceUV_New",
            target: "Shader Graphs/WorldspaceUV_New", // Find the game's version
            textureMappings: new Dictionary<string, string> {
                { "_DiffuseTexture", "_DiffuseTexture" },
                { "_Metallic_Map", "_Metallic_Map" },
                { "_NormalTexture", "_NormalTexture" },
            }
        ),
        // --- NEW FIX for Tyler Decal --- 
        new ShaderFixConfig(
            problematic: "Shader Graphs/Tyler Decal",
            target: "Shader Graphs/Tyler Decal", // Targetting the same shader, just ensuring it's the game's instance
            textureMappings: new Dictionary<string, string> {
                { "_BaseMap", "_BaseMap" },
                { "_Base_Map", "_Base_Map" }, // Include both variants if they exist
                { "_MainTex", "_MainTex" },
                { "_Normal_Map", "_Normal_Map" }
            },
            colorMappings: new Dictionary<string, string> {
                { "_BaseColor", "_BaseColor" }
            },
            floatMappings: new Dictionary<string, string> {
                { "_Normal_Blend", "_Normal_Blend" }
            }
        )
        // Add configs for other problematic shaders here if discovered
    };

        // Make the method public and static
        public static void FixShadersRecursive(GameObject rootObject, bool verboseLogging = false)
        {
            if (rootObject == null)
            {
                MelonLogger.Error($"URPShaderFix: Provided root GameObject is null. Cannot apply fix.");
                return;
            }

            Dictionary<string, Shader> targetShaderCache = new Dictionary<string, Shader>();
            int totalRenderersFixed = 0;
            int totalDecalsFixed = 0;

            if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Starting shader check for '{rootObject.name}'...");

            // --- 1. Process Renderers ---
            Renderer[] renderers = rootObject.GetComponentsInChildren<Renderer>(true);
            if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Found {renderers.Length} renderers under '{rootObject.name}'.");

            foreach (Renderer rend in renderers)
            {
                if (rend == null) continue;

                // Don't process DecalProjector materials here, we'll do it separately
                if (rend.GetType().FullName == "UnityEngine.Rendering.Universal.DecalProjector")
                {
                    if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Skipping Renderer {rend.gameObject.name} as it is a DecalProjector (will be handled separately).");
                    continue;
                }

                Material[] materials = rend.materials; // Get COPY of material array
                if (materials == null || materials.Length == 0) continue;

                bool changedMaterialInArray = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    // Pass the material reference (materials[i]) to the helper
                    // The helper returns true if the material was modified
                    if (ProcessMaterialFix(ref materials[i], targetShaderCache, rend.gameObject, i, verboseLogging))
                    {
                        changedMaterialInArray = true;
                        totalRenderersFixed++;
                    }
                }

                if (changedMaterialInArray)
                {
                    rend.materials = materials; // Assign the potentially modified array back
                }
            }

            // --- 2. Process Decal Projectors ---
            if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Processing DecalProjectors under '{rootObject.name}'...");
            try // Add a try-catch around the whole Decal processing block
            {
                // --- Use Generic GetComponentsInChildren --- 
                UnityEngine.Rendering.Universal.DecalProjector[] decalComponents = 
                    rootObject.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>(true);

                if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Found {decalComponents.Length} DecalProjectors using generic GetComponentsInChildren.");

                foreach (UnityEngine.Rendering.Universal.DecalProjector decalProjectorInstance in decalComponents)
                {
                    if (decalProjectorInstance == null) continue;

                    Material currentDecalMat = null;
                    try { currentDecalMat = decalProjectorInstance.material; }
                    catch (System.Exception ex) 
                    { 
                        MelonLogger.Warning($"URPShaderFix: Error getting material directly from DecalProjector on '{decalProjectorInstance.gameObject.name}': {ex.Message}"); 
                        continue; // Skip this decal if we can't get its material
                    }

                    // --- Log Material and Shader Name BEFORE processing --- 
                    if (verboseLogging)
                    {
                        string matName = currentDecalMat?.name ?? "NULL Material";
                        string shaderName = currentDecalMat?.shader?.name ?? "NULL Shader";
                        MelonLogger.Msg($"URPShaderFix: -> Processing Decal '{decalProjectorInstance.gameObject.name}': Material='{matName}', Shader='{shaderName}'");
                    }
                    // --- 

                    Material originalMatInstance = currentDecalMat;

                    // Process the material (passing the reference)
                    if (ProcessMaterialFix(ref currentDecalMat, targetShaderCache, decalProjectorInstance.gameObject, -1, verboseLogging)) // Use -1 for index as it's not an array
                    {
                        totalDecalsFixed++;
                        if (currentDecalMat != originalMatInstance) { if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Reassigning modified material instance to DecalProjector on '{decalProjectorInstance.gameObject.name}'."); }
                        else { if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Reassigning (potentially modified in-place) material to DecalProjector on '{decalProjectorInstance.gameObject.name}'."); }

                        try { decalProjectorInstance.material = currentDecalMat; }
                        catch (System.Exception ex) { MelonLogger.Error($"URPShaderFix: Error setting material directly onto DecalProjector on '{decalProjectorInstance.gameObject.name}': {ex.Message}"); }
                    }
                }
            }
            catch (System.Exception decalEx)
            {
                 MelonLogger.Error($"URPShaderFix: Unhandled exception during DecalProjector processing loop: {decalEx.ToString()}");
            }
            // --- End Decal Processing ---

            // --- 3. Final Report ---
            int totalFixed = totalRenderersFixed + totalDecalsFixed;
            if (totalFixed > 0)
            {
                MelonLogger.Msg($"URPShaderFix: Finished. Applied shader fixes to {totalRenderersFixed} renderer material(s) and {totalDecalsFixed} DecalProjector material(s) under '{rootObject.name}'.");
            }
            else if (verboseLogging)
            {
                MelonLogger.Msg($"URPShaderFix: Finished check under '{rootObject.name}'. No shader changes required based on config.");
            }
        }

        // --- NEW HELPER FUNCTION to process a single material ---
        // Returns true if the material was modified, false otherwise.
        // Takes material by ref so it can be potentially replaced (though Unity often instances materials on edit anyway).
        private static bool ProcessMaterialFix(ref Material mat, Dictionary<string, Shader> targetShaderCache, GameObject ownerGO, int materialIndex, bool verboseLogging)
        {
            if (mat == null) return false;

            string ownerName = ownerGO?.name ?? "UnknownOwner";
            string materialIdentifier = $"Material '{mat.name}' on '{ownerName}'" + (materialIndex >= 0 ? $" (Index {materialIndex})" : " (Decal)");

            // --- Log Entry --- 
            if (verboseLogging) MelonLogger.Msg($"URPShaderFix: >> ProcessMaterialFix started for {materialIdentifier}");

            Shader currentShader = mat.shader;
            string currentShaderName = currentShader?.name ?? "NULL";
            if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Current Shader Name: '{currentShaderName}'");

            ShaderFixConfig matchingConfig = null;

            // Find matching config based on current shader name
            if (currentShader != null)
            {
                foreach (var config in FixConfigs)
                {
                    if (currentShaderName == config.ProblematicShaderName) // Use cached name
                    {
                        matchingConfig = config;
                        break;
                    }
                }
            }
            // Removed null shader handling block as it likely won't apply here

            if (matchingConfig == null)
            {
                if (verboseLogging) MelonLogger.Msg($"URPShaderFix: No matching config found for shader '{currentShaderName}'. Skipping fix.");
                return false;
            }
            if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Found matching config: Problematic='{matchingConfig.ProblematicShaderName}', Target='{matchingConfig.TargetShaderName}'");


            // Find target shader (use cache)
            Shader targetShaderInstance = null;
            if (!targetShaderCache.TryGetValue(matchingConfig.TargetShaderName, out targetShaderInstance))
            {
                if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Target shader '{matchingConfig.TargetShaderName}' not in cache. Finding...");
                targetShaderInstance = Shader.Find(matchingConfig.TargetShaderName);
                if (targetShaderInstance != null)
                {
                    targetShaderCache[matchingConfig.TargetShaderName] = targetShaderInstance;
                    if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Found and cached target shader '{targetShaderInstance.name}'.");
                }
                else
                {
                    MelonLogger.Error($"URPShaderFix: Could not find target shader '{matchingConfig.TargetShaderName}' for {materialIdentifier}. Skipping fix.", ownerGO);
                    return false;
                }
            }
            else
            {
                if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Found target shader '{matchingConfig.TargetShaderName}' in cache.");
            }

            // Check if shader needs changing
            if (currentShader == targetShaderInstance)
            {
                if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Shader already correct for {materialIdentifier}. No change needed.");
                return false; // No change needed
            }

            // --- Shader needs fixing ---
            if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Attempting shader fix for {materialIdentifier}. From: '{currentShaderName}' To: '{targetShaderInstance.name}'");

            // Store old values before changing shader
            Dictionary<string, Texture> originalTextures = new Dictionary<string, Texture>();
            Dictionary<string, Color> originalColors = new Dictionary<string, Color>();
            Dictionary<string, float> originalFloats = new Dictionary<string, float>();

            // Store Textures
            foreach (var kvp in matchingConfig.TexturePropertyMappings)
            {
                string originalPropName = kvp.Key;
                if (mat.HasProperty(originalPropName))
                {
                    try
                    {
                        originalTextures[originalPropName] = mat.GetTexture(originalPropName);
                        if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Stored Texture['{originalPropName}']: {originalTextures[originalPropName]?.name ?? "NULL"}");
                    }
                    catch (System.Exception e) { if (verboseLogging) MelonLogger.Warning($"URPShaderFix: Failed getting Texture from '{originalPropName}': {e.Message}"); }
                }
            }
            // Store Colors
            foreach (var kvp in matchingConfig.ColorPropertyMappings)
            {
                string originalPropName = kvp.Key;
                if (mat.HasProperty(originalPropName))
                {
                    try
                    {
                        originalColors[originalPropName] = mat.GetColor(originalPropName);
                        if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Stored Color['{originalPropName}']: {originalColors[originalPropName]}");
                    }
                    catch (System.Exception e) { if (verboseLogging) MelonLogger.Warning($"URPShaderFix: Failed getting Color from '{originalPropName}': {e.Message}"); }
                }
            }
            // Store Floats
            foreach (var kvp in matchingConfig.FloatPropertyMappings)
            {
                string originalPropName = kvp.Key;
                if (mat.HasProperty(originalPropName))
                {
                    try
                    {
                        originalFloats[originalPropName] = mat.GetFloat(originalPropName);
                        if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Stored Float['{originalPropName}']: {originalFloats[originalPropName]}");
                    }
                    catch (System.Exception e) { if (verboseLogging) MelonLogger.Warning($"URPShaderFix: Failed getting Float from '{originalPropName}': {e.Message}"); }
                }
            }

            // Change the shader
            try
            {
                mat.shader = targetShaderInstance;
                if (verboseLogging) MelonLogger.Msg($"URPShaderFix: -> Assigned target shader '{targetShaderInstance.name}'. Applying properties...");

                // Apply stored values using TARGET names
                // Apply Textures
                foreach (var kvp in matchingConfig.TexturePropertyMappings)
                {
                    if (originalTextures.TryGetValue(kvp.Key, out Texture storedTex) && mat.HasProperty(kvp.Value))
                    {
                        try
                        {
                            mat.SetTexture(kvp.Value, storedTex);
                            if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Applied Texture['{kvp.Value}']: {storedTex?.name ?? "NULL"}");
                        }
                        catch (System.Exception e) { if (verboseLogging) MelonLogger.Warning($"URPShaderFix: Failed setting Texture to '{kvp.Value}': {e.Message}"); }
                    }
                }
                // Apply Colors
                foreach (var kvp in matchingConfig.ColorPropertyMappings)
                {
                    if (originalColors.TryGetValue(kvp.Key, out Color storedColor) && mat.HasProperty(kvp.Value))
                    {
                        try
                        {
                            mat.SetColor(kvp.Value, storedColor);
                            if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Applied Color['{kvp.Value}']: {storedColor}");
                        }
                        catch (System.Exception e) { if (verboseLogging) MelonLogger.Warning($"URPShaderFix: Failed setting Color to '{kvp.Value}': {e.Message}"); }
                    }
                }
                // Apply Floats
                foreach (var kvp in matchingConfig.FloatPropertyMappings)
                {
                    if (originalFloats.TryGetValue(kvp.Key, out float storedFloat) && mat.HasProperty(kvp.Value))
                    {
                        try
                        {
                            mat.SetFloat(kvp.Value, storedFloat);
                            if (verboseLogging) MelonLogger.Msg($"URPShaderFix: Applied Float['{kvp.Value}']: {storedFloat}");
                        }
                        catch (System.Exception e) { if (verboseLogging) MelonLogger.Warning($"URPShaderFix: Failed setting Float to '{kvp.Value}': {e.Message}"); }
                    }
                }

                if (verboseLogging) MelonLogger.Msg($"URPShaderFix: << SUCCESS applying shader and properties to {materialIdentifier}.");
                return true; // Material was modified
            }
            catch (System.Exception e)
            {
                MelonLogger.Error($"URPShaderFix: -> FAILED during shader/property update for {materialIdentifier}: {e.Message}", ownerGO);
                if (verboseLogging) MelonLogger.Msg($"URPShaderFix: << FAILED shader/property update for {materialIdentifier}.");
                return false; // Modification failed
            }
        }

        // Helper to copy properties via Reflection (keeping for now, might be useful elsewhere)
        private static void CopyDecalProperties(Component source, Component target, bool verbose)
        {
            // List of specific Decal Projector properties to copy based on user request
            string[] propertiesToCopy = {
            "material",
            "drawDistance",
            "fadeScale",
            "startAngleFade",
            "endAngleFade",
            "uvScale",
            "uvBias",
            "renderingLayerMask", // Maps to "Decal Layer Mask"
            "scaleMode",
            "pivot",              // Maps to "Offset"
            "size",
            "fadeFactor"
         };

            System.Type sourceType = source.GetType();
            System.Type targetType = target.GetType();

            if (verbose) MelonLogger.Msg($"    - Attempting to copy properties from {sourceType.FullName} to {targetType.FullName}:");

            foreach (string propName in propertiesToCopy)
            {
                PropertyInfo sourceProp = sourceType.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                PropertyInfo targetProp = targetType.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (sourceProp != null && targetProp != null && targetProp.CanWrite && sourceProp.PropertyType == targetProp.PropertyType)
                {
                    try
                    {
                        object value = sourceProp.GetValue(source);
                        targetProp.SetValue(target, value);
                        if (verbose) MelonLogger.Msg($"      - Copied '{propName}' (Value: {value?.ToString() ?? "null"}).");
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Warning($"[URPShaderFix] Failed to copy property '{propName}': {ex.Message}");
                    }
                }
                else if (verbose)
                { // Log reasons for skipping
                    if (sourceProp == null) MelonLogger.Msg($"      - Skip '{propName}': Source property not found.");
                    else if (targetProp == null) MelonLogger.Msg($"      - Skip '{propName}': Target property not found.");
                    else if (!targetProp.CanWrite) MelonLogger.Msg($"      - Skip '{propName}': Target property not writable.");
                    else if (sourceProp.PropertyType != targetProp.PropertyType) MelonLogger.Msg($"      - Skip '{propName}': Type mismatch (Source: {sourceProp.PropertyType.Name}, Target: {targetProp.PropertyType.Name}).");
                }
            }
        }
    }
}