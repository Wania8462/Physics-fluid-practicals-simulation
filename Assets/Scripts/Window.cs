using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum FieldType
{
    Numeric,
    Dropdown
}

public class Field
{
    public string name;
    public FieldType fieldType;
    public Type dataType;

    public Field(string name, FieldType fieldType, Type dataType)
    {
        this.name = name;
        this.fieldType = fieldType;
        this.dataType = dataType;
    }

    public int GetHeight()
    {
        return fieldType switch
        {
            FieldType.Numeric => 300,
            FieldType.Dropdown => 200,
            _ => throw new Exception($"There is no height for {fieldType}"),
        };

    }
}

public class Window : MonoBehaviour
{
    [SerializeField] private RectTransform background;

    public List<Field> fields = new();

    [SerializeField] private bool topBarHeld;
    private Vector3 mouseOffset;

    private void Update()
    {
        if (Input.GetMouseButtonUp(0))
            topBarHeld = false;

        if (topBarHeld)
            OnTopBarHold();
    } 

    public void UpdateFields()
    {
        int totalHeight = 0;

        foreach (Field field in fields)
        {
            totalHeight += field.GetHeight();
        }

        background.pivot = new Vector2(background.pivot.x, 1f);
        background.anchorMin = new Vector2(background.anchorMin.x, 1f);
        background.anchorMax = new Vector2(background.anchorMax.x, 1f);
        background.anchoredPosition = new Vector2(background.anchoredPosition.x, 0f);
        background.sizeDelta = new Vector2(background.sizeDelta.x, totalHeight);
    }

    public void OnTopBarClick()
    {
        topBarHeld = true;
        Vector3 mousePos = Input.mousePosition;
        mouseOffset = transform.position - mousePos;
        Debug.Log("CLICK!");
    }
    
    private void OnTopBarHold()
    {
        transform.position = Input.mousePosition + mouseOffset;
    }
}