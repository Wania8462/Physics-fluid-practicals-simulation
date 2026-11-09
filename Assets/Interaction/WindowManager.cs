using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Opens the simulation's windows and lets the user drag scales and objects around</summary>
public class WindowManager : MonoBehaviour
{
    /// <summary>Field indices of a scale window</summary>
    private const int ScaleWeight = 0, ScaleShownInWorld = 1;

    /// <summary>Field indices of an object window</summary>
    private const int ObjectVolume = 1, ObjectSubmerged = 5, ObjectDisplaced = 6;

    /// <summary>Field indices of a depth meter window</summary>
    private const int DepthReading = 0, DepthGraph = 1, DepthSecondRay = 2;

    /// <summary>Field indices of a stopwatch window, and the buttons in its row</summary>
    private const int StopwatchTime = 0, StopwatchButtons = 1;
    private const int StartStopButton = 0;

    /// <summary>One open stopwatch</summary>
    private class Stopwatch
    {
        public Window window;
        public bool running;
        /// <summary>Simulation time when it last started</summary>
        public float startedAt;
        /// <summary>Time counted before it last started</summary>
        public float counted;
    }

    /// <summary>How far a new second ray starts from the first, in sim units</summary>
    private const float SecondRaySpacing = 40;

    /// <summary>New object densities, in kg/m³</summary>
    private static readonly (string name, float density)[] Materials =
    {
        ("Cork", 240),
        ("Wood", 600),
        ("Ice", 917),
        ("Aluminium", 2700),
        ("Steel", 7850)
    };

    [SerializeField] private Transform canvas;
    [SerializeField] private GameObject sideMenu;
    [SerializeField] private GameObject settingsCard;
    [SerializeField] private GameObject windowPrefab;
    [SerializeField] private SimulationAPI api;
    [SerializeField] private RenderAPI renderApi;
    [Tooltip("How far one scroll wheel notch moves a dragged scale or object, in sim units")]
    [SerializeField] private float dragScrollStep = 10f;
    [Tooltip("Width and height of the invisible box for grabbing a depth meter's ray, in sim units. Much thicker than the ray so it's easy to hit")]
    [SerializeField] private float depthRayGrabSize = 16f;
    [Tooltip("Seconds of simulation time the depth meter graphs show")]
    [SerializeField, Range(5, DepthMeters.MaxHistory)] private float depthGraphSeconds = 30f;

    /// <summary>Each scale's window, null for a scale that isn't out</summary>
    private readonly Window[] scaleWindows = new Window[RigidBodies3D.MaxScales];

    /// <summary>Each open object window and the id of its body</summary>
    private readonly List<(Window window, int body)> objectWindows = new();

    /// <summary>The wave generator's window, null when it's closed</summary>
    private Window waveWindow;

    /// <summary>Each depth meter's window, null for one that isn't out. Window w owns rays 2w and 2w + 1</summary>
    private readonly Window[] depthWindows = new Window[DepthMeters.MaxMeters / 2];
    /// <summary>The depth readings last shown in the windows</summary>
    private int shownDepthRevision = -1;

    private readonly List<Stopwatch> stopwatches = new();

    /// <summary>Id of the body being dragged, -1 when there's none</summary>
    private int draggedBody = -1;
    /// <summary>The depth meter being dragged, -1 when there's none</summary>
    private int draggedMeter = -1;
    private Plane dragPlane;
    private Vector3 dragOffset;

    private void Update()
    {
        UpdateScales();
        UpdateSubmerged();
        UpdateDepthMeters();
        UpdateStopwatches();
        DragBody();
        BringClickedWindowToFront();
    }

    /// <summary>A clicked window comes to the front</summary>
    private void BringClickedWindowToFront()
    {
        if (!Input.GetMouseButtonDown(0))
            return;

        Window window = Window.TopmostAt(canvas, Input.mousePosition);
        if (window)
            window.transform.SetAsLastSibling();
    }

    /// <summary>Opens or closes the side menu</summary>
    public void OnSideMenuClick()
    {
        sideMenu.SetActive(!sideMenu.activeSelf);
    }

    /// <summary>Swaps the scene for the ripple tank view, or back</summary>
    public void ToggleWaveProjection()
    {
        renderApi.SetWaveProjection(!renderApi.IsWaveProjectionOn());
    }

    /// <summary>Opens or closes the settings card, pausing the simulation while it's open</summary>
    public void OnSettingsClick()
    {
        settingsCard.SetActive(!settingsCard.activeSelf);
        api.SetPaused(settingsCard.activeSelf);
    }

    /// <summary>Spawns a body with a window to edit its mass, volume, material and shape</summary>
    public void CreateObjectWindow()
    {
        const int Mass = 0, Volume = ObjectVolume, Density = 2, Shape = 3;
        const int Wood = 1;
        const float Litres = 2;

        Field[] fields =
        {
            new() { name = "Mass", fieldType = FieldType.Numeric, value = Materials[Wood].density * Litres / 1000, min = 0.1f, max = 80, unit = "kg" },
            new() { name = "Volume", fieldType = FieldType.Numeric, value = Litres, min = 0.5f, max = 10, unit = "L" },
            new() { name = "Density", fieldType = FieldType.Dropdown, options = Materials.Select(m => $"{m.name} ({m.density} kg/m³)").ToArray(), selected = Wood },
            new() { name = "Shape", fieldType = FieldType.Dropdown, options = Enum.GetNames(typeof(BodyShape3D)).Where(name => name != nameof(BodyShape3D.Scale)).ToArray() },
            new() { name = "Measurements", fieldType = FieldType.Foldout, foldedFields = 2 },
            new() { name = "Submerged percent", fieldType = FieldType.Readout, unit = "%" },
            new() { name = "Displaced", fieldType = FieldType.Readout, unit = "L" }
        };

        int id = api.AddBody((BodyShape3D)fields[Shape].selected, fields[Volume].value / 1000, fields[Mass].value);
        Window window = CreateWindow("New object", fields);
        window.Renamable = true;
        window.Groupable = true;

        window.FieldChanged += index =>
        {
            Field mass = window.fields[Mass];
            Field volume = window.fields[Volume];
            float density = Materials[window.fields[Density].selected].density;
            float oldVolume = volume.value;

            if (index == Mass)
                volume.value = Mathf.Clamp(mass.value / density * 1000, volume.min, volume.max);
            mass.value = density * volume.value / 1000;

            window.SetField(Mass, mass);
            window.SetField(Volume, volume);

            api.SetBodyMass(id, mass.value);
            if (index == Volume || index == Shape || volume.value != oldVolume)
                api.SetBodyShape(id, (BodyShape3D)window.fields[Shape].selected, volume.value / 1000);
        };

        objectWindows.Add((window, id));

        window.Closed += () =>
        {
            api.RemoveBody(id);
            objectWindows.Remove((window, id));
        };
    }

    /// <summary>Shows how submerged each object is and how much fluid it displaces. A hidden reading isn't calculated</summary>
    private void UpdateSubmerged()
    {
        foreach ((Window window, int body) in objectWindows)
        {
            if (!window.IsFieldVisible(ObjectSubmerged))
                continue;

            Field submerged = window.fields[ObjectSubmerged];
            submerged.value = api.GetSubmergedPercent(body);
            window.SetField(ObjectSubmerged, submerged);

            Field displaced = window.fields[ObjectDisplaced];
            displaced.value = submerged.value / 100 * window.fields[ObjectVolume].value;
            window.SetField(ObjectDisplaced, displaced);
        }
    }

    /// <summary>Window to edit the fluid's density and viscosity</summary>
    public void CreateFluidWindow()
    {
        Window window = CreateWindow("Fluid properties",
            new Field { 
                name = "Density", 
                fieldType = FieldType.Numeric, 
                value = api.GetDensity(), 
                min = 500, 
                max = 1500, 
                unit = "kg/m³" 
            },
            new Field { 
                name = "Viscosity", 
                fieldType = FieldType.Numeric, 
                value = api.GetViscosity(), 
                min = 0, 
                max = 5, 
                unit = "mPa·s" 
            });

        window.FieldChanged += _ =>
        {
            api.SetDensity(window.fields[0].value);
            api.SetViscosity(window.fields[1].value);
        };
    }

    /// <summary>Window to edit gravity</summary>
    public void CreateGravityWindow()
    {
        Window window = CreateWindow("Gravity properties",
            new Field { 
                name = "Gravity", 
                fieldType = FieldType.Numeric, 
                value = api.GetGravity(),
                min = 0, 
                max = 25, 
                unit = "m/s²" 
            });

        window.FieldChanged += _ => api.SetGravity(window.fields[0].value);
    }

    /// <summary>Toggles for which force arrows and values are drawn, and the depth lines</summary>
    public void CreateVisualisationWindow()
    {
        const int Resultant = (int)BodyForce.Resultant;
        BodyForce[] forces = (BodyForce[])Enum.GetValues(typeof(BodyForce));
        int values = forces.Length;
        int depthLines = values + 2;  // After the separator

        renderApi.SetNewtonScale(api.ForceScale);

        Window window = CreateWindow("Visualisation", forces
            .Select(force => new Field { name = force.ToString(), fieldType = FieldType.Toggle, isOn = renderApi.IsForceDrawn(force) })
            .Append(new Field { name = "Values", fieldType = FieldType.Toggle, isOn = renderApi.AreForceValuesDrawn() })
            .Append(new Field { fieldType = FieldType.Separator })
            .Append(new Field { name = "Depth lines", fieldType = FieldType.Toggle, isOn = renderApi.AreDepthLinesDrawn() })
            .ToArray());

        window.FieldChanged += index =>
        {
            if (index == values)
            {
                renderApi.DrawForceValues(window.fields[values].isOn);
                return;
            }

            if (index == depthLines)
            {
                renderApi.DrawDepthLines(window.fields[depthLines].isOn);
                return;
            }

            bool turnedOn = window.fields[index].isOn;

            if (turnedOn && index == Resultant)
            {
                for (int i = 0; i < Resultant; i++)
                    window.SetToggle(i, false);
            }
            else if (turnedOn)
            {
                window.SetToggle(Resultant, false);
            }

            foreach (BodyForce force in forces)
                renderApi.DrawForce(force, window.fields[(int)force].isOn);
        };
    }

    /// <summary>Puts the dipper in the tank, with a window to switch it on and set its frequency and amplitude</summary>
    public void CreateWaveWindow()
    {
        if (waveWindow)
            return;

        const int On = 0, Frequency = 1, Amplitude = 2;

        api.AddDipper();
        Window window = CreateWindow("Wave generator",
            new Field { name = "On", fieldType = FieldType.Toggle, isOn = false },
            new Field { name = "Frequency", fieldType = FieldType.Numeric, value = 0.15f, min = 0.05f, max = 0.25f, unit = "Hz" },
            new Field { name = "Amplitude", fieldType = FieldType.Numeric, value = 3, min = 0, max = 6, unit = "cm" });
        waveWindow = window;

        void Apply() => api.SetDipper(window.fields[On].isOn, window.fields[Frequency].value, window.fields[Amplitude].value / 100);

        Apply();
        window.FieldChanged += _ => Apply();

        window.Closed += () =>
        {
            api.RemoveDipper();
            waveWindow = null;
        };
    }

    /// <summary>Adds a depth meter's ray to the tank, with a window showing the fluid's height there over time</summary>
    public void CreateDepthMeterWindow()
    {
        int meter = Array.FindIndex(depthWindows, open => !open);
        if (meter < 0)
            return;

        int firstRay = 2 * meter, secondRay = firstRay + 1;

        api.AddDepthMeter(firstRay);
        Window window = CreateWindow($"Depth meter {meter + 1}",
            new Field { name = "Height", fieldType = FieldType.Readout, unit = "cm" },
            new Field { name = "Height over time", fieldType = FieldType.Graph, unit = "cm", graphSeconds = depthGraphSeconds, graphMinRange = 2 },
            new Field { name = "Second ray", fieldType = FieldType.Toggle, isOn = false });
        depthWindows[meter] = window;

        ShowDepthReading(meter);
        window.FieldChanged += _ => SetSecondRay(meter, window.fields[DepthSecondRay].isOn);

        window.Closed += () =>
        {
            api.RemoveDepthMeter(firstRay);
            api.RemoveDepthMeter(secondRay);
            renderApi.HideDepthRay(firstRay);
            renderApi.HideDepthRay(secondRay);
            depthWindows[meter] = null;
        };
    }

    /// <summary>Adds a depth meter's second ray beside the first, towards the middle of the tank so it starts inside, or removes it</summary>
    private void SetSecondRay(int meter, bool on)
    {
        int secondRay = 2 * meter + 1;

        if (on)
        {
            Vector3 first = api.GetDepthMeterPosition(2 * meter);
            api.AddDepthMeter(secondRay);
            api.MoveDepthMeter(secondRay, first.x - Mathf.Sign(first.x) * SecondRaySpacing, first.z);
        }
        else
        {
            api.RemoveDepthMeter(secondRay);
            renderApi.HideDepthRay(secondRay);
        }

        ShowDepthReading(meter);
    }

    /// <summary>Keeps each depth meter's ray and window up to date</summary>
    private void UpdateDepthMeters()
    {
        bool newReadings = false;

        for (int meter = 0; meter < depthWindows.Length; meter++)
        {
            if (!depthWindows[meter])
                continue;

            for (int ray = 2 * meter; ray <= 2 * meter + 1; ray++)
            {
                if (api.HasDepthMeter(ray))
                    renderApi.ShowDepthRay(ray, api.GetDepthMeterPosition(ray), api.GetDepthRayLength());
            }

            if (api.DepthMeterRevision != shownDepthRevision)
            {
                ShowDepthReading(meter);
                newReadings = true;
            }
        }

        if (newReadings)
            shownDepthRevision = api.DepthMeterRevision;
    }

    /// <summary>Writes a depth meter's reading and history to its window. A hidden reading isn't calculated</summary>
    private void ShowDepthReading(int meter)
    {
        const float CentimetresPerMetre = 100;
        Window window = depthWindows[meter];

        if (window.IsFieldVisible(DepthReading))
        {
            Field reading = window.fields[DepthReading];
            reading.value = api.GetDepthMeterReading(2 * meter) * CentimetresPerMetre;
            window.SetField(DepthReading, reading);
        }

        if (window.IsFieldVisible(DepthGraph))
        {
            Field graph = window.fields[DepthGraph];
            graph.graphSeconds = depthGraphSeconds;

            Vector2[] Line(int ray)
            {
                Vector2[] points = api.GetDepthMeterHistory(ray, depthGraphSeconds);
                for (int i = 0; i < points.Length; i++)
                    points[i].y *= CentimetresPerMetre;
                return points;
            }

            int secondRay = 2 * meter + 1;
            graph.graphLines = api.HasDepthMeter(secondRay) ? new[] { Line(2 * meter), Line(secondRay) } : new[] { Line(2 * meter) };
            window.SetField(DepthGraph, graph);
        }
    }

    /// <summary>Window with a stopwatch that counts simulation time</summary>
    public void CreateStopwatchWindow()
    {
        Stopwatch stopwatch = new()
        {
            window = CreateWindow("Stopwatch",
                new Field { name = "Time", fieldType = FieldType.Readout, unit = "s", format = "0.00" },
                new Field { fieldType = FieldType.Buttons, options = new[] { "Start", "Reset" } })
        };
        stopwatches.Add(stopwatch);

        stopwatch.window.FieldChanged += _ => OnStopwatchButton(stopwatch);
        stopwatch.window.Closed += () => stopwatches.Remove(stopwatch);
    }

    /// <summary>Start/Stop starts or holds the count. Reset stops it and clears it</summary>
    private void OnStopwatchButton(Stopwatch stopwatch)
    {
        Field buttons = stopwatch.window.fields[StopwatchButtons];
        float now = api.GetSimulationTime();

        if (buttons.selected == StartStopButton)
        {
            if (stopwatch.running)
                stopwatch.counted += now - stopwatch.startedAt;
            else
                stopwatch.startedAt = now;

            stopwatch.running = !stopwatch.running;
        }
        else
        {
            stopwatch.running = false;
            stopwatch.counted = 0;
        }

        buttons.options = new[] { stopwatch.running ? "Stop" : "Start", "Reset" };
        stopwatch.window.SetField(StopwatchButtons, buttons);
        ShowStopwatch(stopwatch);
    }

    private void UpdateStopwatches()
    {
        foreach (Stopwatch stopwatch in stopwatches)
        {
            if (stopwatch.running)
                ShowStopwatch(stopwatch);
        }
    }

    /// <summary>Writes the stopwatch's time to its window. A hidden reading isn't written</summary>
    private void ShowStopwatch(Stopwatch stopwatch)
    {
        if (!stopwatch.window.IsFieldVisible(StopwatchTime))
            return;

        Field time = stopwatch.window.fields[StopwatchTime];
        time.value = stopwatch.counted + (stopwatch.running ? api.GetSimulationTime() - stopwatch.startedAt : 0);
        stopwatch.window.SetField(StopwatchTime, time);
    }

    /// <summary>Spawns the next scale with a window showing its reading</summary>
    public void CreateScaleWindow()
    {
        int scale = Array.FindIndex(scaleWindows, window => !window);
        if (scale < 0)
            return;

        api.AddScale(scale);
        Window window = CreateWindow($"Scale {scale + 1}",
            new Field { name = "Weight", fieldType = FieldType.Readout, unit = "N" },
            new Field { name = "Show in world", fieldType = FieldType.Toggle, isOn = true });
        scaleWindows[scale] = window;
        renderApi.ShowScaleLabel(scale, true);

        window.FieldChanged += index =>
        {
            if (index == ScaleShownInWorld)
                renderApi.ShowScaleLabel(scale, window.fields[ScaleShownInWorld].isOn);
        };

        window.Closed += () =>
        {
            api.RemoveScale(scale);
            renderApi.SetScaleText(scale, null);
            scaleWindows[scale] = null;
        };
    }

    /// <summary>Shows each scale's reading in its window and on the scale. A hidden reading isn't calculated</summary>
    private void UpdateScales()
    {
        for (int scale = 0; scale < scaleWindows.Length; scale++)
        {
            Window window = scaleWindows[scale];
            if (!window || !window.IsFieldVisible(ScaleWeight))
                continue;

            Field weight = window.fields[ScaleWeight];
            weight.value = api.GetScaleReading(scale);
            window.SetField(ScaleWeight, weight);
            renderApi.SetScaleText(scale, $"{weight.value:0.0} N");
        }
    }

    /// <summary>Where the dragged depth meter or body is now</summary>
    private Vector3 DraggedPosition() => draggedMeter >= 0 ? api.GetDepthMeterPosition(draggedMeter) : api.GetBodyPosition(draggedBody);

    /// <summary>Right mouse drags a scale, object or depth meter around, the scroll wheel moves it nearer or further</summary>
    private void DragBody()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        // The bodies can't be seen in the ripple tank view
        if (Input.GetMouseButtonDown(1) && !renderApi.IsWaveProjectionOn() && !EventSystem.current.IsPointerOverGameObject())
        {
            float nearest = float.PositiveInfinity;
            IEnumerable<int> scales = Enumerable.Range(0, scaleWindows.Length).Where(scale => scaleWindows[scale]).Select(scale => api.GetScaleBodyId(scale));
            foreach (int body in scales.Concat(objectWindows.Select(open => open.body)))
            {
                if (api.GetBodyBounds(body).IntersectRay(ray, out float distance) && distance < nearest)
                {
                    nearest = distance;
                    draggedBody = body;
                }
            }

            for (int depthRay = 0; depthRay < DepthMeters.MaxMeters; depthRay++)
            {
                if (depthWindows[depthRay / 2] && api.HasDepthMeter(depthRay) && api.GetDepthMeterBounds(depthRay, depthRayGrabSize).IntersectRay(ray, out float distance) && distance < nearest)
                {
                    nearest = distance;
                    draggedBody = -1;
                    draggedMeter = depthRay;
                }
            }

            if (draggedBody >= 0 || draggedMeter >= 0)
            {
                Vector3 grabbed = ray.GetPoint(nearest);
                dragPlane = new Plane(-Camera.main.transform.forward, grabbed);
                dragOffset = DraggedPosition() - grabbed;
            }
        }

        if (draggedBody < 0 && draggedMeter < 0)
            return;

        // Closing the window removes it mid-drag
        bool removed = draggedMeter >= 0 ? !api.HasDepthMeter(draggedMeter) : !api.HasBody(draggedBody);
        if (!Input.GetMouseButton(1) || removed)
        {
            if (draggedBody >= 0)
                api.ReleaseBody(draggedBody);

            draggedBody = -1;
            draggedMeter = -1;
            return;
        }

        // Scrolling up moves it away. Measured from where it is, so scrolling into a wall doesn't build up
        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0)
            dragPlane = new Plane(dragPlane.normal, DraggedPosition() - dragOffset - scroll * dragScrollStep * dragPlane.normal);

        if (!dragPlane.Raycast(ray, out float enter))
            return;

        Vector3 target = ray.GetPoint(enter) + dragOffset;
        if (draggedMeter >= 0)
            api.MoveDepthMeter(draggedMeter, target.x, target.z);
        else
            api.MoveBody(draggedBody, target);
    }

    /// <summary>Opens a window with these fields</summary>
    private Window CreateWindow(string title, params Field[] fields)
    {
        Window window = Instantiate(windowPrefab, new(1000, 500, 0), Quaternion.identity, canvas).GetComponent<Window>();
        window.Title = title;
        window.fields.AddRange(fields);
        window.UpdateFields();
        window.DroppedOn += target => Combine(window, target);
        return window;
    }

    /// <summary>Dropping a window onto another groups them</summary>
    private void Combine(Window dragged, Window target)
    {
        if (!target.IsGroup)
        {
            Window group = CreateWindow("Objects");
            group.Groupable = true;
            group.transform.position = target.transform.position;
            group.Add(target);
            target = group;
        }

        target.Add(dragged);
    }
}
