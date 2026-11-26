using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Loading the practical scenes and finding their windows and buttons</summary>
internal static class PlayModeHelpers
{
    /// <summary>Most frames to wait for something before giving up</summary>
    private const int MaxFrames = 600;

    /// <summary>Loads a scene from the build settings and waits for the simulation to start</summary>
    public static IEnumerator LoadScene(string name)
    {
        AsyncOperation loading = SceneManager.LoadSceneAsync(name, LoadSceneMode.Single);
        while (!loading.isDone)
            yield return null;

        // Start runs on the first frame, so wait a few for the buffers and the UI to settle
        for (int i = 0; i < 10; i++)
            yield return null;
    }

    /// <summary>Waits until the condition holds, failing the test if it never does</summary>
    public static IEnumerator WaitUntil(Func<bool> condition, string failure)
    {
        for (int frame = 0; frame < MaxFrames; frame++)
        {
            if (condition())
                yield break;
            yield return null;
        }
        Assert.Fail(failure);
    }

    /// <summary>Waits a number of frames</summary>
    public static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    /// <summary>The single object of this type in the scene</summary>
    public static T Find<T>() where T : Object
    {
        T found = Object.FindFirstObjectByType<T>();
        Assert.IsNotNull(found, $"No {typeof(T).Name} in the scene");
        return found;
    }

    /// <summary>The open windows with this title, not counting ones that are closing</summary>
    public static Window[] Windows(string title)
    {
        return Object.FindObjectsByType<Window>(FindObjectsSortMode.InstanceID).Where(window => window && !window.IsClosing && window.Title == title).ToArray();
    }

    /// <summary>The open windows whose title starts with this</summary>
    public static Window[] WindowsStartingWith(string title)
    {
        return Object.FindObjectsByType<Window>(FindObjectsSortMode.InstanceID).Where(window => window && !window.IsClosing && window.Title.StartsWith(title)).ToArray();
    }

    /// <summary>Closes the windows and waits until they're gone</summary>
    public static IEnumerator Close(params Window[] windows)
    {
        foreach (Window window in windows)
            window.Close();
        yield return WaitUntil(() => windows.All(window => !window), "A window didn't close");
    }

    /// <summary>The side menu button that calls this WindowManager method when clicked</summary>
    public static Button ButtonCalling(string method)
    {
        Button button = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(b => Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Any(i => b.onClick.GetPersistentMethodName(i) == method));
        Assert.IsNotNull(button, $"No button calls {method}");
        return button;
    }

    /// <summary>How see-through a button is, 1 when it isn't greyed out</summary>
    public static float Alpha(Button button)
    {
        return button.TryGetComponent(out CanvasGroup fade) ? fade.alpha : 1;
    }

    /// <summary>The UI of a window's field</summary>
    public static FieldUI FieldOf(Window window, int index)
    {
        return window.GetComponentsInChildren<FieldUI>(true)[index];
    }

    /// <summary>Types text into a field's input box and presses enter</summary>
    public static void Type(FieldUI field, string text)
    {
        TMP_InputField input = field.GetComponentInChildren<TMP_InputField>(true);
        input.text = text;
        input.onEndEdit.Invoke(text);
    }

    /// <summary>How many bodies the simulation has</summary>
    public static int BodyCount(SimulationAPI api)
    {
        return Enumerable.Range(0, 64).Count(api.HasBody);
    }
}
