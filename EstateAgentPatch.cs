using HarmonyLib;
using Il2CppScheduleOne.Dialogue; // Use base DialogueHandler
using Il2CppScheduleOne.Property; // Correct namespace
using Il2CppScheduleOne.Money;    // Correct namespace for MoneyManager
using Il2CppScheduleOne.NPCs; // For NPC
using Il2CppScheduleOne.NPCs.Schedules; // For NPCEvent_LocationDialogue
using MelonLoader;
using UnityEngine; // For AddListener/RemoveListener
using System.Collections.Generic; // For HashSet
using Il2CppSystem; // Need this for Il2CppSystem.Action

namespace ChloesManorMod // Your actual mod namespace
{
    public static class ManorPurchaseEventHandler
    {
        // Track the base DialogueHandler instances
        private static readonly HashSet<DialogueHandler> attachedHandlers = new HashSet<DialogueHandler>();

        // --- Patch to ATTACH listener when the event starts ---
        [HarmonyPatch(typeof(NPCEvent_LocationDialogue), nameof(NPCEvent_LocationDialogue.StartAction))]
        public static class NPCEvent_LocationDialogue_StartAction_Patch
        {
            public static void Postfix(NPCEvent_LocationDialogue __instance)
            {
                // Check if this is the EstateAgent_Sell event
                if (__instance.DialogueOverride != null && __instance.DialogueOverride.name == "EstateAgent_Sell")
                {
                    NPC npc = __instance.npc;
                    if (npc == null)
                    {
                        MelonLogger.Error("ManorPurchaseEventHandler: NPC is null in StartAction for EstateAgent_Sell event.");
                        return;
                    }

                    // --- Get the handler from the NPC's public field ---
                    DialogueHandler handler = npc.dialogueHandler; // Use the direct reference
                    if (handler == null)
                    {
                        // This would be very unusual if the NPC is functional
                        MelonLogger.Error($"ManorPurchaseEventHandler: npc.dialogueHandler is NULL on NPC '{npc.name}'. Cannot attach listener.");
                        return;
                    }

                    // --- Add Listener to the base DialogueHandler ---
                    if (attachedHandlers.Add(handler))
                    {
                        MelonLogger.Msg($"ManorPurchaseEventHandler: Attaching listener to onDialogueChoiceChosen for handler on {npc.name} (Handler Type: {handler.GetType().FullName}, Instance ID: {handler.GetInstanceID()})");
                        // Cast our static method to Il2CppSystem.Action<string>
                        handler.onDialogueChoiceChosen.AddListener((UnityEngine.Events.UnityAction<string>)HandleManorChoice);
                    }
                    else
                    {
                        MelonLogger.Msg($"ManorPurchaseEventHandler: Listener already attached to handler on {npc.name} (Instance ID: {handler.GetInstanceID()}). Skipping.");
                    }
                }
            }
        }

        // --- Patch to REMOVE listener when the event ends ---
        [HarmonyPatch(typeof(NPCEvent_LocationDialogue), nameof(NPCEvent_LocationDialogue.EndAction))]
        public static class NPCEvent_LocationDialogue_EndAction_Patch
        {
            public static void Postfix(NPCEvent_LocationDialogue __instance)
            {
                if (__instance.DialogueOverride != null && __instance.DialogueOverride.name == "EstateAgent_Sell")
                {
                    NPC npc = __instance.npc;
                    if (npc == null) return;

                    // --- Get the handler from the NPC's public field ---
                    DialogueHandler handler = npc.dialogueHandler;
                    if (handler != null)
                    {
                        // --- Remove Listener ---
                        if (attachedHandlers.Remove(handler))
                        {
                            MelonLogger.Msg($"ManorPurchaseEventHandler: Removing listener from onDialogueChoiceChosen for handler on {npc.name} (Instance ID: {handler.GetInstanceID()})");
                            try
                            {
                                // Remove using the cast delegate type
                                handler.onDialogueChoiceChosen.RemoveListener((UnityEngine.Events.UnityAction<string>)HandleManorChoice);
                                MelonLogger.Msg($"ManorPurchaseEventHandler: Successfully removed listener.");
                            }
                            catch (System.Exception ex)
                            {
                                MelonLogger.Error($"ManorPurchaseEventHandler: Error removing listener: {ex.Message}");
                            }
                        }
                        //else
                        //{
                        //    MelonLogger.Warning($"ManorPurchaseEventHandler: Attempted to remove listener, but handler on {npc.name} was not tracked.");
                        //}
                    }
                    //else
                    //{
                    //     MelonLogger.Warning($"ManorPurchaseEventHandler EndAction: npc.dialogueHandler is NULL on NPC '{npc.name}'. Cannot remove listener.");
                    //}
                }
            }
        }

        // --- The Actual Purchase Logic ---
        private static void HandleManorChoice(string choiceLabel)
        {
            if (choiceLabel == "manor")
            {
                MelonLogger.Msg("ManorPurchaseEventHandler: Heard 'manor' choice via event. Attempting purchase...");

                Property manorProperty = PropertyManager.Instance?.GetProperty("manor");
                if (manorProperty == null)
                {
                    MelonLogger.Error("ManorPurchaseEventHandler: Could not find Manor property instance!");
                    return;
                }
                if (manorProperty.IsOwned)
                {
                    MelonLogger.Warning("ManorPurchaseEventHandler: Manor is already owned! Preventing duplicate purchase charge/action.");
                    return;
                }
                float manorPrice = manorProperty.Price;
                MelonLogger.Msg($"ManorPurchaseEventHandler: Found Manor property: {manorProperty.PropertyName}. Price: {manorPrice}");

                MoneyManager moneyManager = MoneyManager.Instance;
                 if (moneyManager == null)
                {
                     MelonLogger.Error("ManorPurchaseEventHandler: MoneyManager.Instance is null!");
                     return;
                }

                bool canAfford = moneyManager.onlineBalance >= manorPrice;

                if (!canAfford)
                {
                    MelonLogger.Warning($"ManorPurchaseEventHandler: Player cannot afford Manor (Price: {manorPrice}, Balance: {moneyManager.onlineBalance}). Purchase aborted.");
                    return;
                }
                MelonLogger.Msg($"ManorPurchaseEventHandler: Player can afford Manor. Balance: {moneyManager.onlineBalance}");

                string transactionName = $"Property Purchase ({manorProperty.PropertyName})";
                string transactionNote = $"Bought {manorProperty.PropertyName}";
                float amountToSpend = -manorPrice;
                float quantity = 1;

                MelonLogger.Msg($"ManorPurchaseEventHandler: Creating online transaction: Name='{transactionName}', Amount={amountToSpend}, Quantity={quantity}");
                moneyManager.CreateOnlineTransaction(transactionName, amountToSpend, quantity, transactionNote);

                MelonLogger.Msg($"ManorPurchaseEventHandler: Calling SetOwned() on Manor property (Instance ID: {manorProperty.GetInstanceID()}).");
                manorProperty.SetOwned();
                MelonLogger.Msg($"ManorPurchaseEventHandler: SetOwned() called. Ownership & Balance should update via network.");
            }
        }
    }
}
