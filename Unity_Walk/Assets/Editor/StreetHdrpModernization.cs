using UnityEditor;
using UnityEngine;

public static class StreetHdrpModernization
{
    public static void BakeAdaptiveProbes()
    {
        int enabled = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (!material || !material.shader)
                continue;
            bool canopy = material.shader.name == "Street/Foliage" &&
                (!material.HasProperty("_Grass") || material.GetFloat("_Grass") <= .5f);
            bool fallingLeaves = material.shader.name == "Street/FallenLeaves";
            if ((!canopy && !fallingLeaves) || material.GetShaderPassEnabled("MotionVectors"))
                continue;
            material.SetShaderPassEnabled("MotionVectors", true);
            EditorUtility.SetDirty(material);
            enabled++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"STREET_HDRP_FOLIAGE_MOTION enabled={enabled}");
        StreetApvBake.BakeWindows();
    }

    public static void Apply()
    {
        StreetHdrpSetup.Configure();
        StreetHdrpTextureDetail.Apply();
        AssetDatabase.SaveAssets();
        Debug.Log("STREET_HDRP_MODERNIZED_SETTINGS");
    }
}