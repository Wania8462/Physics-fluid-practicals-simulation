using System;
using UnityEngine;

public class WindowManager : MonoBehaviour
{
    [SerializeField] private Transform canvas;
    [SerializeField] private GameObject sideMenu;
    [SerializeField] private GameObject windowPrefab;

    public void OnSideMenuClick()
    {
        sideMenu.SetActive(!sideMenu.activeSelf);
    }

    public void CreateWindow()
    {
        // Temporary
        Window window = Instantiate(windowPrefab, new(1000, 500, 0), Quaternion.identity, canvas).GetComponent<Window>();

        window.fields.Add(new("Mass",
            FieldType.Numeric,
            typeof(float)));
        window.fields.Add(new("Volume",
            FieldType.Dropdown,
            typeof(float)));

        window.UpdateFields();
    }
}