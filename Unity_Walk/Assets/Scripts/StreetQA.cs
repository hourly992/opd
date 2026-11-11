using System;
using System.IO;
using System.Collections;
using System.Linq;
using UnityEngine;

// Optional command-line verification; inactive during a normal game launch.
public class StreetQA : MonoBehaviour
{
    private string outputPath;
    private bool aborting;
    private bool renderOnCaptureOnly;
    [Serializable] private class Report
    {
        public bool passed, grounded, buildingCollision, containerCollision, benchCollision, treeCollision;
        public bool approachesWalkable, entranceStepsWalkable, lodConfigured;
        public bool referenceBuildingsConfigured, referenceBuildingCollisions;
        public float walkDistance;
        public int renderers, missingMaterials, entrances, containers, lodGroups;
        public int referenceBuildings;
        public int texturedMaterials, legacyMaterials, reflectiveWindows;
        public int masonryMaterials;
        public bool facadeMaterialsConfigured;
        public bool graphicsMaterialsConfigured;
        public int parkedVehicles, parkingVehicleReferences, meadowCells;
        public bool vehiclesAbsent, graphicsPresentationConfigured;
        public bool roadNetworkConfigured, roadSurfacesContinuous;
        public int roadSurfaceChecks, roadSurfaceHits, roadWalkPasses;
        public int roadTransitionChecks, roadTransitionHits, woodlandPatches, woodlandTrees, woodlandShrubs;
        public bool roadTransitionsContinuous, woodlandPlacementValid, woodlandLodsConfigured;
        public bool lightingConfigured;
        public int shadowCasters, shadowReceivers;
        public int playgrounds, playgroundEquipment, playgroundCollisionHits, residentialEntrances, connectedResidentialEntrances;
        public int residentialSurfaceChecks, residentialSurfaceHits, residentialWalkChecks, residentialWalkPasses;
        public bool playgroundsConfigured, residentialAccessConfigured;
        public bool roadMaterialsDistinct, grassCoverageExpanded;
        public bool residentialDestinationsPaved;
        public int openingInspectionViews, backgroundOpeningViews;
        public int parkingSites, parkingSurfaceChecks, parkingSurfaceHits, parkingAccessChecks, parkingAccessHits;
        public int photoParkingAreas;
        public bool parkingPlacementValid, parkingSurfacesContinuous, parkingAccessConnected;
        public int entranceLandingChecks, entranceLandingHits;
        public bool entrancesHaveContinuousPaving;
        public int occludedSideDetails;
        public bool photoEntrancePresent, photoEntranceSurfaceValid, photoEntranceWalkValid;
        public int streetLampSources,addedCourtyardLamps,activeNightLamps;
        public bool streetLampsConfigured,streetLampCollision,streetLampNightValid;
        public bool centralCourtPresent,centralCourtWalkValid,districtTerrainPresent,districtTerrainValid;
        public bool courtDoorLowPresent,courtDoorLowWalkValid;
        public bool stage05CivilPresent,stage05ApproachPaved,stage05ApproachWalkValid;
        public int stage05ApproachChecks,stage05ApproachHits;
        public bool rayTracingActive, rayTracingFallback;
        public int rayTracingInstances, rayTracedContactPixels;
        public int districtWalkChecks, districtWalkPasses;
        public int leafFlightPaths, leafFlightMoved;
        public int bakedLightProbes, probeLitRenderers;
        public bool bakedProbeDataValid;
        public string districtSector;
        public string engine;
        public EntranceCheck[] entranceChecks;
    }

    [Serializable] private class EntranceCheck
    {
        public string name;
        public bool grounded, climbed;
        public float distance, rise;
    }

    [Serializable] private class BenchmarkReport
    {
        public string gpu, graphicsApi, engine, mode;
        public int width, height, samplesPerView;
        public BenchmarkView[] views;
    }
    [Serializable] private class BenchmarkView
    {
        public string name;
        public float averageFps, medianFrameMs, p95FrameMs;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartIfRequested()
    {
        if (Environment.GetCommandLineArgs().Contains("-streetQA"))
            GameObject.FindGameObjectWithTag("Player")?.AddComponent<StreetQA>();
    }

    private IEnumerator Start()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        var args = Environment.GetCommandLineArgs();
        var camera = GetComponentInChildren<Camera>();
        renderOnCaptureOnly = args.Contains("-qaCaptureOnly");
        if (renderOnCaptureOnly)
            camera.enabled = false;
        int index = Array.IndexOf(args, "-qaOutput");
        string output = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.dataPath, "..", "QA");
        Directory.CreateDirectory(output);
        outputPath = output;
        File.WriteAllText(Path.Combine(output, "report.json"), "{\"passed\":false,\"status\":\"running\"}");
        Application.logMessageReceived += AbortOnException;
        yield return null;
        GetComponent<FirstPersonWalk>().enabled = false;
        GetComponent<StreetSession>().enabled = false;
        var controller = GetComponent<CharacterController>();
        var block = FindAnyObjectByType<PilotBlock>();
        var report = new Report { engine = Application.unityVersion };
        report.parkedVehicles = FindObjectsByType<StreetVehicle>(FindObjectsInactive.Include).Length;
        report.parkingVehicleReferences = FindObjectsByType<ParkingSite>(FindObjectsInactive.Include)
            .Sum(site => site.vehicles?.Length ?? 0);
        report.vehiclesAbsent = report.parkedVehicles == 0 && report.parkingVehicleReferences == 0;
        if (args.Contains("-rayTracingQAOnly"))
        {
            yield return CheckRayTracing(camera, output, report, 960, 540, 1);
            report.passed = RayTracingPassed(report);
            File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.passed ? 0 : 2);
            yield break;
        }
        int sectorArg = Array.IndexOf(args, "-districtSector");
        if (sectorArg >= 0 && sectorArg + 1 < args.Length)
        {
            report.districtSector = args[sectorArg + 1];
            if (report.districtSector is "all" or "stage12" or "stage13")
            {
                string requested = report.districtSector;
                var sectors = new[] { "east", "south", "west", "north_center" };
                if (requested != "all")
                    sectors = sectors.Concat(new[] { "entries", "drive_seams" }).ToArray();
                foreach (var sector in sectors)
                {
                    report.districtSector = sector;
                    string frames = Path.Combine(output, sector);
                    Directory.CreateDirectory(frames);
                    yield return CheckSector(controller, camera, frames, report);
                }
                report.districtSector = requested;
            }
            else
                yield return CheckSector(controller, camera, output, report);
            bool leavesPassed = report.districtSector != "leaf_style" ||
                report.leafFlightPaths >= 25 && report.leafFlightMoved > 0;
            if (!args.Contains("-rayTracingQA") || report.districtWalkChecks == 0 ||
                report.districtWalkPasses != report.districtWalkChecks || !leavesPassed)
            {
                report.passed = report.vehiclesAbsent && report.districtWalkChecks > 0 &&
                    report.districtWalkPasses == report.districtWalkChecks && leavesPassed;
                File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
                Application.Quit(report.passed ? 0 : 2);
                yield break;
            }
            controller.enabled = true;
        }

        yield return Settle(controller);
        report.grounded = controller.isGrounded;
        Vector3 begin = transform.position;
        for (int i = 0; i < 90; i++)
        {
            controller.Move(transform.forward * (4f / 60f) + Vector3.down * .04f);
            yield return null;
        }
        report.walkDistance = Vector3.Distance(begin, transform.position);

        // Test the body away from the entrances: entrance steps now also have real colliders.
        // North of the garden hedge, so this probe still reaches the building itself.
        Teleport(controller, new Vector3(-23, 1.3f, 25), Quaternion.identity);
        for (int i = 0; i < 90; i++)
        {
            controller.Move(Vector3.right * .12f + Vector3.down * .05f);
            yield return null;
        }
        report.buildingCollision = transform.position.x < -18.2f && transform.position.x > -19;

        if (block)
        {
            report.entrances = block.entrances.Length;
            report.containers = block.containers.Length;
            report.entranceChecks = new EntranceCheck[block.entrances.Length];
            for (int e = 0; e < block.entrances.Length; e++)
            {
                var entrance = block.entrances[e];
                Teleport(controller, entrance.position + Vector3.up * 1.1f, entrance.rotation);
                yield return Settle(controller);
                var check = new EntranceCheck { name = entrance.name, grounded = controller.isGrounded };
                begin = transform.position;
                for (int i = 0; i < 80; i++)
                {
                    controller.Move(entrance.forward * .04f + Vector3.down * .04f);
                    yield return null;
                }
                check.distance = Vector3.ProjectOnPlane(transform.position - begin, Vector3.up).magnitude;
                check.rise = transform.position.y - begin.y;
                check.climbed = check.distance > 2.7f && check.rise > .38f && controller.isGrounded;
                report.entranceChecks[e] = check;
            }
            Teleport(controller, block.containerApproach.position + Vector3.up * 1.1f, block.containerApproach.rotation);
            yield return Settle(controller);
            report.approachesWalkable = controller.isGrounded && report.entrances == 5 && report.entranceChecks.All(c => c.grounded);
            report.entranceStepsWalkable = report.entranceChecks.All(c => c.climbed);
            if (block.containers.Length == 3)
            {
                bool all = true;
                foreach (var container in block.containers)
                    yield return ProbeCollision(controller, container, success => all &= success);
                report.containerCollision = all;
            }
            yield return ProbeCollision(controller, block.bench, success => report.benchCollision = success);
            yield return ProbeCollision(controller, block.tree, success => report.treeCollision = success);
            var groups = block.GetComponentsInChildren<LODGroup>();
            report.lodGroups = groups.Length;
            report.lodConfigured = groups.Length >= 6 && groups.All(g =>
            {
                var lods = g.GetLODs();
                return lods.Length >= 3 && lods.All(l => l.renderers.Length > 0 && l.renderers.All(r => r))
                    && Enumerable.Range(1, lods.Length - 1).All(i => lods[i - 1].screenRelativeTransitionHeight > lods[i].screenRelativeTransitionHeight);
            });
        }

        var referenceBuildings = FindObjectsByType<StreetBuilding>().OrderBy(b => b.osmId).ToArray();
        report.referenceBuildings = referenceBuildings.Length;
        report.referenceBuildingsConfigured = referenceBuildings.Length == 18 && referenceBuildings.All(b =>
            b.bodyColliders.Length > 0 && b.bodyColliders.All(c => c) && b.sectionStoreys.Length > 0
            && b.localBounds.size.sqrMagnitude > 1 && b.GetComponent<LODGroup>() && b.GetComponent<LODGroup>().GetLODs().Length == 3);
        bool referenceCollisions = report.referenceBuildingsConfigured;
        foreach (var building in referenceBuildings)
            yield return ProbeBuilding(controller, building, success => referenceCollisions &= success);
        report.referenceBuildingCollisions = referenceCollisions;
        report.openingInspectionViews = referenceBuildings.Sum(b => b.GetComponentsInChildren<Transform>()
            .Count(t => t.name.StartsWith("Opening QA · ", StringComparison.Ordinal)));
        report.backgroundOpeningViews = FindObjectsByType<Transform>()
            .Count(t => t.name.StartsWith("Opening QA · background ", StringComparison.Ordinal));
        var network = FindAnyObjectByType<StreetRoadNetwork>();
        report.roadNetworkConfigured = network && network.profileSections == 7 && network.mappedCrossings == 4 &&
            network.panoramaYear == 2025 && network.streetLength > 770 && network.surfaces.Length >= 3 && network.surfaces.All(c => c && c.sharedMesh)
            &&network.surfaces.Any(c=>c.sharedMesh.name=="ReferenceRoad");
        if (report.roadNetworkConfigured)
        {
            report.roadSurfaceChecks = network.walkingChecks.Length;
            foreach (Vector3 p in network.walkingChecks)
            {
                var ray = new Ray(p + Vector3.up * 2, Vector3.down);
                if (network.surfaces.Any(c => c.Raycast(ray, out var hit, 2.1f) && hit.normal.y > .9f)) report.roadSurfaceHits++;
            }
            // Exercise the real controller on both sides of three independently referenced straight sections.
            foreach (int i in new[] { 18, 19, 32, 33, 48, 49 })
            {
                Vector3 start = network.walkingChecks[i], direction = (network.walkingChecks[i + 2] - start).normalized;
                Teleport(controller, start + Vector3.up * 1.3f, Quaternion.LookRotation(direction));
                yield return Settle(controller);
                Vector3 from = transform.position;
                for (int frame = 0; frame < 100; frame++) { controller.Move(direction * .08f + Vector3.down * .04f); yield return null; }
                if (controller.isGrounded && Vector3.ProjectOnPlane(transform.position - from, Vector3.up).magnitude > 7.5f) report.roadWalkPasses++;
            }
            report.roadSurfacesContinuous = report.roadSurfaceHits == report.roadSurfaceChecks && report.roadWalkPasses == 6;
        }
        var parking = FindObjectsByType<ParkingSite>();
        var mappedParking = parking.Where(p => !p.usesExistingSurface).ToArray();
        report.parkingSites = mappedParking.Length;
        report.photoParkingAreas = parking.Count(p => p.usesExistingSurface);
        report.parkingPlacementValid = mappedParking.Length == 18 && mappedParking.All(p => p.osmId > 0 &&
            p.boundary != null && p.boundary.Length >= 3) && parking.All(p => p.sourceUrls != null && p.sourceUrls.Length > 0 &&
            !string.IsNullOrWhiteSpace(p.evidenceLevel) && p.surfaces != null && p.surfaces.Length > 0);
        foreach (var site in parking)
        {
            report.parkingSurfaceChecks += site.surfaceChecks.Length;
            report.parkingSurfaceHits += site.surfaceChecks.Count(p => ParkingSurfaceAt(site, p));
            report.parkingAccessChecks += site.accessChecks.Length;
            report.parkingAccessHits += site.accessChecks.Count(p => ParkingSurfaceAt(site, p));
        }
        report.parkingSurfacesContinuous = report.parkingSurfaceChecks > 100 && report.parkingSurfaceChecks == report.parkingSurfaceHits;
        report.parkingAccessConnected = mappedParking.All(p => p.connectedToServiceRoad && p.accessRoute.Length >= 2) &&
            mappedParking.All(p => p.accessRoute.Any(point => PavedDestination(point, p.surfaces))) &&
            report.parkingAccessChecks > 0 && report.parkingAccessChecks == report.parkingAccessHits;
        report.meadowCells = FindObjectsByType<LODGroup>().Count(g => g.name.StartsWith("Трава · ячейка ", StringComparison.Ordinal));
        var presentationCheck = camera.GetComponent<StreetPresentation>();
        report.graphicsPresentationConfigured = presentationCheck && presentationCheck.isActiveAndEnabled &&
            presentationCheck.shader && presentationCheck.shader.isSupported && presentationCheck.occlusionShader &&
            presentationCheck.occlusionShader.isSupported && camera.allowHDR && presentationCheck.contactShadows > 0;
        var renderers = FindObjectsByType<MeshRenderer>();
        var probeGroup = FindAnyObjectByType<LightProbeGroup>();
        var bakedProbes = LightmapSettings.lightProbes;
        var probeCoefficients = bakedProbes ? bakedProbes.bakedProbes : null;
        report.bakedLightProbes = bakedProbes ? bakedProbes.count : 0;
        report.probeLitRenderers = renderers.Count(renderer =>
            renderer.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.Off);
        report.bakedProbeDataValid = !probeGroup || bakedProbes && bakedProbes.count > 0 &&
            probeCoefficients != null && probeCoefficients.Length == bakedProbes.count && report.probeLitRenderers > 500 &&
            probeCoefficients.All(probe => Enumerable.Range(0, 27).All(index =>
                !float.IsNaN(probe[index / 9, index % 9]) && !float.IsInfinity(probe[index / 9, index % 9])));
        Debug.Log($"FULL_VISUAL_RUNTIME_GI probes={report.bakedLightProbes} " +
                  $"receivers={report.probeLitRenderers} dataValid={report.bakedProbeDataValid}");
        report.shadowCasters = renderers.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off);
        report.shadowReceivers = renderers.Count(r => r.receiveShadows);
        var sunlight = RenderSettings.sun;
        report.lightingConfigured = sunlight && sunlight.shadows == LightShadows.Soft && sunlight.shadowStrength >= .80f &&
            QualitySettings.shadowCascades == 4 && QualitySettings.shadowDistance >= 190 && report.shadowCasters > 500 &&
            report.shadowReceivers > 500 && presentationCheck.sunShaftShader && presentationCheck.sunShaftShader.isSupported && presentationCheck.sunShafts > 0;
        report.renderers = renderers.Length;
        report.missingMaterials = renderers.Count(r => r.sharedMaterials.Length == 0 ||
            r.sharedMaterials.Any(m => !m || !m.shader || !m.shader.isSupported));
        var materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToArray();
        report.texturedMaterials = materials.Count(m => m.shader.name == "Street/TexturedSurface" && m.GetTexture("_MainTex") && m.GetTexture("_BumpMap") && m.GetTexture("_MaskMap"));
        report.reflectiveWindows = materials.Count(m => m.shader.name == "Street/Glass" && m.GetTexture("_MainTex") && m.GetTexture("_DirtTex"));
        report.legacyMaterials = materials.Count(m => m.shader.name == "Street/Surface");
        report.graphicsMaterialsConfigured = report.texturedMaterials >= 45 && report.reflectiveWindows >= 6 && report.legacyMaterials == 0;
        var roadMaterials = new[] { "ReferenceAsphalt", "ReferenceServiceAsphalt", "ReferenceFootpathAsphalt" }
            .Select(name => materials.FirstOrDefault(m => m.name == name)).ToArray();
        report.roadMaterialsDistinct = roadMaterials.All(m => m && m.GetTexture("_MainTex") && m.GetTexture("_MainTex").width >= 1024 &&
            m.GetTexture("_BumpMap") && m.GetTexture("_MaskMap")) && roadMaterials.Select(m => m.GetTexture("_MainTex")).Distinct().Count() == 3;
        report.grassCoverageExpanded = report.meadowCells >= 250;
        var courtForMaterials=GameObject.Find("Двор №7 · мощение и подпорные стенки по фото · stage04");
        var landscapeMaterials=courtForMaterials?courtForMaterials.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).ToArray():Array.Empty<Material>();
        var masonry = materials.Where(m => !landscapeMaterials.Contains(m)&&m.HasProperty("_SurfaceClass") && m.GetFloat("_SurfaceClass") > 1.5f && m.GetFloat("_SurfaceClass") < 2.5f).ToArray();
        report.masonryMaterials = masonry.Length;
        report.facadeMaterialsConfigured = masonry.Length >= 9 && masonry.All(m => {
            var scale = m.GetVector("_TileMeters");
            // These scans span six bricks and 16/20 courses. A valid 75 mm course gives
            // a 1.20 m tile; the former strict >1.2 rejected that exact physical boundary.
            return m.GetTexture("_MainTex") && m.GetTexture("_MainTex").width >= 1024 &&
            m.GetTexture("_BumpMap") && m.GetTexture("_BumpMap").width >= 1024 && m.GetTexture("_MaskMap") &&
            m.GetFloat("_Saturation") >= .9f && m.GetFloat("_Parallax") <= .0025f &&
            m.GetFloat("_BumpStrength") <= .3f && scale.x >= 1.50f && scale.x <= 1.65f &&
            scale.y >= 1.19f && scale.y <= 1.55f;
        });
        var roadNetwork = FindAnyObjectByType<StreetRoadNetwork>();
        var transitionProbes = roadNetwork.transitionProbePoints.Concat(roadNetwork.junctionProbePoints).ToArray();
        report.roadTransitionChecks = transitionProbes.Length;
        foreach (var point in transitionProbes)
            if (roadNetwork.surfaces.Any(s => s.Raycast(new Ray(point + Vector3.up * 1.5f, Vector3.down), out var hit, 2) && Mathf.Abs(hit.point.y - point.y) < .025f)) report.roadTransitionHits++;
        report.roadTransitionsContinuous = report.roadTransitionChecks >= 96 && report.roadTransitionHits == report.roadTransitionChecks;
        var landscape = FindAnyObjectByType<GeographicLandscape>();
        report.woodlandPatches = landscape ? landscape.patches.Length : 0;
        report.woodlandTrees = landscape ? landscape.patches.Sum(p => p.trees.Length) : 0;
        report.woodlandShrubs = landscape ? landscape.patches.Sum(p => p.understorey.Length) : 0;
        report.woodlandPlacementValid = landscape && landscape.patches.Length == 6 && report.woodlandTrees >= 1400 && landscape.patches.All(p =>
            p.trees.Length > 0 && p.trees.Concat(p.understorey).All(t => t && GeographicLandscape.Contains(p.boundary, t.position)));
        report.woodlandLodsConfigured = landscape && landscape.patches.SelectMany(p => p.trees.Concat(p.understorey)).All(t =>
            t.GetComponent<LODGroup>() && t.GetComponent<LODGroup>().GetLODs().Length >= 3 &&
            t.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(m => m.enableInstancing)));
        var playgrounds = FindObjectsByType<PlaygroundSite>();
        report.playgrounds = playgrounds.Length;
        report.playgroundEquipment = playgrounds.Sum(p => p.equipment.Length);
        foreach (var equipment in playgrounds.SelectMany(p => p.equipment))
            if (equipment.GetComponentsInChildren<MeshCollider>().Any(c =>
                c.Raycast(new Ray(equipment.position + Vector3.up * 5, Vector3.down), out _, 6))) report.playgroundCollisionHits++;
        report.playgroundsConfigured = playgrounds.Length == 5 && playgrounds.All(p => p.ground && p.ground.sharedMesh &&
            p.equipment.Length >= 2 && p.equipment.All(t => t && GeographicLandscape.Contains(p.footprint, t.position) &&
                t.GetComponent<LODGroup>() && t.GetComponentsInChildren<Collider>().Length > 0)) && report.playgroundCollisionHits == report.playgroundEquipment;
        var access = FindAnyObjectByType<ResidentialAccess>();
        if (access)
        {
            report.residentialEntrances = access.entranceCount;
            report.occludedSideDetails = access.occludedSideDetails.Length;
            report.connectedResidentialEntrances = access.connectedEntrances;
            report.residentialDestinationsPaved = access.routes.All(r => r.points.Length > 0 &&
                PavedDestination(r.points[r.points.Length - 1], access.surfaces));
            report.residentialSurfaceChecks = access.surfaceChecks.Length;
            report.entranceLandingChecks = access.entranceLandingChecks.Length;
            report.entranceLandingHits = access.entranceLandingChecks.Count(point => access.surfaces.Any(c => c &&
                c.Raycast(new Ray(point + Vector3.up * 2, Vector3.down), out var hit, 2.3f) && hit.normal.y > .85f && Mathf.Abs(hit.point.y - point.y) < .025f));
            report.entrancesHaveContinuousPaving = report.entranceLandingChecks > 100 && report.entranceLandingChecks == report.entranceLandingHits;
            foreach (var point in access.surfaceChecks)
                if (access.surfaces.Any(c => c && c.Raycast(new Ray(point + Vector3.up * 2, Vector3.down), out var hit, 2.2f) &&
                    Mathf.Abs(hit.point.y - point.y) < .06f)) report.residentialSurfaceHits++;
            var longRoutes = access.routes.Where(r => r.points.Length > 1 && Vector3.Distance(r.points[0], r.points[r.points.Length - 1]) > 4).ToArray();
            var walkRoutes = longRoutes.Take(3).Concat(access.routes.Where(r => r.points.Length > 1 && ComplexAccessRoute(r))).Distinct().ToArray();
            report.residentialWalkChecks = walkRoutes.Length;
            foreach (var route in walkRoutes)
            {
                Vector3[] points = route.points.Reverse().ToArray();
                Teleport(controller, points[0] + Vector3.up * 1.25f, Quaternion.identity);
                yield return Settle(controller);
                bool traversed = true;
                for (int i = 1; i < points.Length && traversed; i++)
                {
                    Vector3 delta = Vector3.ProjectOnPlane(points[i] - transform.position, Vector3.up);
                    int frames = Mathf.CeilToInt(delta.magnitude / .07f) + 15;
                    for (int f = 0; f < frames; f++)
                    {
                        delta = Vector3.ProjectOnPlane(points[i] - transform.position, Vector3.up);
                        if (delta.magnitude < .22f) break;
                        controller.Move(delta.normalized * .07f + Vector3.down * .05f); yield return null;
                    }
                    traversed &= delta.magnitude < .3f && controller.isGrounded;
                }
                if (traversed) report.residentialWalkPasses++;
            }
            report.residentialAccessConfigured = access.entranceCount >= 18 && access.connectedEntrances == access.entranceCount &&
                report.residentialSurfaceChecks > 0 && report.residentialSurfaceChecks == report.residentialSurfaceHits &&
                report.residentialWalkChecks >= 3 && report.residentialWalkPasses == report.residentialWalkChecks && report.residentialDestinationsPaved;
        }
        var photoEntrance=GameObject.Find("Photo entrance 11 · threshold stage01");
        report.photoEntrancePresent=photoEntrance;
        if(photoEntrance)
        {
            var photoSurface=photoEntrance.GetComponentInChildren<MeshCollider>();
            Vector3 entry=new Vector3(125.4542f,.16f,5.86925f);
            report.photoEntranceSurfaceValid=photoSurface && new[]{entry,entry+Vector3.right*.65f,entry+Vector3.forward*.7f}.All(p=>
                photoSurface.Raycast(new Ray(p+Vector3.up*2,Vector3.down),out var hit,2.4f)&&hit.normal.y>.85f&&Mathf.Abs(hit.point.y-.16f)<.01f);
            Vector3 start=entry+Vector3.left*2.2f;start.y=1.3f;
            Teleport(controller,start,Quaternion.LookRotation(Vector3.right));yield return Settle(controller);
            Vector3 from=transform.position;
            for(int frame=0;frame<65;frame++){controller.Move(Vector3.right*.045f+Vector3.down*.04f);yield return null;}
            report.photoEntranceWalkValid=controller.isGrounded&&transform.position.x-from.x>2.55f&&Mathf.Abs(transform.position.z-entry.z)<.3f;
        }
        var lampNetwork=FindAnyObjectByType<StreetLampNetwork>();
        if(lampNetwork)
        {
            report.streetLampSources=lampNetwork.lamps.Length;report.addedCourtyardLamps=lampNetwork.addedCourtyardPositions;
            report.streetLampsConfigured=lampNetwork.retainedStreetPositions>10&&lampNetwork.addedCourtyardPositions>0&&lampNetwork.lamps.Length>20
                &&lampNetwork.lamps.All(l=>l&&l.type==LightType.Spot&&l.range<=20&&l.shadows==LightShadows.None);
            var pole=lampNetwork.GetComponentsInChildren<CapsuleCollider>().First(c=>c.name.Contains("Courtyard"));
            Vector3 direction=pole.transform.right;Vector3 start=pole.transform.position-direction*1.3f;start.y+=1.3f;
            Teleport(controller,start,Quaternion.LookRotation(direction));yield return Settle(controller);
            for(int frame=0;frame<70;frame++){controller.Move(direction*.035f+Vector3.down*.04f);yield return null;}
            float distance=Vector3.Dot(transform.position-pole.transform.position,direction);
            report.streetLampCollision=controller.isGrounded&&distance<-.2f&&distance>-.65f;
        }
        var centralCourt=GameObject.Find("Двор №7 · мощение и подпорные стенки по фото · stage04");
        report.centralCourtPresent=centralCourt;
        if(centralCourt)
        {
            Vector3 start=new Vector3(40.1f,1.3f,29.7f);Teleport(controller,start,Quaternion.identity);yield return Settle(controller);
            Vector3 from=transform.position;
            for(int frame=0;frame<260;frame++){controller.Move(Vector3.forward*.055f+Vector3.down*.04f);yield return null;}
            report.centralCourtWalkValid=controller.isGrounded&&transform.position.z-from.z>13.2f&&Mathf.Abs(transform.position.x-40.1f)<.3f;
        }
        var terrain=FindObjectsByType<MeshCollider>().FirstOrDefault(c=>c.name=="Grass"&&c.sharedMesh&&c.sharedMesh.vertexCount>50000&&c.bounds.size.x>1000);
        report.districtTerrainPresent=terrain;
        report.districtTerrainValid=terrain&&terrain.sharedMesh.vertexCount>50000&&terrain.bounds.size.y>.04f;
        report.courtDoorLowPresent=GameObject.Find("Photo courtyard7 · low entrance threshold");
        if(report.courtDoorLowPresent)
        {
            Vector3 start=new Vector3(58.891938f,1.3f,38.305f);Teleport(controller,start,Quaternion.LookRotation(Vector3.right));yield return Settle(controller);
            Vector3 from=transform.position;
            for(int frame=0;frame<65;frame++){controller.Move(Vector3.right*.045f+Vector3.down*.04f);yield return null;}
            report.courtDoorLowWalkValid=controller.isGrounded&&transform.position.x-from.x>2.55f;
        }
        report.stage05CivilPresent=GameObject.Find("Двор №7 · сопряжения и рельеф газонов stage05");
        if(report.stage05CivilPresent)
        {
            var connector=FindObjectsByType<MeshCollider>().Single(c=>c.sharedMesh&&c.sharedMesh.name=="CourtDoorContinuousPaving05");
            // Independent rectangle samples inspect the full widened corridor, not
            // the mesh vertices and not the old approach's short authored route.
            for(float x=41.73f;x<60.13f;x+=.23f)for(float z=37.18f;z<39.44f;z+=.16f)
            {
                report.stage05ApproachChecks++;
                if(connector.Raycast(new Ray(new Vector3(x,1,z),Vector3.down),out var hit,2)&&hit.normal.y>.97f)report.stage05ApproachHits++;
            }
            report.stage05ApproachPaved=report.stage05ApproachChecks>500&&report.stage05ApproachChecks==report.stage05ApproachHits;
            Teleport(controller,new Vector3(42,1.3f,38.305f),Quaternion.LookRotation(Vector3.right));yield return Settle(controller);var from=transform.position;
            for(int frame=0;frame<300;frame++){controller.Move(Vector3.right*.06f+Vector3.down*.04f);yield return null;}
            report.stage05ApproachWalkValid=controller.isGrounded&&transform.position.x-from.x>17.7f&&Mathf.Abs(transform.position.z-38.305f)<.25f;
        }
        report.passed = report.grounded && report.buildingCollision && report.walkDistance > 4.5f && report.missingMaterials == 0
            && report.approachesWalkable && report.entranceStepsWalkable && report.containerCollision
            && report.benchCollision && report.treeCollision && report.lodConfigured
            && report.referenceBuildingsConfigured && report.referenceBuildingCollisions && report.graphicsMaterialsConfigured
            && report.vehiclesAbsent && report.meadowCells >= 80 && report.graphicsPresentationConfigured
            && report.roadNetworkConfigured && report.roadSurfacesContinuous && report.facadeMaterialsConfigured
            && report.roadTransitionsContinuous && report.woodlandPlacementValid && report.woodlandLodsConfigured && report.lightingConfigured
            && report.bakedProbeDataValid
            && report.playgroundsConfigured && report.residentialAccessConfigured && report.roadMaterialsDistinct && report.grassCoverageExpanded
            && report.parkingPlacementValid && report.parkingSurfacesContinuous && report.parkingAccessConnected
            && report.entrancesHaveContinuousPaving && (!report.photoEntrancePresent || report.photoEntranceSurfaceValid&&report.photoEntranceWalkValid)
            &&(!lampNetwork||report.streetLampsConfigured&&report.streetLampCollision)
            &&(!report.centralCourtPresent||report.centralCourtWalkValid&&report.districtTerrainPresent&&report.districtTerrainValid)
            &&(!report.courtDoorLowPresent||report.courtDoorLowWalkValid)
            &&(!report.stage05CivilPresent||report.stage05ApproachPaved&&report.stage05ApproachWalkValid);

        controller.enabled = false;
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        camera.transform.localPosition = Vector3.zero;
        if (block && block.inspectionViews.Length == 4)
        {
            string[] names = { "01_street.png", "02_house5.png", "03_courtyard.png", "04_overview.png" };
            for (int i = 0; i < names.Length; i++)
                yield return Capture(camera, block.inspectionViews[i], Path.Combine(output, names[i]));
        }
        else report.passed = false;
        if (args.Contains("-buildingQA")) yield return CaptureBuildings(camera, referenceBuildings, output);
        if (args.Contains("-buildingQA")) yield return CaptureBackgroundOpenings(camera, output);
        if (args.Contains("-graphicsQA")) yield return CaptureGraphics(camera, output);
        if (args.Contains("-graphicsQA")) yield return CaptureGeographicEnvironment(camera, output);
        if (args.Contains("-graphicsQA")) yield return CaptureLighting(camera, output);
        if (args.Contains("-graphicsQA")) yield return CaptureCourtyards(camera, playgrounds, access, output);
        if (args.Contains("-graphicsQA")) yield return CaptureParking(camera, parking, output);
        if(report.centralCourtPresent)yield return CaptureDistrictViews(camera,output);
        if (args.Contains("-rayTracingQA"))
        {
            yield return CheckRayTracing(camera, output, report, 1600, 900, 4);
            report.passed &= RayTracingPassed(report);
        }
        if(lampNetwork)
        {
            yield return CaptureStreetLamps(camera,lampNetwork,report,output);
            report.passed &= report.streetLampNightValid;
        }
        if (args.Contains("-graphicsBenchmark")) yield return Benchmark(camera, block, output);
        File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
        Debug.Log("STREET_QA " + JsonUtility.ToJson(report));
        Application.Quit(report.passed ? 0 : 2);
    }

    private IEnumerator CheckRayTracing(Camera camera, string output, Report report, int width, int height, int samples)
    {
        var effect = camera.GetComponent<StreetPresentation>();
        if (!effect) yield break;
        var view = new GameObject("DXR contact inspection").transform;
        view.position = new Vector3(28, 1.75f, 38.6f);
        view.rotation = Quaternion.LookRotation(new Vector3(33, .73f, 35.8f) - view.position);
        bool enabled = effect.RayTracingEnabled;
        effect.RayTracingEnabled = true;
        yield return Capture(camera, view, Path.Combine(output, "ray_contacts.png"), width, height, samples);
        report.rayTracingActive = effect.RayTracingActive;
        report.rayTracingInstances = effect.RayTracingInstances;
        report.rayTracedContactPixels = effect.CountRayTracedContacts();
        effect.RayTracingEnabled = false;
        yield return Capture(camera, view, Path.Combine(output, "ray_contacts_fallback.png"), width, height, samples);
        report.rayTracingFallback = !effect.RayTracingActive;
        effect.RayTracingEnabled = enabled;
        Destroy(view.gameObject);
    }

    private static bool RayTracingPassed(Report report) => report.rayTracingActive && report.rayTracingFallback &&
        report.rayTracingInstances > 0 && report.rayTracedContactPixels > 100;

    private IEnumerator CaptureStreetLamps(Camera camera,StreetLampNetwork network,Report report,string output)
    {
        string directory=Path.Combine(output,"lamps");Directory.CreateDirectory(directory);
        var lamp=network.GetComponentsInChildren<CapsuleCollider>().First(c=>c.name.Contains("Courtyard"));
        var view=new GameObject("QA · фонари").transform;
        view.position=lamp.transform.position+lamp.transform.right*6-lamp.transform.forward*9+Vector3.up*2.8f;
        view.rotation=Quaternion.LookRotation(lamp.transform.position+Vector3.up*2.2f-view.position);
        yield return Capture(camera,view,Path.Combine(directory,"01_daylight.png"));
        var sun=RenderSettings.sun;float intensity=sun.intensity;
        var sky=RenderSettings.ambientSkyColor;var equator=RenderSettings.ambientEquatorColor;var ground=RenderSettings.ambientGroundColor;
        var skybox=RenderSettings.skybox;bool hasExposure=skybox&&skybox.HasProperty("_Exposure");float exposure=hasExposure?skybox.GetFloat("_Exposure"):0;
        bool night=network.forceNight, cameraWasEnabled=camera.enabled;
        try
        {
            sun.intensity=.055f;RenderSettings.ambientSkyColor=new Color(.035f,.043f,.065f);
            RenderSettings.ambientEquatorColor=new Color(.027f,.032f,.048f);RenderSettings.ambientGroundColor=new Color(.018f,.019f,.025f);
            if(hasExposure)skybox.SetFloat("_Exposure",.15f);
            network.forceNight=true;camera.transform.SetPositionAndRotation(view.position,view.rotation);
            // The lamp network locates the enabled main camera, including in capture-only QA.
            camera.enabled = true;
            yield return new WaitForSecondsRealtime(.65f);
            camera.enabled = cameraWasEnabled;
            report.activeNightLamps=network.lamps.Count(l=>l&&l.enabled);
            report.streetLampNightValid=report.activeNightLamps>0&&report.activeNightLamps<=8;
            yield return Capture(camera,view,Path.Combine(directory,"02_dusk_lights.png"));
        }
        finally
        {
            sun.intensity=intensity;RenderSettings.ambientSkyColor=sky;RenderSettings.ambientEquatorColor=equator;RenderSettings.ambientGroundColor=ground;
            if(hasExposure)skybox.SetFloat("_Exposure",exposure);network.forceNight=night;camera.enabled=cameraWasEnabled;Destroy(view.gameObject);
        }
    }
    private IEnumerator CaptureDistrictViews(Camera camera,string output)
    {
        string directory=Path.Combine(output,"district");Directory.CreateDirectory(directory);
        var view=new GameObject("QA · весь район").transform;
        foreach(var shot in new[]{
            ("north",new Vector3(-140,8,163),new Vector3(-90,0,139)),
            ("west",new Vector3(-116,9,12),new Vector3(-70,0,12)),
            ("center",new Vector3(19,9,17),new Vector3(44,.1f,38)),
            ("east",new Vector3(231,10,36),new Vector3(193,0,34)),
            ("south",new Vector3(139,11,-215),new Vector3(72,0,-158)),
            ("library6",new Vector3(-43.8f,2.1f,-55.4f),new Vector3(-46.6f,1.9f,-45.9f)),
            ("court7_benches",new Vector3(28,1.75f,38.6f),new Vector3(33,.73f,35.8f)),
            ("court_eye",new Vector3(26,1.7f,37),new Vector3(56,1.6f,40)),
            ("court_walls",new Vector3(44,1.7f,40),new Vector3(30,.35f,45)),
            ("court7_door",new Vector3(56.8f,1.7f,40.3f),new Vector3(62.4f,1.6f,38.305f)),
            ("shop11_door",new Vector3(120.5f,1.7f,8.4f),new Vector3(126.8f,1.6f,5.869f)),
            ("court7_round_window",new Vector3(50,8,38.305f),new Vector3(62.4f,15.75f,38.305f))})
        {
            view.position=shot.Item2;view.rotation=Quaternion.LookRotation(shot.Item3-shot.Item2);
            yield return Capture(camera,view,Path.Combine(directory,shot.Item1+".png"));
        }
        Destroy(view.gameObject);
    }

    private IEnumerator CheckSector(CharacterController controller, Camera camera, string output, Report report)
    {
        Vector3 spawn = transform.position;
        Quaternion facing = transform.rotation;
        Vector3 cameraOffset = camera.transform.localPosition;
        Quaternion cameraRotation = camera.transform.localRotation;
        var access = FindAnyObjectByType<ResidentialAccess>();
        var endpoints = report.districtSector switch
        {
            "east" => new[] { new Vector2(125.454f, 5.869f), new Vector2(178.355f, 32.318f) },
            "south" => new[] { new Vector2(-18.790f, -104.057f), new Vector2(16.102f, -174.723f),
                new Vector2(72.810f, -107.209f) },
            "west" => new[] { new Vector2(-46.623f, -47.697f) },
            "north_center" => new[] { new Vector2(-103.582f, 156.452f) },
            "entries" => new[] { new Vector2(72.810f, -107.209f), new Vector2(-103.582f, 156.452f),
                new Vector2(166.8544f, 98.192f) },
            _ => Array.Empty<Vector2>()
        };
        var view = new GameObject("District approach inspection").transform;
        for (int i = 0; i < endpoints.Length; i++)
        {
            Vector2 endpoint = endpoints[i];
            report.districtWalkChecks++;
            var route = access.routes.FirstOrDefault(r => r.points.Length > 2 &&
                (new Vector2(r.points[0].x, r.points[0].z) - endpoint).sqrMagnitude < .01f);
            if (route == null)
                continue;
            // Stop on the exterior landing, clear of the closed door and its body collider.
            var points = route.points.Skip(1).Reverse().ToArray();
            yield return WalkSectorRoute(controller, points, report, route.address);
            Vector3 towardDoor = Vector3.ProjectOnPlane(route.points[0] - points[0], Vector3.up).normalized;
            view.position = points[0] - towardDoor * .55f + Vector3.up * 1.7f;
            view.rotation = Quaternion.LookRotation(route.points[0] + Vector3.up * .75f - view.position);
            yield return Capture(camera, view, Path.Combine(output, $"approach_{i + 1}.png"));
        }
        if (report.districtSector == "west")
        {
            view.position = new Vector3(-44.8f, 1.8f, -53.4f);
            view.rotation = Quaternion.LookRotation(new Vector3(-46.3f, .65f, -48.5f) - view.position);
            yield return Capture(camera, view, Path.Combine(output, "library_parapet.png"));
        }
        if (report.districtSector == "south")
        {
            view.position = new Vector3(77.6f, 1.7f, -104.9f);
            view.rotation = Quaternion.LookRotation(new Vector3(72.7f, .1f, -107.2f) - view.position);
            yield return Capture(camera, view, Path.Combine(output, "house15_join.png"));
        }
        if (report.districtSector == "north_center")
        {
            report.districtWalkChecks++;
            yield return WalkSectorRoute(controller, new[] { new Vector3(28.8f, .10f, 43.2f),
                new Vector3(27.1f, .10f, 44.9f) }, report, "center crossing");
            view.position = new Vector3(26.2f, 1.82f, 41.2f);
            view.rotation = Quaternion.LookRotation(new Vector3(28.7f, .12f, 44.7f) - view.position);
            yield return Capture(camera, view, Path.Combine(output, "center_join.png"));
        }
        if (report.districtSector == "entries")
        {
            foreach (var walk in new[] {
                ("library descent", new Vector3(-46.623f, .02f, -47.70f), new Vector3(-46.623f, -.18f, -46.27f), -.11f),
                ("house 13 steps", new Vector3(165.55f, .02f, 99.62f), new Vector3(165.55f, .139f, 98.70f), .055f)
            })
            {
                report.districtWalkChecks++;
                yield return WalkSectorRoute(controller, new[] { walk.Item2, walk.Item3 }, report, walk.Item1, walk.Item4);
            }
            foreach (var shot in new[] {
                ("house15_surfaces", new Vector3(77.6f, 1.7f, -104.9f), new Vector3(72.7f, .1f, -107.2f)),
                ("house1_fence", new Vector3(-120.7f, 1.75f, 158.5f), new Vector3(-103.6f, 3.4f, 156.45f)),
                ("house1_fronton", new Vector3(-113.2f, 1.75f, 155.6f), new Vector3(-103.6f, 3.5f, 156.45f)),
                ("house1_join", new Vector3(-113.2f, 1.74f, 155.0f), new Vector3(-110.8f, .13f, 155.35f)),
                ("house13_platform", new Vector3(162.9f, 1.65f, 102.4f), new Vector3(166.65f, .52f, 98.4f)),
                ("house13_canopy_reverse", new Vector3(173.2f, 1.75f, 101.4f), new Vector3(166.65f, 1.3f, 98.5f)),
                ("house13_canopy_front", new Vector3(166.85f, 1.75f, 104.2f), new Vector3(166.85f, 1.25f, 98.2f)),
                ("east_woodland", new Vector3(170.5f, 1.75f, 109f), new Vector3(151f, 4.5f, 151f)),
                ("east_woodland_open_ground", new Vector3(129f, 1.75f, 116f), new Vector3(173f, 4.2f, 148f)),
                ("library_descent", new Vector3(-46.75f, 1.65f, -48.02f), new Vector3(-46.23f, .58f, -45.89f))
            })
            {
                view.position = shot.Item2;
                view.rotation = Quaternion.LookRotation(shot.Item3 - shot.Item2);
                yield return Capture(camera, view, Path.Combine(output, shot.Item1 + ".png"));
            }
        }
        if (report.districtSector == "drive_seams")
        {
            foreach (var seam in new[] {
                (name: "south_apron", from: new Vector3(101.2f, .06f, -212.1f), to: new Vector3(102.5f, .03f, -214.2f)),
                (name: "east_paving", from: new Vector3(209.95f, .05f, 51.2f), to: new Vector3(211.35f, .04f, 51.2f))
            })
            {
                report.districtWalkChecks++;
                yield return WalkSectorRoute(controller, new[] { seam.from, seam.to }, report, seam.name);
                view.position = seam.to + Vector3.up * 1.6f + (seam.to - seam.from).normalized * 1.25f;
                view.rotation = Quaternion.LookRotation(seam.from - view.position);
                yield return Capture(camera, view, Path.Combine(output, seam.name + ".png"));
            }
        }
        if (report.districtSector == "house5")
            yield return CheckHouse5Entrances(controller, camera, view, output, report);
        if (report.districtSector == "district_entrances")
            yield return CheckDistrictEntrances(controller, camera, view, output, report, access);
        if (report.districtSector == "leaf_style")
        {
            foreach (var path in new[]
            {
                new[] { new Vector3(26, .1f, 37), new Vector3(40, .1f, 38.3f) },
                new[] { new Vector3(42, .1f, 38.3f), new Vector3(60, .1f, 38.3f) },
                new[] { new Vector3(28.8f, .1f, 43.2f), new Vector3(27.1f, .1f, 44.9f) }
            })
            {
                report.districtWalkChecks++;
                yield return WalkSectorRoute(controller, path, report, "courtyard study route");
            }
            yield return CheckLeafStyle(camera, view, output, report);
        }
        Destroy(view.gameObject);
        Teleport(controller, spawn, facing);
        camera.transform.SetLocalPositionAndRotation(cameraOffset, cameraRotation);
    }

    private IEnumerator CheckLeafStyle(Camera camera, Transform view, string output, Report report)
    {
        var flights = FindObjectsByType<StreetWindblownLeaves>().SelectMany(group => group.paths)
            .Where(path => path.renderer && path.flightTime > 0).ToArray();
        report.leafFlightPaths = flights.Length;
        var shots = new[]
        {
            ("02_court7", new Vector3(26, 1.7f, 37), new Vector3(56, 1.6f, 40)),
            ("03_court_reverse", new Vector3(58, 1.7f, 38.3f), new Vector3(32, 1.3f, 38.3f)),
            ("04_leaf_carpet", new Vector3(29, 1.53f, 38.15f), new Vector3(34, .2f, 36.5f)),
            ("05_court_benches", new Vector3(28, 1.75f, 38.6f), new Vector3(33, .73f, 35.8f))
        };
        foreach (var (name, from, to) in shots)
        {
            view.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
            yield return Capture(camera, view, Path.Combine(output, name + ".png"));
            if (name != "02_court7") continue;
            var visible = flights.Where(path => path.renderer.enabled &&
                (path.renderer.transform.position - from).sqrMagnitude < 18f * 18f)
                .Select(path => (path.renderer, path.renderer.transform.position)).ToArray();
            yield return new WaitForSecondsRealtime(.18f);
            report.leafFlightMoved = visible.Count(leaf => leaf.renderer.enabled &&
                (leaf.renderer.transform.position - leaf.position).sqrMagnitude > .0004f);
        }
        Debug.Log($"LEAF_STYLE_CHECK bakedPaths={report.leafFlightPaths} movedNearPlayer={report.leafFlightMoved}");
    }

    private IEnumerator CheckDistrictEntrances(CharacterController controller, Camera camera,
        Transform view, string output, Report report, ResidentialAccess access)
    {
        var routes = access.routes.Where(r => r.address.Contains(" / ", StringComparison.Ordinal))
            .OrderBy(r => r.address, StringComparer.Ordinal).ToArray();
        foreach (var route in routes)
        {
            // Stop outside the door plane. The flights are checked separately below.
            var points = route.points.Take(6).Skip(1).Reverse().ToArray();
            report.districtWalkChecks++;
            if (points.Length >= 2)
                yield return WalkSectorRoute(controller, points, report, route.address);
        }

        var buildings = FindObjectsByType<StreetBuilding>().OrderBy(b => b.address).ThenBy(b => b.osmId);
        foreach (var building in buildings)
        {
            var porches = building.GetComponentsInChildren<Transform>()
                .Where(marker => marker.Find("Крыльцо · цельные ступени")).OrderBy(marker => marker.name).ToArray();
            foreach (var porch in porches)
            {
                Vector3 outside = porch.position + porch.forward * .55f;
                Vector3 landing = porch.position - porch.forward * .92f;
                report.districtWalkChecks++;
                yield return WalkSectorRoute(controller, new[] { outside, landing }, report,
                    $"{building.address} / {porch.name} uphill", .34f);
            }

            if (porches.Length == 0)
                continue;
            var sample = porches[0];
            Vector3 from = sample.position + sample.forward * .85f + sample.right * 2.7f + Vector3.up * 1.65f;
            Vector3 to = sample.position - sample.forward * .95f + Vector3.up * .9f;
            view.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
            yield return Capture(camera, view, Path.Combine(output, $"{building.address}_{building.osmId}_porch.png"));
        }
        yield return CheckHouse5Entrances(controller, camera, view, output, report);
    }

    private IEnumerator CheckHouse5Entrances(CharacterController controller, Camera camera,
        Transform view, string output, Report report)
    {
        var block = FindAnyObjectByType<PilotBlock>();
        if (!block || block.entrances.Length != 5)
        {
            report.districtWalkChecks++;
            yield break;
        }

        foreach (var approach in block.entrances.OrderBy(t => t.name))
        {
            Vector3 outside = approach.position;
            Vector3 landing = outside + approach.forward * 3.2f;
            report.districtWalkChecks += 2;
            yield return WalkSectorRoute(controller, new[] { outside, landing }, report, approach.name + " uphill", .38f);
            yield return WalkSectorRoute(controller, new[] { landing, outside }, report, approach.name + " downhill", -.38f);

            var portal = approach.parent;
            string number = approach.name.Split(' ').Last();
            foreach (var shot in new[] {
                ("stairs", new Vector3(-3.2f, 1.65f, 1.73f), new Vector3(.8f, .8f, 1.95f)),
                ("jamb", new Vector3(-.70f, 1.65f, 2.28f), new Vector3(2.60f, 1.2f, .22f))
            })
            {
                Vector3 from = portal.TransformPoint(shot.Item2);
                view.SetPositionAndRotation(from, Quaternion.LookRotation(portal.TransformPoint(shot.Item3) - from));
                yield return Capture(camera, view, Path.Combine(output, $"house5_{number}_{shot.Item1}.png"));
            }
        }
    }

    private IEnumerator WalkSectorRoute(CharacterController controller, Vector3[] points, Report report, string label,
        float minimumHeightChange = 0)
    {
        Teleport(controller, points[0] + Vector3.up * 1.25f, Quaternion.identity);
        yield return Settle(controller);
        float startHeight = transform.position.y;
        bool traversed = controller.isGrounded;
        for (int i = 1; i < points.Length && traversed; i++)
        {
            Vector3 delta = Vector3.ProjectOnPlane(points[i] - transform.position, Vector3.up);
            int frames = Mathf.CeilToInt(delta.magnitude / .07f) + 16;
            for (int frame = 0; frame < frames && delta.magnitude >= .15f; frame++)
            {
                controller.Move(delta.normalized * .07f + Vector3.down * .05f);
                yield return null;
                delta = Vector3.ProjectOnPlane(points[i] - transform.position, Vector3.up);
            }
            traversed = delta.magnitude < .24f && controller.isGrounded;
        }
        float heightChange = transform.position.y - startHeight;
        if (minimumHeightChange != 0)
            traversed &= heightChange * Mathf.Sign(minimumHeightChange) >= Mathf.Abs(minimumHeightChange);
        if (traversed) report.districtWalkPasses++;
        Debug.Log($"DISTRICT_WALK {label} passed={traversed} at={transform.position:F3} heightChange={heightChange:F3}");
    }

    private static bool ComplexAccessRoute(ResidentialAccess.Route route) => route.address.Contains("114788560") ||
        route.address.Contains("114788167") && route.address.Contains("к крыльцу 2") ||
        route.address.Contains("114788172") && route.address.Contains("к крыльцу 1");

    private static bool ParkingSurfaceAt(ParkingSite site, Vector3 point) => site.surfaces.Any(c => c &&
        c.Raycast(new Ray(point + Vector3.up * 1.5f, Vector3.down), out var hit, 2) &&
        hit.normal.y > .85f && Mathf.Abs(hit.point.y - point.y) < .035f);

    private IEnumerator CaptureParking(Camera camera, ParkingSite[] parking, string output)
    {
        string directory = Path.Combine(output, "parking"); Directory.CreateDirectory(directory);
        foreach (var site in parking.OrderBy(p => p.osmId))
            for (int i = 0; i < site.inspectionViews.Length; i++)
                yield return Capture(camera, site.inspectionViews[i], Path.Combine(directory, site.osmId + "_" + i + ".png"));
    }

    private static bool PavedDestination(Vector3 point, MeshCollider[] connectors)
    {
        foreach (var hit in Physics.RaycastAll(point + Vector3.up * 1.3f, Vector3.down, 1.7f))
        {
            var collider = hit.collider as MeshCollider;
            if (!collider || hit.normal.y < .85f) continue;
            var provenance=collider.GetComponent<SavedPavedSurface>();
            bool primary=collider.sharedMesh&&(collider.sharedMesh.name=="ReferenceRoad"||collider.sharedMesh.name.Contains("ReferenceWalkways"));
            if(connectors.Contains(collider)&&!primary&&(!provenance||!provenance.existingNetworkDestination))continue;
            var renderer = collider.GetComponent<Renderer>();
            if (!renderer || !collider.sharedMesh) continue;
            int triangle = hit.triangleIndex;
            for (int i = 0; i < collider.sharedMesh.subMeshCount && i < renderer.sharedMaterials.Length; i++)
            {
                int count = (int)collider.sharedMesh.GetIndexCount(i) / 3;
                if (triangle >= count) { triangle -= count; continue; }
                var material = renderer.sharedMaterials[i];
                if (!material) break;
                float type = material.HasProperty("_SurfaceClass") ? material.GetFloat("_SurfaceClass") : 0;
                if (material.name != "Grass" && !(type > .5f && type < 1.5f)) return true;
                break;
            }
        }
        // Rounded unions can move the road edge a few centimetres beyond the old
        // endpoint. Require a real, continuously paved short bridge to that network.
        if(!PavedPoint(point,connectors,false))return false;
        for(int direction=0;direction<8;direction++)
        {
            float angle=direction*Mathf.PI/4;
            var target=point+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*.35f;
            if(!PavedPoint(target,connectors,true))continue;
            bool continuous=true;
            for(int sample=1;sample<=4;sample++)continuous&=PavedPoint(Vector3.Lerp(point,target,sample/4f),connectors,false);
            if(continuous)return true;
        }
        return false;
    }

    private static bool PavedPoint(Vector3 point,MeshCollider[] connectors,bool destination)
    {
        foreach(var hit in Physics.RaycastAll(point+Vector3.up*1.3f,Vector3.down,1.7f))
        {
            var collider=hit.collider as MeshCollider;if(!collider||!collider.sharedMesh||hit.normal.y<.85f)continue;
            var renderer=collider.GetComponent<Renderer>();if(!renderer)continue;int end=0;
            for(int sub=0;sub<collider.sharedMesh.subMeshCount;sub++)
            {
                end+=(int)collider.sharedMesh.GetIndexCount(sub)/3;if(hit.triangleIndex>=end)continue;
                var material=sub<renderer.sharedMaterials.Length?renderer.sharedMaterials[sub]:null;if(!material)break;
                float kind=material.HasProperty("_SurfaceClass")?material.GetFloat("_SurfaceClass"):0;string n=material.name.ToLowerInvariant();
                bool hard=!(kind>.5f&&kind<1.5f)&&(kind>3.5f||n.Contains("asphalt")||n.Contains("pav")||n.Contains("footpath")||n=="path"||n.Contains("concrete"));
                if(!hard)break;if(!destination)return true;
                var provenance=collider.GetComponent<SavedPavedSurface>();
                if(provenance&&provenance.existingNetworkDestination||collider.sharedMesh.name=="ReferenceRoad"||collider.sharedMesh.name.Contains("ReferenceWalkways")||!connectors.Contains(collider))return true;
                break;
            }
        }
        return false;
    }

    private IEnumerator CaptureCourtyards(Camera camera, PlaygroundSite[] playgrounds, ResidentialAccess access, string output)
    {
        string directory = Path.Combine(output, "courtyards"); Directory.CreateDirectory(directory);
        var view = new GameObject("QA · тротуары к домам и площадки").transform;
        try
        {
            foreach (var site in playgrounds)
            {
                Vector3 center = site.worldBounds.center; center.y = 1;
                view.position = center + new Vector3(-14, 9, -17); view.rotation = Quaternion.LookRotation(center - view.position);
                yield return Capture(camera, view, Path.Combine(directory, "playground_" + site.osmId + "_context.png"));
                if (site.equipment.Length == 0) continue;
                Vector3 detail = site.equipment[0].position;
                view.position = detail + new Vector3(-4, 2.1f, 6); view.rotation = Quaternion.LookRotation(detail + Vector3.up - view.position);
                yield return Capture(camera, view, Path.Combine(directory, "playground_" + site.osmId + "_detail.png"));
            }
            int index = 0;
            var newRoutes = access.routes.Where(r => !r.usesExistingSurface && r.points.Length > 1).ToArray();
            foreach (var route in newRoutes.Take(8).Concat(newRoutes.Where(ComplexAccessRoute)).Distinct())
            {
                Vector3 end = route.points[0], outside = route.points[route.points.Length - 1];
                Vector3 side = Vector3.Cross((outside - end).normalized, Vector3.up);
                if (ComplexAccessRoute(route)) outside = route.points[Mathf.Min(12, route.points.Length - 1)];
                view.position = outside + side * 4 + Vector3.up * 2.3f;
                view.rotation = Quaternion.LookRotation((end + outside) * .5f + Vector3.up * .5f - view.position);
                yield return Capture(camera, view, Path.Combine(directory, "residential_access_" + index++ + ".png"));
            }
        }
        finally { Destroy(view.gameObject); }
    }

    private IEnumerator CaptureLighting(Camera camera, string output)
    {
        string directory = Path.Combine(output, "lighting"); Directory.CreateDirectory(directory);
        var view = new GameObject("QA · солнце, лучи и тени").transform;
        var effect = camera.GetComponent<StreetPresentation>();
        var sun = RenderSettings.sun;
        float shafts = effect.sunShafts, strength = sun.shadowStrength;
        try
        {
            var landscape = FindAnyObjectByType<GeographicLandscape>();
            Vector3 tree = landscape.patches.First(p => p.osmId == 114788557).trees[0].position;
            var direction = -sun.transform.forward;
            Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            view.position = tree - horizontal * 11 + Vector3.up * 1.7f;
            view.rotation = Quaternion.LookRotation(direction);
            yield return Capture(camera, view, Path.Combine(directory, "01_sun_through_canopy.png"));
            effect.sunShafts = 0;
            yield return Capture(camera, view, Path.Combine(directory, "02_control_without_shafts.png"));
            effect.sunShafts = shafts;
            yield return Capture(camera, view, Path.Combine(directory, "05_sun_through_canopy_no_msaa.png"), samples: 1);
            view.position = tree + new Vector3(7, 1.7f, -8);
            view.rotation = Quaternion.LookRotation(tree + Vector3.up * 2 - view.position);
            yield return Capture(camera, view, Path.Combine(directory, "03_ground_shadows.png"));
            sun.shadowStrength = 0;
            yield return Capture(camera, view, Path.Combine(directory, "04_control_without_sun_shadows.png"));
            sun.shadowStrength = strength;
        }
        finally
        {
            effect.sunShafts = shafts; sun.shadowStrength = strength; Destroy(view.gameObject);
        }
    }

    private IEnumerator CaptureGeographicEnvironment(Camera camera, string output)
    {
        string directory = Path.Combine(output, "environment"); Directory.CreateDirectory(directory);
        var view = new GameObject("QA · географическое окружение").transform;
        try
        {
            var landscape = FindAnyObjectByType<GeographicLandscape>();
            foreach (var patch in landscape.patches)
            {
                var center = patch.boundary.Aggregate(Vector3.zero, (a, b) => a + b) / patch.boundary.Length;
                float radius = patch.boundary.Max(p => Vector3.Distance(p, center));
                view.position = center + new Vector3(-.65f, .9f, -.8f) * Mathf.Max(50, radius * 1.65f);
                view.rotation = Quaternion.LookRotation(center - view.position);
                yield return Capture(camera, view, Path.Combine(directory, "wood_" + patch.osmId + "_context.png"));
            }
            var near = landscape.patches.First(p => p.osmId == 114788557).trees[0].position;
            view.position = near + new Vector3(7, 1.7f, -8); view.rotation = Quaternion.LookRotation(near + Vector3.up * 3 - view.position);
            yield return Capture(camera, view, Path.Combine(directory, "wood_near_player.png"));
            var network = FindAnyObjectByType<StreetRoadNetwork>();
            for (int i = 0; i < network.transitionProbePoints.Length; i += 12)
            {
                Vector3 point = network.transitionProbePoints[i];
                view.position = point + new Vector3(-5, 1.6f, -7); view.rotation = Quaternion.LookRotation(point + Vector3.up * .15f - view.position);
                yield return Capture(camera, view, Path.Combine(directory, "road_transition_" + i + ".png"));
            }
        }
        finally { Destroy(view.gameObject); }
    }

    private static void Teleport(CharacterController controller, Vector3 position, Quaternion rotation)
    {
        controller.enabled = false;
        controller.transform.SetPositionAndRotation(position, rotation);
        controller.enabled = true;
        Physics.SyncTransforms();
    }

    private static IEnumerator Settle(CharacterController controller)
    {
        for (int i = 0; i < 35; i++)
        {
            controller.Move(Vector3.down * .06f);
            yield return null;
        }
    }

    private IEnumerator ProbeCollision(CharacterController controller, Collider obstacle, Action<bool> result)
    {
        if (!obstacle) { result(false); yield break; }
        Bounds bounds = obstacle.bounds;
        Vector3 start = new Vector3(bounds.center.x, 1.3f, bounds.min.z - 1.2f);
        Teleport(controller, start, Quaternion.identity);
        yield return Settle(controller);
        for (int i = 0; i < 60; i++)
        {
            controller.Move(Vector3.forward * .055f + Vector3.down * .04f);
            yield return null;
        }
        result(transform.position.z <= bounds.min.z - .18f && transform.position.z > start.z + .5f);
    }

    private IEnumerator ProbeBuilding(CharacterController controller, StreetBuilding building, Action<bool> result)
    {
        var body = building.bodyColliders.FirstOrDefault() as BoxCollider;
        if (!body) { result(false); yield break; }
        Vector3 direction = body.transform.right;
        Vector3 plane = body.transform.TransformPoint(body.center - Vector3.right * body.size.x * .5f);
        Vector3 start = plane - direction * 1.2f; start.y = 1.3f;
        Teleport(controller, start, Quaternion.LookRotation(direction));
        yield return Settle(controller);
        for (int i = 0; i < 60; i++)
        {
            controller.Move(direction * .055f + Vector3.down * .04f);
            yield return null;
        }
        float distance = Vector3.Dot(transform.position - plane, direction);
        bool success = distance <= -.18f && distance > -.72f && controller.isGrounded;
        Debug.Log("BUILDING_QA " + building.osmId + " collision=" + success + " wallDistance=" + distance);
        result(success);
    }

    private IEnumerator CaptureBuildings(Camera camera, StreetBuilding[] buildings, string output)
    {
        string directory = Path.Combine(output, "buildings"); Directory.CreateDirectory(directory);
        var renderers = FindObjectsByType<Renderer>();
        var previous = renderers.Select(r => r.forceRenderingOff).ToArray();
        var view = new GameObject("QA · осмотр здания").transform;
        try
        {
            foreach (var context in new[] {
                ("05_house6_street.png", new Vector3(-37, 4.5f, 45), new Vector3(-69, 7, -8)),
                ("06_house7_courtyard.png", new Vector3(106, 5, 42), new Vector3(60, 8, 39)) })
            {
                view.position = context.Item2; view.rotation = Quaternion.LookRotation(context.Item3 - context.Item2);
                yield return Capture(camera, view, Path.Combine(output, context.Item1));
            }
            foreach (var building in buildings)
            {
                var visible = building.GetComponentsInChildren<Renderer>();
                // Isolated real player renders supplement, not replace, the four contextual views.
                for (int i = 0; i < renderers.Length; i++) renderers[i].forceRenderingOff = previous[i] ||
                    (renderers[i].name != "Grass" && !visible.Contains(renderers[i]));
                Bounds bounds = building.localBounds;
                Vector3 center = building.transform.TransformPoint(bounds.center);
                float distance = bounds.extents.magnitude / Mathf.Sin(camera.fieldOfView * Mathf.Deg2Rad * .5f) * 1.15f;
                foreach (int sign in new[] { -1, 1 })
                {
                    Vector3 direction = new Vector3(sign, .4f, sign * .65f).normalized;
                    view.position = center + direction * distance;
                    view.rotation = Quaternion.LookRotation(center - view.position);
                    yield return Capture(camera, view, Path.Combine(directory, building.address + "_" + building.osmId + (sign < 0 ? "_west.png" : "_east.png")));
                }
                string openings = Path.Combine(directory, "openings"); Directory.CreateDirectory(openings);
                foreach (var detail in building.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Opening QA · ", StringComparison.Ordinal)))
                {
                    bool groundContext = detail.name.Contains("door") || detail.name.Contains("portal return") || detail.name.Contains("side entrance")
                        || detail.name.Contains("photo11threshold");
                    for (int i = 0; i < renderers.Length; i++) renderers[i].forceRenderingOff = previous[i] ||
                        (!groundContext && renderers[i].name != "Grass" && !visible.Contains(renderers[i]));
                    yield return Capture(camera, detail, Path.Combine(openings, building.address + "_" + building.osmId + "_" + detail.name.Replace("Opening QA · ", "") + ".png"));
                }
                if (building.osmId == 114788195 || building.osmId == 110515918)
                {
                    string lodDirectory = Path.Combine(directory, "lod"); Directory.CreateDirectory(lodDirectory);
                    var group = building.GetComponent<LODGroup>();
                    view.position = center + new Vector3(-1, .4f, -.65f).normalized * distance;
                    view.rotation = Quaternion.LookRotation(center - view.position);
                    try
                    {
                        for (int lod = 0; lod < 3; lod++)
                        {
                            group.ForceLOD(lod);
                            yield return Capture(camera, view, Path.Combine(lodDirectory, building.address + "_" + building.osmId + "_LOD" + lod + ".png"));
                        }
                    }
                    finally { group.ForceLOD(-1); }
                }
            }
        }
        finally
        {
            for (int i = 0; i < renderers.Length; i++) if (renderers[i]) renderers[i].forceRenderingOff = previous[i];
            Destroy(view.gameObject);
        }
    }

    private IEnumerator CaptureBackgroundOpenings(Camera camera, string output)
    {
        string directory = Path.Combine(output, "buildings", "background_openings"); Directory.CreateDirectory(directory);
        foreach (var detail in FindObjectsByType<Transform>().Where(t => t.name.StartsWith("Opening QA · background ", StringComparison.Ordinal)).OrderBy(t => t.name))
            yield return Capture(camera, detail, Path.Combine(directory, detail.name.Replace("Opening QA · background ", "") + ".png"));
    }

    private IEnumerator CaptureGraphics(Camera camera, string output)
    {
        string directory = Path.Combine(output, "graphics"); Directory.CreateDirectory(directory);
        var view = new GameObject("QA · материалы всей игры").transform;
        try
        {
            foreach (var shot in new[] {
                ("01_eye_level_house5", new Vector3(-29.6f, 1.8f, 13), new Vector3(-19, 3.2f, 10)),
                ("02_brick_and_glass", new Vector3(-24.5f, 3.5f, 7), new Vector3(-19.4f, 5, 10)),
                ("03_roof", new Vector3(29, 25, -42), new Vector3(2, 12, -10)),
                ("04_wood_and_paint", new Vector3(5.7f, 1.55f, -40.7f), new Vector3(4.3f, .8f, -38)),
                ("05_containers", new Vector3(27.9f, 1.65f, -40.7f), new Vector3(26.5f, .9f, -37.7f)),
                ("06_ground_and_foliage", new Vector3(-27.5f, 1.7f, 24), new Vector3(-24, 1.2f, 21)),
                ("07_paving_and_road", new Vector3(-40, 2, 23), new Vector3(-43, .1f, 16)),
                ("10_meadow_close", new Vector3(14, .75f, -45.5f), new Vector3(14.5f, .08f, -43.5f)),
                ("11_courtyard_eye_level", new Vector3(18, 1.75f, -45.5f), new Vector3(10, 3, -31)),
                ("12_street_eye_level", new Vector3(-31.5f, 1.75f, 20), new Vector3(-4, 3, -43)),
                ("13_facade_sunlit", new Vector3(6.8f, 4, -33.5f), new Vector3(6.8f, 4, -26.1f)),
                ("14_cream_brick_detail", new Vector3(-22.8f, 4.25f, 9.1f), new Vector3(-19.48f, 4.25f, 9.1f)),
                ("15_red_brick_detail", new Vector3(14, 3, -30.2f), new Vector3(14, 3, -26.06f)) })
            {
                view.position = shot.Item2; view.rotation = Quaternion.LookRotation(shot.Item3 - view.position);
                yield return Capture(camera, view, Path.Combine(directory, shot.Item1 + ".png"));
            }
            foreach (string address in new[] { "1", "2", "9", "15" })
            {
                var building = FindObjectsByType<StreetBuilding>().First(b => b.address == address);
                Vector3 target = building.transform.TransformPoint(building.localBounds.center);
                view.position = target + new Vector3(-1, .23f, -.65f).normalized * building.localBounds.extents.magnitude * 2.25f;
                view.rotation = Quaternion.LookRotation(target - view.position);
                yield return Capture(camera, view, Path.Combine(directory, "street_address_" + address + ".png"));
            }
            float normalFov = camera.fieldOfView;
            try
            {
                camera.fieldOfView = 60;
                foreach (var shot in new[] {
                    ("road_01_north_2025", new Vector3(-110.75f, 2.4f, 145.27f), new Vector3(13.05f,146.46f,0)),
                    ("road_02_house5_2025", new Vector3(-39.67f,2.4f,11.02f), new Vector3(17.62f,147.43f,0)),
                    ("road_03_south_2025", new Vector3(40.64f,2.4f,-130.80f), new Vector3(15.14f,148.39f,0)),
                    ("road_04_mapped_crossing", new Vector3(-83,2.4f,92), new Vector3(12,151,0)) })
                {
                    view.SetPositionAndRotation(shot.Item2, Quaternion.Euler(shot.Item3));
                    yield return Capture(camera, view, Path.Combine(directory, shot.Item1 + ".png"),1600,630);
                }
            }
            finally { camera.fieldOfView = normalFov; }
            var presentation = camera.GetComponent<StreetPresentation>();
            float contact = presentation.contactShadows;
            try
            {
                view.position = new Vector3(5.7f, 1.55f, -40.7f); view.rotation = Quaternion.LookRotation(new Vector3(4.3f, .8f, -38) - view.position);
                presentation.contactShadows = 0;
                yield return Capture(camera, view, Path.Combine(directory, "09_contact_shadows_off.png"));
            }
            finally { presentation.contactShadows = contact; }
            var all = FindObjectsByType<Renderer>(); var previous = all.Select(r => r.forceRenderingOff).ToArray();
            var trees = new[] { FindAnyObjectByType<PilotBlock>().tree.GetComponent<LODGroup>(),
                FindObjectsByType<LODGroup>().First(g => g.GetLODs().Any(l => l.renderers.Any(r =>
                    r.sharedMaterials.Any(m => m && m.name == "SpruceNeedles")))) };
            try
            {
                for (int tree = 0; tree < trees.Length; tree++)
                {
                    var group = trees[tree]; var visible = group.GetComponentsInChildren<Renderer>();
                    for (int i = 0; i < all.Length; i++) all[i].forceRenderingOff = previous[i] || all[i].name != "Grass" && !visible.Contains(all[i]);
                    Vector3 center = group.transform.position + Vector3.up * 4.5f;
                    view.position = center + new Vector3(-11, 1.1f, -12); view.rotation = Quaternion.LookRotation(center - view.position);
                    try
                    {
                        for (int lod = 0; lod < group.GetLODs().Length; lod++)
                        {
                            group.ForceLOD(lod);
                            yield return Capture(camera, view, Path.Combine(directory, (tree == 0 ? "tree" : "spruce") + "_LOD" + lod + ".png"));
                        }
                    }
                    finally { group.ForceLOD(-1); }
                }
            }
            finally { for (int i = 0; i < all.Length; i++) if (all[i]) all[i].forceRenderingOff = previous[i]; }
        }
        finally { Destroy(view.gameObject); }
    }

    private IEnumerator Benchmark(Camera camera, PilotBlock block, string output)
    {
        int oldTargetRate = Application.targetFrameRate, oldVsync = QualitySettings.vSyncCount;
        Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
        var report = new BenchmarkReport { gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            engine = Application.unityVersion, width = Screen.width, height = Screen.height, samplesPerView = 180,
            mode = "Explicit Camera.Render, synchronized by a 1-pixel GPU readback; includes readback overhead, excludes simulation and PNG encoding",
            views = new BenchmarkView[4] };
        var forestView = new GameObject("Густой лес · на высоте игрока").transform;
        Vector3 forestTree = FindAnyObjectByType<GeographicLandscape>().patches.First(p => p.osmId == 114788557).trees[0].position;
        forestView.position = forestTree + new Vector3(7, 1.7f, -8);
        forestView.rotation = Quaternion.LookRotation(forestTree + Vector3.up * 3 - forestView.position);
        var target = new RenderTexture(Screen.width, Screen.height, 24) { antiAliasing = 4 };
        var readback = new Texture2D(1, 1, TextureFormat.RGB24, false);
        var oldTarget = camera.targetTexture;
        var oldActive = RenderTexture.active;
        bool wasEnabled = camera.enabled;
        camera.enabled = false;
        camera.targetTexture = target;
        try
        {
            for (int v = 0; v < report.views.Length; v++)
            {
                var view = v == 3 ? forestView : block.inspectionViews[v == 2 ? 3 : v];
                camera.transform.SetPositionAndRotation(view.position, view.rotation);
                for (int i = 0; i < 60; i++) { RenderMeasuredFrame(camera, target, readback); yield return null; }
                var samples = new double[report.samplesPerView];
                var clock = new System.Diagnostics.Stopwatch();
                for (int i = 0; i < samples.Length; i++)
                {
                    yield return null;
                    clock.Restart();
                    RenderMeasuredFrame(camera, target, readback);
                    clock.Stop(); samples[i] = clock.Elapsed.TotalMilliseconds;
                }
                Array.Sort(samples);
                report.views[v] = new BenchmarkView { name = view.name, averageFps = (float)(1000 / samples.Average()),
                    medianFrameMs = (float)samples[samples.Length / 2], p95FrameMs = (float)samples[(int)(samples.Length * .95)] };
            }
        }
        finally
        {
            camera.targetTexture = oldTarget;
            camera.enabled = wasEnabled;
            RenderTexture.active = oldActive;
            target.Release(); Destroy(target); Destroy(readback);
            Destroy(forestView.gameObject);
            Application.targetFrameRate = oldTargetRate; QualitySettings.vSyncCount = oldVsync;
        }
        File.WriteAllText(Path.Combine(output, "benchmark.json"), JsonUtility.ToJson(report, true));
        Debug.Log("GRAPHICS_BENCHMARK " + JsonUtility.ToJson(report));
    }

    private static void RenderMeasuredFrame(Camera camera, RenderTexture target, Texture2D readback)
    {
        camera.Render();
        RenderTexture.active = target;
        // Hidden or minimized Unity players can skip normal rendering while still updating scripts.
        // ReadPixels fences the actual GPU render so such frames cannot report fictitious FPS.
        readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
    }

    private IEnumerator Capture(Camera camera, Transform view, string path, int width = 1600, int height = 900, int samples = 4)
    {
        if (renderOnCaptureOnly)
        {
            if (width > 960)
            {
                height = height * 960 / width;
                width = 960;
            }
            samples = 1;
        }
        camera.transform.SetPositionAndRotation(view.position, view.rotation);
        yield return new WaitForSecondsRealtime(.35f);
        var targetTexture = new RenderTexture(width, height, 24) { antiAliasing = samples };
        var previous = RenderTexture.active;
        camera.targetTexture = targetTexture;
        camera.Render();
        RenderTexture.active = targetTexture;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = previous;
        Destroy(image);
        targetTexture.Release();
        Destroy(targetTexture);
        yield return null;
    }

    private void AbortOnException(string condition, string stackTrace, LogType type)
    {
        if (aborting || type != LogType.Exception && type != LogType.Assert && type != LogType.Error) return;
        aborting = true;
        File.WriteAllText(Path.Combine(outputPath, "report.json"), "{\"passed\":false,\"status\":\"exception; see player log\"}");
        Application.Quit(2);
    }

    private void OnDestroy() => Application.logMessageReceived -= AbortOnException;
}
