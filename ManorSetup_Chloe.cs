using UnityEngine;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.Persistence;
using Il2CppSystem;
using Il2CppInterop;
using Il2CppInternal;
using Il2CppMono;
using Il2Cpp;

public class ManorSetup_Chloe : MonoBehaviour
{
    public List<LoadingDock> loadingdocks = new(); // these will have their "Property" value assigned (Property as in real-estate, not coding lingo)
    public Transform NPCSpawn;
    public SavePoint savePoint;
    public Transform listingPoster;
    // ManorGate foundManorGate;

    Property foundManor = null;

    void Start()
    {
        foundManor = FindManor();


        // assign the property to the loading docks
        foreach (LoadingDock dock in loadingdocks)
            dock.ParentProperty = foundManor;

        // assign the loading docks to the property
        foundManor.LoadingDocks = loadingdocks.ToArray();

        // assign the NPC spawn point to the property
        foundManor.NPCSpawnPoint = NPCSpawn;

        // assign the listing poster to the property
        foundManor.ListingPoster = listingPoster;

        // TODO: Find the gate for specifically the manor
        
        // Gate Hookup
        /*if (foundManorGate != null)
        {
             UpdateGateAccess(foundManor.IsOwned);
             foundManor.onThisPropertyAcquired.RemoveListener(OnManorAcquired);
             foundManor.onThisPropertyAcquired.AddListener(OnManorAcquired);
        }*/
    }

    Property FindManor()
    {
        return PropertyManager.Instance?.GetProperty("manor");
    }

    void OnManorAcquired() {
        UpdateGateAccess(true);
    }

    void UpdateGateAccess(bool canEnter) {
        //if (manorGate != null) manorGate.SetEnterable(canEnter);
    }
}
