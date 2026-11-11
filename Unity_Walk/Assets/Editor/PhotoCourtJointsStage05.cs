using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// Independent read-only inspection of the saved courtyard connection. In
// particular, a new floor is never used as its own adjoining-seam witness.
public static class PhotoCourtJointsStage05
{
    const float X0=41.7f,X1=60.141938f,Z0=37.155f,Z1=39.455f;
    const float AcrossStep=.15f,AlongStep=.2f,SeamOffset=.015f,MaximumSeamJump=.03f,ArtifactTolerance=.008f;
    const string Output="street_qa/district_stage05_editor/court_joint_audit.json";
    [Serializable] public sealed class Seam
    {
        public string edge,classification,neighborCollider,neighborMaterial;
        public Vector3 inside,outside,neighborPoint;
        public bool newFloorHit,oldFloorHit,passed;
        public float newHeight,neighborHeight,heightDifference;
    }
    [Serializable] public sealed class Artifact
    {
        public string gameObject,mesh,material;
        public int submesh,triangle;
        public Vector3 point;
        public float newFloorHeight,heightAboveFloor;
    }
    [Serializable] public sealed class Report
    {
        public string scene,checkedUtc;
        public string note="Read-only physical samples of the new No.7 connector: full width grid plus bilateral seam pairs. Each true paved neighbor is a different existing collider, not the new path itself. Kerbs/retaining edges and exposed soil are classified separately from continuous paving joins. Existing vertical hardscape triangles are clipped to the strict walk rectangle and compared with the actual new collider, not the generated vertex heights. This validates geometry, not surveyed geographic accuracy.";
        public bool passed,artifactListTruncated;
        public float alongStep=AlongStep,acrossStep=AcrossStep,seamOffset=SeamOffset,allowedSeamJump=MaximumSeamJump,
            artifactHeightTolerance=ArtifactTolerance,maximumSeamJump,maximumPhysicalSlope,maximumArtifactHeight;
        public int gridSamples,gridHits,gridNormalIssues,gridMissingSamples,seamSamples,newSeamMisses,
            pavedNeighbors,exposedLandEdges,retainedEdges,otherExposedEdges,excessiveSeamJumps,narrowUnpavedSeams,
            inspectedVerticalTriangles,internalArtifactTriangles;
        public Seam[] seams;
        public Vector3[] uncoveredGrid;
        public Artifact[] artifacts;
    }
    sealed class Hit
    {
        public bool paved,retained,natural;
        public string collider,material;
        public Vector3 point;
    }
    sealed class MeshInfo
    {
        public int[] ends;public Material[] materials;
        public Material At(int triangle){for(int i=0;i<ends.Length;i++)if(triangle<ends[i])return i<materials.Length?materials[i]:null;return null;}
    }

    public static void Audit()
    {
        if(SceneManager.GetActiveScene().path!="Assets/Scenes/Street.unity")throw new InvalidOperationException("Open the saved Street scene before auditing the courtyard seams.");
        Physics.SyncTransforms();
        var fresh=UnityEngine.Object.FindObjectsByType<MeshCollider>().SingleOrDefault(c=>c.enabled&&c.sharedMesh&&c.sharedMesh.name=="CourtDoorContinuousPaving05");
        if(!fresh)throw new InvalidOperationException("The Stage05 continuous courtyard floor is missing.");
        var report=new Report{scene=SceneManager.GetActiveScene().path,checkedUtc=DateTime.UtcNow.ToString("O")};
        var info=new Dictionary<Collider,MeshInfo>();
        foreach(var collider in UnityEngine.Object.FindObjectsByType<MeshCollider>())
        {
            var renderer=collider.GetComponent<Renderer>();if(!collider.enabled||!collider.sharedMesh||!renderer||!renderer.enabled)continue;
            int end=0;var data=new MeshInfo{ends=new int[collider.sharedMesh.subMeshCount],materials=renderer.sharedMaterials};
            for(int sub=0;sub<data.ends.Length;sub++){end+=(int)collider.sharedMesh.GetIndexCount(sub)/3;data.ends[sub]=end;}info.Add(collider,data);
        }
        var uncovered=new List<Vector3>();int nx=Mathf.CeilToInt((X1-X0)/AlongStep),nz=Mathf.CeilToInt((Z1-Z0)/AcrossStep);
        var heights=new float[nx+1,nz+1];
        for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
        {
            var point=new Vector3(Mathf.Lerp(X0+.006f,X1-.006f,x/(float)nx),0,Mathf.Lerp(Z0+.006f,Z1-.006f,z/(float)nz));report.gridSamples++;
            if(!NewHit(fresh,point,out var hit)){report.gridMissingSamples++;uncovered.Add(point);heights[x,z]=float.NaN;continue;}
            report.gridHits++;heights[x,z]=hit.point.y;if(hit.normal.y<.85f)report.gridNormalIssues++;
        }
        for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
        {
            if(!float.IsFinite(heights[x,z]))continue;
            if(x>0&&float.IsFinite(heights[x-1,z]))report.maximumPhysicalSlope=Mathf.Max(report.maximumPhysicalSlope,Mathf.Abs(heights[x,z]-heights[x-1,z])/((X1-X0-.012f)/nx));
            if(z>0&&float.IsFinite(heights[x,z-1]))report.maximumPhysicalSlope=Mathf.Max(report.maximumPhysicalSlope,Mathf.Abs(heights[x,z]-heights[x,z-1])/((Z1-Z0-.012f)/nz));
        }
        var seams=new List<Seam>();
        SampleEdge("west",new Vector3(X0,0,Z0+.01f),new Vector3(X0,0,Z1-.01f),Vector3.left,AcrossStep,fresh,info,seams,report);
        SampleEdge("east",new Vector3(X1,0,Z0+.01f),new Vector3(X1,0,Z1-.01f),Vector3.right,AcrossStep,fresh,info,seams,report);
        SampleEdge("south",new Vector3(X0+.01f,0,Z0),new Vector3(X1-.01f,0,Z0),Vector3.back,AlongStep,fresh,info,seams,report);
        SampleEdge("north",new Vector3(X0+.01f,0,Z1),new Vector3(X1-.01f,0,Z1),Vector3.forward,AlongStep,fresh,info,seams,report);
        var artifacts=new List<Artifact>();InspectVerticalTriangles(fresh,report,artifacts);
        report.seams=seams.ToArray();report.uncoveredGrid=uncovered.ToArray();report.artifacts=artifacts.ToArray();
        report.passed=report.gridMissingSamples==0&&report.gridNormalIssues==0&&report.maximumPhysicalSlope<.15f&&report.newSeamMisses==0&&report.excessiveSeamJumps==0&&report.narrowUnpavedSeams==0&&report.internalArtifactTriangles==0;
        Directory.CreateDirectory(Path.GetDirectoryName(Output));File.WriteAllText(Output,JsonUtility.ToJson(report,true));
        Debug.Log("COURT_JOINT_AUDIT05 passed="+report.passed+" grid="+report.gridHits+"/"+report.gridSamples+" pavedNeighbors="+report.pavedNeighbors+" exposedLand="+report.exposedLandEdges+" realSeamJump="+report.maximumSeamJump+" verticalArtifacts="+report.internalArtifactTriangles+" narrowUnpavedSeams="+report.narrowUnpavedSeams);
        if(!report.passed)throw new InvalidOperationException("Court joint audit found geometry blockers; see "+Output);
    }

    static void SampleEdge(string name,Vector3 start,Vector3 end,Vector3 outward,float step,MeshCollider fresh,Dictionary<Collider,MeshInfo> info,List<Seam> seams,Report report)
    {
        int count=Mathf.CeilToInt(Vector3.Distance(start,end)/step);
        for(int i=0;i<=count;i++)
        {
            var boundary=Vector3.Lerp(start,end,i/(float)count);var seam=new Seam{edge=name,inside=boundary-outward*SeamOffset,outside=boundary+outward*SeamOffset,passed=true};report.seamSamples++;
            if(!NewHit(fresh,seam.inside,out var own))
            {
                report.newSeamMisses++;seam.classification="missing_new_floor";seam.passed=false;seams.Add(seam);continue;
            }
            seam.newFloorHit=true;seam.newHeight=own.point.y;
            var neighbor=OldHit(seam.outside,fresh,info,own.point.y);seam.neighborCollider=neighbor.collider;seam.neighborMaterial=neighbor.material;seam.neighborPoint=neighbor.point;
            if(neighbor.paved)
            {
                report.pavedNeighbors++;seam.classification="paved_neighbor";seam.oldFloorHit=true;seam.neighborHeight=neighbor.point.y;seam.heightDifference=Mathf.Abs(own.point.y-neighbor.point.y);
                report.maximumSeamJump=Mathf.Max(report.maximumSeamJump,seam.heightDifference);
                if(seam.heightDifference>MaximumSeamJump){report.excessiveSeamJumps++;seam.passed=false;}
            }
            else if(neighbor.retained){report.retainedEdges++;seam.classification="intentional_kerb_or_retaining_edge";}
            else
            {
                // A paved floor just 6cm away distinguishes a narrow uncovered seam
                // from an ordinary unpaved outer boundary of this connector.
                var far=OldHit(boundary+outward*.06f,fresh,info,own.point.y);
                if(far.paved&&Mathf.Abs(far.point.y-own.point.y)<.15f)
                {report.narrowUnpavedSeams++;seam.classification="narrow_unpaved_seam_to_paving";seam.neighborCollider=far.collider;seam.neighborMaterial=far.material;seam.neighborPoint=far.point;seam.passed=false;}
                else if(neighbor.natural){report.exposedLandEdges++;seam.classification="exposed_land_edge";}
                else{report.otherExposedEdges++;seam.classification="exposed_edge_without_floor_neighbor";}
            }
            seams.Add(seam);
        }
    }
    static bool NewHit(MeshCollider fresh,Vector3 point,out RaycastHit hit)=>fresh.Raycast(new Ray(new Vector3(point.x,1.6f,point.z),Vector3.down),out hit,2.5f);
    static Hit OldHit(Vector3 point,MeshCollider fresh,Dictionary<Collider,MeshInfo> info,float expected)
    {
        var result=new Hit{collider="none",material="none",point=point};
        foreach(var hit in Physics.RaycastAll(new Vector3(point.x,1.6f,point.z),Vector3.down,3,~0,QueryTriggerInteraction.Ignore).OrderByDescending(h=>h.point.y))
        {
            if(hit.collider==fresh||hit.collider.GetComponentInParent<StreetVehicle>()||hit.normal.y<.3f||hit.point.y>expected+1||hit.point.y<expected-1)continue;
            Material material=null;if(info.TryGetValue(hit.collider,out var data))material=data.At(hit.triangleIndex);
            else if(hit.collider.GetComponent<Renderer>() is Renderer renderer&&renderer.sharedMaterials.Length==1)material=renderer.sharedMaterials[0];
            bool retained=Retained(material),natural=Natural(hit.collider,material),paved=!retained&&!natural&&Paved(hit.collider,material)&&hit.normal.y>.82f;
            if(!retained&&!natural&&!paved)continue;
            result=new Hit{paved=paved,retained=retained,natural=natural,collider=hit.collider.name,material=material?material.name:"none",point=hit.point};
            if(retained||paved)return result;
            // Keep the natural witness unless a genuine hard neighbor is below it.
        }
        return result;
    }
    static bool Natural(Collider collider,Material material)
    {
        if(collider&&collider.name=="Grass"||collider is TerrainCollider)return true;if(!material)return false;string n=material.name.ToLowerInvariant();
        float kind=material.HasProperty("_SurfaceClass")?material.GetFloat("_SurfaceClass"):0;
        return kind>.5f&&kind<1.5f||n.Contains("grass")||n.Contains("leaf")||n.Contains("foliage")||n.Contains("bark")||n.Contains("mulch")||n.Contains("soil")||n.Contains("woodland");
    }
    static bool Retained(Material material)
    {
        if(!material)return false;string n=material.name.ToLowerInvariant();return n.Contains("kerb")||n.Contains("curb")||n.Contains("coping")||n.Contains("retaining");
    }
    static bool Paved(Collider collider,Material material)
    {
        if(material)
        {
            float kind=material.HasProperty("_SurfaceClass")?material.GetFloat("_SurfaceClass"):0;string n=material.name.ToLowerInvariant();
            return kind>=3.5f||n.Contains("asphalt")||n.Contains("pav")||n.Contains("concrete")||n.Contains("footpath")||n.Contains("edge")||n=="path";
        }
        return collider is BoxCollider&&collider.bounds.size.y<1.2f&&(collider.name.Contains("threshold")||collider.name.Contains("step")||collider.name.Contains("порог")||collider.name.Contains("ступ"));
    }
    static void InspectVerticalTriangles(MeshCollider fresh,Report report,List<Artifact> artifacts)
    {
        foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            var mesh=filter.sharedMesh;var renderer=filter.GetComponent<Renderer>();
            if(!mesh||!renderer||!renderer.enabled||!filter.gameObject.activeInHierarchy||filter.gameObject==fresh.gameObject||filter.GetComponentInParent<StreetBuilding>()||filter.GetComponentInParent<StreetVehicle>())continue;
            var bounds=renderer.bounds;if(bounds.max.x<X0||bounds.min.x>X1||bounds.max.z<Z0||bounds.min.z>Z1||bounds.min.y>1.3f||bounds.max.y<0)continue;
            var positions=mesh.vertices;var materials=renderer.sharedMaterials;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var material=sub<materials.Length?materials[sub]:null;
                // Restrict this artifact test to vertical hardscape/slab/kerb faces.
                // Facades, doors, furniture and plant geometry are not mistaken for
                // a leftover road slab merely because they are vertical.
                if(!material||Natural(filter.GetComponent<Collider>(),material)||!Paved(filter.GetComponent<Collider>(),material)&&!Retained(material))continue;
                var indices=mesh.GetTriangles(sub);
                for(int at=0;at<indices.Length;at+=3)
                {
                    var a=filter.transform.TransformPoint(positions[indices[at]]);var b=filter.transform.TransformPoint(positions[indices[at+1]]);var c=filter.transform.TransformPoint(positions[indices[at+2]]);var cross=Vector3.Cross(b-a,c-a);
                    if(cross.sqrMagnitude<1e-12f||Mathf.Abs(cross.y)/cross.magnitude>.35f)continue;
                    var clipped=new List<Vector3>{a,b,c};clipped=Clip(clipped,0,X0+.002f,true);clipped=Clip(clipped,0,X1-.002f,false);clipped=Clip(clipped,2,Z0+.002f,true);clipped=Clip(clipped,2,Z1-.002f,false);
                    if(clipped.Count<2)continue;report.inspectedVerticalTriangles++;
                    var probes=new List<Vector3>(clipped);for(int i=0;i<clipped.Count;i++)probes.Add((clipped[i]+clipped[(i+1)%clipped.Count])*.5f);probes.Add(clipped.Aggregate(Vector3.zero,(p,q)=>p+q)/clipped.Count);
                    float maximum=0,level=0;Vector3 highest=Vector3.zero;
                    foreach(var p in probes)
                    {
                        if(!NewHit(fresh,p,out var hit))continue;float excess=p.y-hit.point.y;
                        if(excess>ArtifactTolerance&&excess<=1&&excess>maximum){maximum=excess;level=hit.point.y;highest=p;}
                    }
                    if(maximum<=ArtifactTolerance)continue;
                    report.internalArtifactTriangles++;report.maximumArtifactHeight=Mathf.Max(report.maximumArtifactHeight,maximum);
                    if(artifacts.Count<5000)artifacts.Add(new Artifact{gameObject=filter.name,mesh=mesh.name,material=material.name,submesh=sub,triangle=at/3,point=highest,newFloorHeight=level,heightAboveFloor=maximum});else report.artifactListTruncated=true;
                }
            }
        }
    }
    static List<Vector3> Clip(List<Vector3> source,int axis,float limit,bool minimum)
    {
        var result=new List<Vector3>();if(source.Count==0)return result;var previous=source[source.Count-1];bool prior=minimum?previous[axis]>=limit:previous[axis]<=limit;
        foreach(var current in source)
        {
            bool inside=minimum?current[axis]>=limit:current[axis]<=limit;
            if(inside!=prior){float difference=current[axis]-previous[axis];if(Mathf.Abs(difference)>1e-9f)result.Add(Vector3.Lerp(previous,current,(limit-previous[axis])/difference));}
            if(inside)result.Add(current);previous=current;prior=inside;
        }
        return result;
    }
}
