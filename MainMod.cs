using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays; // For Il2CppReferenceArray<>
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Map;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using Il2CppScheduleOne.Dialogue; // For DialogueContainer, DialogueNodeData, DialogueChoiceData
using Il2CppSystem;             // For Guid
using Il2CppScheduleOne.NPCs; // For NPC base class and potentially Ray
using Il2CppScheduleOne.NPCs.Schedules; // For NPCEvent_LocationDialogue
using Ray = Il2CppScheduleOne.NPCs.CharacterClasses.Ray; // Assuming Ray is a subclass of NPC
using System.Collections; // Required for IEnumerator
using Il2CppScheduleOne.Vehicles; // Needed for LandVehicle

// === FishNet using Statements ===
// You MUST add a reference to Il2CppFishNet.Runtime.dll in Visual Studio
using Il2CppFishNet.Object;        // For NetworkObject
using Il2CppFishNet.Managing;      // For NetworkManager, InstanceFinder
using Il2CppFishNet.Managing.Server; // For ServerManager
using Il2CppFishNet.Connection;    // For NetworkConnection (needed for Spawn overload)
// using FishNet.Transporting; // Might be needed for Channel enum? Not used directly here.
// === End FishNet using ===

[assembly: MelonInfo(typeof(ChloesManorMod.MainMod), BuildInfo.Name, BuildInfo.Version, BuildInfo.Author, BuildInfo.DownloadLink)]
[assembly: MelonColor()]
[assembly: MelonGame("TVGS", "Schedule I")]

public static class BuildInfo
{
    public const string Name = "Manor Setup Mod (Network+Local Spawn)";
    public const string Description = "Adds missing setup components to the Manor property.";
    public const string Author = "Chloe";
    public const string Company = null;
    public const string Version = "2.1.3";
    public const string DownloadLink = null;
}

namespace ChloesManorMod
{ //23,880
    public partial class MainMod : MelonMod
    {
        private static Il2CppAssetBundle il2cppCustomAssetsBundle;
        private static GameObject manorSetupPrefab;

        private const string BundleName = "chloemanorsetup";
        private const string PrefabName = "ManorSetup-Chloe";
        private const string TargetSceneName = "Main";
        private const string ManorPropertyCode = "manor";

        private static GameObject spawnedInstanceRoot = null;
        private static NetworkObject spawnedNetworkObject = null;

        private const string RootObjectName = "ManorSetup-Chloe";
        private const string CubeTest1Name = "Cube Test";
        private const string CubeTest2Name = "Cube Test 2";

        private static bool dialogueModified = false;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg($"{BuildInfo.Name} v{BuildInfo.Version} Initializing...");
            LoadAssetBundleViaManager();
            LogURPVersion(); // Keep URP version log - useful for compatibility info

            // Apply Harmony Patches (Keep error log)
            try
            {
                HarmonyInstance.PatchAll(typeof(MainMod).Assembly);
                LoggerInstance.Msg("Harmony patches applied."); // Shortened success msg
            }
            catch (System.Exception e)
            {
                LoggerInstance.Error($"Failed to apply Harmony patches: {e}");
            }
        }

        // --- ADDED: Helper method to log URP version ---
        private void LogURPVersion()
        {
            LoggerInstance.Msg("--- Checking URP Version ---"); // Shortened
            Assembly urpAssembly = null;
            string potentialAssemblyName1 = "Unity.RenderPipelines.Universal.Runtime";
            string potentialAssemblyName2 = "UnityEngine.RenderPipelines.Universal.Runtime";

            try
            {
                Assembly[] loadedAssemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                foreach (Assembly assembly in loadedAssemblies)
                {
                    string assemblyName = assembly.GetName().Name;
                    if (string.Equals(assemblyName, potentialAssemblyName1, System.StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(assemblyName, potentialAssemblyName2, System.StringComparison.OrdinalIgnoreCase))
                    {
                        urpAssembly = assembly;
                        break;
                    }
                }

                if (urpAssembly != null)
                {
                    System.Version version = urpAssembly.GetName().Version;
                    LoggerInstance.Msg($"Detected URP Runtime Version: {version.Major}.{version.Minor}.{version.Build}");
                }
                else
                {
                    LoggerInstance.Warning("Could not find a loaded URP Runtime assembly. Unable to determine exact URP version.");
                }
            }
            catch (System.Exception e) { LoggerInstance.Error($"Exception while checking URP version: {e}"); }
            LoggerInstance.Msg("----------------------------"); // Shortened
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            // LoggerInstance.Msg($"Scene loaded: {sceneName}"); // Can usually remove this
            if (sceneName == TargetSceneName)
            {
                MelonCoroutines.Start(SetupAfterSceneLoad());
            }
            else
            {
                CleanUp(); // Cleanup logs are important
            }
        }

        private IEnumerator SetupAfterSceneLoad()
        {
            // LoggerInstance.Msg("SetupAfterSceneLoad: Waiting one frame..."); // Removed
            yield return null; // Keep the wait

            // Keep dialogue mod logs if functionality is kept
            if (!dialogueModified)
            {
                 // ModifyEstateAgentChoicesDirectly(); // Keep call if needed
                 // FindAndLogEstateAgentEvent(); // Keep call if needed
            }

            LoadPrefabsFromIl2CppBundle(); // Keep errors from this
            SpawnAndConfigurePrefab(); // Keep essential logs/errors from this
        }

        private void LoadAssetBundleViaManager()
        {
            if (il2cppCustomAssetsBundle != null) return;

            // LoggerInstance.Msg($"Loading AssetBundle '{BundleName}' via Il2CppAssetBundleManager..."); // Removed verbose
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string resourceName = $"{typeof(MainMod).Namespace}.{BundleName}";

                using (System.IO.Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) { LoggerInstance.Error($"Failed to find embedded resource stream: {resourceName}"); return; }
                    byte[] assetData = new byte[stream.Length];
                    stream.Read(assetData, 0, assetData.Length);
                    il2cppCustomAssetsBundle = Il2CppAssetBundleManager.LoadFromMemory(assetData);

                    if (il2cppCustomAssetsBundle == null) { LoggerInstance.Error("Il2CppAssetBundleManager.LoadFromMemory failed or returned null!"); }
                    else { LoggerInstance.Msg($"AssetBundle '{BundleName}' loaded successfully."); } // Keep success/fail
                }
            }
            catch (System.MissingMethodException mmex) { LoggerInstance.Error($"Il2CppAssetBundleManager.LoadFromMemory method not found! {mmex.Message}"); il2cppCustomAssetsBundle = null; }
            catch (System.Exception e) { LoggerInstance.Error($"Exception loading AssetBundle: {e}"); il2cppCustomAssetsBundle = null; }
        }

        private void LoadPrefabsFromIl2CppBundle()
        {
            if (il2cppCustomAssetsBundle == null) { LoggerInstance.Error("Cannot load prefab, AssetBundle is not loaded."); return; } // Keep error
            if (manorSetupPrefab != null) { return; }

            // LoggerInstance.Msg($"Loading prefab '{PrefabName}' using Il2CppAssetBundle wrapper..."); // Removed verbose
            try
            {
                UnityEngine.Object loadedAsset = il2cppCustomAssetsBundle.LoadAsset<GameObject>(PrefabName);
                if (loadedAsset == null) { LoggerInstance.Error($"Failed to load '{PrefabName}' prefab!"); } // Keep error
                else
                {
                    manorSetupPrefab = loadedAsset.TryCast<GameObject>();
                    if (manorSetupPrefab != null) LoggerInstance.Msg($"Prefab '{PrefabName}' loaded successfully."); // Keep success
                    else LoggerInstance.Error($"Loaded asset '{PrefabName}' failed to cast to GameObject!"); // Keep error
                }
            }
            catch (System.Exception e) { LoggerInstance.Error($"Error loading prefab: {e}"); manorSetupPrefab = null; } // Keep error
        }

        private void SpawnAndConfigurePrefab()
        {
            if (manorSetupPrefab == null) { LoggerInstance.Error("Manor setup prefab not loaded. Cannot spawn."); return; } // Keep error
            if (spawnedInstanceRoot != null) { LoggerInstance.Warning("Manor setup instance already exists. Skipping spawn."); return; } // Keep warning

            bool spawnedNetworked = false;
            NetworkManager networkManager = (NetworkManager._instances.Count > 0) ? NetworkManager._instances[0] : null;

            // Network Spawn Attempt
            if (networkManager != null && networkManager.ServerManager.Started)
            {
                if (manorSetupPrefab.GetComponent<NetworkObject>() != null)
                {
                    // LoggerInstance.Msg($"Server active. Attempting Network Spawn for '{PrefabName}'..."); // Removed verbose
                    GameObject instanceToSpawn = null;
                    NetworkObject nobToSpawn = null;
                    try
                    {
                        instanceToSpawn = GameObject.Instantiate(manorSetupPrefab, Vector3.zero, Quaternion.identity);
                        if (instanceToSpawn != null)
                        {
                            instanceToSpawn.name = PrefabName + "_PreSpawnInstance"; // Keep internal name change
                            nobToSpawn = instanceToSpawn.GetComponent<NetworkObject>();
                            if (nobToSpawn != null)
                            {
                                ServerManager serverManager = networkManager.ServerManager;
                                serverManager.Spawn(nobToSpawn, null);
                                spawnedNetworkObject = nobToSpawn;
                                spawnedInstanceRoot = instanceToSpawn;
                                spawnedInstanceRoot.name = PrefabName + "_NetworkInstance"; // Keep internal name change
                                spawnedNetworked = true;
                                LoggerInstance.Msg($"Network Spawn successful: {spawnedInstanceRoot.name}"); // Keep success
                            }
                            else { LoggerInstance.Error("Instantiated prefab missing NetworkObject! Cannot network spawn."); GameObject.Destroy(instanceToSpawn); } // Keep error
                        }
                        else { LoggerInstance.Error("Instantiate returned null during network attempt!"); } // Keep error
                    }
                    catch (System.Exception e) { LoggerInstance.Error($"Exception during Network Spawn attempt: {e}"); if (instanceToSpawn != null) GameObject.Destroy(instanceToSpawn); spawnedNetworked = false; spawnedInstanceRoot = null; spawnedNetworkObject = null; } // Keep error
                }
                else { LoggerInstance.Warning($"Prefab '{PrefabName}' missing NetworkObject. Falling back to local spawn."); } // Keep warning
            }
            else { LoggerInstance.Msg("Server not active or NetworkManager not found. Using local spawn."); } // Keep info

            // Local Spawn (Fallback)
            if (!spawnedNetworked)
            {
                // LoggerInstance.Msg($"Attempting Local Instantiate for '{PrefabName}'..."); // Removed verbose
                try
                {
                    spawnedInstanceRoot = GameObject.Instantiate(manorSetupPrefab, Vector3.zero, Quaternion.identity);
                    if (spawnedInstanceRoot != null)
                    {
                        spawnedInstanceRoot.name = PrefabName + "_LocalInstance"; // Keep internal name change
                        LoggerInstance.Msg($"Local Instantiate successful: {spawnedInstanceRoot.name}"); // Keep success
                    }
                    else { LoggerInstance.Error("Local Instantiate returned null!"); return; } // Keep error
                }
                catch (System.Exception e) { LoggerInstance.Error($"Exception during Local Instantiate: {e}"); spawnedInstanceRoot = null; return; } // Keep error
            }

            // Parenting and Configuration
            if (spawnedInstanceRoot != null)
            {
                Property manorProperty = FindManor();
                if (manorProperty == null) { LoggerInstance.Error("Manor property not found post-spawn. Cannot configure."); } // Keep error
                else
                {
                    // LoggerInstance.Msg($"Found Manor property '{manorProperty.PropertyName}'. Parenting '{spawnedInstanceRoot.name}'..."); // Removed verbose parenting log
                    try
                    {
                        // Use worldPositionStays = true as last requested, KEEP THIS LOG to confirm behavior
                        spawnedInstanceRoot.transform.SetParent(manorProperty.transform, true);
                        LoggerInstance.Msg($"Parented '{spawnedInstanceRoot.name}' to Manor (worldPositionStays=true).");
                        // LoggerInstance.Msg($"   New Local Position: {spawnedInstanceRoot.transform.localPosition}"); // Maybe remove detailed pos
                        // LoggerInstance.Msg($"   New World Position: {spawnedInstanceRoot.transform.position}"); // Maybe remove detailed pos
                    }
                    catch (System.Exception e) { LoggerInstance.Error($"Exception during SetParent: {e}"); return; } // Keep error

                    // Call Configuration Helper
                    // LoggerInstance.Msg("Calling ManorSetupHelper configuration..."); // Removed verbose
                    ManorSetupHelper.ConfigureManorSetup(spawnedInstanceRoot, manorProperty); // Keep errors from helper
                    // LoggerInstance.Msg("ManorSetupHelper configuration called."); // Removed verbose
                }
            }
            else { LoggerInstance.Error("Spawned instance root is null after spawn attempts. Cannot configure."); } // Keep error
        }

        Property FindManor()
        {
            if (Il2CppScheduleOne.Property.PropertyManager.Instance == null) { /* LoggerInstance.Error("FindManor: PropertyManager instance not found!"); */ return null; } // Silent fail maybe? Or keep error? Let's keep for now.
            Property prop = Il2CppScheduleOne.Property.PropertyManager.Instance.GetProperty(ManorPropertyCode);
            if (prop == null) { /* LoggerInstance.Error($"FindManor: Could not find Property with code '{ManorPropertyCode}'."); */ } // Silent fail maybe? Let's keep for now.
            // else { LoggerInstance.Msg($"FindManor: Found property '{prop.PropertyName}'."); } // Remove success log
            return prop;
        }

        // Keep Static version if needed by Harmony
        public static Property FindManor_Static()
        {
             // Keep logs here as they are static context
             if (Il2CppScheduleOne.Property.PropertyManager.Instance == null) { MelonLoader.MelonLogger.Error("StaticFindManor: PropertyManager instance not found!"); return null; }
             Property prop = Il2CppScheduleOne.Property.PropertyManager.Instance.GetProperty("manor");
             if (prop == null) MelonLoader.MelonLogger.Error($"StaticFindManor: Could not find Property with code 'manor'.");
             return prop;
        }

        void CleanUp()
        {
             // Keep cleanup logs as they indicate resource release
            if (spawnedNetworkObject != null)
            {
                LoggerInstance.Msg($"Cleaning up NETWORKED instance (ObjectId: {spawnedNetworkObject.ObjectId}).");
                NetworkManager networkManager = (NetworkManager._instances.Count > 0) ? NetworkManager._instances[0] : null;
                if (networkManager != null && networkManager.ServerManager.Started)
                {
                    try
                    {
                        networkManager.ServerManager.Despawn(spawnedNetworkObject.gameObject, new(DespawnType.Destroy));
                        LoggerInstance.Msg("Network Despawn requested.");
                    }
                    catch (System.Exception e) { LoggerInstance.Error($"Exception during Network Despawn: {e}"); if (spawnedInstanceRoot != null) GameObject.Destroy(spawnedInstanceRoot); }
                }
                else
                {
                    LoggerInstance.Warning("Server not active/found. Destroying local network object instance.");
                    if (spawnedInstanceRoot != null) GameObject.Destroy(spawnedInstanceRoot);
                }
            }
            else if (spawnedInstanceRoot != null)
            {
                LoggerInstance.Msg($"Cleaning up LOCAL instance '{spawnedInstanceRoot.name}'.");
                GameObject.Destroy(spawnedInstanceRoot);
            }
            spawnedInstanceRoot = null;
            spawnedNetworkObject = null;
            dialogueModified = false; // Reset dialogue flag if used
        }

        // Keep F-Key Teleport for debug builds / personal use
        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F7))
            {
                 // LoggerInstance.Msg("F7 pressed. Teleporting..."); // Remove verbose
                 TeleportToGasMartWestTruck(); // Keep errors from this
            }
        }

        private void TeleportToGasMartWestTruck()
        {
            LandVehicle targetVehicle = null;
            string targetVehicleName = "Dan's Hardware"; // Updated based on last known target

            try
            {
                 Il2CppArrayBase<LandVehicle> allVehicles = GameObject.FindObjectsOfType<LandVehicle>();
                 if (allVehicles == null || allVehicles.Count == 0) { /* LoggerInstance.Warning("No LandVehicles found."); */ return; } // Silent fail

                 // LoggerInstance.Msg($"Found {allVehicles.Count} vehicles. Searching for '{targetVehicleName}'..."); // Remove verbose
                 foreach (LandVehicle vehicle in allVehicles)
                 {
                     if (vehicle != null && vehicle.gameObject != null && vehicle.gameObject.name == targetVehicleName)
                     {
                         targetVehicle = vehicle;
                         break;
                     }
                 }

                 if (targetVehicle == null) { LoggerInstance.Warning($"Could not find vehicle named '{targetVehicleName}'."); return; } // Keep warning

                 Player playerInstance = GameObject.FindObjectOfType<Player>();
                 if (playerInstance == null || playerInstance.transform == null) { /* LoggerInstance.Warning("Could not find Player."); */ return; } // Silent fail

                 Vector3 teleportPos = targetVehicle.transform.position + Vector3.up * 2.0f;
                 // LoggerInstance.Msg($"Teleporting player to: {teleportPos}"); // Remove verbose
                 playerInstance.transform.position = teleportPos;
                 LoggerInstance.Msg("Teleported player via F7."); // Keep confirmation
            }
            catch (System.Exception e) { LoggerInstance.Error($"Exception during F7 teleport: {e}"); } // Keep error
        }

        // Keep dialogue modification methods/logs if that feature is still intended
        // ModifyEstateAgentChoicesDirectly()
        // FindAndLogEstateAgentEvent()

        // Remove testing methods if no longer needed
        // TeleportToTestCube()
        // FindDeepChild() // Keep if used by non-test methods
        // LogInstanceDebugInfo()
        // LogManorGateMaterials()

    } // End partial class MainMod
} // End namespace