using NUnit.Framework;
using UnityEngine;

/// <summary>UISprites: the rounded boxes and glyphs the UI is drawn with</summary>
public class UISpritesTests
{
    /// <summary>The same sprite is drawn once and then kept</summary>
    [Test]
    public void SameArguments_SameSprite()
    {
        Assert.AreSame(UISprites.Rounded(8), UISprites.Rounded(8));
        Assert.AreSame(UISprites.Tick(24), UISprites.Tick(24));
    }

    /// <summary>Different arguments give different sprites</summary>
    [Test]
    public void DifferentArguments_DifferentSprites()
    {
        Assert.AreNotSame(UISprites.Rounded(8), UISprites.Rounded(9));
        Assert.AreNotSame(UISprites.Rounded(8), UISprites.Rounded(8, Corners.Top));
        Assert.AreNotSame(UISprites.Tick(24), UISprites.Chevron(24));
    }

    /// <summary>A rounded box is just big enough for its corners, which stay unstretched when sliced</summary>
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(8)]
    public void Rounded_SizeAndBorder(int radius)
    {
        Sprite sprite = UISprites.Rounded(radius);
        Assert.AreEqual(2 * radius + 2, sprite.texture.width);
        Assert.AreEqual(new Vector4(radius, radius, radius, radius), sprite.border);
    }

    /// <summary>Glyphs are square at the asked size</summary>
    [TestCase(1)]
    [TestCase(32)]
    public void Glyphs_AreSquare(int size)
    {
        foreach (Sprite sprite in new[] { UISprites.Tick(size), UISprites.Chevron(size), UISprites.BackArrow(size), UISprites.CheckBox(size, 2) })
        {
            Assert.AreEqual(size, sprite.texture.width);
            Assert.AreEqual(size, sprite.texture.height);
        }
    }

    /// <summary>A shadow's border covers its corner and its blur</summary>
    [Test]
    public void Shadow_BorderCoversBlur()
    {
        Sprite sprite = UISprites.Shadow(6, 10);
        Assert.AreEqual(2 * 16 + 2, sprite.texture.width);
        Assert.AreEqual(16, sprite.border.x);
    }
}
