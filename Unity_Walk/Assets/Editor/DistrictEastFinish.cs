using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// A local edit of the photographed No.11 approach and a continuity audit of the saved east routes.
public static class DistrictEastFinish
{
    private const string Root = "Assets/PhotoReconstruction/Stage10East";
    private const string Applied = "Восток · подходы №11 и №13";
    private const string OriginalRamp = "Assets/PhotoReconstruction/Stage01/Meshes/Photo11LowThreshold.asset";
    private const string FinishedRamp = Root + "/Meshes/East11PavedThreshold.asset";
    private const float RampBackX = 125.4542f - .15f;
    private const float RampNoseX = 125.4542f - .95f;
    private const float DoorZ = 5.86925f;
    private const float HalfWidth = 1.2f;
    private const float SillY = .16f;
    private const float OldNoseY = .065f;
    private const string ShopPhoto = "https://avatars.mds.yandex.net/get-altay/16333276/2a00000198bb44206efe19b972c5a4dbad39/orig";
    private const string ShopPage = "https://yandex.ru/maps/org/91706029280/";
    private const string Shop13Page = "https://yandex.ru/maps/org/169841655322/";
    private const string Shop13SidePhoto = "https://avatars.mds.yandex.net/get-altay/16569030/2a00000198543ee61cdd8958713a87e97a35/orig";
    private const string Office13Page = "https://yandex.ru/maps/org/47436925079/";
    private const string Office13Photo = "https://avatars.mds.yandex.net/get-altay/5098734/2a00000187e4e7a7d172f43d7cc232a78f80/orig";

    [Serializable] private sealed class Audit
    {
        public int routes11, routes13, routeChecks, routeHitsBefore, routeHitsAfter;
        public int checkedRampPoints, rampJointChecks;
        public bool savedPavingAtNose, door13Ambiguous = true;
        public float rampNoseBefore, rampNoseAfterLeft, rampNoseAfterRight, largestRampRise, rampNoseCenterZ, rampNoseHalfWidth, maxRampJointGap;
        public string[] sources;
        public string note;
    }

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before updating the east approaches.");
        var evidence = GameObject.Find(Applied);

        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        var threshold = GameObject.Find("House 11 · low threshold and short approach");
        var collider = threshold ? threshold.GetComponent<MeshCollider>() : null;
        var filter = threshold ? threshold.GetComponent<MeshFilter>() : null;
        var renderer = threshold ? threshold.GetComponent<MeshRenderer>() : null;
        var original = AssetDatabase.LoadAssetAtPath<Mesh>(OriginalRamp);
        if (!access || !collider || !filter || !renderer || !filter.sharedMesh ||
            filter.sharedMesh != collider.sharedMesh || !original ||
            (filter.sharedMesh != original && AssetDatabase.GetAssetPath(filter.sharedMesh) != FinishedRamp) ||
            renderer.sharedMaterials.Length < 1 || !renderer.sharedMaterial)
            throw new InvalidOperationException("The saved No.11 threshold and its matching visible and collision meshes are required.");

        var floors = access.surfaces.Where(c => c && c.enabled && c.sharedMesh).Distinct().ToArray();
        var routeSamples = EastRouteSamples(access, out int routes11, out int routes13);
        if (routes11 < 1 || routes13 < 1 || routeSamples.Length == 0)
            throw new InvalidOperationException("The saved routes for both east addresses are required.");
        var red = floors.Select(c => c.GetComponent<Renderer>())
            .Where(r => r).SelectMany(r => r.sharedMaterials)
            .FirstOrDefault(m => m && m.name == "ResidentialAccessPaving");
        if (!red)
            throw new InvalidOperationException("The existing red pedestrian paving material was not found.");

        Physics.SyncTransforms();
        var nose = AlignedNose(access);
        var report = new Audit
        {
            routes11 = routes11,
            routes13 = routes13,
            routeChecks = routeSamples.Length,
            routeHitsBefore = CountHits(floors, routeSamples),
            rampNoseBefore = OldNoseY,
            rampNoseAfterLeft = OldNoseY,
            rampNoseAfterRight = OldNoseY,
            rampNoseCenterZ = nose.centerZ,
            rampNoseHalfWidth = nose.halfWidth,
            sources = new[] { ShopPage, ShopPhoto, Shop13Page, Shop13SidePhoto, Office13Page, Office13Photo },
            note = "№11: красный наклонный участок подхода и бетонная площадка у двери. Отметка порога сохранена. " +
                   "№13: площадку и ступени по фото пока нельзя привязать к одному проёму в двух сохранённых корпусах. " +
                   "Проверены существующие связи, положение двери и геометрия ступеней сохранены."
        };

        // Use the existing adjoining hardscape at both sides. If either side cannot
        // be measured in the saved scene, retain the old end of the slope.
        var adjoining = floors.Where(floor => floor != collider).ToArray();
        float left = FloorHeight(adjoining, new Vector3(RampNoseX - .005f, OldNoseY, nose.centerZ - nose.halfWidth));
        float right = FloorHeight(adjoining, new Vector3(RampNoseX - .005f, OldNoseY, nose.centerZ + nose.halfWidth));
        if (Mathf.Abs(left - OldNoseY) <= .045f && Mathf.Abs(right - OldNoseY) <= .045f &&
            Mathf.Abs(left - right) <= .02f && Mathf.Max(left, right) < SillY)
        {
            report.savedPavingAtNose = true;
            report.rampNoseAfterLeft = left;
            report.rampNoseAfterRight = right;
        }
        report.largestRampRise = SillY - Mathf.Min(report.rampNoseAfterLeft, report.rampNoseAfterRight);

        Directory.CreateDirectory(Root + "/Meshes");
        Directory.CreateDirectory(Root + "/Materials");
        AssetDatabase.Refresh();
        var modified = BuildRamp(filter.transform, original, report.rampNoseAfterLeft, report.rampNoseAfterRight, nose.centerZ, nose.halfWidth);
        var surface = SaveMesh(modified);
        var redFinish = SavePavingMaterial(red);
        var concrete = renderer.sharedMaterial;
        filter.sharedMesh = surface;
        collider.sharedMesh = null;
        collider.sharedMesh = surface;
        renderer.sharedMaterials = new[] { concrete, redFinish };
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        EditorUtility.SetDirty(filter);
        EditorUtility.SetDirty(collider);
        EditorUtility.SetDirty(renderer);

        Physics.SyncTransforms();
        report.checkedRampPoints = CheckRamp(collider, report.rampNoseAfterLeft, report.rampNoseAfterRight, nose.centerZ, nose.halfWidth);
        foreach (float u in new[] { .05f, .2f, .35f, .5f, .65f, .8f, .95f })
        {
            float z = nose.centerZ + Mathf.Lerp(-nose.halfWidth, nose.halfWidth, u);
            float y = Mathf.Lerp(report.rampNoseAfterLeft, report.rampNoseAfterRight, u);
            float adjoiningY = FloorHeight(adjoining, new Vector3(RampNoseX - .005f, y, z));
            if (!float.IsFinite(adjoiningY))
                throw new InvalidOperationException("The aligned No.11 ramp end must stay inside the existing hardscape.");
            report.maxRampJointGap = Mathf.Max(report.maxRampJointGap, Mathf.Abs(adjoiningY - y));
            report.rampJointChecks++;
        }
        if (report.maxRampJointGap > .025f)
            throw new InvalidOperationException($"The aligned No.11 ramp has a {report.maxRampJointGap:F3} m mismatch with its adjoining path.");
        report.routeHitsAfter = CountHits(floors, routeSamples);
        if (report.routeHitsAfter < report.routeHitsBefore)
            throw new InvalidOperationException($"The east approach lost {report.routeHitsBefore - report.routeHitsAfter} supported route samples.");

        if (!evidence)
        {
            evidence = new GameObject(Applied) { tag = "EditorOnly" };
            evidence.transform.SetParent(access.transform, false);
        }
        var marker = evidence.GetComponent<PhotoSurveyMarker>() ?? evidence.AddComponent<PhotoSurveyMarker>();
        marker.category = "Восточный сектор · вход №11 и проверка №13";
        marker.observation = "Летний кадр №11 показывает красную дорожку через газон и светлый дорожный бордюр. " +
            "Красное покрытие продолжено по наклонному участку. Нижняя кромка подогнана к оси и ширине существующей диагональной дорожки.";
        marker.uncertainty = "Ширина и отметки порога приблизительные. У №13 фото подтверждают невысокий узел со ступенями, " +
            "но не соответствие конкретному модельному проёму. Геометрия №13 сохранена.";
        marker.geometryProvisional = true;
        marker.studyBounds = collider.bounds;
        marker.sourceUrls = report.sources;
        Directory.CreateDirectory("street_qa");
        File.WriteAllText("street_qa/district_east_editor.json", JsonUtility.ToJson(report, true));
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"STREET_EAST_FINISH routes11={routes11} routes13={routes13} routeHits={report.routeHitsAfter}/{report.routeChecks} " +
            $"rampChecks={report.checkedRampPoints} nose={report.rampNoseAfterLeft:F3}/{report.rampNoseAfterRight:F3} door13Ambiguous=True");
    }

    private static Mesh BuildRamp(Transform basis, Mesh source, float leftHeight, float rightHeight, float centerZ, float noseHalfWidth)
    {
        if (source.subMeshCount != 1)
            throw new InvalidOperationException("The original No.11 ramp must have one material.");
        var vertices = source.vertices;
        var world = vertices.Select(basis.TransformPoint).ToArray();
        var triangles = source.GetTriangles(0);
        var retained = new List<int>();
        var paved = new List<int>();
        for (int at = 0; at < triangles.Length; at += 3)
        {
            int a = triangles[at], b = triangles[at + 1], c = triangles[at + 2];
            // The nose, tread and side arrises all continue the same red surface.
            // The raised concrete landing behind the rear joint keeps its own material.
            bool ramp = Mathf.Min(world[a].x, Mathf.Min(world[b].x, world[c].x)) <= RampNoseX + .001f &&
                Mathf.Max(world[a].x, Mathf.Max(world[b].x, world[c].x)) <= RampBackX + .001f;
            var target = ramp ? paved : retained;
            target.Add(a);
            target.Add(b);
            target.Add(c);
        }
        if (paved.Count != 24)
            throw new InvalidOperationException($"Expected the eight faces of the saved No.11 ramp, found {paved.Count / 3} triangles.");

        for (int i = 0; i < vertices.Length; i++)
        {
            if (Mathf.Abs(world[i].x - RampNoseX) < .001f)
            {
                bool left = world[i].z < DoorZ;
                float height = Mathf.Abs(world[i].y - OldNoseY) < .001f ? (left ? leftHeight : rightHeight) : world[i].y;
                vertices[i] = basis.InverseTransformPoint(new Vector3(world[i].x, height, centerZ + (left ? -noseHalfWidth : noseHalfWidth)));
            }
        }
        var copy = UnityEngine.Object.Instantiate(source);
        copy.name = "East11PavedThreshold";
        copy.vertices = vertices;
        copy.subMeshCount = 2;
        copy.SetTriangles(retained, 0);
        copy.SetTriangles(paved, 1);
        copy.RecalculateNormals();
        copy.tangents = EnvironmentMesh.Tangents(copy.normals);
        copy.RecalculateBounds();
        return copy;
    }

    private static int CheckRamp(MeshCollider collider, float leftHeight, float rightHeight, float centerZ, float noseHalfWidth)
    {
        int checkedPoints = 0;
        foreach (float across in new[] { -HalfWidth + .10f, 0, HalfWidth - .10f })
        {
            for (int i = 0; i < 9; i++)
            {
                float t = .025f + i * .12f;
                float u = (across + HalfWidth) / (2 * HalfWidth);
                float y = u < t ? Mathf.Lerp(SillY, leftHeight, t) + (rightHeight - leftHeight) * u :
                    Mathf.Lerp(SillY, rightHeight, t);
                var position = new Vector3(Mathf.Lerp(RampBackX, RampNoseX, t),
                    y, Mathf.Lerp(DoorZ + across, centerZ + across * noseHalfWidth / HalfWidth, t));
                if (!collider.Raycast(new Ray(position + Vector3.up, Vector3.down), out var hit, 1.3f) ||
                    hit.normal.y < .96f || Mathf.Abs(hit.point.y - position.y) > .003f)
                    throw new InvalidOperationException($"No.11 ramp has a hole or incorrect grade at {position}.");
                checkedPoints++;
            }
        }
        var sill = new Vector3(125.4542f, SillY, DoorZ);
        if (!collider.Raycast(new Ray(sill + Vector3.up, Vector3.down), out var sillHit, 1.3f) ||
            Mathf.Abs(sillHit.point.y - SillY) > .003f)
            throw new InvalidOperationException("The unchanged No.11 landing must remain at its saved sill level.");
        return checkedPoints + 1;
    }

    private static (float centerZ, float halfWidth) AlignedNose(ResidentialAccess access)
    {
        var route = access.routes.FirstOrDefault(candidate => candidate?.points != null && candidate.points.Length > 1 &&
            !string.IsNullOrEmpty(candidate.address) && candidate.address.StartsWith("11 ", StringComparison.Ordinal) &&
            new Vector2(candidate.points[0].x - 125.4542f, candidate.points[0].z - DoorZ).sqrMagnitude < .01f);
        if (route == null)
            throw new InvalidOperationException("The saved diagonal No.11 connector is required to align its ramp.");
        for (int i = 1; i < route.points.Length; i++)
        {
            Vector3 a = route.points[i - 1], b = route.points[i];
            if (Mathf.Min(a.x, b.x) > RampNoseX || Mathf.Max(a.x, b.x) < RampNoseX || Mathf.Abs(b.x - a.x) < .001f)
                continue;
            float t = (RampNoseX - a.x) / (b.x - a.x);
            Vector3 direction = b - a;
            direction.y = 0;
            direction.Normalize();
            // The saved connector is 1.6 m wide. Its horizontal slice is wider
            // at a diagonal, with a small inset to keep the transition inside it.
            float halfWidth = Mathf.Min(HalfWidth, .8f / Mathf.Abs(direction.x) * .96f);
            return (Mathf.Lerp(a.z, b.z, t), halfWidth);
        }
        throw new InvalidOperationException("The saved No.11 path does not cross the ramp end.");
    }

    private static Vector3[] EastRouteSamples(ResidentialAccess access, out int routes11, out int routes13)
    {
        var samples = new List<Vector3>();
        routes11 = 0;
        routes13 = 0;
        foreach (var route in access.routes)
        {
            if (route.points == null || route.points.Length < 2 || string.IsNullOrEmpty(route.address))
                continue;
            if (route.address.StartsWith("11 ", StringComparison.Ordinal))
                routes11++;
            else if (route.address.StartsWith("13 ", StringComparison.Ordinal))
                routes13++;
            else
                continue;
            samples.Add(route.points[0]);
            for (int i = 1; i < route.points.Length; i++)
            {
                Vector3 from = route.points[i - 1], to = route.points[i];
                int divisions = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / .25f));
                for (int j = 1; j <= divisions; j++)
                    samples.Add(Vector3.Lerp(from, to, (float)j / divisions));
            }
        }
        return samples.ToArray();
    }

    private static int CountHits(MeshCollider[] floors, Vector3[] samples) =>
        samples.Count(point => float.IsFinite(FloorHeight(floors, point)));

    private static float FloorHeight(IEnumerable<MeshCollider> floors, Vector3 point)
    {
        float closest = float.NaN;
        var ray = new Ray(point + Vector3.up * 1.25f, Vector3.down);
        foreach (var floor in floors)
            if (floor.Raycast(ray, out var hit, 1.5f) && hit.normal.y > .85f &&
                Mathf.Abs(hit.point.y - point.y) < .10f && (!float.IsFinite(closest) || hit.point.y > closest))
                closest = hit.point.y;
        return closest;
    }

    private static Mesh SaveMesh(Mesh copy)
    {
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(FinishedRamp);
        if (!saved)
        {
            AssetDatabase.CreateAsset(copy, FinishedRamp);
            return copy;
        }
        EditorUtility.CopySerialized(copy, saved);
        UnityEngine.Object.DestroyImmediate(copy);
        EditorUtility.SetDirty(saved);
        return saved;
    }

    private static Material SavePavingMaterial(Material source)
    {
        const string path = Root + "/Materials/East11RampPaving.mat";
        var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!copy)
        {
            copy = new Material(source);
            AssetDatabase.CreateAsset(copy, path);
        }
        return copy;
    }
}
