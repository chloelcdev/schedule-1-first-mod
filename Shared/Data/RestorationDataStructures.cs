// This file should be copied from the Unity Project:
// Schedule1-Modding-Decomp/ExportedProject/Assets/ManorMod/Shared/Data/RestorationDataStructures.cs
// Ensure it's included in the MelonLoader project build.

using UnityEngine;
using System.Collections.Generic;
using System;

namespace ComponentRestoration.Data
{
    // NOTE: These classes are temporarily simplified for JsonUtility testing
    [System.Serializable] // Must be serializable for JsonUtility (but keep for now)
    public class ComponentData
    {
        public string typeFullName;
        
        // Explicit fields matching DecalProjector properties for JsonUtility test
        public float drawDistance;
        public float fadeScale;
        public float startAngleFade;
        public float endAngleFade;
        public Vector2 uvScale; 
        public Vector2 uvBias;
        public uint renderingLayerMask;
        public int scaleMode; // Store enum as int
        public Vector3 pivot;
        public Vector3 size;
        public float fadeFactor;
        public string material; // Was material_placeholder
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