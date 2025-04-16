using UnityEngine;
using MelonLoader; // Or your logging namespace

public static class URPShaderFix
{
    // Make the method public and static
    public static void FixShadersRecursive(GameObject rootObject,
                                           string targetShaderName = "Universal Render Pipeline/Lit",
                                           string problematicShaderName = "Universal Render Pipeline/Lit",
                                           bool replaceNullShader = true,
                                           bool verboseLogging = true) // Add parameter for logging control
    {
        if (rootObject == null)
        {
            MelonLogger.Error("[URPShaderFix] Provided root GameObject is null. Cannot apply fix.");
            return;
        }

        // 1. Find the target shader instance
        Shader targetShaderInstance = Shader.Find(targetShaderName);
        if (targetShaderInstance == null)
        {
            MelonLogger.Error($"[URPShaderFix] Could not find target shader '{targetShaderName}'! Cannot apply fix.");
            return;
        }
        if (verboseLogging) MelonLogger.Msg($"[URPShaderFix] Target shader '{targetShaderName}' found. Applying fix to hierarchy under '{rootObject.name}'.");

        // 3. Get all renderers in children
        Renderer[] renderers = rootObject.GetComponentsInChildren<Renderer>(true); // Include inactive

        if (verboseLogging) MelonLogger.Msg($"[URPShaderFix] Found {renderers.Length} renderers under '{rootObject.name}'.");

        int fixedCount = 0;

        // 4. Iterate through renderers
        foreach (Renderer rend in renderers)
        {
            if (rend == null) continue;

            Material[] materials = rend.materials; // Gets a COPY
            if (materials == null || materials.Length == 0) continue;

            bool changedMaterial = false;

            // 5. Iterate through the materials array copy
            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null) continue;

                Shader currentShader = mat.shader;
                string currentShaderName = (currentShader != null) ? currentShader.name : "NULL";
                bool needsFix = false;

                // 6. Check if the shader needs replacing
                if (currentShader == null && replaceNullShader)
                {
                    needsFix = true;
                    if (verboseLogging) MelonLogger.Msg($"   - Fixing NULL shader on '{rend.gameObject.name}' Material '{mat.name}' (Index {i}).", rend);
                }
                else if (currentShader != null && currentShader.name == problematicShaderName)
                {
                    if (currentShader != targetShaderInstance) // Check instance ID
                    {
                        needsFix = true;
                         if (verboseLogging) MelonLogger.Msg($"   - Fixing incompatible shader '{currentShaderName}' on '{rend.gameObject.name}' Material '{mat.name}' (Index {i}).", rend);
                    }
                }

                // 7. Apply the fix to the material in the copied array
                if (needsFix)
                {
                    if (verboseLogging) MelonLogger.Msg($"   - Fixing material '{mat.name}' on '{rend.gameObject.name}' (Index {i}). CURRENT Shader: '{currentShaderName}'");
                    try
                    {
                        materials[i].shader = targetShaderInstance;
                        changedMaterial = true;
                        fixedCount++;
                        if (verboseLogging) MelonLogger.Msg($"     -> SUCCESS. NEW Shader assigned: '{materials[i].shader.name}'");
                    }
                    catch (System.Exception e)
                    {
                        MelonLogger.Error($"     -> FAILED to apply shader: {e.Message}", rend);
                    }
                }
            }

            // 8. Assign the modified array back if changes were made
            if (changedMaterial)
            {
                if (verboseLogging) MelonLogger.Msg($"   -> Reassigning materials array for '{rend.gameObject.name}'.", rend);
                rend.materials = materials;
            }
        }

        if (fixedCount > 0)
        {
            MelonLogger.Msg($"[URPShaderFix] Finished. Applied target shader '{targetShaderName}' to {fixedCount} material instances under '{rootObject.name}'.");
        }
        else if (verboseLogging)
        {
             MelonLogger.Msg($"[URPShaderFix] Finished check under '{rootObject.name}'. No materials required fixing.");
        }
    }
}
