using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Editor menu item that counts the lines of code in Assets</summary>
public static class CountLines
{
    [MenuItem("Tools/Count lines")]
    public static void Count()
    {
        var filePaths = AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".cs") || p.EndsWith(".compute") || p.EndsWith(".hlsl") || p.EndsWith(".shader"))
            .ToArray();

        int count = 0;
        foreach (var path in filePaths)
        {
            var file = File.ReadAllText(path);
            count += file.Split('\n').Length;
        }

        Debug.Log($"Number of lines: {count}");
    }
}