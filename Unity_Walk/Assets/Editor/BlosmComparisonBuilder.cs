using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using UnityEditor.Build.Reporting;using System.IO;
public static class BlosmComparisonBuilder {
 public static void Build(){AssetDatabase.Refresh();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
 var path="Assets/BlosmComparison/Dunina_Blosm.fbx";var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!model)throw new System.Exception("Missing FBX");
 var map=(GameObject)PrefabUtility.InstantiatePrefab(model);map.name="BLOSM · separate OSM comparison";
 var rs=map.GetComponentsInChildren<MeshRenderer>();var bounds=new Bounds();bool first=true;
 foreach(var r in rs){if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);var m=new Material(Shader.Find("Standard"));m.color=r.name.Contains("roads")||r.name.Contains("paths")?new Color(.22f,.23f,.24f):r.name.Contains("vegetation")?new Color(.3f,.4f,.2f):new Color(.65f,.58f,.48f);AssetDatabase.CreateAsset(m,"Assets/BlosmComparison/Mat_"+r.name+".mat");var mats=r.sharedMaterials;for(int i=0;i<mats.Length;i++)mats[i]=m;r.sharedMaterials=mats;}
 Debug.Log("BLOSM_BOUNDS "+bounds+" renderers="+rs.Length);if(bounds.size.x<100||bounds.size.x>5000||bounds.size.z<100||bounds.size.z>5000)throw new System.Exception("Invalid metre scale "+bounds);
 var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.name="Test ground";ground.transform.position=new Vector3(bounds.center.x,-.2f,bounds.center.z);ground.transform.localScale=new Vector3(bounds.size.x+100,.2f,bounds.size.z+100);
 var gm=new Material(Shader.Find("Standard"));gm.color=new Color(.25f,.33f,.22f);AssetDatabase.CreateAsset(gm,"Assets/BlosmComparison/Ground.mat");ground.GetComponent<Renderer>().sharedMaterial=gm;
 var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.2f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(45,-35,0);RenderSettings.ambientLight=new Color(.5f,.55f,.6f);
 var c=new GameObject("Comparison camera").AddComponent<Camera>();c.transform.position=new Vector3(-350,500,-450);c.transform.LookAt(Vector3.zero);c.farClipPlane=5000;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.55f,.7f,.85f);c.gameObject.AddComponent<BlosmComparisonQA>();
 EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),"Assets/Scenes/BlosmComparison.unity");AssetDatabase.SaveAssets();
 var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/BlosmComparison.unity"},target=BuildTarget.StandaloneWindows64,locationPathName="Build/BlosmComparison/BlosmComparison.exe"});if(result.summary.result!=BuildResult.Succeeded)throw new System.Exception("Build failed");Debug.Log("BLOSM_BUILD_OK");}
}

