using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Designed landscaping around the photo-based house, not a surveyed courtyard reconstruction.
internal static class PilotBlockBuilder
{
    private static Material paving, edging, concrete, metal, timber, rubber, green, ivory, blue, soil, glow;

    internal static void PrepareHouse(GameObject house)
    {
        EnvironmentMesh.EnsureFolders();
        var entrances = new[]
        {
            new Vector3(-19.425f, 0, 13), new Vector3(-5.775f, 0, -13),
            new Vector3(5.775f, 0, 13), new Vector3(19.425f, 0, -13), new Vector3(-11.25f, 0, -.58f)
        };
        var outward = new[] { Vector3.left, Vector3.left, Vector3.right, Vector3.right, Vector3.back };
        for (int e = 0; e < entrances.Length; e++)
        {
            var steps = new GameObject("Крыльцо " + (e + 1) + " · коллизия и контрольная точка");
            steps.transform.SetParent(house.transform, false);
            steps.transform.localPosition = entrances[e];
            steps.transform.localRotation = Quaternion.LookRotation(outward[e]);
            for (int i = 0; i < 4; i++)
            {
                float height = (i + 1) * .16f;
                EnvironmentMesh.Collider(steps, new Vector3(0, height * .5f, 1.66f - i * .28f), new Vector3(2.32f, height, .28f));
            }
            EnvironmentMesh.Collider(steps, new Vector3(0, .32f, .60f), new Vector3(2.07f, .64f, .98f));
            var approach = new GameObject("Подход " + (e + 1));
            approach.transform.SetParent(steps.transform, false);
            approach.transform.localPosition = Vector3.forward * 4;
            approach.transform.localRotation = Quaternion.Euler(0, 180, 0);
        }

        var plaque = new EnvironmentMesh();
        var paint = EnvironmentMesh.Material("AddressBlue", new Color(.075f, .19f, .25f));
        plaque.RoundedBox(paint, Vector3.zero, new Vector3(.045f, .48f, 2.4f), .015f);
        var sign = plaque.Create("HouseAddress", "Адресная табличка · Дунина-Горкавича, 5");
        sign.transform.SetParent(house.transform, false);
        sign.transform.localPosition = new Vector3(-19.51f, 2.75f, 10.0f);
        EnvironmentMesh.Label(sign.transform, "Дунина-Горкавича, 5", new Vector3(-.025f, 0, 0), Quaternion.Euler(0, 90, 0), .026f, Color.white);
        foreach (var renderer in house.GetComponentsInChildren<MeshRenderer>())
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);
        PrefabUtility.SaveAsPrefabAssetAndConnect(house, EnvironmentMesh.Root + "/Prefabs/House5.prefab", InteractionMode.AutomatedAction);
    }

    internal static void Generate(Transform parent, GameObject house)
    {
        EnvironmentMesh.EnsureFolders();
        Palette();
        // Parked vehicles are placed only inside evidenced sites by ParkingEnvironmentBuilder.
        var root = new GameObject("Первый участок · дом №5 и проектное благоустройство");
        root.transform.SetParent(parent, false);
        var block = root.AddComponent<PilotBlock>();
        Transform surfaces = Group(root.transform, "01 · Дорожки и площадки");
        Transform planting = Group(root.transform, "02 · Озеленение · LOD");
        Transform furniture = Group(root.transform, "03 · Скамейки, урны и светильники");
        Transform waste = Group(root.transform, "04 · Контейнерная площадка · без игровых механик");
        Transform views = Group(root.transform, "05 · Точки осмотра и проверки");

        BuildSurfaces(surfaces);
        GameObject bench = Bench(), litterBin = LitterBin(), lamp = Lamp(), container = Container(), screen = Screen();
        var bench1 = Place(bench, furniture, new Vector3(4, .14f, -38.15f));
        Place(bench, furniture, new Vector3(15, .14f, -38.15f));
        block.bench = bench1.GetComponent<Collider>();
        foreach (var position in new[] { new Vector3(6, .14f, -38.2f), new Vector3(17, .14f, -38.2f), new Vector3(-24.7f, .14f, 16.8f) })
            Place(litterBin, furniture, position);
        foreach (var position in new[] { new Vector3(-31.2f, 0, 23), new Vector3(-9.7f, 0, -24.5f),
            new Vector3(9.4f, 0, -38), new Vector3(21, 0, -34), new Vector3(25.2f, 0, -3.5f) })
            Place(lamp, furniture, position);

        Place(screen, waste, new Vector3(25.5f, .14f, -37.7f));
        block.containers = new[] { 22.95f, 25.5f, 28.05f }.Select(x =>
            Place(container, waste, new Vector3(x, .14f, -37.7f)).GetComponent<Collider>()).ToArray();
        block.containerApproach = Point(views, "Подход к контейнерам", new Vector3(25.5f, .14f, -40.7f), Vector3.forward);

        GameObject birch = SummerVegetationBuilder.Broadleaf("Birch", 9.5f, 185), young = SummerVegetationBuilder.Broadleaf("BirchYoung", 7.6f, 291);
        GameObject shrub = SummerVegetationBuilder.Shrub(), hedge = SummerVegetationBuilder.Shrub(true), spruce = SummerVegetationBuilder.Spruce();
        var trees = new[] { new Vector3(-23.6f, 0, 21), new Vector3(-10.8f, 0, -22), new Vector3(8, 0, -43.1f),
            new Vector3(18.5f, 0, -43.5f), new Vector3(28, 0, -2), new Vector3(11.8f, 0, 23) };
        for (int i = 0; i < trees.Length; i++)
        {
            if (!StreetBuilder.CanPlant(trees[i], .2f)) continue;
            var tree = Place(SummerVegetationBuilder.SelectBroadleaf(i % 3 == 1 ? 7.6f : 9.5f, i % 4), planting, trees[i], i * 67);
            if (!block.tree) block.tree = tree.GetComponent<Collider>();
        }
        var bushes = new[] { new Vector3(-25, 0, 22.8f), new Vector3(-22.8f, 0, 19.6f), new Vector3(-10.7f, 0, -24.1f),
            new Vector3(8.4f, 0, -36.7f), new Vector3(10.7f, 0, -36.7f), new Vector3(13, 0, -36.7f),
            new Vector3(27.2f, 0, -6), new Vector3(27.2f, 0, -8.2f), new Vector3(11.4f, 0, 19.8f) };
        for (int i = 0; i < bushes.Length; i++) if (StreetBuilder.CanPlant(bushes[i], .2f)) Place(shrub, planting, bushes[i], i * 43);
        DressGardens(planting, hedge);
        DressStreet(planting, birch, young, spruce, trees);

        block.entrances = house.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Подход ", StringComparison.Ordinal)).ToArray();
        block.inspectionViews = new[]
        {
            View(views, "01 · Уличный ракурс по игровому ориентиру", new Vector3(-44, 7.5f, 22), new Vector3(-9, 3, -18)),
            View(views, "02 · Дом №5 и южная территория", new Vector3(-28, 6, -43), new Vector3(0, 5.6f, -1)),
            View(views, "03 · Контейнерная площадка", new Vector3(32.5f, 2.5f, -45.4f), new Vector3(25.4f, 1.2f, -37.1f)),
            View(views, "04 · Обзор первого участка", new Vector3(53, 48, -65), new Vector3(-1, 0, -5))
        };
        Debug.Log("PILOT_GENERATED entrances=" + block.entrances.Length + " containers=" + block.containers.Length + " trees=" + trees.Length);
    }

    private static void Palette()
    {
        paving = EnvironmentMesh.Material("WarmPaving", new Color(.43f, .35f, .30f), 3);
        edging = EnvironmentMesh.Material("Kerbstone", new Color(.47f, .48f, .46f), 6);
        concrete = EnvironmentMesh.Material("Concrete", new Color(.38f, .395f, .38f), 6);
        metal = EnvironmentMesh.Material("GraphiteMetal", new Color(.27f, .29f, .30f), 0, .36f, .3f);
        timber = EnvironmentMesh.Material("OiledTimber", new Color(.48f, .41f, .33f), 5, .22f);
        rubber = EnvironmentMesh.Material("Rubber", new Color(.055f, .064f, .064f));
        green = EnvironmentMesh.Material("ContainerGreen", new Color(.38f, .43f, .39f), 0, .26f, .12f);
        ivory = EnvironmentMesh.Material("IvoryPaint", new Color(.84f, .85f, .77f));
        blue = EnvironmentMesh.Material("AddressBlue", new Color(.075f, .19f, .25f));
        soil = EnvironmentMesh.Material("Mulch", new Color(.22f, .20f, .13f), 4);
        glow = EnvironmentMesh.Material("LampDiffuser", new Color(.94f, .88f, .68f), 0, .3f, 0, false,
            new Color(.32f, .27f, .16f));
    }

    private static void BuildSurfaces(Transform parent)
    {
        var mesh = new EnvironmentMesh();
        void Path(float width, params Vector3[] points) => mesh.Path(paving, null, points, width);
        Vector3 P(float x, float z) => new Vector3(x, 0, z);
        Path(2.5f, P(-22.5f, 13), P(-21.05f, 13));
        Path(2.5f, P(-8.7f, -13), P(-7.4f, -13));
        Path(2.1f, P(-19.3f, -4), P(-11.25f, -4), P(-11.25f, -2.15f));
        Path(2.2f, P(-5.3f, -28.7f), P(23, -28.7f), P(23, 2.7f), P(9.2f, 2.7f), P(9.2f, 29));
        Path(2.5f, P(7.4f, 13), P(9.2f, 13));
        Path(2.5f, P(21.05f, -13), P(23, -13));
        Path(2.2f, P(23, -28.7f), P(23, -35.2f));
        Path(2.2f, P(5, -28.7f), P(5, -30.1f));
        Path(2.2f, P(5, -34.3f), P(5, -40.2f));
        Path(2.4f, P(5, -40.2f), P(21.2f, -40.2f));
        // Keep the actual service road at road level. No raised path across traffic.
        for (int i = 0; i < 5; i++) mesh.Box(ivory, new Vector3(5, .05f, -30.65f - i * .71f), new Vector3(2.1f, .012f, .35f));
        mesh.RoundedBox(concrete, new Vector3(25.5f, .065f, -38.4f), new Vector3(8.6f, .15f, 6.4f), .18f);
        // Leave the northern footpath connection and western entrance open.
        mesh.Box(edging, new Vector3(21.55f, .09f, -35.2f), new Vector3(.7f, .18f, .12f));
        mesh.Box(edging, new Vector3(26.95f, .09f, -35.2f), new Vector3(5.7f, .18f, .12f));
        mesh.Box(edging, new Vector3(29.8f, .09f, -38.4f), new Vector3(.12f, .18f, 6.4f));
        foreach (float x in new[] { 4f, 15f })
            mesh.Box(paving, new Vector3(x, .065f, -38), new Vector3(4.2f, .15f, 2));

        // A narrow gravel/drainage apron along unprojected walls, below the entrance steps.
        foreach (var c in new[] { new Vector3(-6.825f, 0, 13), new Vector3(6.825f, 0, -13) })
        {
            foreach (int side in new[] { -1, 1 })
                mesh.Box(concrete, c + new Vector3(side * 11.52f, .025f, 0), new Vector3(.56f, .07f, 26));
            mesh.Box(concrete, c + new Vector3(0, .025f, c.z > 0 ? 13.25f : -13.25f), new Vector3(22.5f, .07f, .5f));
        }
        foreach (var bed in new[] { new Vector3(-23.6f, 0, 21), new Vector3(-10.8f, 0, -22),
            new Vector3(11.8f, 0, 23), new Vector3(28, 0, -2) })
            mesh.RoundedBox(soil, bed - Vector3.up * .012f, new Vector3(2.7f, .025f, 3.2f), .45f);
        mesh.RoundedBox(soil, new Vector3(10.7f, -.012f, -36.7f), new Vector3(7.1f, .025f, 1.9f), .4f);
        foreach (var tree in new[] { new Vector3(8, 0, -43.1f), new Vector3(18.5f, 0, -43.5f) })
            mesh.Cylinder(soil, tree - Vector3.up * .025f, tree - Vector3.up * .004f, 1.1f, 1.1f, 32);
        var surface = mesh.Create("PilotSurfaces", "Мощение, отмостка, клумбы и площадка");
        surface.transform.SetParent(parent, false);
        surface.AddComponent<MeshCollider>().sharedMesh = surface.GetComponent<MeshFilter>().sharedMesh;
        GameObjectUtility.SetStaticEditorFlags(surface, StaticEditorFlags.BatchingStatic);
    }

    private static GameObject Bench()
    {
        var mesh = new EnvironmentMesh();
        foreach (float x in new[] { -.78f, .78f })
        {
            mesh.Box(metal, new Vector3(x, .25f, 0), new Vector3(.075f, .50f, .51f));
            mesh.RoundedBox(metal, new Vector3(x, .035f, 0), new Vector3(.25f, .07f, .65f), .055f);
            mesh.Cylinder(metal, new Vector3(x, .44f, .2f), new Vector3(x, 1.05f, .33f), .033f);
            mesh.Cylinder(metal, new Vector3(x, .47f, -.16f), new Vector3(x, .72f, -.16f), .024f);
            mesh.Cylinder(metal, new Vector3(x, .72f, -.23f), new Vector3(x, .72f, .25f), .025f);
        }
        mesh.Box(metal, new Vector3(0, .34f, 0), new Vector3(1.6f, .055f, .06f));
        for (int i = 0; i < 5; i++)
            mesh.RoundedBox(timber, new Vector3(0, .49f, -.24f + i * .115f), new Vector3(2, .065f, .095f), .025f);
        for (int i = 0; i < 3; i++)
            mesh.Box(timber, new Vector3(0, .74f + i * .125f, .27f + i * .026f), new Vector3(2, .105f, .055f), Quaternion.Euler(12, 0, 0));
        foreach (float x in new[] { -.78f, .78f })
            for (int i = 0; i < 3; i++)
                mesh.Cylinder(metal, new Vector3(x, .74f + i * .125f, .233f + i * .026f), new Vector3(x, .74f + i * .125f, .225f + i * .026f), .013f, .013f, 8);
        var root = mesh.Create("Bench", "Скамейка · дерево и стальной каркас");
        EnvironmentMesh.Collider(root, new Vector3(0, .48f, .03f), new Vector3(2, 1, .65f));
        return EnvironmentMesh.SavePrefab(root, "Bench");
    }

    private static GameObject LitterBin()
    {
        var mesh = new EnvironmentMesh();
        mesh.RoundedBox(metal, new Vector3(0, .49f, 0), new Vector3(.45f, .78f, .43f), .065f, .86f);
        mesh.RoundedBox(metal, new Vector3(0, .11f, 0), new Vector3(.52f, .08f, .49f), .055f);
        mesh.RoundedBox(concrete, new Vector3(0, .035f, 0), new Vector3(.56f, .07f, .53f), .07f);
        for (int s = -1; s <= 1; s += 2)
            mesh.Box(metal, new Vector3(s * .20f, .99f, 0), new Vector3(.035f, .23f, .32f));
        mesh.RoundedBox(metal, new Vector3(0, 1.12f, 0), new Vector3(.53f, .06f, .49f), .075f);
        mesh.RoundedBox(rubber, new Vector3(0, .889f, 0), new Vector3(.37f, .016f, .34f), .05f);
        for (int i = 0; i < 7; i++)
            mesh.Box(timber, new Vector3(-.162f + i * .054f, .50f, -.222f), new Vector3(.034f, .62f, .024f));
        var root = mesh.Create("LitterBin", "Уличная урна · деревянные рейки");
        EnvironmentMesh.Collider(root, new Vector3(0, .56f, 0), new Vector3(.53f, 1.12f, .49f));
        return EnvironmentMesh.SavePrefab(root, "LitterBin");
    }

    private static GameObject Container()
    {
        var mesh = new EnvironmentMesh();
        mesh.RoundedBox(green, new Vector3(0, .78f, 0), new Vector3(1.36f, .91f, 1.02f), .11f, .84f);
        mesh.RoundedBox(metal, new Vector3(0, 1.26f, 0), new Vector3(1.45f, .12f, 1.12f), .11f);
        mesh.Box(green, new Vector3(0, .36f, 0), new Vector3(1.17f, .07f, .87f));
        foreach (float x in new[] { -.52f, .52f })
            foreach (float z in new[] { -.35f, .35f })
            {
                mesh.Box(metal, new Vector3(x, .25f, z), new Vector3(.075f, .23f, .10f));
                mesh.Cylinder(rubber, new Vector3(x - .055f, .14f, z), new Vector3(x + .055f, .14f, z), .135f, .135f, 16);
                mesh.Cylinder(metal, new Vector3(x - .058f, .14f, z), new Vector3(x + .058f, .14f, z), .047f, .047f, 12);
            }
        foreach (float x in new[] { -.48f, -.24f, .24f, .48f })
            mesh.Box(green, new Vector3(x, .78f, -.499f), new Vector3(.045f, .59f, .045f));
        foreach (float x in new[] { -.69f, .69f })
        {
            mesh.Cylinder(metal, new Vector3(x, .95f, -.26f), new Vector3(x, 1.06f, -.26f), .025f);
            mesh.Cylinder(metal, new Vector3(x, 1.06f, -.26f), new Vector3(x, 1.06f, .26f), .025f);
            mesh.Cylinder(metal, new Vector3(x, 1.06f, .26f), new Vector3(x, .95f, .26f), .025f);
        }
        mesh.RoundedBox(ivory, new Vector3(0, .90f, -.529f), new Vector3(.67f, .27f, .018f), .008f);
        mesh.Box(ivory, new Vector3(-.51f, 1.12f, -.526f), new Vector3(.12f, .10f, .015f));
        mesh.Box(ivory, new Vector3(.51f, 1.12f, -.526f), new Vector3(.12f, .10f, .015f));
        mesh.Cylinder(metal, new Vector3(-.52f, 1.29f, .56f), new Vector3(.52f, 1.29f, .56f), .035f);
        var root = mesh.Create("Container1100", "Контейнер 1100 л · на четырёх колёсах");
        EnvironmentMesh.Collider(root, new Vector3(0, .74f, 0), new Vector3(1.45f, 1.32f, 1.12f));
        EnvironmentMesh.Label(root.transform, "СМЕШАННЫЕ\nОТХОДЫ", new Vector3(0, .90f, -.542f), Quaternion.identity, .028f, new Color(.12f, .18f, .17f));
        return EnvironmentMesh.SavePrefab(root, "Container1100");
    }

    private static GameObject Screen()
    {
        var mesh = new EnvironmentMesh();
        foreach (float x in new[] { -4f, -1.34f, 1.34f, 4f })
            mesh.Box(metal, new Vector3(x, .95f, 1.65f), new Vector3(.08f, 1.9f, .08f));
        foreach (float x in new[] { -4f, 4f })
            mesh.Box(metal, new Vector3(x, .95f, -1.45f), new Vector3(.08f, 1.9f, .08f));
        for (int i = 0; i < 11; i++)
        {
            mesh.Box(timber, new Vector3(0, .23f + i * .145f, 1.65f), new Vector3(7.95f, .10f, .045f));
            foreach (float x in new[] { -4f, 4f })
                mesh.Box(timber, new Vector3(x, .23f + i * .145f, .1f), new Vector3(.045f, .10f, 3.1f));
        }
        mesh.RoundedBox(blue, new Vector3(0, 1.95f, 1.58f), new Vector3(3.6f, .40f, .065f), .03f);
        var root = mesh.Create("ContainerScreen", "Контейнерная площадка · открытый вход");
        EnvironmentMesh.Collider(root, new Vector3(0, .93f, 1.65f), new Vector3(8.1f, 1.86f, .11f));
        foreach (float x in new[] { -4f, 4f })
            EnvironmentMesh.Collider(root, new Vector3(x, .93f, .1f), new Vector3(.11f, 1.86f, 3.1f));
        EnvironmentMesh.Label(root.transform, "ПЛОЩАДКА ДЛЯ ОТХОДОВ", new Vector3(0, 1.95f, 1.54f), Quaternion.identity, .033f, Color.white);
        return EnvironmentMesh.SavePrefab(root, "ContainerScreen");
    }

    private static GameObject Lamp()
    {
        var mesh = new EnvironmentMesh();
        mesh.RoundedBox(concrete, new Vector3(0, .07f, 0), new Vector3(.52f, .14f, .52f), .08f);
        mesh.Cylinder(metal, new Vector3(0, .14f, 0), new Vector3(0, 4.65f, 0), .085f, .045f, 16);
        mesh.Cylinder(metal, new Vector3(0, 4.45f, 0), new Vector3(0, 4.92f, -.28f), .044f, .036f, 12);
        mesh.Cylinder(metal, new Vector3(0, 4.92f, -.28f), new Vector3(0, 5.02f, -.78f), .036f, .027f, 12);
        mesh.RoundedBox(metal, new Vector3(0, 5, -.82f), new Vector3(.42f, .13f, .86f), .16f);
        mesh.RoundedBox(glow, new Vector3(0, 4.929f, -.84f), new Vector3(.33f, .025f, .65f), .12f);
        mesh.Box(metal, new Vector3(0, .75f, -.079f), new Vector3(.075f, .25f, .012f));
        var root = mesh.Create("CourtyardLamp", "Дворовый светильник · дневной режим");
        var collider = root.AddComponent<CapsuleCollider>();
        collider.radius = .10f; collider.height = 4.7f; collider.center = Vector3.up * 2.35f;
        return EnvironmentMesh.SavePrefab(root, "CourtyardLamp");
    }

    private static void DressGardens(Transform parent, GameObject hedge)
    {
        foreach (float z in new[] { 6f, 8f, 10f, 17f, 19f, 21f, 23f })
            if (StreetBuilder.CanPlant(new Vector3(-21.8f, 0, z), .2f)) Place(hedge, parent, new Vector3(-21.8f, 0, z), 90).transform.localScale = new Vector3(1, .78f, 1);
        foreach (float z in new[] { -24f, -22f, -20f, -8f, -6f })
            if (StreetBuilder.CanPlant(new Vector3(-8.0f, 0, z), .2f)) Place(hedge, parent, new Vector3(-8.0f, 0, z), 90).transform.localScale = new Vector3(1, .72f, 1);
        foreach (float z in new[] { -23f, -21f, -19f, -8f, -6f })
            Place(hedge, parent, new Vector3(25.9f, 0, z), 90);
        foreach (float x in new[] { .6f, 2.6f, 11f, 13f, 15f })
            Place(hedge, parent, new Vector3(x, 0, -36.7f)).transform.localScale = new Vector3(1, .75f, 1);
    }

    private static void DressStreet(Transform parent, GameObject mature, GameObject young, GameObject spruce, Vector3[] existing)
    {
        var positions = new System.Collections.Generic.List<Vector3>(existing);
        Vector3 hero = new Vector3(-44, 0, 22);
        foreach (var sample in StreetBuilder.FocalRoadSamples(16.5f))
        {
            float pole = Mathf.Repeat(sample.distance - 12, 26);
            if (Mathf.Min(pole, 26 - pole) < 1.5f) continue;
            Vector3 n = new Vector3(sample.forward.z, 0, -sample.forward.x);
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 p = sample.position + n * (side * 4.55f);
                if (!StreetBuilder.CanPlant(p, .55f) || Vector3.Distance(p, hero) < 7 || positions.Any(t => Vector3.Distance(t, p) < 5)) continue;
                Place(positions.Count % 5 == 0 ? spruce : mature, parent, p, positions.Count * 71);
                positions.Add(p);
            }
        }
        foreach (var p in new[] { new Vector3(-27, 0, -2), new Vector3(23, 0, 8) })
            if (StreetBuilder.CanPlant(p, .3f)) { Place(spruce, parent, p, 39); positions.Add(p); }
        var random = new System.Random(1706);
        int added = 0;
        for (int attempt = 0; attempt < 900 && added < 24; attempt++)
        {
            var p = new Vector3(-110 + (float)random.NextDouble() * 155, 0, -125 + (float)random.NextDouble() * 197);
            if ((p.x > -38 && p.x < 33 && p.z > -49 && p.z < 31) || Vector3.Distance(p, hero) < 8
                || !StreetBuilder.CanPlant(p, 4.5f) || positions.Any(t => Vector3.Distance(t, p) < 7)) continue;
            var instance = Place(added % 4 == 0 ? spruce : added % 3 == 0 ? young : mature, parent, p, added * 67);
            instance.transform.localScale = Vector3.one * (.87f + (float)random.NextDouble() * .27f);
            positions.Add(p); added++;
        }
        Debug.Log("SUMMER_GREENERY trees=" + positions.Count);
    }

    private static Transform Group(Transform parent, string name)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        return root;
    }

    private static GameObject Place(GameObject prefab, Transform parent, Vector3 position, float yaw = 0)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        return instance;
    }

    private static Transform Point(Transform parent, string name, Vector3 position, Vector3 forward)
    {
        var point = Group(parent, name);
        point.SetLocalPositionAndRotation(position, Quaternion.LookRotation(forward));
        return point;
    }

    private static Transform View(Transform parent, string name, Vector3 position, Vector3 target)
    {
        return Point(parent, name, position, target - position);
    }
}
