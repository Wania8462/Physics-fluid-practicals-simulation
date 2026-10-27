using System;
using System.Linq;
using UnityEngine;

public class WindowManager : MonoBehaviour
{
    // The New object window's density options, in kg/m³
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
    [SerializeField] private GameObject windowPrefab;
    [SerializeField] private SimulationAPI api;

    public void OnSideMenuClick()
    {
        sideMenu.SetActive(!sideMenu.activeSelf);
    }

    // Spawns a body and ties the window to it. The material fixes the density, so a new mass changes the volume and
    // anything else changes the mass
    public void CreateObjectWindow()
    {
        const int Mass = 0, Volume = 1, Density = 2, Shape = 3;
        const int Wood = 1;
        const float Litres = 2;

        Field[] fields =
        {
            new() { name = "Mass", fieldType = FieldType.Numeric, value = Materials[Wood].density * Litres / 1000, min = 0.1f, max = 80, unit = "kg" },
            new() { name = "Volume", fieldType = FieldType.Numeric, value = Litres, min = 0.5f, max = 10, unit = "L" },
            new() { name = "Density", fieldType = FieldType.Dropdown, options = Materials.Select(m => $"{m.name} ({m.density} kg/m³)").ToArray(), selected = Wood },
            new() { name = "Shape", fieldType = FieldType.Dropdown, options = Enum.GetNames(typeof(BodyShape3D)) }
        };

        // Before the window, so a full simulation leaves no window without a body
        int id = api.AddBody((BodyShape3D)fields[Shape].selected, fields[Volume].value / 1000, fields[Mass].value);
        Window window = CreateWindow("New object", fields);

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

        window.Closed += () => api.RemoveBody(id);
    }

    public void CreateFluidWindow()
    {
        Window window = CreateWindow("Fluid properties",
            new Field { name = "Density", fieldType = FieldType.Numeric, value = api.GetDensity(), min = 500, max = 1500, unit = "kg/m³" },
            new Field { name = "Viscosity", fieldType = FieldType.Numeric, value = api.GetViscosity(), min = 0, max = 5, unit = "mPa·s" });

        window.FieldChanged += _ =>
        {
            api.SetDensity(window.fields[0].value);
            api.SetViscosity(window.fields[1].value);
        };
    }

    // The simulation's gravity points down as a negative number, the window shows its strength
    public void CreateGravityWindow()
    {
        Window window = CreateWindow("Gravity properties",
            new Field { name = "Gravity", fieldType = FieldType.Numeric, value = -api.GetGravity(), min = 0, max = 25, unit = "m/s²" });

        window.FieldChanged += _ => api.SetGravity(-window.fields[0].value);
    }

    // Nothing draws the forces yet, so the toggles only keep the resultant on its own: it is the sum of the others
    public void CreateVisualisationWindow()
    {
        string[] forces = { "Gravity", "Contact", "Buoyancy", "Resultant" };
        int resultant = forces.Length - 1;

        Window window = CreateWindow("Visualisation",
            forces.Select(force => new Field { name = force, fieldType = FieldType.Toggle }).ToArray());

        void TurnOff(int index)
        {
            Field field = window.fields[index];
            field.isOn = false;
            window.SetField(index, field);
        }

        window.FieldChanged += index =>
        {
            if (!window.fields[index].isOn) return;

            if (index == resultant)
                for (int i = 0; i < resultant; i++)
                    TurnOff(i);
            else
                TurnOff(resultant);
        };
    }

    private Window CreateWindow(string title, params Field[] fields)
    {
        Window window = Instantiate(windowPrefab, new(1000, 500, 0), Quaternion.identity, canvas).GetComponent<Window>();
        window.Title = title;
        window.fields.AddRange(fields);
        window.UpdateFields();
        return window;
    }
}
