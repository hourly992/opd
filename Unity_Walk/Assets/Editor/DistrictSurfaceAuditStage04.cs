using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Independent geometry sampling. Reads authored routes and physics, never the
// earlier generated surfaceChecks, never opens or saves an editor scene.
public static class DistrictSurfaceAuditStage04
{
    const float WalkStep=.30f, WalkHalfWidth=.45f, DriveStep=.40f, DriveHalfWidth=.70f;
    const float MaxHeightJump=.22f, MinimumNormalY=.82f;

    [Serializable] public sealed class Finding
    {
        public string category,routeType,name,zone,lane,collider,material;
        public Vector3 point,actual,normal;
        public float chainage,heightDifference;
        public bool expectedStairTransition;
    }
    [Serializable] public sealed class Zone
    {
        public string name;
        public int routes,samples,hardHits,gaps,normalFindings,heightJumps,expectedStairJumps,doorBodySamples;
    }
    [Serializable] public sealed class RouteResult
    {
        public string type,name;
        public float length,sampleStep,halfWidth,doorTrimStart,doorTrimEnd;
        public int samples,hardHits,gaps,normalFindings,heightJumps,expectedStairJumps,doorBodySamples;
    }
    [Serializable] public sealed class Report
    {
        public string scene,checkedUtc,note;
        public bool passed;
        public int pedestrianRoutes,parkingRoutes,totalSamples,hardHits,gaps,normalFindings,heightJumps,expectedStairJumps,doorBodySamples;
        public float pedestrianStep,pedestrianHalfWidth,parkingStep,parkingHalfWidth,maximumHeightJump,minimumNormalY;
        public Zone[] zones;
        public RouteResult[] routes;
        public Finding[] findings;
    }
    sealed class Probe
    {
        public bool hard,doorBody;
        public Vector3 point,normal;
        public string collider,material;
    }
    sealed class Sample
    {
        public Vector3 point,tangent;
        public float distance;
    }

    [MenuItem("Street/Audit district route widths and pavement gaps")]
    public static void Check()
    {
        if(!SceneManager.GetActiveScene().IsValid())throw new InvalidOperationException("No active scene to audit.");
        var access=UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        if(!access)throw new InvalidOperationException("The current scene has no ResidentialAccess metadata. Open the existing Street scene first.");
        Physics.SyncTransforms();
        var report=new Report{
            scene=SceneManager.GetActiveScene().path,checkedUtc=DateTime.UtcNow.ToString("O"),
            note="Independent center and width samples along authored routes. No earlier surfaceChecks consumed; no scene edit/open/save. Door ends are detected against existing entrance markers and trimmed by up to 1m. A sample inside a tall building body immediately beside a door is separately reported as doorBodySamples, not treated as a lawn gap. Parked cars and vegetation are excluded from floor classification. Results concern geometric coverage, not surveyed geographic accuracy.",
            pedestrianStep=WalkStep,pedestrianHalfWidth=WalkHalfWidth,parkingStep=DriveStep,parkingHalfWidth=DriveHalfWidth,
            maximumHeightJump=MaxHeightJump,minimumNormalY=MinimumNormalY};
        var findings=new List<Finding>();var routes=new List<RouteResult>();
        var zones=new Dictionary<string,Zone>();
        foreach(string name in new[]{"north","west","center","east","south"})zones.Add(name,new Zone{name=name});
        var doors=UnityEngine.Object.FindObjectsByType<Transform>()
            .Where(t=>t.name.StartsWith("Access · ",StringComparison.Ordinal)||t.name.StartsWith("Подход ",StringComparison.Ordinal))
            .Select(t=>t.position).ToArray();
        var bodies=UnityEngine.Object.FindObjectsByType<StreetBuilding>()
            .SelectMany(b=>b.bodyColliders??Array.Empty<Collider>()).OfType<BoxCollider>()
            .Where(c=>c&&c.enabled&&c.bounds.size.y>2).Distinct().ToArray();
        foreach(var route in access.routes??Array.Empty<ResidentialAccess.Route>())
        {
            Inspect("pedestrian",route.address,route.points,WalkStep,WalkHalfWidth,true,doors,bodies,zones,findings,routes);
            report.pedestrianRoutes++;
        }
        foreach(var parking in UnityEngine.Object.FindObjectsByType<ParkingSite>())
        {
            if(parking.usesExistingSurface&&(parking.accessRoute==null||parking.accessRoute.Length<2))
            {
                var result=new RouteResult{type="parking-existing-surface",name=parking.name};routes.Add(result);
                foreach(var vehicle in parking.vehicles??Array.Empty<StreetVehicle>())foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
                {
                    var p=vehicle.transform.TransformPoint(vehicle.localBounds.center+new Vector3(x*vehicle.localBounds.extents.x,0,z*vehicle.localBounds.extents.z));p.y=.065f;
                    var probe=Ground(p,false,bodies);var zone=zones[ZoneAt(p)];zone.samples++;result.samples++;
                    if(probe.hard){zone.hardHits++;result.hardHits++;}
                    else{zone.gaps++;result.gaps++;findings.Add(new Finding{category="uncovered-existing-parking",routeType=result.type,name=parking.name,zone=zone.name,point=p,actual=probe.point,collider=probe.collider,material=probe.material});}
                }
                report.parkingRoutes++;continue;
            }
            Inspect("parking",parking.name+" / "+parking.address,parking.accessRoute,DriveStep,DriveHalfWidth,false,doors,bodies,zones,findings,routes);
            report.parkingRoutes++;
        }
        report.zones=zones.Values.ToArray();report.routes=routes.ToArray();report.findings=findings.ToArray();
        report.totalSamples=routes.Sum(r=>r.samples);report.hardHits=routes.Sum(r=>r.hardHits);
        report.gaps=routes.Sum(r=>r.gaps);report.normalFindings=routes.Sum(r=>r.normalFindings);
        report.heightJumps=routes.Sum(r=>r.heightJumps);report.expectedStairJumps=routes.Sum(r=>r.expectedStairJumps);
        report.doorBodySamples=routes.Sum(r=>r.doorBodySamples);
        report.passed=report.totalSamples>0&&report.gaps==0&&report.normalFindings==0&&report.heightJumps==0;
        Directory.CreateDirectory("street_qa");
        File.WriteAllText("street_qa/district_stage04_geometry_audit.json",JsonUtility.ToJson(report,true));
        Debug.Log("DISTRICT_STAGE04_AUDIT samples="+report.totalSamples+" hardHits="+report.hardHits+" gaps="+report.gaps
            +" normalFindings="+report.normalFindings+" heightJumps="+report.heightJumps+" doorBodySamples="+report.doorBodySamples+" passed="+report.passed);
    }

    static string ZoneAt(Vector3 point)
    {
        if(point.z>85)return "north";
        if(point.z<=-55)return "south";
        if(point.x<-25)return "west";
        return point.x<100?"center":"east";
    }
    static float Horizontal(Vector3 a,Vector3 b)=>new Vector2(a.x-b.x,a.z-b.z).magnitude;
    static float NearestDoor(Vector3 point,Vector3[] doors)=>doors.Length==0?float.PositiveInfinity:doors.Min(d=>Horizontal(point,d));
    static bool ExpectedSteps(string name)
    {
        name=(name??"").ToLowerInvariant();
        return name.Contains("ступ")||name.Contains("steps")||name.Contains("stair");
    }
    static void Inspect(string type,string name,Vector3[] input,float step,float halfWidth,bool trimDoor,
        Vector3[] doors,BoxCollider[] bodies,Dictionary<string,Zone> zones,List<Finding> findings,List<RouteResult> results)
    {
        var points=new List<Vector3>();
        foreach(var p in input??Array.Empty<Vector3>())if(points.Count==0||Horizontal(points[points.Count-1],p)>.005f)points.Add(p);
        var result=new RouteResult{type=type,name=name??"",sampleStep=step,halfWidth=halfWidth};results.Add(result);
        if(points.Count<2)
        {
            var p=points.Count==0?Vector3.zero:points[0];result.gaps++;
            var zone=zones[ZoneAt(p)];zone.gaps++;zone.routes++;
            findings.Add(new Finding{category="route-too-short",routeType=type,name=name,zone=zone.name,point=p});return;
        }
        var distances=new float[points.Count];
        for(int i=1;i<points.Count;i++)distances[i]=distances[i-1]+Horizontal(points[i-1],points[i]);
        result.length=distances[distances.Length-1];
        float start=0,end=result.length;
        if(trimDoor)
        {
            float first=NearestDoor(points[0],doors),last=NearestDoor(points[points.Count-1],doors);
            float trim=Mathf.Min(1,result.length*.4f);
            if(first<=2.5f&&first<=last){start=trim;result.doorTrimStart=trim;}
            else if(last<=2.5f){end-=trim;result.doorTrimEnd=trim;}
        }
        var touchedZones=new HashSet<string>();var previous=new Probe[3];
        bool steps=ExpectedSteps(name);
        int count=Mathf.Max(1,Mathf.CeilToInt((end-start)/step));
        for(int s=0;s<=count;s++)
        {
            float d=Mathf.Lerp(start,end,(float)s/count);
            int segment=1;while(segment<distances.Length-1&&distances[segment]<d)segment++;
            float t=Mathf.InverseLerp(distances[segment-1],distances[segment],d);
            Vector3 p=Vector3.Lerp(points[segment-1],points[segment],t);
            Vector3 tangent=points[segment]-points[segment-1];tangent.y=0;tangent.Normalize();
            Vector3 right=new Vector3(tangent.z,0,-tangent.x);
            for(int lane=0;lane<3;lane++)
            {
                float offset=lane==0?0:lane==1?-halfWidth:halfWidth;
                Vector3 query=p+right*offset;string zoneName=ZoneAt(query);var zone=zones[zoneName];touchedZones.Add(zoneName);
                var probe=Ground(query,trimDoor&&NearestDoor(query,doors)<1.6f,bodies);
                result.samples++;zone.samples++;
                string laneName=lane==0?"center":lane==1?"left":"right";
                void Add(string category,float difference=0,bool expected=false)
                {
                    findings.Add(new Finding{category=category,routeType=type,name=name,zone=zoneName,lane=laneName,
                        point=query,actual=probe.point,normal=probe.normal,collider=probe.collider,material=probe.material,
                        chainage=d,heightDifference=difference,expectedStairTransition=expected});
                }
                if(probe.doorBody)
                {
                    result.doorBodySamples++;zone.doorBodySamples++;previous[lane]=null;continue;
                }
                if(!probe.hard)
                {
                    result.gaps++;zone.gaps++;Add("uncovered-hardscape");previous[lane]=null;continue;
                }
                result.hardHits++;zone.hardHits++;
                if(probe.normal.y<MinimumNormalY)
                {
                    result.normalFindings++;zone.normalFindings++;Add("surface-normal");
                }
                if(previous[lane]!=null)
                {
                    float change=probe.point.y-previous[lane].point.y;
                    if(Mathf.Abs(change)>MaxHeightJump)
                    {
                        if(steps){result.expectedStairJumps++;zone.expectedStairJumps++;Add("expected-stair-height-jump",change,true);}
                        else{result.heightJumps++;zone.heightJumps++;Add("height-jump",change);}
                    }
                }
                previous[lane]=probe;
            }
        }
        foreach(string nameOfZone in touchedZones)zones[nameOfZone].routes++;
    }

    static Probe Ground(Vector3 point,bool allowDoorBody,BoxCollider[] bodies)
    {
        var result=new Probe{point=point,normal=Vector3.zero,collider="none",material="none"};
        var hits=Physics.RaycastAll(point+Vector3.up*3,Vector3.down,5,~0,QueryTriggerInteraction.Ignore)
            .OrderByDescending(hit=>hit.point.y);
        foreach(var hit in hits)
        {
            var collider=hit.collider;
            if(collider.GetComponentInParent<StreetVehicle>())continue;
            if(hit.point.y>point.y+1||hit.point.y<point.y-1.25f||hit.normal.y<.1f)continue;
            Material material=HitMaterial(hit);
            if(result.collider=="none")
            {
                result.point=hit.point;result.normal=hit.normal;result.collider=collider.name;result.material=material?material.name:"none";
            }
            if(!Hard(collider,material))continue;
            result.hard=true;result.point=hit.point;result.normal=hit.normal;result.collider=collider.name;result.material=material?material.name:"entrance-step-collider";
            return result;
        }
        if(allowDoorBody)
        {
            var test=point+Vector3.up*.4f;
            foreach(var body in bodies)
                if(body.bounds.Contains(test)&&(body.ClosestPoint(test)-test).sqrMagnitude<.000001f)
                {
                    result.doorBody=true;result.collider=body.name;return result;
                }
        }
        return result;
    }
    static Material HitMaterial(RaycastHit hit)
    {
        var renderer=hit.collider.GetComponent<Renderer>();
        if(!renderer)return null;
        var materials=renderer.sharedMaterials;
        var meshCollider=hit.collider as MeshCollider;
        if(!meshCollider||!meshCollider.sharedMesh||hit.triangleIndex<0)return materials.Length==1?materials[0]:null;
        int end=0;
        for(int i=0;i<meshCollider.sharedMesh.subMeshCount;i++)
        {
            end+=(int)meshCollider.sharedMesh.GetIndexCount(i)/3;
            if(hit.triangleIndex<end)return i<materials.Length?materials[i]:null;
        }
        return null;
    }
    static bool Hard(Collider collider,Material material)
    {
        string name=collider.name.ToLowerInvariant();
        if(name=="grass"||collider is TerrainCollider)return false;
        if(material)
        {
            float kind=material.HasProperty("_SurfaceClass")?material.GetFloat("_SurfaceClass"):0;
            if(kind>.5f&&kind<1.5f)return false;
            string m=material.name.ToLowerInvariant();
            if(m.Contains("grass")||m.Contains("leaf")||m.Contains("foliage")||m.Contains("bark")||m.Contains("mulch")||m.Contains("soil")||m.Contains("rubber"))return false;
            if(kind>=3.5f||m.Contains("asphalt")||m.Contains("pav")||m.Contains("concrete")||m.Contains("kerb")||m.Contains("curb")||m.Contains("edge"))return true;
        }
        if(collider is BoxCollider&&collider.bounds.size.y<=1.2f)
            return collider.GetComponentInParent<StreetBuilding>()||name.Contains("ступ")||name.Contains("step")||name.Contains("threshold")||name.Contains("порог");
        return false;
    }
}
