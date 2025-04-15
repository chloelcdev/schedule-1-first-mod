using HarmonyLib;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.Vehicles;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Map;
using MelonLoader; // Or your logger namespace
using UnityEngine;

namespace ChloesManorMod.Patches // Your namespace
{

    [HarmonyPatch(typeof(DeliveryInstance), nameof(DeliveryInstance.SetStatus))]
    static class DeliveryInstance_SetStatus_Patch
    {
        static void Prefix(DeliveryInstance __instance, EDeliveryStatus status)
        {
             MelonLogger.Msg($"-- Delivery Debug: SetStatus --");
             MelonLogger.Msg($"   ID: {__instance.DeliveryID}");
             MelonLogger.Msg($"   Current Status: {__instance.Status}");
             MelonLogger.Msg($"   Setting Status To: {status}");
             MelonLogger.Msg($"   Destination: {__instance.Destination?.PropertyName ?? "NULL"} (Code: {__instance.DestinationCode})");
             MelonLogger.Msg($"   Dock Index: {__instance.LoadingDockIndex}");
             MelonLogger.Msg($"-----------------------------");

             if (status == EDeliveryStatus.Arrived)
             {
                 MelonLogger.Msg($"   >> Arrived state set. Preparing to call ActiveVehicle.Activate...");
                 // Log details about the vehicle *before* Activate is called
                 DeliveryVehicle vehicle = __instance.ActiveVehicle ?? Il2CppScheduleOne.DevUtilities.NetworkSingleton<DeliveryManager>.Instance?.GetShopInterface(__instance.StoreName)?.DeliveryVehicle;
                 MelonLogger.Msg($"      Vehicle Found: {vehicle?.Vehicle?.name ?? "NULL"}");
                 LoadingDock dock = __instance.LoadingDock; // Relies on Destination & Index being valid
                 MelonLogger.Msg($"      Target Dock: {dock?.name ?? "NULL"} (Index: {__instance.LoadingDockIndex})");
                 ParkingLot parking = dock?.Parking;
                 MelonLogger.Msg($"      Target Parking Lot: {parking?.name ?? "NULL"}");
                  if (parking != null)
                  {
                     MelonLogger.Msg($"         Parking Lot Spots Count: {parking.ParkingSpots?.Count ?? -1}");
                     if (parking.ParkingSpots?.Count > 0)
                     {
                         MelonLogger.Msg($"         Spot 0: {parking.ParkingSpots[0]?.name ?? "NULL"}");

                         MelonLogger.Msg($"         Spot 0 Alignment Point: {parking.ParkingSpots[0]?.AlignmentPoint?.name ?? "NULL"}");
                     }
                 }

             }
             else if (status == EDeliveryStatus.Completed)
             {
                 MelonLogger.Msg($"   >> Completed state set. Preparing to call ActiveVehicle.Deactivate...");
                 MelonLogger.Msg($"      Vehicle: {__instance.ActiveVehicle?.Vehicle?.name ?? "None"}");
                 MelonLogger.Msg($"      Current Vehicle Position: {__instance.ActiveVehicle?.Vehicle?.transform.position.ToString() ?? "N/A"}");
             }
        }
    }

    [HarmonyPatch(typeof(DeliveryVehicle), nameof(DeliveryVehicle.Activate))]
    static class DeliveryVehicle_Activate_Patch
    {
        static void Prefix(DeliveryVehicle __instance, DeliveryInstance instance)
        {
            MelonLogger.Msg($"-- Delivery Debug: Activate Vehicle --");
            MelonLogger.Msg($"   Vehicle: {__instance.Vehicle?.name ?? "NULL"}");
            MelonLogger.Msg($"   For Delivery ID: {instance?.DeliveryID ?? "NULL_INSTANCE"}");
            if (instance == null) return; // Stop if instance is null

            LoadingDock dock = instance.LoadingDock; // This getter uses the index
            MelonLogger.Msg($"   Target Dock: {dock?.name ?? "NULL"} (From Instance Index: {instance.LoadingDockIndex})");

            ParkingLot parking = dock?.Parking;
            MelonLogger.Msg($"   Target Parking Lot: {parking?.name ?? "NULL"}");

            if (parking == null)
            {
                 MelonLogger.Error($"      >> Parking lot is NULL! Activate will likely fail.");
                 return;
            }

            MelonLogger.Msg($"      Parking Spots Count: {parking.ParkingSpots?.Count ?? -1}");
            if (parking.ParkingSpots == null || parking.ParkingSpots.Count == 0)
            {
                MelonLogger.Error($"      >> Parking lot has no spots! Activate will fail.");
                 return;
            }

            ParkingSpot spotZero = parking.ParkingSpots[0];
            MelonLogger.Msg($"      Spot 0: {spotZero?.name ?? "NULL"}");
            if (spotZero == null)
            {
                 MelonLogger.Error($"      >> Parking Spot at index 0 is NULL! Activate will fail.");
                 return;
            }

            Transform alignmentPoint = spotZero.AlignmentPoint;
            MelonLogger.Msg($"      Spot 0 Alignment Point: {alignmentPoint?.name ?? "NULL"}");
             if (alignmentPoint == null)
            {
                 MelonLogger.Error($"      >> Spot 0 Alignment Point is NULL! Vehicle.Park will likely fail.");
                 // Note: Vehicle.Park might still position based on the spot's transform itself, but logging this is vital.
            }

            MelonLogger.Msg($"   Calling SetStaticOccupant...");
            // SetStaticOccupant is simple, unlikely to fail unless 'dock' is null

            MelonLogger.Msg($"   Preparing to call Vehicle.Park...");
            MelonLogger.Msg($"------------------------------------");
        }

         // Optional: Postfix to confirm completion without error
         static void Postfix(DeliveryVehicle __instance, DeliveryInstance instance)
         {
             if (instance == null) return;
              MelonLogger.Msg($"-- Delivery Debug: Activate Vehicle Postfix --");
              MelonLogger.Msg($"   Vehicle: {__instance.Vehicle?.name ?? "NULL"}");
              MelonLogger.Msg($"   For Delivery ID: {instance.DeliveryID}");
              MelonLogger.Msg($"   Completed Activate call successfully.");
              MelonLogger.Msg($"-----------------------------------------");
         }
    }

    [HarmonyPatch(typeof(DeliveryVehicle), nameof(DeliveryVehicle.Deactivate))]
    static class DeliveryVehicle_Deactivate_Patch
    {
        static void Prefix(DeliveryVehicle __instance)
        {
             MelonLogger.Msg($"-- Delivery Debug: Deactivate Vehicle --");
             MelonLogger.Msg($"   Vehicle: {__instance.Vehicle?.name ?? "NULL"}");
             MelonLogger.Msg($"   For Delivery ID: {__instance.ActiveDelivery?.DeliveryID ?? "None Active"}");
             MelonLogger.Msg($"   Current Position: {__instance.Vehicle?.transform.position.ToString() ?? "N/A"}");
             MelonLogger.Msg($"   Setting position to (0, -100, 0)...");
             MelonLogger.Msg($"--------------------------------------");
        }
    }
}
