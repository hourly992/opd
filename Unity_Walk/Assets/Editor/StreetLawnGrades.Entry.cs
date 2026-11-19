using System;
using System.Linq;
using UnityEngine;

public static partial class StreetLawnGrades
{
    private const float WalkWest = 22.08f, WalkEast = 23.76f;
    private const float WalkStart = -25.94f, WalkEnd = -29.26f;

    private static void ConnectHouse5Walk(Transform root, EditReport report)
    {
        // The existing pilot courtyard path comes in at x=23, north of the
        // west terrace. The garage bay and canopy end before the walk cut.
        var filter = UnityEngine.Object.FindObjectsByType<MeshFilter>()
            .Single(x => x.name == "Примыкание к дому · Запад");
        var collider = filter.GetComponent<MeshCollider>();
        var source = filter.sharedMesh;
        var materials = filter.GetComponent<MeshRenderer>().sharedMaterials;
        var boundary = new[] {
            new Vector2(WalkWest,WalkEnd), new Vector2(WalkEast + .01f,WalkEnd),
            new Vector2(WalkEast + .01f,WalkStart + .03f), new Vector2(WalkWest,WalkStart + .03f)
        };
        var clipped = GarageSurfaceCut.Clip(source,filter.transform.localToWorldMatrix,boundary,out int clippedTriangles);
        if (!clipped || clippedTriangles < 3 || collider.sharedMesh != source)
            throw new InvalidOperationException("The expected pedestrian wall was not found on the west terrace.");
        filter.sharedMesh = Save(clipped,"House5WestTerracePedestrianCut");
        collider.sharedMesh = filter.sharedMesh;
        Physics.SyncTransforms();

        float GroundAt(float x)
        {
            var ray = new Ray(new Vector3(x,.55f,WalkStart + .07f),Vector3.down);
            var hits = Physics.RaycastAll(ray,.90f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            var grade = hits.Where(hit => hit.collider != collider && hit.normal.y > .70f)
                .OrderByDescending(hit => hit.point.y).FirstOrDefault();
            if (!grade.collider || grade.point.y < -.16f || grade.point.y > .22f)
                throw new InvalidOperationException($"The original house 5 footpath is missing at x={x:F2}.");
            return grade.point.y + .003f;
        }

        float leftY = GroundAt(WalkWest + .015f), rightY = GroundAt(WalkEast - .015f);
        const float top = .855f;
        var a = new Vector3(WalkWest,leftY,WalkStart + .07f);
        var b = new Vector3(WalkEast,rightY,WalkStart + .07f);
        var c = new Vector3(WalkEast,top,WalkEnd);
        var d = new Vector3(WalkWest,top,WalkEnd);
        var ramp = new SurfaceMesh();
        ramp.Triangle(a,b,c,true);
        ramp.Triangle(a,c,d,true);
        Install(ramp.Finish("House 5 pedestrian grade"),materials[0],root,
            "Вход во двор №5 · пешеходный переход","House5Walk");
        var edges = new SurfaceMesh();
        var raisedLeft = new Vector3(a.x,top,WalkStart);
        var outsideRight = new Vector3(b.x,-.09f,WalkStart);
        edges.Triangle(a,raisedLeft,d,false);
        edges.Triangle(d,raisedLeft,a,false);
        edges.Triangle(c,b,outsideRight,false);
        edges.Triangle(c,outsideRight,b,false);
        edges.Triangle(c,outsideRight,new Vector3(c.x,-.09f,c.z),false);
        edges.Triangle(c,new Vector3(c.x,-.09f,c.z),outsideRight,false);
        Install(edges.Finish("House 5 join faces"),materials[1],root,
            "Вход во двор №5 · стыки","House5WalkSide");
        report.entryBottom = (a + b) * .5f;
        report.entryTop = (c + d) * .5f;
    }

    public static void ApplyAndBuild()
    {
        Apply();
        Build();
    }
}
