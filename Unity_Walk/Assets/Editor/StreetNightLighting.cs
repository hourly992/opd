using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public static class StreetNightLighting
{
    [Serializable]
    private sealed class Result
    {
        public string scene;
        public int networkCount, fixtures, ledLensRenderers;
        public int activeBudget = 12, shadowBudget = 2;
        public float streetCandela = 2200f, courtyardCandela = 1400f;
    }

    public static void Apply(Scene scene)
    {
        var result = new Result { scene = scene.name };
        var networks = UnityEngine.Object.FindObjectsByType<StreetLampNetwork>(FindObjectsInactive.Include)
            .Where(network => network.gameObject.scene == scene).ToArray();
        result.networkCount = networks.Length;
        foreach (var network in networks)
        {
            network.visibleDistance = 66f;
            network.nightIntensityMultiplier = 1f;
            EditorUtility.SetDirty(network);
            foreach (var lamp in network.lamps)
            {
                if (!lamp) continue;
                if (lamp.type != LightType.Spot)
                    throw new InvalidOperationException($"Unexpected non-spot fixture {lamp.name}.");
                var data = lamp.GetComponent<HDAdditionalLightData>() ?? lamp.gameObject.AddComponent<HDAdditionalLightData>();
                lamp.lightmapBakeType = LightmapBakeType.Realtime;
                lamp.lightUnit = LightUnit.Candela;
                lamp.intensity = lamp.range > 15f ? result.streetCandela : result.courtyardCandela;
                lamp.color = Color.white;
                lamp.useColorTemperature = true;
                lamp.colorTemperature = 3200f;
                lamp.innerSpotAngle = lamp.spotAngle * .60f;
                lamp.shadows = LightShadows.None;
                lamp.enabled = false;
                data.lightDimmer = 1f;
                data.affectDiffuse = true;
                data.affectSpecular = true;
                data.affectsVolumetric = true;
                data.volumetricDimmer = .09f;
                data.interactsWithSky = false;
                data.SetShadowResolution(512);
                data.shadowUpdateMode = ShadowUpdateMode.EveryFrame;
                data.normalBias = .45f;
                data.shadowDimmer = .88f;
                data.shadowNearPlane = .15f;
                data.shadowFadeDistance = 42f;
                data.useRayTracedShadows = false;
                EditorUtility.SetDirty(lamp);
                EditorUtility.SetDirty(data);
                result.fixtures++;
            }
            result.ledLensRenderers += network.GetComponentsInChildren<MeshRenderer>(true)
                .Count(renderer => renderer.sharedMaterials.Any(material => material &&
                    material.name.StartsWith("WarmLedLens", StringComparison.Ordinal)));
        }
        Debug.Log("STREET_NIGHT_LIGHTING " + JsonUtility.ToJson(result));
    }
}
