using MelonLoader;
using UnityEngine;
using UnityEngine.AI;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays; // For Il2CppReferenceArray<>
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Dialogue; // For DialogueContainer, DialogueNodeData, DialogueChoiceData
using Il2CppScheduleOne.NPCs; // For NPC base class and potentially Ray
using Il2CppScheduleOne.NPCs.Schedules; // For NPCEvent_LocationDialogue
using Unity.AI.Navigation;
using System.Collections; // Required for IEnumerator

// === FishNet using Statements ===
// You MUST add a reference to Il2CppFishNet.Runtime.dll in Visual Studio
using Il2CppFishNet.Object;        // For NetworkObject
using Il2CppFishNet.Managing;      // For NetworkManager, InstanceFinder
using Il2CppFishNet.Managing.Server; // For ServerManager
// using FishNet.Transporting; // Might be needed for Channel enum? Not used directly here.
// === End FishNet using ===


using Ray = Il2CppScheduleOne.NPCs.CharacterClasses.Ray; // Assuming Ray is a subclass of NPC

[assembly: MelonInfo(typeof(ChloesManorMod.MainMod), BuildInfo.Name, BuildInfo.Version, BuildInfo.Author, BuildInfo.DownloadLink)]
[assembly: MelonColor()]
[assembly: MelonGame("TVGS", "Schedule I")]

public static class BuildInfo
{
    public const string Name = "Manor Mod";
    public const string Description = "Finishes the missing touches to make the Manor fully functional/playable.";
    public const string Author = "Chloe";
    public const string Company = null;
    public const string Version = "0.8.4";
    public const string DownloadLink = null;
}

namespace ChloesManorMod
{ //23,880
    public partial class MainMod : MelonMod
    {

        private static Il2CppAssetBundle il2cppCustomAssetsBundle;
        private static GameObject manorSetupPrefab;

        // List of objects to disable before ManorSetup is instantiated
        private static readonly List<string> objectsToDisableBeforeSetup = new List<string>
        {
            // replace mansion door
            "@Properties/Manor/House/Door Frames/Mansion Door Frame",
            "@Properties/Manor/House/MansionDoor",
            "@Properties/Manor/House/mansion/DoorFrame",

            // break down front wallfor testing
            /*"@Properties/Manor/House/mansion/Downstairs Exterior Walls",
            "@Properties/Manor/House/mansion/Downstairs Interior Walls",
            "@Properties/Manor/House/mansion/Skirt.002",
            "@Properties/Manor/House/mansion/Skirt.003",
            "@Properties/Manor/House/Room (2)/ModularSwitch (2)",

            "@Properties/Manor/House/WallLantern (1)",
            "@Properties/Manor/House/WallLantern",
            "@Properties/Manor/House/Colliders/ExteriorDoorFrameCollider",*/


        };

        private const string BundleName = "chloemanorsetup";
        private const string PrefabName = "ManorSetup-Chloe";
        private const string TargetSceneName = "Main";
        private const string ManorPropertyCode = "manor";

        private static GameObject spawnedInstanceRoot = null;
        private static NetworkObject spawnedNetworkObject = null;

        private const string RootObjectName = "ManorSetup-Chloe";

        private static bool dialogueModified = false;

        // --- Teleporter Manager Fields ---
        private List<(Transform source, Transform target)> teleporterPairs = new List<(Transform, Transform)>();
        private Dictionary<GameObject, (Transform sourceZone, float entryTime)> employeeEnterTimes = new Dictionary<GameObject, (Transform, float)>();
        private const float TeleporterActivationRadius = 1.4f; // Meters
        private const float TeleporterActivationRadiusSqr = TeleporterActivationRadius * TeleporterActivationRadius; // Use squared distance for efficiency
        private const float TeleporterDwellTime = 2.0f; // Seconds
        private Property currentManorPropertyRef = null; // Cache manor property reference in OnUpdate
        // --- End Teleporter Fields ---

    public override void OnInitializeMelon()
    {
            LoggerInstance.Msg($"v{BuildInfo.Version} Initializing...");
            LoadAssetBundleViaManager();

            // Apply Harmony Patches
            try
            {
                HarmonyInstance.PatchAll(typeof(MainMod).Assembly);
                LoggerInstance.Msg($"Harmony patches applied.");
            }
            catch (System.Exception e)
            {
                LoggerInstance.Error($"Failed to apply Harmony patches: {e}");
            }
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

            // --- Disable Original Objects ---
            DisableOriginalManorObjects();

            if (!dialogueModified)
            {
                ModifyEstateAgentChoicesDirectly(); // Ensure this line exists and is NOT commented out
                // FindAndLogEstateAgentEvent(); // Logging call, can remain commented
            }

            LoadPrefabsFromIl2CppBundle();
            SpawnAndConfigurePrefab();

            // --- Debug: Check for NavMeshLink after spawning ---
            MelonLogger.Msg($"--- Checking for NavMeshLink on DoorLinkAttempt ---");
            // Find the child object directly using transform.Find
            GameObject doorLinkGO = null;
            string relativePath = "AtTheProperty/Extra Navigation/DoorLinkAttempt";
            if (spawnedInstanceRoot != null)
            {
                Transform doorLinkTransform = spawnedInstanceRoot.transform.Find(relativePath);
                if (doorLinkTransform != null)
                {
                    doorLinkGO = doorLinkTransform.gameObject;
                }
            }

            if (doorLinkGO != null)
            {
                MelonLogger.Msg($"Found GameObject: '{doorLinkGO.name}' using relative path: '{relativePath}'");
                NavMeshLink navLink = doorLinkGO.GetComponent<NavMeshLink>();
                if (navLink != null)
                {
                    MelonLogger.Msg($"-> Found NavMeshLink component!");
                    // Optional: Log specific properties if needed
                    // MelonLogger.Msg($"     - StartPoint: {navLink.m_StartPoint}");
                    // MelonLogger.Msg($"     - EndPoint: {navLink.m_EndPoint}");
                    // MelonLogger.Msg($"     - AgentTypeID: {navLink.m_AgentTypeID}");
                }
                else
                {
                     MelonLogger.Warning($"-> NavMeshLink component NOT FOUND on this GameObject!");
                     MelonLogger.Warning($"-> Printing components!");
                     foreach (Component component in doorLinkGO.GetComponents<Component>())
                     {
                        MelonLogger.Warning($"-> {component.name}: {component.GetType().Name}");
                     }
                }
            }
            else
            {
                MelonLogger.Error($"Could not find GameObject for NavMeshLink debug using relative path: '{relativePath}' within '{spawnedInstanceRoot?.name ?? "NULL"}'");
            }
            MelonLogger.Msg($"--- Finished NavMeshLink Check ---");
            // --- End Debug ---
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

                    if (il2cppCustomAssetsBundle == null) { LoggerInstance.Error($"Il2CppAssetBundleManager.LoadFromMemory failed or returned null!"); }
                    else { LoggerInstance.Msg($"AssetBundle '{BundleName}' loaded successfully."); }
                }
            }
            catch (System.MissingMethodException mmex) { LoggerInstance.Error($"Il2CppAssetBundleManager.LoadFromMemory method not found! {mmex.Message}"); il2cppCustomAssetsBundle = null; }
            catch (System.Exception e) { LoggerInstance.Error($"Exception loading AssetBundle: {e}"); il2cppCustomAssetsBundle = null; }
        }

        private void LoadPrefabsFromIl2CppBundle()
        {
            if (il2cppCustomAssetsBundle == null) { LoggerInstance.Error($"Cannot load prefab, AssetBundle is not loaded."); return; }
            if (manorSetupPrefab != null) { return; }

            // LoggerInstance.Msg($"Loading prefab '{PrefabName}' using Il2CppAssetBundle wrapper..."); // Removed verbose
            try
            {
                UnityEngine.Object loadedAsset = il2cppCustomAssetsBundle.LoadAsset<GameObject>(PrefabName);
                if (loadedAsset == null) { LoggerInstance.Error($"Failed to load '{PrefabName}' prefab!"); }
                else
                {
                    manorSetupPrefab = loadedAsset.TryCast<GameObject>();
                    if (manorSetupPrefab != null) LoggerInstance.Msg($"Prefab '{PrefabName}' loaded successfully.");
                    else LoggerInstance.Error($"Loaded asset '{PrefabName}' failed to cast to GameObject!");
                }
            }
            catch (System.Exception e) { LoggerInstance.Error($"Error loading prefab: {e}"); manorSetupPrefab = null; }
        }

        private void SpawnAndConfigurePrefab()
        {
            if (manorSetupPrefab == null) { LoggerInstance.Error($"Manor setup prefab not loaded. Cannot spawn."); return; }
            if (spawnedInstanceRoot != null) { LoggerInstance.Warning($"Manor setup instance already exists. Skipping spawn."); return; }

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
                            instanceToSpawn.name = PrefabName + "_PreSpawnInstance";
                            nobToSpawn = instanceToSpawn.GetComponent<NetworkObject>();
                            if (nobToSpawn != null)
                            {
                                ServerManager serverManager = networkManager.ServerManager;
                                serverManager.Spawn(nobToSpawn, null);
                                spawnedNetworkObject = nobToSpawn;
                                spawnedInstanceRoot = instanceToSpawn;
                                spawnedInstanceRoot.name = PrefabName + "_NetworkInstance";
                                spawnedNetworked = true;
                                LoggerInstance.Msg($"Network Spawn successful: {spawnedInstanceRoot.name}");
                            }
                            else { LoggerInstance.Error($"Instantiated prefab missing NetworkObject! Cannot network spawn."); GameObject.Destroy(instanceToSpawn); }
                        }
                        else { LoggerInstance.Error($"Instantiate returned null during network attempt!"); }
                    }
                    catch (System.Exception e) { LoggerInstance.Error($"Exception during Network Spawn attempt: {e}"); if (instanceToSpawn != null) GameObject.Destroy(instanceToSpawn); spawnedNetworked = false; spawnedInstanceRoot = null; spawnedNetworkObject = null; }
                }
                else { LoggerInstance.Warning($"Prefab '{PrefabName}' missing NetworkObject. Falling back to local spawn."); }
            }
            else { LoggerInstance.Msg($"Server not active or NetworkManager not found. Using local spawn."); }

            // Local Spawn (Fallback)
            if (!spawnedNetworked)
            {
                // LoggerInstance.Msg($"Attempting Local Instantiate for '{PrefabName}'..."); // Removed verbose
                try
                {
                    spawnedInstanceRoot = GameObject.Instantiate(manorSetupPrefab, Vector3.zero, Quaternion.identity);
                    if (spawnedInstanceRoot != null)
                    {
                        spawnedInstanceRoot.name = PrefabName + "_LocalInstance";
                        LoggerInstance.Msg($"Local Instantiate successful: {spawnedInstanceRoot.name}");
                    }
                    else { LoggerInstance.Error($"Local Instantiate returned null!"); return; }
                }
                catch (System.Exception e) { LoggerInstance.Error($"Exception during Local Instantiate: {e}"); spawnedInstanceRoot = null; return; }
            }

            // --- Ensure prefab is active before component restoration/setup ---
            if (spawnedInstanceRoot == null) return;
            spawnedInstanceRoot.SetActive(true);

            // --- ADDED: Initialize Teleporter Pairs (replaces SetupTeleporterComponents) ---
            InitializeTeleporterPairs(spawnedInstanceRoot);

            // --- Restore Components (If using JSON method) ---
            // ComponentRestorer.RestoreComponentsFromJSON(spawnedInstanceRoot, "path/to/your/component_data.json");

            // --- Final Setup/Logging ---
            // LoggerInstance.Msg($"Manor setup prefab '{spawnedInstanceRoot.name}' instantiated and configured."); // Can be noisy

            // --- Parenting and Configuration ---
            if (spawnedInstanceRoot != null)
            {
                // --- Load Hierarchy JSON ---
                string hierarchyJson = null;
                TextAsset jsonTextAsset = null;
                if (il2cppCustomAssetsBundle != null)
                {
                    try
                    {
                        jsonTextAsset = il2cppCustomAssetsBundle.LoadAsset<TextAsset>("PreBundleBuildHierarchy");
                        if (jsonTextAsset != null)
                        {
                            hierarchyJson = jsonTextAsset.text;
                            MelonLogger.Msg($"Loaded PreBundleBuildHierarchy.json TextAsset from bundle.");
                        }
                        else { MelonLogger.Warning($"Could not find 'PreBundleBuildHierarchy' TextAsset in the bundle."); }
                    }
                    catch (System.Exception e) { MelonLogger.Error($"Error loading TextAsset from bundle: {e.Message}"); }
                }
                 else { MelonLogger.Error($"Cannot load hierarchy JSON, asset bundle is null."); }
                // --- End Load JSON ---

                // --- Component Restoration ---
                if (!string.IsNullOrEmpty(hierarchyJson))
                {
                    MelonLogger.Msg($"Attempting component restoration from JSON BEFORE parenting...");
                    // Pass the root transform directly, path lookup starts from here
                    ComponentRestorer.RestoreComponentsFromJSON(spawnedInstanceRoot, hierarchyJson, verboseLogging: true);
                }
                // --- End Component Restoration ---

                // Now find the parent AFTER restoration
                Property manorProperty = FindManor();
                if (manorProperty == null) {
                    MelonLogger.Error($"Manor property not found post-spawn/restoration. Cannot parent or configure further.");
                    // Consider destroying spawnedInstanceRoot if parenting fails?
                    // UnityEngine.Object.Destroy(spawnedInstanceRoot);
                    // spawnedInstanceRoot = null;
                 }
                else
                {
                    // --- Parenting ---
                    try
                    {
                       spawnedInstanceRoot.transform.SetParent(manorProperty.transform, true);
                       MelonLogger.Msg($"Parented '{spawnedInstanceRoot.name}' to Manor (worldPositionStays=true) AFTER component restoration.");
        }
        catch (System.Exception e) {
                        MelonLogger.Error($"Exception during SetParent: {e}"); 
                        // Consider destroying instance if parenting fails
                        return; 
                    }
                    // --- End Parenting ---
                    
                    // --- Shader Fix --- 
                    try
                    {
                         MelonLogger.Msg($"Attempting recursive shader fix BEFORE helper configuration...");
                         URPShaderFix.FixShadersRecursive(spawnedInstanceRoot);
                         MelonLogger.Msg($"Shader fix applied recursively to spawned instance.");
                    }
                    catch(System.Exception e)
                    {
                         MelonLogger.Error($"Exception during URPShaderFix execution: {e}");
                         // return; 
                    }
                    // --- END SHADER FIX CALL ---

                    // --- Final Configuration Helper ---
                    // LoggerInstance.Msg("Calling ManorSetupHelper configuration..."); 
                    ManorSetupHelper.ConfigureManorSetup(spawnedInstanceRoot, manorProperty); 
                    // LoggerInstance.Msg("ManorSetupHelper configuration called."); 
                }
            }
            else { MelonLogger.Error($"Spawned instance root is null after spawn attempts. Cannot configure."); } 
        }

        Property FindManor()
        {
            if (Il2CppScheduleOne.Property.PropertyManager.Instance == null) { /* LoggerInstance.Error("FindManor: PropertyManager instance not found!"); */ return null; }
            Property prop = Il2CppScheduleOne.Property.PropertyManager.Instance.GetProperty(ManorPropertyCode);
            if (prop == null) { /* LoggerInstance.Error($"FindManor: Could not find Property with code '{ManorPropertyCode}'."); */ }
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
            dialogueModified = false;

            // --- Clear Teleporter Data ---
            teleporterPairs.Clear();
            employeeEnterTimes.Clear();
            currentManorPropertyRef = null;
            // --- End Clear ---
        }

        // Keep F-Key Teleport for debug builds / personal use
        public override void OnUpdate()
        {
            base.OnUpdate(); // Ensure base.OnUpdate is called if necessary

            if (Input.GetKeyDown(KeyCode.F7))
            {
                OnDebugKey();
            }

            // --- Centralized Teleporter Logic --- 
            ProcessTeleporters();
            // --- End Teleporter Logic ---
        }
        
        void OnDebugKey()
        {
            MelonLogger.Msg("F7 ManorMod debug key pressed.");
            // run some debug handler here
        }

        private void ModifyEstateAgentChoicesDirectly()
        {
            // Prevent running multiple times if already successful
            if (dialogueModified)
            {
                LoggerInstance.Msg($"ModifyEstateAgentChoicesDirectly: Skipping, dialogue already modified.");
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
                    LoggerInstance.Error($"ModifyEstateAgentChoicesDirectly: Could not find Ray NPC instance via specific component or GameObject name. Aborting choice modification.");
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
                LoggerInstance.Error($"ModifyEstateAgentChoicesDirectly: Target event component's DialogueOverride is null! Cannot modify.");
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
                LoggerInstance.Error($"ModifyEstateAgentChoicesDirectly: Could not find the Property Choice node (GUID: {PropertyChoiceNodeGuid}) within the event's specific container instance ('{container.name}').");
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
                LoggerInstance.Msg($"Dialogue choices on this specific event container seem to already include 'manor'. Skipping modification.");
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

        // --- Method to disable specific GameObjects by path --- 
        private void DisableOriginalManorObjects()
        {
            MelonLogger.Msg($"--- Disabling Original Manor Objects ---");
            int disabledCount = 0;
            foreach (string path in objectsToDisableBeforeSetup)
            {
                GameObject objToDisable = GameObject.Find(path);
                if (objToDisable != null)
                {   
                    MelonLogger.Msg($"    - Found and disabling: '{path}'");
                    objToDisable.SetActive(false);
                    disabledCount++;
                }
                else 
                { 
                     MelonLogger.Warning($"    - Could not find object to disable at path: '{path}'");
                }
            }
            MelonLogger.Msg($"--- Finished Disabling Objects ({disabledCount} disabled) ---");
        }

        // --- Renamed/Modified Helper: Finds teleporter pairs and stores them ---
        private void InitializeTeleporterPairs(GameObject root)
        {
            if (root == null)
            {
                LoggerInstance.Error($"InitializeTeleporterPairs: Cannot initialize teleporters, root object is null.");
                return;
            }

            MelonLogger.Msg($"--- Initializing Teleporter Pairs ---");
            teleporterPairs.Clear(); // Clear any old pairs first

            // Define the relative paths from the root to the teleporter GameObjects
            // IMPORTANT: Adjust these paths if the hierarchy in your prefab is different!
            string[] teleporterPaths = {
                "AtTheProperty/Extra Navigation/TempTeleporter1",
                "AtTheProperty/Extra Navigation/TempTeleporter2",
                "AtTheProperty/Extra Navigation/TempTeleporter3",
                "AtTheProperty/Extra Navigation/TempTeleporter4"
            };

            foreach (string path in teleporterPaths)
            {
                Transform teleporterGroupTransform = root.transform.Find(path);
                if (teleporterGroupTransform != null)
                {
                    // Find the child position transforms WITHIN the group
                    Transform pos1Transform = teleporterGroupTransform.Find("Pos1");
                    Transform pos2Transform = teleporterGroupTransform.Find("Pos2");

                    if (pos1Transform != null && pos2Transform != null)
                    {
                        // Store the pair
                        teleporterPairs.Add((pos1Transform, pos2Transform));
                        MelonLogger.Msg($"  Added teleporter pair: Source='{pos1Transform.name}' -> Target='{pos2Transform.name}' (Parent Group: {path})");
                    }
                    else
                    {
                        MelonLogger.Warning($"  Could not find 'Pos1' ({pos1Transform == null}) or 'Pos2' ({pos2Transform == null}) child Transforms under group '{path}'. Skipping this pair.");
                    }
                }
                else
                {
                    MelonLogger.Warning($"  Could not find teleporter group Transform at path: '{path}'. Skipping.");
                }
            }
            MelonLogger.Msg($"--- Finished Initializing Teleporter Pairs. Found {teleporterPairs.Count} pairs. ---");
        }

        // --- NEW: Centralized Teleporter Processing Logic ---
        private void ProcessTeleporters()
        {
            // Only process if teleporters have been initialized
            if (teleporterPairs.Count == 0) return;

            // Update manor property reference if null
            if (currentManorPropertyRef == null)
                currentManorPropertyRef = FindManor();

            // Early exit if manor or employees list is invalid
            if (currentManorPropertyRef == null || currentManorPropertyRef.Employees == null) return;

            // Get a stable reference to the employees list for this frame
            var employees = currentManorPropertyRef.Employees;
            if (employees.Count == 0) return; // No employees to check

            // Create a list of employees to untrack if they are no longer valid or in any zone
            List<GameObject> employeesToRemove = null; // Lazy initialization

            // --- Check currently tracked employees first for exits or warps ---
            foreach (var kvp in employeeEnterTimes)
            {
                GameObject employeeGO = kvp.Key;
                Transform trackedSourceZone = kvp.Value.sourceZone;
                float entryTime = kvp.Value.entryTime;

                // Validate employee still exists and is active
                if (employeeGO == null || !employeeGO.activeInHierarchy)
                {
                    if (employeesToRemove == null) employeesToRemove = new List<GameObject>();
                    employeesToRemove.Add(employeeGO);
                    continue;
                }

                // Check distance to the specific zone they were tracked entering
                float sqrDistToTrackedZone = (employeeGO.transform.position - trackedSourceZone.position).sqrMagnitude;

                if (sqrDistToTrackedZone >= TeleporterActivationRadiusSqr)
                {
                    // Exited the zone they were in
                    if (employeesToRemove == null) employeesToRemove = new List<GameObject>();
                    employeesToRemove.Add(employeeGO);
                    // MelonLogger.Msg($"Teleporter: Employee '{employeeGO.name}' exited zone '{trackedSourceZone.name}'. Untracking."); // Optional Log
                }
                else
                {
                    // Still in the zone, check dwell time
                    if (Time.time - entryTime >= TeleporterDwellTime)
                    {
                        // Find the corresponding target for this source zone
                        Transform targetTransform = null;
                        foreach (var pair in teleporterPairs)
                        {
                            if (pair.source == trackedSourceZone)
                            {
                                targetTransform = pair.target;
                                break;
                            }
                        }

                        if (targetTransform != null)
                        {
                            NavMeshAgent agent = employeeGO.GetComponent<NavMeshAgent>();
                            if (agent != null)
                            {
                                // Calculate Y-Offset
                                Vector3 currentNpcPosition = employeeGO.transform.position;
                                Vector3 sourceTriggerPosition = trackedSourceZone.position;
                                Vector3 baseTargetPosition = targetTransform.position;
                                float yOffset = currentNpcPosition.y - sourceTriggerPosition.y;
                                Vector3 finalTargetPosition = new Vector3(baseTargetPosition.x, baseTargetPosition.y + yOffset, baseTargetPosition.z);

                                // MelonLogger.Msg($"Teleporter: Warping '{employeeGO.name}' from '{trackedSourceZone.name}' to '{targetTransform.name}' (Y-Offset: {yOffset:F2})."); // Optional Log
                                agent.Warp(finalTargetPosition);

                                // Remove immediately after warp
                                if (employeesToRemove == null) employeesToRemove = new List<GameObject>();
                                employeesToRemove.Add(employeeGO);
                            }
                            else
                            {
                                // MelonLogger.Warning($"Teleporter: Employee '{employeeGO.name}' in zone '{trackedSourceZone.name}' has no NavMeshAgent. Untracking."); // Optional Log
                                if (employeesToRemove == null) employeesToRemove = new List<GameObject>();
                                employeesToRemove.Add(employeeGO);
                            }
                        }
                        else
                        {
                            // Should not happen if initialized correctly, but handle defensively
                            // MelonLogger.Warning($"Teleporter: Could not find target pair for source zone '{trackedSourceZone.name}'. Untracking employee '{employeeGO.name}'."); // Optional Log
                            if (employeesToRemove == null) employeesToRemove = new List<GameObject>();
                            employeesToRemove.Add(employeeGO);
                        }
                    }
                    // Else: Still in zone, but dwell time not met - do nothing
                }
            }

            // --- Remove untracked employees ---
            if (employeesToRemove != null)
            {
                foreach (GameObject empToRemove in employeesToRemove)
                    employeeEnterTimes.Remove(empToRemove);
            }

            // --- Check for new entries --- 
            // Convert Il2Cpp List to something iterable if needed, or use index
            for (int i = 0; i < employees.Count; i++)
            {
                Il2CppScheduleOne.Employees.Employee employee = employees[i];
                if (employee == null) continue; // Skip null employee entries
                GameObject employeeGO = employee.gameObject;
                if (employeeGO == null || !employeeGO.activeInHierarchy) continue; // Skip inactive/null GO

                // Skip if already tracked (means they are currently in a zone or just warped)
                if (employeeEnterTimes.ContainsKey(employeeGO)) continue;

                // Check proximity to each teleporter source
                foreach (var pair in teleporterPairs)
                {
                    Transform source = pair.source;
                    float sqrDist = (employeeGO.transform.position - source.position).sqrMagnitude;

                    if (sqrDist < TeleporterActivationRadiusSqr)
                    {
                        // Entered this zone, start tracking
                        employeeEnterTimes.Add(employeeGO, (source, Time.time));
                        // MelonLogger.Msg($"Teleporter: Employee '{employeeGO.name}' entered zone '{source.name}'. Tracking."); // Optional Log
                        break; // Employee entered one zone, no need to check others for them this frame
                    }
                }
            }
        }
        // --- END NEW LOGIC ---
    } // End partial class MainMod
} // End namespace