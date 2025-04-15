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
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        LoggerInstance.Msg($"Scene loaded: {sceneName}");
        if (sceneName == TargetSceneName)
        {
                MelonCoroutines.Start(SetupAfterSceneLoad());
            }
            else
            {
                CleanUp();
            }
        }

        private IEnumerator SetupAfterSceneLoad()
        {
            LoggerInstance.Msg("SetupAfterSceneLoad: Waiting one frame...");
            yield return null;

            if (!dialogueModified)
            {
                ModifyEstateAgentChoicesDirectly();
            }

            LoadPrefabsFromIl2CppBundle();
            SpawnAndConfigurePrefab();
            FindAndLogEstateAgentEvent();
        }

        private void LoadAssetBundleViaManager()
        {
            if (il2cppCustomAssetsBundle != null) return;

            LoggerInstance.Msg($"Loading AssetBundle '{BundleName}' via Il2CppAssetBundleManager...");
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();

                // --- DEBUG: List all embedded resources ---
                LoggerInstance.Msg("--- Detected Embedded Resources ---");
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    LoggerInstance.Msg($"Resource: {name}");
                }
                LoggerInstance.Msg("---------------------------------");
                // --- End Debug ---

                string resourceName = $"{typeof(MainMod).Namespace}.{BundleName}";

                using (System.IO.Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        LoggerInstance.Error($"Failed to find embedded resource stream: {resourceName}");
                        return;
                    }
                    byte[] assetData = new byte[stream.Length];
                    stream.Read(assetData, 0, assetData.Length);

                    il2cppCustomAssetsBundle = Il2CppAssetBundleManager.LoadFromMemory(assetData);

                    if (il2cppCustomAssetsBundle == null)
                    {
                        LoggerInstance.Error("Il2CppAssetBundleManager.LoadFromMemory failed or returned null!");
                    }
                    else
                    {
                        LoggerInstance.Msg("AssetBundle loaded successfully via Il2CppAssetBundleManager.");
                    }
                }
            }
            catch (System.MissingMethodException mmex)
            {
                LoggerInstance.Error($"Il2CppAssetBundleManager.LoadFromMemory method not found! {mmex.Message}");
                il2cppCustomAssetsBundle = null;
            }
            catch (System.Exception e)
            {
                LoggerInstance.Error($"Exception loading AssetBundle via Manager: {e}");
                il2cppCustomAssetsBundle = null;
            }
        }

        private void LoadPrefabsFromIl2CppBundle()
        {
            if (il2cppCustomAssetsBundle == null) { LoggerInstance.Error("Cannot load prefab, Il2CppAssetBundle is not loaded."); return; }
            if (manorSetupPrefab != null) { return; }

            LoggerInstance.Msg($"Loading prefab '{PrefabName}' using Il2CppAssetBundle wrapper...");
            try
            {
                UnityEngine.Object loadedAsset = il2cppCustomAssetsBundle.LoadAsset<GameObject>(PrefabName);

                if (loadedAsset == null)
                {
                    LoggerInstance.Error($"Failed to load '{PrefabName}' prefab using Il2CppAssetBundle wrapper!");
                }
                else
                {
                    manorSetupPrefab = loadedAsset.TryCast<GameObject>();
                    if (manorSetupPrefab != null) LoggerInstance.Msg($"Prefab '{PrefabName}' loaded successfully via Il2Cpp wrapper.");
                    else LoggerInstance.Error($"Loaded asset '{PrefabName}' failed to cast to GameObject!");
                }
            }
            catch (System.Exception e) { LoggerInstance.Error($"Error loading prefab via Il2Cpp wrapper: {e}"); manorSetupPrefab = null; }
        }

        private void SpawnAndConfigurePrefab()
        {
            if (manorSetupPrefab == null) { LoggerInstance.Error("Manor setup prefab not loaded. Cannot spawn."); return; }
            if (spawnedInstanceRoot != null) { LoggerInstance.Warning("Manor setup instance already exists. Skipping spawn."); return; }

            bool spawnedNetworked = false;
            NetworkManager networkManager = (NetworkManager._instances.Count > 0) ? NetworkManager._instances[0] : null;

            if (networkManager != null && networkManager.ServerManager.Started)
            {
                if (manorSetupPrefab.GetComponent<NetworkObject>() != null)
                {
                    LoggerInstance.Msg($"Server is active. Attempting Network Spawn for '{PrefabName}'...");
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
                                LoggerInstance.Msg($"Network Spawn successful. Instance: {spawnedInstanceRoot.name}");
                            }
                            else { LoggerInstance.Error("Instantiated prefab missing NetworkObject component! Cannot network spawn."); GameObject.Destroy(instanceToSpawn); }
                        }
                        else { LoggerInstance.Error("Instantiate returned null!"); }
                    }
                    catch (System.Exception e)
                    {
                        LoggerInstance.Error($"Exception during Network Spawn attempt: {e}");
                        if (instanceToSpawn != null) GameObject.Destroy(instanceToSpawn);
                        spawnedNetworked = false;
                        spawnedInstanceRoot = null;
                        spawnedNetworkObject = null;
                    }
                }
                else
                {
                    LoggerInstance.Warning($"Prefab '{PrefabName}' missing NetworkObject component. Cannot network spawn. Falling back to local spawn.");
                }
            }
            else
            {
                if (networkManager == null) LoggerInstance.Msg("NetworkManager not found.");
                else LoggerInstance.Msg("Server is not active.");
                LoggerInstance.Msg("Falling back to local spawn.");
            }

            if (!spawnedNetworked)
            {
                LoggerInstance.Msg($"Attempting Local Instantiate for '{PrefabName}'...");
                try
                {
                    spawnedInstanceRoot = GameObject.Instantiate(manorSetupPrefab, Vector3.zero, Quaternion.identity);
                    if (spawnedInstanceRoot != null)
                    {
                        spawnedInstanceRoot.name = PrefabName + "_LocalInstance";
                        LoggerInstance.Msg($"Local Instantiate successful. Instance: {spawnedInstanceRoot.name}");
                    }
                    else
                    {
                        LoggerInstance.Error("Local Instantiate returned null!");
                        return;
                    }
                }
                catch (System.Exception e)
                {
                    LoggerInstance.Error($"Exception during Local Instantiate: {e}");
                    spawnedInstanceRoot = null;
                    return;
                }
            }

            if (spawnedInstanceRoot != null)
            {
                Property manorProperty = FindManor();
                if (manorProperty == null)
                {
                    LoggerInstance.Error("Manor property not found post-spawn. Cannot parent or configure components.");
                }
                else
                {
                    LoggerInstance.Msg($"Found Manor property '{manorProperty.PropertyName}'. Parenting '{spawnedInstanceRoot.name}' to it.");
                    try
                    {
                        spawnedInstanceRoot.transform.SetParent(manorProperty.transform, true);
                        LoggerInstance.Msg($"Parenting successful (worldPositionStays=true). New parent: {spawnedInstanceRoot.transform.parent?.name ?? "None"}");
                        LoggerInstance.Msg($"   New Local Position: {spawnedInstanceRoot.transform.localPosition}");
                        LoggerInstance.Msg($"   New World Position: {spawnedInstanceRoot.transform.position}");
                    }
                    catch (System.Exception e)
                    {
                        LoggerInstance.Error($"Exception during SetParent: {e}");
                        return;
                    }

                    LoggerInstance.Msg("Calling ManorSetupHelper configuration...");
                    ManorSetupHelper.ConfigureManorSetup(spawnedInstanceRoot, manorProperty);
                    LoggerInstance.Msg("ManorSetupHelper configuration called.");
                }
            }
            else
            {
                LoggerInstance.Error("Spawned instance root is null after attempting both network and local spawn. Cannot parent or configure.");
            }
        }

        // FindManor instance method (used by SpawnAndConfigurePrefab)
        Property FindManor()
        {
            // Ensure correct namespace for PropertyManager and Property are used
            if (Il2CppScheduleOne.Property.PropertyManager.Instance == null)
            {
                LoggerInstance.Error("FindManor: PropertyManager instance not found!");
                return null;
            }
            // Use the class constant ManorPropertyCode
            Property prop = Il2CppScheduleOne.Property.PropertyManager.Instance.GetProperty(ManorPropertyCode);
            if (prop == null)
            {
                LoggerInstance.Error($"FindManor: Could not find Property with code '{ManorPropertyCode}'.");
            }
            return prop;
        }

        // --- Keep the existing static version needed by Harmony ---
        public static Property FindManor_Static()
        {
            const string ManorPropertyCode_StaticAccess = "manor";
            if (Il2CppScheduleOne.Property.PropertyManager.Instance == null)
            {
                MelonLoader.MelonLogger.Error("StaticFindManor: PropertyManager instance not found!");
                return null;
            }
            Property prop = Il2CppScheduleOne.Property.PropertyManager.Instance.GetProperty(ManorPropertyCode_StaticAccess);
            if (prop == null) MelonLoader.MelonLogger.Error($"StaticFindManor: Could not find Property with code '{ManorPropertyCode_StaticAccess}'.");
            return prop;
        }

        void CleanUp()
        {
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
                LoggerInstance.Msg($"Cleaning up LOCAL instance.");
                GameObject.Destroy(spawnedInstanceRoot);
            }

            spawnedInstanceRoot = null;
            spawnedNetworkObject = null;
        }

        private void TeleportToTestCube(string cubeName)
        {
            if (spawnedInstanceRoot == null)
            {
                LoggerInstance.Warning($"Teleport requested ('{cubeName}') but setup instance root not found!");
                return;
            }

            Transform cubeTransform = FindDeepChild(spawnedInstanceRoot.transform, cubeName);

            if (cubeTransform == null)
            {
                LoggerInstance.Warning($"Teleport target '{cubeName}' not found within the spawned instance!");
                return;
            }

            Vector3 targetPosition = cubeTransform.position + Vector3.up * 0.5f;
            LoggerInstance.Msg($"Teleport target '{cubeName}' found at world position: {cubeTransform.position}. Target teleport position: {targetPosition}");

            // --- ADD MATERIAL/SHADER LOGGING FOR CUBE ---
            MeshRenderer cubeRenderer = cubeTransform.GetComponent<MeshRenderer>();
            if (cubeRenderer != null)
            {
                LoggerInstance.Msg($"Cube '{cubeName}' Renderer Enabled: {cubeRenderer.enabled}");
                Material cubeMat = cubeRenderer.material; // Gets the instance of the material
                if (cubeMat != null)
                {
                    string textureName = cubeMat.mainTexture != null ? cubeMat.mainTexture.name : "NULL";
                    string shaderName = cubeMat.shader != null ? cubeMat.shader.name : "NULL";
                    LoggerInstance.Msg($" -> Material: {cubeMat.name}");
                    LoggerInstance.Msg($" -> Main Texture: {textureName}");
                    LoggerInstance.Msg($" -> Shader: {shaderName}");
                }
                else
                {
                    LoggerInstance.Warning(" -> Material on Renderer is NULL!");
                }
            }
            else
            {
                LoggerInstance.Warning($" -> MeshRenderer component NOT found on '{cubeName}'!");
            }
            // --- END LOGGING ---

            Player playerInstance = null;
            try
            {
                playerInstance = GameObject.FindObjectOfType<Player>();
            }
            catch (System.Exception e)
            {
                LoggerInstance.Error($"Exception during FindObjectOfType<Player>: {e}");
            return;
            }

            if (playerInstance != null && playerInstance.transform != null)
            {
                Transform playerTransform = playerInstance.transform;
                try
                {
                    playerTransform.position = targetPosition;
                    LoggerInstance.Msg($"Teleported player to '{cubeName}'");
                }
                catch (System.Exception e)
                {
                    LoggerInstance.Error($"Exception during playerTransform.position assignment: {e}");
                }
            }
            else
            {
                LoggerInstance.Warning("Could not find valid Player object/transform to teleport!");
            }
        }

        private Transform FindDeepChild(Transform parent, string childName)
        {
            if (parent == null) return null;
            Transform child = parent.Find(childName);
            if (child != null) return child;
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.name == childName)
                {
                    return t;
                }
            }
            return null;
        }

        private void LogInstanceDebugInfo()
        {
            LoggerInstance.Msg("--- Instance Debug Info Requested ---");
            if (spawnedInstanceRoot == null)
            {
                LoggerInstance.Warning("Spawned instance root is NULL.");
                return;
            }

            LoggerInstance.Msg($"Root Object Name: {spawnedInstanceRoot.name}");
            LoggerInstance.Msg($" -> Position: {spawnedInstanceRoot.transform.position}");
            LoggerInstance.Msg($" -> Rotation: {spawnedInstanceRoot.transform.rotation.eulerAngles}");
            LoggerInstance.Msg($" -> Active Self: {spawnedInstanceRoot.activeSelf}");
            LoggerInstance.Msg($" -> Active in Hierarchy: {spawnedInstanceRoot.activeInHierarchy}");

            Transform cube1 = FindDeepChild(spawnedInstanceRoot.transform, "Cube Test");
            if (cube1 != null)
            {
                LoggerInstance.Msg($"Cube 'Cube Test':");
                LoggerInstance.Msg($" -> Local Pos: {cube1.localPosition}");
                LoggerInstance.Msg($" -> World Pos: {cube1.position}");
                LoggerInstance.Msg($" -> Active Self: {cube1.gameObject.activeSelf}");
                LoggerInstance.Msg($" -> Active in Hierarchy: {cube1.gameObject.activeInHierarchy}");
                MeshRenderer mr = cube1.GetComponent<MeshRenderer>();
                if (mr != null) LoggerInstance.Msg($" -> MeshRenderer Enabled: {mr.enabled}");
                else LoggerInstance.Msg(" -> MISSING MeshRenderer!");
            }
            else
            {
                LoggerInstance.Warning($" -> Cube 'Cube Test' object NOT found!");
            }

            Transform cube2 = FindDeepChild(spawnedInstanceRoot.transform, "Cube Test 2");
            if (cube2 != null)
            {
                LoggerInstance.Msg($"Cube 'Cube Test 2':");
                LoggerInstance.Msg($" -> Local Pos: {cube2.localPosition}");
                LoggerInstance.Msg($" -> World Pos: {cube2.position}");
                LoggerInstance.Msg($" -> Active Self: {cube2.gameObject.activeSelf}");
                LoggerInstance.Msg($" -> Active in Hierarchy: {cube2.gameObject.activeInHierarchy}");
                MeshRenderer mr = cube2.GetComponent<MeshRenderer>();
                if (mr != null) LoggerInstance.Msg($" -> MeshRenderer Enabled: {mr.enabled}");
                else LoggerInstance.Msg(" -> MISSING MeshRenderer!");
            }
            else
            {
                LoggerInstance.Warning($" -> Cube 'Cube Test 2' object NOT found!");
            }

            LoggerInstance.Msg("--- End Instance Debug Info ---");
        }

        private void LogManorGateMaterials()
        {
            LoggerInstance.Msg("--- ManorGate Material/Shader Check ---");

            // Find ANY ManorGate instance in the scene
            ManorGate gateInstance = GameObject.FindObjectOfType<ManorGate>();

            if (gateInstance == null)
            {
                LoggerInstance.Warning("Could not find any ManorGate instance in the scene.");
                return;
            }

            LoggerInstance.Msg($"Found ManorGate: {gateInstance.gameObject.name}");

            // Get all MeshRenderers in its children (including self if it has one)
            var childRenderers = gateInstance.GetComponentsInChildren<MeshRenderer>(true); // Include inactive renderers

            if (childRenderers == null || childRenderers.Length == 0)
            {
                LoggerInstance.Warning($"No MeshRenderers found in children of {gateInstance.gameObject.name}.");
            return;
            }

            LoggerInstance.Msg($"Found {childRenderers.Length} MeshRenderers in children:");
            int count = 0;
            foreach (MeshRenderer renderer in childRenderers)
            {
                count++;
                if (renderer == null) continue;

                LoggerInstance.Msg($"  Renderer {count} on GameObject: '{renderer.gameObject.name}' (Enabled: {renderer.enabled})");
                Material mat = renderer.material; // Instance
                if (mat != null)
                {
                    string textureName = mat.mainTexture != null ? mat.mainTexture.name : "NULL";
                    string shaderName = mat.shader != null ? mat.shader.name : "NULL";
                    LoggerInstance.Msg($"   -> Material: {mat.name}");
                    LoggerInstance.Msg($"   -> Main Texture: {textureName}");
                    LoggerInstance.Msg($"   -> Shader: {shaderName}");
                }
                else
                {
                    LoggerInstance.Warning("   -> Material on Renderer is NULL!");
                }
            }
            LoggerInstance.Msg("--- End ManorGate Check ---");
        }

        // --- Modified: Method to directly overwrite choices on the specific event ---
        private void ModifyEstateAgentChoicesDirectly()
        {
            // Prevent running multiple times if already successful
            if (dialogueModified)
            {
                LoggerInstance.Msg("ModifyEstateAgentChoicesDirectly: Skipping, dialogue already modified.");
                return;
            }

            const string TargetDialogueContainerName = "EstateAgent_Sell";
            const string PropertyChoiceNodeGuid = "8e2ef594-96d9-43f2-8cfa-6efaea823a56";

            LoggerInstance.Msg($"Attempting to find Ray's specific '{TargetDialogueContainerName}' event to modify choices...");

            // --- Find Ray and the specific event component ---
            NPCEvent_LocationDialogue targetEventComponent = null;
            Ray rayInstance = GameObject.FindObjectOfType<Ray>(); // Try finding the specific Ray component first
            Component searchTargetComponent = null;

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
                    // Case-insensitive comparison for safety
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
                    LoggerInstance.Error("ModifyEstateAgentChoicesDirectly: Could not find Ray NPC instance via specific component or GameObject name.");
                    return; // Cannot proceed without Ray
                }
            }

            if (searchTargetComponent == null)
            {
                LoggerInstance.Error("ModifyEstateAgentChoicesDirectly: Search target component for Ray is null after attempts.");
                return;
            }

            // Search for the event component on the found target (Ray)
            var locationDialogueEvents = searchTargetComponent.GetComponentsInChildren<Il2CppScheduleOne.NPCs.Schedules.NPCEvent_LocationDialogue>(true); // Include inactive
            if (locationDialogueEvents == null || locationDialogueEvents.Length == 0)
            {
                LoggerInstance.Warning($"ModifyEstateAgentChoicesDirectly: No NPCEvent_LocationDialogue components found on Ray ('{searchTargetComponent.gameObject.name}') or his children.");
                return;
            }

            LoggerInstance.Msg($"ModifyEstateAgentChoicesDirectly: Found {locationDialogueEvents.Length} NPCEvent_LocationDialogue components on '{searchTargetComponent.gameObject.name}'. Checking each...");
            foreach (var eventComponent in locationDialogueEvents)
            {
                // Check if the event component and its override are valid, and if the override name matches
                if (eventComponent != null && eventComponent.DialogueOverride != null && eventComponent.DialogueOverride.name == TargetDialogueContainerName)
                {
                    targetEventComponent = eventComponent;
                    LoggerInstance.Msg($"ModifyEstateAgentChoicesDirectly: Found target event component on '{eventComponent.gameObject.name}'.");
                    break; // Found the correct event
                }
                else if (eventComponent != null)
                {
                     // Log details if it's not the target but has an override
                     string overrideName = (eventComponent.DialogueOverride != null) ? eventComponent.DialogueOverride.name : "NULL";
                     LoggerInstance.Msg($" -> Skipping event on '{eventComponent.gameObject.name}'. DialogueOverride: '{overrideName}'");
                }
            }

            if (targetEventComponent == null)
            {
                LoggerInstance.Error($"ModifyEstateAgentChoicesDirectly: Could not find the specific NPCEvent_LocationDialogue using '{TargetDialogueContainerName}' on Ray.");
                return; // Cannot proceed without the specific event
            }
            // --- End finding specific event component ---

            // --- Modify the container directly referenced by the event ---
            DialogueContainer container = targetEventComponent.DialogueOverride; // Use the container *from the event*!
            if (container == null)
            {
                LoggerInstance.Error("ModifyEstateAgentChoicesDirectly: Target event component's DialogueOverride is null! Cannot modify.");
            return;
        }

            LoggerInstance.Msg($"Modifying choices in the specific DialogueContainer instance (Name: {container.name}, ID: {container.GetInstanceID()}) used by Ray's event.");

            // Find the specific node within *this container instance*
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

            // Check if ALREADY modified (on *this* instance)
            // Be careful comparing Il2CppReferenceArray elements
            bool alreadyHasManor = false;
            if (choiceNode.choices != null && choiceNode.choices.Count >= 4) // Check count before indexing
            {
                // Look for the "manor" label specifically, index might change
                 for(int i = 0; i < choiceNode.choices.Count; i++)
                 {
                    var choice = choiceNode.choices[i];
                    if(choice != null && choice.ChoiceLabel == "manor")
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


            // --- Create the new, complete list of choices ---
            // Use Il2CppSystem.Collections.Generic.List for compatibility if assigning directly isn't reliable
            // But using standard List<T> and converting often works.
            var newChoicesList = new List<Il2CppScheduleOne.Dialogue.DialogueChoiceData>();

            // Add Bungalow
            newChoicesList.Add(new DialogueChoiceData { Guid = "f3a15b13-14d1-420a-ad17-d731155701d8", ChoiceText = "The Bungalow (<PRICE>)", ChoiceLabel = "bungalow" });
            // Add Barn
            newChoicesList.Add(new DialogueChoiceData { Guid = "b668ec37-4c92-4949-a113-62d1effdb3b2", ChoiceText = "The Barn (<PRICE>)", ChoiceLabel = "barn" });
            // Add Docks Warehouse
            newChoicesList.Add(new DialogueChoiceData { Guid = "ad2170b0-1789-4a61-94fb-6ffc2b720252", ChoiceText = "The Docks Warehouse (<PRICE>)", ChoiceLabel = "dockswarehouse" });
            // Add Manor (New Data)
            newChoicesList.Add(new DialogueChoiceData { Guid = System.Guid.NewGuid().ToString(), ChoiceText = "Hilltop Manor (<PRICE>)", ChoiceLabel = "manor" });
            // Add Nevermind
            newChoicesList.Add(new DialogueChoiceData { Guid = "7406b61c-cb6e-418f-ba2b-bfb28f1b0b70", ChoiceText = "Nevermind", ChoiceLabel = "" });
            // --- Finished creating new list ---

            // --- Assign the new list back to the node in *this specific container* ---
            try
            {
                // Convert standard List<T> to Il2CppReferenceArray<T> for assignment
                choiceNode.choices = new Il2CppReferenceArray<DialogueChoiceData>(newChoicesList.ToArray());

                // Verification Log: Check the count and maybe the last item's label right after assignment
                if (choiceNode.choices != null && choiceNode.choices.Count == 5)
                {
                     string lastLabel = (choiceNode.choices[4] != null) ? choiceNode.choices[4].ChoiceLabel : "NULL_CHOICE";
                     string manorLabel = (choiceNode.choices[3] != null) ? choiceNode.choices[3].ChoiceLabel : "NULL_CHOICE";
                     LoggerInstance.Msg($"Successfully OVERWROTE choices for node '{choiceNode.DialogueNodeLabel}' within the event's specific container. New count: {choiceNode.choices.Count}. Manor Label: '{manorLabel}', Last Label: '{lastLabel}'.");
                     dialogueModified = true; // Set flag on success
                }
                else
                {
                    int currentCount = (choiceNode.choices != null) ? choiceNode.choices.Count : -1; // -1 indicates null array
                    LoggerInstance.Error($"Assignment failed or resulted in unexpected count! Current count: {currentCount}");
                }
            }
            catch (System.Exception e)
            {
                LoggerInstance.Error($"Error assigning new choices array to the specific event container: {e}");
                // Potentially reset dialogueModified flag if error handling requires retry logic?
                // dialogueModified = false;
            }
        }

        // --- NEW: Method to find the specific dialogue event on Ray ---
        private void FindAndLogEstateAgentEvent()
        {
            LoggerInstance.Msg("Attempting to find Ray and his Estate Agent Dialogue Event...");
            Ray rayInstance = GameObject.FindObjectOfType<Ray>();
            Component searchTargetComponent = null;
            if (rayInstance == null)
            {
                NPC[] allNpcs = GameObject.FindObjectsOfType<NPC>();
                NPC rayNpc = null;
                foreach (var npc in allNpcs) { if (npc.gameObject.name.Equals("Ray", System.StringComparison.OrdinalIgnoreCase)) { rayNpc = npc; break; } }
                if (rayNpc != null) { searchTargetComponent = rayNpc; LoggerInstance.Msg($"Found NPC GameObject named 'Ray'. Searching its components..."); }
                else { LoggerInstance.Error("Could not find Ray NPC instance in the scene."); return; }
            }
            else { searchTargetComponent = rayInstance; } // Use specific Ray component if found

            if (searchTargetComponent == null) { LoggerInstance.Error("Search target component for Ray is null."); return; }
            LoggerInstance.Msg($"Searching children of '{searchTargetComponent.gameObject.name}' for NPCEvent_LocationDialogue...");

            var locationDialogueEvents = searchTargetComponent.GetComponentsInChildren<Il2CppScheduleOne.NPCs.Schedules.NPCEvent_LocationDialogue>(true);
            if (locationDialogueEvents == null || locationDialogueEvents.Length == 0) { LoggerInstance.Warning($"No NPCEvent_LocationDialogue components found on Ray or his children."); return; }

            LoggerInstance.Msg($"Found {locationDialogueEvents.Length} NPCEvent_LocationDialogue components. Checking each:");
            bool foundTargetEvent = false;
            foreach (var eventComponent in locationDialogueEvents)
            {
                if (eventComponent == null) continue;

                DialogueContainer dialogueOverride = eventComponent.DialogueOverride;
                string overrideName = dialogueOverride != null ? dialogueOverride.name : "NULL";
                LoggerInstance.Msg($" -> Checking event on GameObject: '{eventComponent.gameObject.name}', DialogueOverride: '{overrideName}'");

                // Check if this is the one we want
                if (dialogueOverride != null && dialogueOverride.name == "EstateAgent_Sell")
                {
                    LoggerInstance.Msg($"   -> SUCCESS: Found target event!");
                    foundTargetEvent = true;
                }
            }

            if (!foundTargetEvent)
            {
                LoggerInstance.Warning("Checked all found events, none had 'EstateAgent_Sell' as DialogueOverride.");
            }
            else
            {
                LoggerInstance.Msg("Correct Estate Agent dialogue event was located.");
            }
        }

        // --- ADDED: Update method for F7 key press ---
        public override void OnUpdate()
        {
            // Check if F7 was pressed this frame
            if (Input.GetKeyDown(KeyCode.F7))
            {
                LoggerInstance.Msg("F7 pressed. Attempting to teleport to truck...");
                TeleportToGasMartWestTruck();
            }
        }
        // --- END ADDED: Update method ---

        private void TeleportToGasMartWestTruck()
        {
            LandVehicle targetVehicle = null;
            string targetVehicleName = "Dan's Hardware"; // The exact name to find

            try
            {
                 // Find all LandVehicle components in the scene
                 Il2CppArrayBase<LandVehicle> allVehicles = GameObject.FindObjectsOfType<LandVehicle>();

                 if (allVehicles == null || allVehicles.Count == 0)
                 {
                     LoggerInstance.Warning("Could not find any LandVehicle components in the scene.");
                     return;
                 }

                 LoggerInstance.Msg($"Found {allVehicles.Count} LandVehicles. Searching for '{targetVehicleName}'...");

                 // Iterate through the found vehicles
                 foreach (LandVehicle vehicle in allVehicles)
                 {
                     // Check if the vehicle and its GameObject are valid and if the name matches
                     if (vehicle != null && vehicle.gameObject != null && vehicle.gameObject.name == targetVehicleName)
                     {
                         targetVehicle = vehicle;
                         LoggerInstance.Msg($"Found target vehicle: '{targetVehicle.gameObject.name}'");
                         break; // Stop searching once found
                     }
                 }

                 if (targetVehicle == null)
                 {
                     LoggerInstance.Warning($"Could not find a LandVehicle named '{targetVehicleName}'.");
                     return;
                 }

                 // Find the player instance
                 Player playerInstance = GameObject.FindObjectOfType<Player>();
                 if (playerInstance == null || playerInstance.transform == null)
                 {
                     LoggerInstance.Warning("Could not find valid Player object/transform to teleport!");
                     return;
                 }

                 // Calculate teleport position (slightly above the vehicle)
                 Vector3 teleportPos = targetVehicle.transform.position + Vector3.up * 2.0f;
                 LoggerInstance.Msg($"Target position: {targetVehicle.transform.position}. Teleporting player to: {teleportPos}");

                 // Perform the teleport
                 playerInstance.transform.position = teleportPos;
                 LoggerInstance.Msg("Player teleported successfully.");

            }
            catch (System.Exception e)
            {
                 LoggerInstance.Error($"Exception during teleport attempt: {e}");
            }
        }
    }

}