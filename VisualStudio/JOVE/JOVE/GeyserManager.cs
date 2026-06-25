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
    private const float MAX_INTERVAL = 24f;

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
        Vector3d vesselPos = FlightGlobals.ActiveVessel.CoMD;

        // range in meters (min-max) from craft in which geyser can spawn
        float distance = UnityEngine.Random.Range(20f, 100f);
        float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;

        // make actual worldspace position to spawn geyser at
        double vesselLat = FlightGlobals.ActiveVessel.latitude;
        double vesselLon = FlightGlobals.ActiveVessel.longitude;

        double latOffset = (distance * Mathf.Cos(angle)) / _targetBody.Radius * (180.0 / Math.PI);
        double lonOffset = (distance * Mathf.Sin(angle)) / _targetBody.Radius * (180.0 / Math.PI);

        double spawnLat = vesselLat + latOffset;
        double spawnLon = vesselLon + lonOffset;
        double surfaceAltitude = 0;

        if (_targetBody.pqsController != null)
        {
            Vector3d nVector = _targetBody.GetRelSurfaceNVector(spawnLat, spawnLon);
            surfaceAltitude = _targetBody.pqsController.GetSurfaceHeight(nVector) - _targetBody.Radius;

            if (double.IsNaN(surfaceAltitude) || surfaceAltitude < 0)
                surfaceAltitude = 0.0;
        }

        // worldspace position and turn it upwards
        Vector3d surfacePoint = _targetBody.GetWorldSurfacePosition(spawnLat, spawnLon, surfaceAltitude);
        Vector3d surfaceNormal = (_targetBody.position - surfacePoint).normalized * -1;

        GameObject GeyserObj = new GameObject("JOVE_Geyser");
        GeyserObj.transform.position = surfacePoint;

        // oh my god quaternions
        GeyserObj.transform.rotation = Quaternion.FromToRotation(Vector3.forward, (Vector3)surfaceNormal);

        GeyserEffect effect = GeyserObj.AddComponent<GeyserEffect>();
        effect.Init(surfaceNormal);

        Debug.Log($"[JOVE.Geysers] Spawned Geyser at {spawnLat:F2}, {spawnLon:F2}");
    }
}

public class GeyserEffect : MonoBehaviour
{
    private ParticleSystem _ps;
    private float _lifetime = 12f;  // how long geyser exists
    private float _age = 0f;

    public void Init(Vector3d surfaceNormal)
    {
        _ps = gameObject.AddComponent<ParticleSystem>();
        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);    // stop emitter before emitting otherwise it wont work

        // particle behavior
        var main = _ps.main;
        main.loop = false;
        main.duration = 4f;           // how long to emit
        main.startLifetime = 8f;      // lifetime after emission
        main.startSpeed = 80f;        // start velocity m/s
        main.startSize = new ParticleSystem.MinMaxCurve(5f, 20f);  // random size
        main.startColor = new Color(0.85f, 0.9f, 1.0f, 0.6f);
        main.gravityModifier = -0.05f; // negative for antigravity
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // emission
        var emission = _ps.emission;
        emission.enabled = true;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(30f);

        // emitter shape
        var shape = _ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;  // cone angle
        shape.radius = 3f;

        // particle movement
        var vel = _ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);

        // particle visual
        var col = _ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),       // start transparnt
                new GradientAlphaKey(0.7f, 0.1f),   // fade in fast
                new GradientAlphaKey(0.5f, 0.7f),
                new GradientAlphaKey(0f, 1f)        // fade out
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // make particles big
        var size = _ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(
            1f,
            AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f)
        );

        // erupt!
        _ps.Play();
    }

    void Update()
    {
        _age += Time.deltaTime;
        if (_age >= _lifetime)
        {
            Destroy(gameObject);
            // Destroy(marker, 10f);
        }
    }
}