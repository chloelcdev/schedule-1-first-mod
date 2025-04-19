using UnityEngine;
using System.Collections.Generic;
using System;

namespace ComponentRestoration.Data
{
    // NOTE: These classes are temporarily simplified for JsonUtility testing
    [System.Serializable] // Must be serializable for JsonUtility
    public class ComponentData
    {
        public string typeFullName;
        
        // NEW: Dictionary to hold component properties
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }


    [System.Serializable] // Must be serializable
    public class GameObjectData
    {
        public string name;
        public string path;
        public List<ComponentData> components = new List<ComponentData>();
    }

    [System.Serializable] // Must be serializable
    public class PrefabHierarchyData
    {
        public List<GameObjectData> gameObjects = new List<GameObjectData>();
    }
} 