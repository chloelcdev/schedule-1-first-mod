using UnityEngine;
using MelonLoader; // Or your logging namespace
using System.Collections.Generic; // <<< NEEDED for Dictionary and List

public static class URPShaderFix
{
    // --- Configuration Structure ---
    private class ShaderFixConfig
    {
        public string ProblematicShaderName; // Name of the shader to replace
        public string TargetShaderName;      // Name of the shader to find and apply
        public Dictionary<string, string> TexturePropertyMappings; // Key=Original Property Name, Value=Target Property Name

        public ShaderFixConfig(string problematic, string target, Dictionary<string, string> mappings)
        {
            ProblematicShaderName = problematic;
            TargetShaderName = target;
            TexturePropertyMappings = mappings ?? new Dictionary<string, string>(); // Ensure dictionary exists
        }
    }

    // --- Define Fix Configurations ---
    private static readonly List<ShaderFixConfig> FixConfigs = new List<ShaderFixConfig>
    {
        // Fix for standard Lit shader
        new ShaderFixConfig(
            problematic: "Universal Render Pipeline/Lit",
            target: "Universal Render Pipeline/Lit", // Find the game's version
            mappings: new Dictionary<string, string> {
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
            mappings: new Dictionary<string, string> {
                { "_BaseMap", "_BaseMap" },
                { "_MainTex", "_MainTex" }, 
                { "_EmissionMap", "_EmissionMap" },
                { "_BumpMap", "_BumpMap" },
                // Add any other texture properties used by this shader graph
            }
        ),
        // Fix for the Worldspace UV shader
        new ShaderFixConfig(
            problematic: "Shader Graphs/WorldspaceUV_New",
            target: "Shader Graphs/WorldspaceUV_New", // Find the game's version
            mappings: new Dictionary<string, string> {
                { "_DiffuseTexture", "_DiffuseTexture" },
                { "_Metallic_Map", "_Metallic_Map" }, 
                { "_NormalTexture", "_NormalTexture" },
                // Add any other texture properties used by this shader graph
            }
        )
        // Add configs for other problematic shaders here if discovered
    };

    // Make the method public and static
    public static void FixShadersRecursive(GameObject rootObject, bool verboseLogging = false)
    {
        if (rootObject == null)
        {
            MelonLogger.Error("[URPShaderFix] Provided root GameObject is null. Cannot apply fix.");
            return;
        }

        // Cache found target shaders to avoid repeated Shader.Find
        Dictionary<string, Shader> targetShaderCache = new Dictionary<string, Shader>();
        int totalFixedCount = 0;

        if (verboseLogging) MelonLogger.Msg($"[URPShaderFix] Starting shader check for '{rootObject.name}'...");

        // 3. Get all renderers in children
        Renderer[] renderers = rootObject.GetComponentsInChildren<Renderer>(true); // Include inactive

        if (verboseLogging) MelonLogger.Msg($"[URPShaderFix] Found {renderers.Length} renderers under '{rootObject.name}'.");

        foreach (Renderer rend in renderers)
        {
            if (rend == null) continue;

            Material[] materials = rend.materials; // Get COPY of material array
            if (materials == null || materials.Length == 0) continue;

            bool changedMaterialInArray = false; // Track if we need to reassign the array

            // 4. Iterate through the materials array copy
            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null) continue;

                Shader currentShader = mat.shader;
                ShaderFixConfig matchingConfig = null;

                // Find if the current shader matches a problematic configuration
                if (currentShader != null)
                {
                    foreach (var config in FixConfigs)
                    {
                        if (currentShader.name == config.ProblematicShaderName)
                        {
                            matchingConfig = config;
                            break;
                        }
                    }
                }
                // Handle null shader case - Default to URP/Lit fix? (Optional, adjust if needed)
                else
                {
                    // Example: Treat null shader as needing the URP/Lit fix
                    matchingConfig = FixConfigs.Find(cfg => cfg.ProblematicShaderName == "Universal Render Pipeline/Lit");
                     if (verboseLogging && matchingConfig != null) MelonLogger.Msg($"   - Material '{mat.name}' on '{rend.gameObject.name}' has NULL shader. Will attempt fix using '{matchingConfig.TargetShaderName}'.", rend);
                     else if (verboseLogging) MelonLogger.Msg($"   - Material '{mat.name}' on '{rend.gameObject.name}' has NULL shader. No default fix configured.", rend);
                }

                // Proceed if we found a configuration match
                if (matchingConfig != null)
                {
                    // Find (or get from cache) the target shader instance
                    Shader targetShaderInstance = null;
                    if (!targetShaderCache.TryGetValue(matchingConfig.TargetShaderName, out targetShaderInstance))
                    {
                        targetShaderInstance = Shader.Find(matchingConfig.TargetShaderName);
                        if (targetShaderInstance != null) { targetShaderCache[matchingConfig.TargetShaderName] = targetShaderInstance; }
                        else
                        {
                             MelonLogger.Error($"[URPShaderFix] Could not find target shader '{matchingConfig.TargetShaderName}' for material '{mat.name}' on '{rend.gameObject.name}'. Skipping fix.", rend);
                             continue; // Skip this material
                        }
                    }

                    // Check if the shader instance actually needs changing
                    if (currentShader != targetShaderInstance)
                    {
                         if (verboseLogging) MelonLogger.Msg($"   - Fixing Shader for Material '{mat.name}' on '{rend.gameObject.name}' (Index {i}). From: '{(currentShader?.name ?? "NULL")}' To: '{targetShaderInstance.name}'");

                        // 1. Store old texture values based on the config's ORIGINAL names
                        Dictionary<string, Texture> originalTextures = new Dictionary<string, Texture>();
                        foreach (var kvp in matchingConfig.TexturePropertyMappings)
                        {
                            string originalPropName = kvp.Key;
                            if (mat.HasProperty(originalPropName))
                            {
                                Texture tex = mat.GetTexture(originalPropName);
                                originalTextures[originalPropName] = tex;
                                // if (verboseLogging) MelonLogger.Msg($"     - Stored Texture from '{originalPropName}': {tex?.name ?? "NULL"}");
                            }
                        }

                        // 2. Change the shader
                        try
                        {
                            mat.shader = targetShaderInstance; // Assign new shader TO THE MATERIAL IN OUR COPIED ARRAY

                            // 3. Apply stored textures using the config's TARGET names
                            foreach (var kvp in matchingConfig.TexturePropertyMappings)
                            {
                                string originalPropName = kvp.Key;
                                string targetPropName = kvp.Value;

                                if (originalTextures.TryGetValue(originalPropName, out Texture storedTex))
                                {
                                    if (mat.HasProperty(targetPropName)) // Check if NEW shader has the target property
                                    {
                                        mat.SetTexture(targetPropName, storedTex);
                                         // if (verboseLogging) MelonLogger.Msg($"     - Applied Texture to '{targetPropName}' (from '{originalPropName}'): {storedTex?.name ?? "NULL"}");
                                    }
                                    // else if (verboseLogging) MelonLogger.Msg($"     - Target Property '{targetPropName}' not found on new shader.");
                                }
                            }

                            changedMaterialInArray = true; // Mark that we need to reassign the array
                            totalFixedCount++;
                            if (verboseLogging) MelonLogger.Msg($"     -> SUCCESS.");


                        } catch (System.Exception e) { MelonLogger.Error($"     -> FAILED during shader/texture update: {e.Message}", rend); } // QUALIFIED
                    }
                     //else if (verboseLogging) MelonLogger.Msg($"   - Shader already correct for Material '{mat.name}' on '{rend.gameObject.name}'."); // Reduced log noise

                } // End if matchingConfig != null

            } // End material loop

            // IMPORTANT: Assign the potentially modified materials array back to the renderer
            if (changedMaterialInArray)
            {
                 // if (verboseLogging) MelonLogger.Msg($"   -> Reassigning materials array for '{rend.gameObject.name}'."); // Reduced log noise
                 rend.materials = materials;
            }

        } // End renderer loop

        if (totalFixedCount > 0) { MelonLogger.Msg($"[URPShaderFix] Finished. Applied shader fixes to {totalFixedCount} material instances under '{rootObject.name}'."); }
        else if (verboseLogging) { MelonLogger.Msg($"[URPShaderFix] Finished check under '{rootObject.name}'. No shader changes required based on config."); }
    }
}
