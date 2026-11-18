using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class StreetPhotoBoundaryRepair
{
    private const string RelocationOutput = PathRoot + "/SeparatedGarageCanopies";
    private const float WestCenter = 21.30f, EastCenter = 33.75f, HalfGarage = 2.46f;
    private const float GarageMouth = -50f, BayEnd = -29.30f;
    private const string LinkName = "Примыкание к дому · ";

    [MenuItem("Street/Separate southern garage canopies")]
    public static void SeparateCanopies()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Street.unity", OpenSceneMode.Single);
        var court = scene.GetRootGameObjects().Single(x => x.name == CourtRootName);
        var repair = scene.GetRootGameObjects().Single(x => x.name == NewRootName);
        var deck = court.transform.Find("Подъём и асфальтовая площадка");
        var entries = court.GetComponentsInChildren<UndergroundParkingEntrance>().OrderBy(e => e.transform.position.x).ToArray();
        if (entries.Length != 2 || Mathf.Abs(entries[0].transform.position.x - 22.9f) > .01f ||
            Mathf.Abs(entries[1].transform.position.x - 31.1f) > .01f)
            throw new InvalidOperationException("Expected the original south garage pair.");

        var houseSide = new[]
        {
            new Side(WestCenter, -1f, 17.96f, -25.94f, "Запад"),
            new Side(EastCenter, 1f, 36.25f, -25.28f, "Восток")
        };
        if (!AssetDatabase.IsValidFolder(RelocationOutput))
            AssetDatabase.CreateFolder(PathRoot, "SeparatedGarageCanopies");

        var deckCollider = deck.GetComponent<MeshCollider>();
        var asphalt = deck.GetComponent<MeshRenderer>().sharedMaterial;
        var concrete = entries[0].GetComponent<MeshRenderer>().sharedMaterials[1];
        var lamp = UnityEngine.Object.FindObjectsByType<MeshFilter>().Single(f => f.sharedMesh &&
            f.sharedMesh.name == "CourtyardLamp" && Vector2.Distance(
                new Vector2(f.transform.position.x, f.transform.position.z), new Vector2(19.995f, -34f)) < .05f).transform;

        Physics.SyncTransforms();
        // Use the central ramp in the saved scene as the height reference for
        // the new shoulders, including the slope at the far end.
        float Height(float x, float z)
        {
            if (deckCollider.Raycast(new Ray(new Vector3(x, 2f, z), Vector3.down), out var hit, 3f))
                return hit.point.y;
            throw new InvalidOperationException($"The original ramp is missing at ({x:F2}, {z:F2}).");
        }

        var approach = new MeshForm(1);
        var road = UnityEngine.Object.FindObjectsByType<MeshCollider>().Single(c =>
            c.sharedMesh && AssetDatabase.GetAssetPath(c.sharedMesh) == PathRoot + "/Surfaces/ReferenceRoad.asset");
        for (int i = 0; i < houseSide.Length; i++)
        {
            var side = houseSide[i];
            var position = entries[i].transform.position;
            entries[i].transform.position = new Vector3(side.Center, position.y, position.z);

            float centerEdge = side.Direction < 0 ? InnerWest : InnerEast;
            TopStrip(approach, side.Inner, centerEdge, GarageMouth, -26.801f, (_, z) => Height(27f, z));
            var stations = new[] { 0f, .15f, .55f, 1.2f, 2.2f, 3.2f, 5f };
            float startHeight(float x)
            {
                foreach (float offset in new[] { -.045f, -.18f, -.40f })
                {
                    if (road.Raycast(new Ray(new Vector3(x, 2, FrontAt(x) + offset), Vector3.down),
                        out var hit, 3f))
                        return hit.point.y - .003f;
                }
                var ray = new Ray(new Vector3(x, 1f, FrontAt(x) - .08f), Vector3.down);
                return Physics.Raycast(ray, out var verge, 1.2f) ? Mathf.Min(.065f, verge.point.y) : .032f;
            }
            Vector3 ApproachPoint(float x, float offset)
            {
                float front = FrontAt(x);
                float z = Mathf.Min(front + offset, GarageMouth);
                float y = Mathf.Lerp(startHeight(x), position.y, Mathf.SmoothStep(0f, 1f, offset / .72f));
                return new Vector3(x, y, z);
            }
            float outer = Mathf.Min(side.Outer, centerEdge);
            float inner = Mathf.Max(side.Outer, centerEdge);
            for (float x = outer; x < inner - .001f;)
            {
                float next = Mathf.Min(x + .45f, inner);
                for (int j = 0; j + 1 < stations.Length; j++)
                    approach.Quad(ApproachPoint(x, stations[j]), ApproachPoint(x, stations[j + 1]),
                        ApproachPoint(next, stations[j + 1]), ApproachPoint(next, stations[j]), 0);
                x = next;
            }

            AddRearConnection(repair.transform, side, asphalt, concrete);
        }

        // The new asphalt and ramps replace the old wings inside their
        // footprints. The original central walk mesh keeps its own triangles.
        var oldDeck = deck.GetComponent<MeshFilter>().sharedMesh;
        Mesh core = GarageSurfaceCut.Clip(oldDeck, deck.localToWorldMatrix, Rectangle(15f, InnerWest, -56f, -26f), out _);
        Mesh clipped = GarageSurfaceCut.Clip(core, deck.localToWorldMatrix, Rectangle(InnerEast, 39f, -56f, -26f), out _);
        UnityEngine.Object.DestroyImmediate(core);
        var main = SaveRelocation(clipped, "CentralAsphalt");
        deck.GetComponent<MeshFilter>().sharedMesh = main;
        deckCollider.sharedMesh = main;
        foreach (var name in new[] { "Восточная верхняя площадка", "Западная верхняя площадка",
            "Восточный парапет от обхода", "Западный парапет от обхода" })
        {
            var previous = repair.transform.Find(name);
            if (!previous) throw new InvalidOperationException($"Missing geometry to replace: {name}.");
            UnityEngine.Object.DestroyImmediate(previous.gameObject);
        }

        var passages = new[]
        {
            new Vector2(houseSide[0].Outer, FrontAt(houseSide[0].Outer)),
            new Vector2(houseSide[1].Outer, FrontAt(houseSide[1].Outer)),
            new Vector2(houseSide[1].Outer, BayEnd),
            new Vector2(houseSide[0].Outer, BayEnd)
        };
        CutGround(passages);
        GarageLawnMask.Apply(scene, passages, RelocationOutput);
        Install(repair.transform, "Асфальт у разнесённых въездов", approach, new[] { asphalt }, "GarageApproaches");
        lamp.position = new Vector3(23.98f, DeckY, -34f);

        Physics.SyncTransforms();
        if (!CheckHouseContact("Дом №5", new Vector3(houseSide[0].HouseX, .65f, houseSide[0].HouseZ)) ||
            !CheckHouseContact("Building_114788164", new Vector3(houseSide[1].HouseX, .65f, houseSide[1].HouseZ)))
            throw new InvalidOperationException("A new canopy connection misses its house.");

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the separated canopies.");
        AssetDatabase.SaveAssets();
        Debug.Log($"GARAGE_CANOPIES_SEPARATED {WestCenter:F2} {EastCenter:F2}");
    }

    private readonly struct Side
    {
        public readonly float Center, Direction, HouseX, HouseZ;
        public readonly string Label;

        public Side(float center, float direction, float houseX, float houseZ, string label)
        {
            Center = center;
            Direction = direction;
            HouseX = houseX;
            HouseZ = houseZ;
            Label = label;
        }

        public float Outer => Center + Direction * HalfGarage;
        public float Inner => Center - Direction * HalfGarage;
    }

    private static Vector2[] Rectangle(float x0, float x1, float z0, float z1) =>
        new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) };

    private static float FrontAt(float x) => -54.46068f - .017343f * (x - 27f);

    private static void TopStrip(MeshForm form, float a, float b, float start, float end, Func<float, float, float> height)
    {
        float left = Mathf.Min(a,b), right = Mathf.Max(a,b);
        Vector3 Point(float x, float z) => new(x, height(x, z), z);
        var stations = new[] { start, -44f, -31.8f, end }
            .Where(z => z >= start && z <= end).Distinct().OrderBy(z => z).ToArray();
        for (int section = 1; section < stations.Length; section++)
        {
            float from = stations[section - 1], to = stations[section];
            float z0 = from, z1 = to;
            form.Quad(Point(left,z0),Point(left,z1),Point(right,z1),Point(right,z0),0);
        }
    }

    private static void AddRearConnection(Transform root, Side side, Material asphalt, Material concrete)
    {
        var form = new MeshForm(2);
        float Edge(float z) => Mathf.Lerp(side.Outer, side.HouseX, Mathf.InverseLerp(BayEnd, side.HouseZ, z));
        Vector3 Rim(float x,float z) => new(x,WingY,z);
        var a = Rim(side.Outer,RoofEnd);
        var b = Rim(side.Outer,BayEnd);
        var c = Rim(side.HouseX,side.HouseZ);
        var d = Rim(side.Inner,side.HouseZ);
        var e = Rim(side.Inner,BayEnd);
        var f = Rim(side.Inner,RoofEnd);
        void Top(Vector3 p, Vector3 q, Vector3 r, Vector3 s)
        {
            if (side.Direction < 0) form.Quad(p,q,r,s,0);
            else form.Quad(s,r,q,p,0);
        }
        Top(a,b,e,f);
        Top(b,c,d,e);

        // Exposed concrete remains attached to the moved outer garage wall
        // and enters the house plinth, closing the outside approach.
        const float cap = 1.16f;
        Vector3 P(float x,float y,float z) => new(x,y,z);
        void Wall(float z0,float z1)
        {
            float x0=Edge(z0), x1=Edge(z1), offset=-side.Direction*.23f;
            Vector3 p=P(x0,cap,z0), q=P(x1,cap,z1);
            Vector3 r=P(x1+offset,cap,z1), s=P(x0+offset,cap,z0);
            void Face(Vector3 u,Vector3 v,Vector3 w,Vector3 x)
            {
                if (side.Direction < 0) form.Quad(u,v,w,x,1);
                else form.Quad(x,w,v,u,1);
            }
            Face(p,q,r,s);
            Face(P(x0,-.13f,z0),P(x1,-.13f,z1),q,p);
            Face(s,r,P(x1+offset,WingY,z1),P(x0+offset,WingY,z0));
            Face(p,s,P(x0+offset,-.13f,z0),P(x0,-.13f,z0));
        }
        Wall(RoofEnd, BayEnd);
        Wall(BayEnd, side.HouseZ);
        // Back and inside retaining faces support the terrace at the tie.
        var lowD=P(d.x,-.07f,d.z);
        var lowC=P(c.x,-.07f,c.z);
        var lowF=P(f.x,-.07f,f.z);
        if (side.Direction < 0)
        {
            form.Quad(c,lowC,lowD,d,1);
            form.Quad(f,d,lowD,lowF,1);
        }
        else
        {
            form.Quad(d,lowD,lowC,c,1);
            form.Quad(lowF,lowD,d,f,1);
        }
        Install(root,LinkName + side.Label,form,new[] { asphalt,concrete },"GarageHouseLink" + (side.Direction < 0 ? "West" : "East"));
    }

    private static void CutGround(Vector2[] footprint)
    {
        var area = GarageSurfaceCut.Bounds(footprint);
        var changed = 0;
        string preserved = PathRoot + "/Surfaces/ReferenceRoad.asset";
        foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            var source = filter.sharedMesh;
            if (!source || !source.isReadable || !filter.TryGetComponent<MeshRenderer>(out var renderer) || !renderer.enabled)
                continue;
            Bounds box = renderer.bounds;
            if (box.min.y < -.55f || box.max.y > .45f || box.max.x <= area.xMin || box.min.x >= area.xMax ||
                box.max.z <= area.yMin || box.min.z >= area.yMax) continue;
            var path = AssetDatabase.GetAssetPath(source);
            if (path == preserved || path.StartsWith(Output + "/", StringComparison.Ordinal) ||
                path.EndsWith("/CourtAsphalt.asset", StringComparison.Ordinal)) continue;
            bool surface = path.StartsWith(PathRoot + "/", StringComparison.Ordinal) ||
                path.StartsWith("Assets/PhotoReconstruction/Stage03Surfaces/", StringComparison.Ordinal) ||
                path.StartsWith("Assets/PhotoReconstruction/Stage04Courtyard/", StringComparison.Ordinal) ||
                path.StartsWith("Assets/Art/AutumnGrass/Cell_", StringComparison.Ordinal) ||
                path.StartsWith("Assets/Art/PaintedDistrict/Leaves/", StringComparison.Ordinal);
            if (!surface) continue;
            var cut = GarageSurfaceCut.Clip(source, filter.transform.localToWorldMatrix, footprint, out _);
            if (!cut) continue;
            cut = SaveRelocation(cut, $"GarageEdge{++changed:D2}_{source.name}");
            filter.sharedMesh = cut;
            foreach (var collider in filter.GetComponents<MeshCollider>())
                if (collider.sharedMesh == source) collider.sharedMesh = cut;
        }
        Debug.Log($"GARAGE_SITE_LOCAL_SURFACES {changed}");
    }

    private static Mesh SaveRelocation(Mesh mesh,string name)
    {
        RemoveClippingSlivers(mesh);
        var path = $"{RelocationOutput}/{name}.asset";
        var previous = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!previous)
        {
            AssetDatabase.CreateAsset(mesh,path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh,previous);
        UnityEngine.Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(previous);
        return previous;
    }

    private static void RemoveClippingSlivers(Mesh mesh)
    {
        var vertices = mesh.vertices;
        for (int slot = 0; slot < mesh.subMeshCount; slot++)
        {
            var indices = mesh.GetTriangles(slot);
            var retained = new List<int>(indices.Length);
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
                float longestEdge = Mathf.Max((b - a).sqrMagnitude, Mathf.Max((c - a).sqrMagnitude, (c - b).sqrMagnitude));
                // Float clipping of nearly vertical source faces can leave
                // zero-width triangles that PhysX treats as thin barriers.
                const float minimumAltitudeSquared = .00008f * .00008f;
                if (Vector3.Cross(b - a, c - a).sqrMagnitude <= longestEdge * minimumAltitudeSquared) continue;
                retained.Add(indices[i]);
                retained.Add(indices[i + 1]);
                retained.Add(indices[i + 2]);
            }
            if (retained.Count != indices.Length) mesh.SetTriangles(retained, slot, false);
        }
    }

    private static void Install(Transform root,string name,MeshForm form,Material[] materials,string asset)
    {
        var node=new GameObject(name);
        node.transform.SetParent(root,false);
        var mesh=SaveRelocation(form.Build(name),asset);
        node.AddComponent<MeshFilter>().sharedMesh=mesh;
        node.AddComponent<MeshRenderer>().sharedMaterials=materials;
        node.AddComponent<MeshCollider>().sharedMesh=mesh;
    }

    public static void BuildSeparatedCanopies()
    {
        const string path = "Build/StreetWindowsHDRPSeparatedCanopies/DuninaGorkavicha.exe";
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Street.unity", "Assets/Scenes/Street_LeafStudy.unity" },
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = Path.GetFullPath(path)
        });
        if (result.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Separated garage player build failed.");
        Debug.Log("GARAGE_CANOPIES_BUILD " + path);
    }

    public static void SeparateCanopiesAndBuild()
    {
        SeparateCanopies();
        BuildSeparatedCanopies();
    }
}
