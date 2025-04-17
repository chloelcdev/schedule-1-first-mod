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
            // LoggerInstance.Msg("SetupAfterSceneLoad: Waiting one frame...");
            yield return null;

            if (!dialogueModified)
            {
                ModifyEstateAgentChoicesDirectly(); // Ensure this line exists and is NOT commented out
                // FindAndLogEstateAgentEvent(); // Logging call, can remain commented
            }

            LoadPrefabsFromIl2CppBundle();
            SpawnAndConfigurePrefab();
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

                    // --- Load Hierarchy JSON ---
                    string hierarchyJson = null;
                    TextAsset jsonTextAsset = null;
                    if (il2cppCustomAssetsBundle != null) // Make sure bundle is loaded
                    {
                        try
                        {
                            // Load the TextAsset (adjust name if different)
                            jsonTextAsset = il2cppCustomAssetsBundle.LoadAsset<TextAsset>("PreBundleBuildHierarchy");
                            if (jsonTextAsset != null)
                            {
                                hierarchyJson = jsonTextAsset.text;
                                MelonLogger.Msg("[MainMod] Loaded PreBundleBuildHierarchy.json TextAsset from bundle.");
                            }
                            else { MelonLogger.Warning("[MainMod] Could not find 'PreBundleBuildHierarchy' TextAsset in the bundle."); }
                        }
                        catch (System.Exception e) { MelonLogger.Error($"[MainMod] Error loading TextAsset from bundle: {e.Message}"); }
                    }
                     else { MelonLogger.Error("[MainMod] Cannot load hierarchy JSON, asset bundle is null."); }
                    // --- End Load JSON ---

                    // --- Component Restoration ---
                    if (!string.IsNullOrEmpty(hierarchyJson))
                    {
                        try
                        {
                             MelonLogger.Msg("[MainMod] Attempting component restoration from JSON...");
                            ComponentRestorer.RestoreComponentsFromJSON(spawnedInstanceRoot, hierarchyJson, verboseLogging: true); // Set verbose true/false
                        }
                         catch(System.Exception e) { MelonLogger.Error($"[MainMod] Exception during ComponentRestorer execution: {e}"); }
                    }
                    // --- End Component Restoration ---

                    // ***** MOVED SHADER FIX CALL HERE *****
                    try
                    {
                         MelonLogger.Msg("Attempting recursive shader fix BEFORE helper configuration...");
                         // Ensure spawnedInstanceRoot is passed correctly if the method expects GameObject
                         URPShaderFix.FixShadersRecursive(spawnedInstanceRoot); // Pass GameObject directly
                         MelonLogger.Msg("Shader fix applied recursively to spawned instance.");
                    }
                    catch(System.Exception e)
                    {
                         MelonLogger.Error($"Exception during URPShaderFix execution: {e}");
                         // Decide if we should return or continue if shader fix fails
                         // return; 
                    }
                    // ***** END SHADER FIX CALL *****

                    // --- Decal Fix (Old Method - Now handled by ComponentRestorer) ---
                    /*
                    try
                    {
                        // URPShaderFix.FixDecalProjectorsRecursive(...); // COMMENT OUT or REMOVE this call
                    }
                     catch(System.Exception e) { // Error log }
                    */

                    // Call Configuration Helper AFTER shader and decal fixes
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
            base.OnUpdate(); // Ensure base.OnUpdate is called if necessary

            // --- F7 Teleport Debug ---
            if (Input.GetKeyDown(KeyCode.F7))
            {
                MelonLogger.Msg("F7 pressed - Attempting to teleport to Manor Listing Poster...");

                // Define the expected path *after* reparenting
                string listingPath = "/Map/Container/RE Office/Interior/Whiteboard/PropertyListing Docks Manor";
                GameObject listingObject = GameObject.Find(listingPath);

                if (listingObject != null)
                {
                    MelonLogger.Msg($"Found listing object at '{listingPath}'. Attempting to find player...");

                    // --- Find Player Transform (Prioritize Player Component) ---
                    Transform playerTransformToMove = null;
                    Player playerInstance = GameObject.FindObjectOfType<Player>(); // Using Il2CppScheduleOne.PlayerScripts.Player

                    if (playerInstance != null)
                    {
                        playerTransformToMove = playerInstance.transform;
                        MelonLogger.Msg($"Using Player component's transform ('{playerInstance.gameObject.name}') for teleport.");
                    }
                    // --- End Find Player Transform ---

                    if (playerTransformToMove != null)
                    {
                        Vector3 targetPosition = listingObject.transform.position;
                        MelonLogger.Msg($"Teleporting player object '{playerTransformToMove.name}' from {playerTransformToMove.position} to listing position {targetPosition}");

                        // --- Attempt Teleport --- 
                        // Directly setting position on the Player's root transform is more likely to work
                        // than setting it on the camera if it's a child.
                        playerTransformToMove.position = targetPosition;
                        MelonLogger.Msg($"Player position set. Verify in-game movement.");
                        
                        // If this STILL doesn't work, the CharacterController might need temporary disabling or specific API calls.
                        // Example (Conceptual - Needs CharacterController check):
                        // CharacterController controller = playerTransformToMove.GetComponent<CharacterController>();
                        // if (controller != null) {
                        //     MelonLogger.Msg("Found CharacterController, attempting disable/enable method...");
                        //     controller.enabled = false;
                        //     playerTransformToMove.position = targetPosition;
                        //     controller.enabled = true; 
                        // }
                    }
                    else
                    {
                        MelonLogger.Warning("Could not find Player/Camera transform to teleport after all checks.");
                    }
                }
                else
                {
                    MelonLogger.Warning($"Could not find listing object at path '{listingPath}'. Was it reparented correctly? Is the name exact?");
                }
            }
            // --- End F7 Teleport Debug ---
        } // End OnUpdate

        // Keep dialogue modification methods/logs if that feature is still intended
        //ModifyEstateAgentChoicesDirectly();
        // FindAndLogEstateAgentEvent()

        // Remove testing methods if no longer needed
        // TeleportToTestCube()
        // FindDeepChild() // Keep if used by non-test methods
        // LogInstanceDebugInfo()
        // LogManorGateMaterials()

        private void ModifyEstateAgentChoicesDirectly()
        {
            // Prevent running multiple times if already successful
            if (dialogueModified)
            {
                LoggerInstance.Msg("ModifyEstateAgentChoicesDirectly: Skipping, dialogue already modified.");
                return;
            }

            const string TargetDialogueContainerName = "EstateAgent_Sell";
            const string PropertyChoiceNodeGuid = "8e2ef594-96d9-43f2-8cfa-6efaea823a56"; // GUID of the node presenting property choices

            LoggerInstance.Msg($"Attempting to modify Ray's '{TargetDialogueContainerName}' dialogue event choices...");

            // --- 1. Find Ray (NPC or Component) ---
            Component searchTargetComponent = null;
            Ray rayInstance = GameObject.FindObjectOfType<Ray>(); // Try finding the specific Ray component first
            if (rayInstance != null)
            {
                searchTargetComponent = rayInstance;
                LoggerInstance.Msg($"ModifyEstateAgentChoicesDirectly: Found specific Ray component instance.");
            }
            else
            {
                // Fallback: Find NPC GameObject named "Ray"
                LoggerInstance.Warning($"ModifyEstateAgentChoicesDirectly: Could not find specific Ray component. Falling back to finding NPC GameObject named 'Ray'.");
                NPC[] allNpcs = GameObject.FindObjectsOfType<NPC>();
                NPC rayNpc = null;
                foreach (var npc in allNpcs)
                {
                    if (npc != null && npc.gameObject != null && npc.gameObject.name.Equals("Ray", System.StringComparison.OrdinalIgnoreCase))
                    {
                        rayNpc = npc;
                        break;
                    }
                }

                if (rayNpc != null)
                {
                    searchTargetComponent = rayNpc;
                    LoggerInstance.Msg($"ModifyEstateAgentChoicesDirectly: Found NPC GameObject named 'Ray'.");
                }
                else
                {
                    LoggerInstance.Error("ModifyEstateAgentChoicesDirectly: Could not find Ray NPC instance via specific component or GameObject name. Aborting choice modification.");
                    return; // Cannot proceed without Ray
                }
            }
            // --- End Find Ray ---

            // --- 2. Find the Specific Dialogue Event Component on Ray ---
            NPCEvent_LocationDialogue targetEventComponent = null;
            // Search children of the found Ray component/GameObject
            var locationDialogueEvents = searchTargetComponent.GetComponentsInChildren<Il2CppScheduleOne.NPCs.Schedules.NPCEvent_LocationDialogue>(true); // Include inactive

            if (locationDialogueEvents == null || locationDialogueEvents.Length == 0)
            {
                LoggerInstance.Warning($"ModifyEstateAgentChoicesDirectly: No NPCEvent_LocationDialogue components found on Ray ('{searchTargetComponent.gameObject.name}') or his children.");
                return;
            }

            LoggerInstance.Msg($"ModifyEstateAgentChoicesDirectly: Found {locationDialogueEvents.Length} NPCEvent_LocationDialogue components on '{searchTargetComponent.gameObject.name}'. Checking each...");
            foreach (var eventComponent in locationDialogueEvents)
            {
                // Check if the event component and its DialogueOverride are valid, and if the override name matches
                if (eventComponent != null && eventComponent.DialogueOverride != null && eventComponent.DialogueOverride.name == TargetDialogueContainerName)
                {
                    targetEventComponent = eventComponent;
                    LoggerInstance.Msg($"ModifyEstateAgentChoicesDirectly: Found target event component on '{eventComponent.gameObject.name}' using DialogueOverride '{TargetDialogueContainerName}'.");
                    break; // Found the correct event
                }
                // else if (eventComponent != null) { // Optional: Log skipped events
                //      string overrideName = (eventComponent.DialogueOverride != null) ? eventComponent.DialogueOverride.name : "NULL";
                //      LoggerInstance.Msg($" -> Skipping event on '{eventComponent.gameObject.name}'. DialogueOverride: '{overrideName}'");
                // }
            }

            if (targetEventComponent == null)
            {
                LoggerInstance.Error($"ModifyEstateAgentChoicesDirectly: Could not find the specific NPCEvent_LocationDialogue using '{TargetDialogueContainerName}' on Ray after checking all components.");
                return; // Cannot proceed without the specific event
            }
            // --- End Find Event Component ---

            // --- 3. Modify the Choices in the Specific Container Instance ---
            DialogueContainer container = targetEventComponent.DialogueOverride; // Use the container *from the specific event instance*!
            if (container == null)
            {
                LoggerInstance.Error("ModifyEstateAgentChoicesDirectly: Target event component's DialogueOverride is null! Cannot modify.");
                return;
            }

            LoggerInstance.Msg($"Modifying choices in the specific DialogueContainer instance (Name: {container.name}, ID: {container.GetInstanceID()}) used by Ray's event.");

            // Find the specific node within *this container instance* using its GUID
            DialogueNodeData choiceNode = null;
            if (container.DialogueNodeData != null)
            {
                // Iterate using index for Il2CppReferenceArray
                for (int i = 0; i < container.DialogueNodeData.Count; i++)
                {
                    var node = container.DialogueNodeData[i];
                    if (node != null && node.Guid == PropertyChoiceNodeGuid)
                    {
                        choiceNode = node;
                        break;
                    }
                }
            }

            if (choiceNode == null)
            {
                LoggerInstance.Error($"Could not find the Property Choice node (GUID: {PropertyChoiceNodeGuid}) within the event's specific container instance ('{container.name}').");
                return;
            }
            LoggerInstance.Msg($"Found Property Choice node: '{choiceNode.DialogueNodeLabel}' (GUID: {choiceNode.Guid}) within the event's container.");

            // Check if ALREADY modified (on *this* instance) to prevent adding duplicate "manor" choices
            bool alreadyHasManor = false;
            if (choiceNode.choices != null) // Check if choices array exists
            {
                for (int i = 0; i < choiceNode.choices.Count; i++)
                {
                    var choice = choiceNode.choices[i];
                    // Check choice isn't null and label matches "manor"
                    if (choice != null && choice.ChoiceLabel == "manor")
                    {
                        alreadyHasManor = true;
                        break;
                    }
                }
            }

            if (alreadyHasManor)
            {
                LoggerInstance.Msg("Dialogue choices on this specific event container seem to already include 'manor'. Skipping modification.");
                dialogueModified = true; // Set flag even if skipped, assumes it's correctly modified
                return;
            }
            else
            {
                LoggerInstance.Msg($"Choice node '{choiceNode.DialogueNodeLabel}' does not currently contain 'manor' choice. Proceeding with modification.");
            }

            // --- 4. Create the New, Complete List of Choices ---
            var newChoicesList = new List<Il2CppScheduleOne.Dialogue.DialogueChoiceData>();

            // Add existing Bungalow choice data
            newChoicesList.Add(new DialogueChoiceData { Guid = "f3a15b13-14d1-420a-ad17-d731155701d8", ChoiceText = "The Bungalow (<PRICE>)", ChoiceLabel = "bungalow" });
            // Add existing Barn choice data
            newChoicesList.Add(new DialogueChoiceData { Guid = "b668ec37-4c92-4949-a113-62d1effdb3b2", ChoiceText = "The Barn (<PRICE>)", ChoiceLabel = "barn" });
            // Add existing Docks Warehouse choice data
            newChoicesList.Add(new DialogueChoiceData { Guid = "ad2170b0-1789-4a61-94fb-6ffc2b720252", ChoiceText = "The Docks Warehouse (<PRICE>)", ChoiceLabel = "dockswarehouse" });
            // Add NEW Manor choice data (Generate a new GUID for it)
            newChoicesList.Add(new DialogueChoiceData { Guid = System.Guid.NewGuid().ToString(), ChoiceText = "Hilltop Manor (<PRICE>)", ChoiceLabel = "manor" });
            // Add existing Nevermind choice data
            newChoicesList.Add(new DialogueChoiceData { Guid = "7406b61c-cb6e-418f-ba2b-bfb28f1b0b70", ChoiceText = "Nevermind", ChoiceLabel = "" });
            // --- Finished creating new list ---

            // --- 5. Assign the new list back to the node in *this specific container* ---
            try
            {
                // Convert standard List<T> to Il2CppReferenceArray<T> for assignment
                choiceNode.choices = new Il2CppReferenceArray<DialogueChoiceData>(newChoicesList.ToArray());

                // Verification Log: Check the count and maybe the last item's label right after assignment
                if (choiceNode.choices != null && choiceNode.choices.Count == 5) // Should now have 5 choices
                {
                    string manorLabel = (choiceNode.choices[3] != null) ? choiceNode.choices[3].ChoiceLabel : "NULL_CHOICE"; // Manor is 4th item (index 3)
                    string lastLabel = (choiceNode.choices[4] != null) ? choiceNode.choices[4].ChoiceLabel : "NULL_CHOICE"; // Nevermind is 5th item (index 4)
                    LoggerInstance.Msg($"Successfully OVERWROTE choices for node '{choiceNode.DialogueNodeLabel}' within the event's specific container. New count: {choiceNode.choices.Count}. Manor Label: '{manorLabel}', Last Label: '{lastLabel}'.");
                    dialogueModified = true; // Set flag on success
                }
                else
                {
                    int currentCount = (choiceNode.choices != null) ? choiceNode.choices.Count : -1; // -1 indicates null array
                    LoggerInstance.Error($"Assignment failed or resulted in unexpected count! Current count: {currentCount}. Expected 5.");
                }
            }
            catch (System.Exception e)
            {
                LoggerInstance.Error($"Error assigning new choices array to the specific event container: {e}");
                // Potentially reset dialogueModified flag if error handling requires retry logic?
                // dialogueModified = false;
            }
        } // End ModifyEstateAgentChoicesDirectly

    } // End partial class MainMod
} // End namespace