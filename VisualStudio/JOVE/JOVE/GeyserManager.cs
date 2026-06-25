using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

[KSPAddon(KSPAddon.Startup.Flight, false)]
public class GeyserManager : MonoBehaviour
{
    // hard coded for debug this will be data driven eventually
    private const string TARGET_BODY = "Thatmo";
    private const float TRIGGER_RANGE_KM = 50f;
    private const float MIN_INTERVAL = 12f;
    private const float MAX_INTERVAL = 24f; // TODO change before release

    private float _nextGeyserTime;
    private CelestialBody _targetBody;

    void Start()
    {
        Debug.Log("[JOVE.Geysers] Initialised.");
        ScheduleNextGeyser();

        foreach (CelestialBody body in FlightGlobals.Bodies)
        {
            if (body.bodyName == TARGET_BODY)
            {
                _targetBody = body;
                break;
            }
        }

        if (_targetBody == null)
            Debug.LogWarning("[JOVE.Geysers] Could not find body: " + TARGET_BODY);
    }

    void Update()
    {
        // check block, checking for if theres a vessel, if near planet, if on surface, if time has passed
        if (FlightGlobals.ActiveVessel == null) return;
        if (_targetBody == null) return;
        if (FlightGlobals.ActiveVessel.mainBody != _targetBody) return;

        double altitude = FlightGlobals.ActiveVessel.altitude;
        if (altitude > TRIGGER_RANGE_KM * 1000f) return;

        if (Time.time < _nextGeyserTime) return;

        // if all checks pass spawn geyser
        SpawnGeyser();
        ScheduleNextGeyser();
    }

    void ScheduleNextGeyser()
    {
        _nextGeyserTime = Time.time + UnityEngine.Random.Range(MIN_INTERVAL, MAX_INTERVAL);
    }

    void SpawnGeyser()
    {
        // range in meters from craft in which geyser can spawn
        float distance = UnityEngine.Random.Range(20f, 100f);
        float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;

        // make actual worldspace position to spawn geyser at
        double vesselLat = FlightGlobals.ActiveVessel.latitude;
        double vesselLon = FlightGlobals.ActiveVessel.longitude;

        double latOffset = (distance * Mathf.Cos(angle)) / _targetBody.Radius * (180.0 / Math.PI);
        double lonOffset = (distance * Mathf.Sin(angle)) / _targetBody.Radius * (180.0 / Math.PI);

        double spawnLat = vesselLat + latOffset;
        double spawnLon = vesselLon + lonOffset;
        double surfaceAltitude = 0.0;

        if (_targetBody.pqsController != null)
        {
            Vector3d nVector = _targetBody.GetRelSurfaceNVector(spawnLat, spawnLon);
            surfaceAltitude = _targetBody.pqsController.GetSurfaceHeight(nVector) - _targetBody.Radius;

            if (double.IsNaN(surfaceAltitude) || surfaceAltitude < 0.0)
                surfaceAltitude = 0.0;
        }
        Vector3d surfacePoint = _targetBody.GetWorldSurfacePosition(spawnLat, spawnLon, surfaceAltitude + 2.0);

        // local up direction away from the body center
        Vector3d surfaceNormal = (surfacePoint - _targetBody.position).normalized;

        GameObject geyserObj = new GameObject("JOVE_Geyser");

        // rotate
        geyserObj.transform.position = surfacePoint;
        geyserObj.transform.rotation = Quaternion.FromToRotation(Vector3.forward, (Vector3)surfaceNormal);

        GeyserEffect effect = geyserObj.AddComponent<GeyserEffect>();
        effect.Init(surfaceNormal);

        Debug.Log($"[JOVE.Geysers] Spawned Geyser at {spawnLat:F2}, {spawnLon:F2}");
    }
}

public class GeyserEffect : MonoBehaviour
{
    private readonly List<ParticleSystem> _systems = new List<ParticleSystem>();

    private float _lifetime = 18f;
    private float _age = 0f;

    public void Init(Vector3d surfaceNormal)
    {
        CreateCoreJet();
        CreateMistCloud();
        CreateBasePuff();
        CreateIceFlecks();

        foreach (ParticleSystem ps in _systems)
        {
            ps.Play();
        }
    }

    private ParticleSystem CreateChildSystem(string name, Vector3 localPosition)
    {
        GameObject child = new GameObject(name);
        child.transform.parent = transform;
        child.transform.localPosition = localPosition;
        child.transform.localRotation = Quaternion.identity;

        ParticleSystem ps = child.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        _systems.Add(ps);
        return ps;
    }

    private void ConfigureRenderer(ParticleSystem ps)
    {
        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");

        if (shader == null)
            shader = Shader.Find("Particles/Alpha Blended");

        if (shader != null)
        {
            renderer.material = new Material(shader);
        }
        else
        {
            Debug.LogWarning("[JOVE.Geysers] Could not find particle shader. Particle system may render incorrectly.");
        }
    }

    private Gradient MakeGradient(
        Color colorA,
        Color colorB,
        float alphaPeak,
        float alphaMid)
    {
        Gradient grad = new Gradient();

        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(colorA, 0f),
                new GradientColorKey(colorB, 0.55f),
                new GradientColorKey(colorA, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(alphaPeak, 0.08f),
                new GradientAlphaKey(alphaMid, 0.65f),
                new GradientAlphaKey(0f, 1f)
            }
        );

        return grad;
    }

    private void CreateCoreJet()
    {
        ParticleSystem ps = CreateChildSystem("JOVE_Geyser_CoreJet", Vector3.zero);

        // particle behavior
        var main = ps.main;
        main.loop = false;
        main.duration = 5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(90f, 160f);
        main.startSize = new ParticleSystem.MinMaxCurve(3f, 9f);
        main.startColor = new Color(0.75f, 0.9f, 1f, 0.55f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 900;

        // emission behavior
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(65f);
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0f, (short)100),
            new ParticleSystem.Burst(0.35f, (short)70),
            new ParticleSystem.Burst(1.1f, (short)45)
        });

        // emitter shape
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 7f;
        shape.radius = 1.4f;

        // particle lifetime behavior
        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(
            MakeGradient(
                new Color(0.7f, 0.9f, 1f),
                Color.white,
                0.55f,
                0.22f
            )
        );

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(
            1f,
            AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1.8f)
        );

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 4f;
        noise.frequency = 0.35f;
        noise.scrollSpeed = 0.4f;

        ConfigureRenderer(ps);
    }

    private void CreateMistCloud()
    {
        // start the mist a little above the vent so it blooms around the upper plume
        ParticleSystem ps = CreateChildSystem("JOVE_Geyser_MistCloud", new Vector3(0f, 0f, 25f));

        var main = ps.main;
        main.loop = false;
        main.duration = 8f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 14f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(12f, 42f);
        main.startSize = new ParticleSystem.MinMaxCurve(20f, 65f);
        main.startColor = new Color(0.85f, 0.95f, 1f, 0.22f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 700;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(35f);
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0.6f, (short)35),
            new ParticleSystem.Burst(2.0f, (short)45)
        });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 32f;
        shape.radius = 7f;

        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(
            MakeGradient(
                new Color(0.75f, 0.9f, 1f),
                Color.white,
                0.22f,
                0.09f
            )
        );

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(
            1f,
            AnimationCurve.EaseInOut(0f, 0.45f, 1f, 2.5f)
        );

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 15f;
        noise.frequency = 0.22f;
        noise.scrollSpeed = 0.15f;

        ConfigureRenderer(ps);
    }

    private void CreateBasePuff()
    {
        ParticleSystem ps = CreateChildSystem("JOVE_Geyser_BasePuff", Vector3.zero);

        var main = ps.main;
        main.loop = false;
        main.duration = 1.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 28f);
        main.startSize = new ParticleSystem.MinMaxCurve(10f, 34f);
        main.startColor = new Color(0.9f, 0.97f, 1f, 0.32f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 250;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0f, (short)90),
            new ParticleSystem.Burst(0.45f, (short)45)
        });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 75f;
        shape.radius = 5f;

        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(
            MakeGradient(
                new Color(0.8f, 0.92f, 1f),
                Color.white,
                0.28f,
                0.08f
            )
        );

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(
            1f,
            AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.8f)
        );

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 8f;
        noise.frequency = 0.5f;

        ConfigureRenderer(ps);
    }

    private void CreateIceFlecks()
    {
        ParticleSystem ps = CreateChildSystem("JOVE_Geyser_IceFlecks", Vector3.zero);

        var main = ps.main;
        main.loop = false;
        main.duration = 3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(35f, 95f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.8f);
        main.startColor = new Color(0.9f, 0.97f, 1f, 0.85f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 180;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(8f);
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0f, (short)45),
            new ParticleSystem.Burst(1.0f, (short)25)
        });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 2f;

        var color = ps.colorOverLifetime;
        color.enabled = true;

        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.65f, 0.85f, 1f), 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.9f, 0.05f),
                new GradientAlphaKey(0.65f, 0.65f),
                new GradientAlphaKey(0f, 1f)
            }
        );

        color.color = new ParticleSystem.MinMaxGradient(grad);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(
            1f,
            AnimationCurve.EaseInOut(0f, 1.2f, 1f, 0.2f)
        );

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 3f;
        noise.frequency = 1.2f;

        ConfigureRenderer(ps);
    }

    void Update()
    {
        _age += Time.deltaTime;

        if (_age >= _lifetime)
        {
            Destroy(gameObject);
            // Destroy(markerObject);
        }
    }
}