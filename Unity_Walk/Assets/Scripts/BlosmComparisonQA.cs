using UnityEngine;
using System.Collections;
using System.IO;
public class BlosmComparisonQA : MonoBehaviour {
 IEnumerator Start(){if(!System.Array.Exists(System.Environment.GetCommandLineArgs(),a=>a=="-blosmQA"))yield break;
 yield return new WaitForSeconds(3);Directory.CreateDirectory("comparison_blosm");var cam=GetComponent<Camera>();var rt=new RenderTexture(1600,900,24);cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes("comparison_blosm/unity_preview.png",tex.EncodeToPNG());cam.targetTexture=null;RenderTexture.active=null;Destroy(rt);Destroy(tex);
 var rs=FindObjectsByType<MeshRenderer>();int missing=0;foreach(var r in rs)foreach(var m in r.sharedMaterials)if(!m||!m.shader.isSupported)missing++;
 File.WriteAllText("comparison_blosm/unity_report.json","{\"renderers\":"+rs.Length+",\"missingMaterials\":"+missing+",\"engine\":\""+Application.unityVersion+"\"}");yield return new WaitForSeconds(2);Application.Quit(missing==0?0:2);}
}

