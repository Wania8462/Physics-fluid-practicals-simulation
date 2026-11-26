using System.Collections;
using System.Globalization;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>A number box in a window: what typing into it does, with the gravity window's 0 to 25 limits</summary>
public class FieldUITests
{
    private const float Min = 0, Max = 25, Start = 9.81f;

    private GameObject canvas;
    private FieldUI fieldUI;
    private TMP_InputField input;
    private Slider slider;
    /// <summary>The value Changed last reported, and how many times it was raised</summary>
    private Field reported;
    private int changes;

    /// <summary>Typed numbers are read in the machine's culture, so the tests pin one that uses a decimal point</summary>
    private CultureInfo culture;

    [UnitySetUp]
    public IEnumerator CreateField()
    {
        culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");

        canvas = new GameObject("Test canvas", typeof(Canvas));
#if UNITY_EDITOR
        FieldUI prefab = AssetDatabase.LoadAssetAtPath<FieldUI>("Assets/Prefabs/Field.prefab");
#else
        FieldUI prefab = null;
#endif
        Assume.That(prefab, Is.Not.Null, "The Field prefab is only loaded in the editor");

        fieldUI = Object.Instantiate(prefab, canvas.transform);
        fieldUI.Bind(new Field { name = "Gravity", fieldType = FieldType.Numeric, value = Start, min = Min, max = Max, unit = "m/s²" }, 0);
        fieldUI.Changed += (_, field) =>
        {
            reported = field;
            changes++;
        };

        input = fieldUI.GetComponentInChildren<TMP_InputField>(true);
        slider = fieldUI.GetComponentInChildren<Slider>(true);
        changes = 0;
        yield return null;
    }

    [TearDown]
    public void DestroyField()
    {
        Object.Destroy(canvas);
        CultureInfo.CurrentCulture = culture;
    }

    private void Enter(string text)
    {
        input.text = text;
        input.onEndEdit.Invoke(text);
    }

    #region Normal
    /// <summary>A number inside the limits is taken as it is</summary>
    [TestCase("12", 12)]
    [TestCase("3.5", 3.5f)]
    [TestCase(" 7 ", 7)]
    public void Number_Accepted(string text, float expected)
    {
        Enter(text);
        Assert.AreEqual(1, changes);
        Assert.AreEqual(expected, reported.value, 1e-5f);
        Assert.IsNull(fieldUI.ErrorMessage);
    }

    /// <summary>The box shows the value rounded to 2 places</summary>
    [Test]
    public void Number_ShownRounded()
    {
        Enter("3.14159");
        Assert.AreEqual(3.14159f.ToString("0.##"), input.text);
        Assert.AreEqual(3.14159f, slider.value, 1e-5f);
    }
    #endregion

    #region Boundary
    /// <summary>The limits themselves are allowed, with no message</summary>
    [TestCase("0", Min)]
    [TestCase("25", Max)]
    [TestCase("24.999", 24.999f)]
    [TestCase("0.001", 0.001f)]
    public void Limits_Accepted(string text, float expected)
    {
        Enter(text);
        Assert.AreEqual(expected, reported.value, 1e-5f);
        Assert.IsNull(fieldUI.ErrorMessage);
    }

    /// <summary>Just past a limit is pulled back to it, with a message saying which way</summary>
    [TestCase("25.001", Max, FieldUI.TooBigMessage)]
    [TestCase("26", Max, FieldUI.TooBigMessage)]
    [TestCase("1e9", Max, FieldUI.TooBigMessage)]
    [TestCase("Infinity", Max, FieldUI.TooBigMessage)]
    [TestCase("-0.001", Min, FieldUI.TooSmallMessage)]
    [TestCase("-1", Min, FieldUI.TooSmallMessage)]
    [TestCase("-Infinity", Min, FieldUI.TooSmallMessage)]
    public void PastLimit_ClampedWithMessage(string text, float expected, string message)
    {
        Enter(text);
        Assert.AreEqual(expected, reported.value);
        Assert.AreEqual(expected.ToString("0.##"), input.text);
        Assert.AreEqual(message, fieldUI.ErrorMessage);
    }

    /// <summary>Dragging the slider can't go past the limits, and gives no message</summary>
    [Test]
    public void Slider_ClampedWithoutMessage()
    {
        slider.value = 100;
        Assert.AreEqual(Max, reported.value);
        slider.value = -100;
        Assert.AreEqual(Min, reported.value);
        Assert.IsNull(fieldUI.ErrorMessage);
    }
    #endregion

    #region Erroneous and null
    /// <summary>Anything that isn't a number is rejected: the old value comes back with a message, and nothing changes</summary>
    [TestCase("abc")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("5 kg")]
    [TestCase("1.2.3")]
    [TestCase("--5")]
    [TestCase("ten")]
    [TestCase(null)]
    public void NotANumber_Rejected(string text)
    {
        Enter(text);
        Assert.AreEqual(0, changes);
        Assert.AreEqual(Start.ToString("0.##"), input.text);
        Assert.AreEqual(FieldUI.NotANumberMessage, fieldUI.ErrorMessage);
    }

    /// <summary>"NaN" parses as a float but isn't a number the simulation can use</summary>
    [Test, Category("KnownBug")]
    public void NaN_Rejected()
    {
        Enter("NaN");
        Assert.AreEqual(0, changes, "NaN was passed on to the simulation");
        Assert.AreEqual(FieldUI.NotANumberMessage, fieldUI.ErrorMessage);
    }
    #endregion

    #region The message
    /// <summary>The message is shown in red in place of the value</summary>
    [Test]
    public void Message_ShownInRed()
    {
        Enter("abc");

        TMP_Text message = null;
        foreach (TMP_Text text in input.GetComponentsInChildren<TMP_Text>())
        {
            if (text.text == FieldUI.NotANumberMessage)
                message = text;
        }

        Assert.IsNotNull(message, "No visible message");
        Assert.AreEqual(UITheme.Colors.error, message.color);
        Assert.IsFalse(input.textComponent.enabled, "The value shows under the message");
    }

    /// <summary>The message goes after a few seconds and the value shows again</summary>
    [UnityTest]
    public IEnumerator Message_GoesAfterAFewSeconds()
    {
        Enter("100");
        Assert.IsNotNull(fieldUI.ErrorMessage);

        yield return new WaitForSecondsRealtime(FieldUI.ErrorSeconds + 0.2f);
        Assert.IsNull(fieldUI.ErrorMessage);
        Assert.IsTrue(input.textComponent.enabled);
    }

    /// <summary>Clicking back into the box clears the message straight away</summary>
    [Test]
    public void Message_ClearedOnSelect()
    {
        Enter("abc");
        input.onSelect.Invoke(input.text);
        Assert.IsNull(fieldUI.ErrorMessage);
        Assert.IsTrue(input.textComponent.enabled);
    }

    /// <summary>A new mistake replaces the old message and restarts its time</summary>
    [UnityTest]
    public IEnumerator Message_ReplacedByNewOne()
    {
        Enter("abc");
        yield return new WaitForSecondsRealtime(FieldUI.ErrorSeconds * 0.75f);
        Enter("-3");
        Assert.AreEqual(FieldUI.TooSmallMessage, fieldUI.ErrorMessage);

        yield return new WaitForSecondsRealtime(FieldUI.ErrorSeconds * 0.5f);
        Assert.AreEqual(FieldUI.TooSmallMessage, fieldUI.ErrorMessage, "The first message's timer hid the second one early");
    }

    /// <summary>A correct entry after a mistake clears the message</summary>
    [Test]
    public void Message_ClearedByCorrectEntryAfterSelect()
    {
        Enter("abc");
        input.onSelect.Invoke(input.text);
        Enter("5");
        Assert.IsNull(fieldUI.ErrorMessage);
        Assert.AreEqual(5, reported.value);
    }

    /// <summary>Hiding the field while a message shows doesn't leave it stuck</summary>
    [UnityTest]
    public IEnumerator Message_ClearedWhenHidden()
    {
        Enter("abc");
        fieldUI.gameObject.SetActive(false);
        yield return null;
        fieldUI.gameObject.SetActive(true);
        Assert.IsNull(fieldUI.ErrorMessage);
    }
    #endregion
}
