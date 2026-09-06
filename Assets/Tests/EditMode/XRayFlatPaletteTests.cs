using NUnit.Framework;
using UnityEngine;
using Relic;

public class XRayFlatPaletteTests
{
    private Material _terrain, _background, _object;
    private Shader _shader;

    [SetUp]
    public void SetUp()
    {
        _shader = Shader.Find("Custom/XRayFlat");
        Assert.IsNotNull(_shader, "Custom/XRayFlat 셰이더를 찾을 수 없다.");
        _terrain = new Material(_shader);
        _background = new Material(_shader);
        _object = new Material(_shader);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_terrain);
        Object.DestroyImmediate(_background);
        Object.DestroyImmediate(_object);
    }

    [Test]
    public void Apply_각_머티리얼에_해당_톤_색을_주입한다()
    {
        var palette = new XRayFlatPalette(_terrain, _background, _object);

        palette.Apply(Color.red, Color.green, Color.blue);

        Assert.AreEqual(Color.red, _terrain.GetColor("_FlatColor"));
        Assert.AreEqual(Color.green, _background.GetColor("_FlatColor"));
        Assert.AreEqual(Color.blue, _object.GetColor("_FlatColor"));
    }

    [Test]
    public void Get_톤에_대응하는_머티리얼을_반환한다()
    {
        var palette = new XRayFlatPalette(_terrain, _background, _object);

        Assert.AreSame(_terrain, palette.Get(XRayTone.Terrain));
        Assert.AreSame(_background, palette.Get(XRayTone.Background));
        Assert.AreSame(_object, palette.Get(XRayTone.Object));
    }

    [Test]
    public void IsComplete_머티리얼이_하나라도_없으면_false()
    {
        var full = new XRayFlatPalette(_terrain, _background, _object);
        var partial = new XRayFlatPalette(_terrain, null, _object);

        Assert.IsTrue(full.IsComplete);
        Assert.IsFalse(partial.IsComplete);
    }

    [Test]
    public void Apply_머티리얼이_없어도_예외를_던지지_않는다()
    {
        var partial = new XRayFlatPalette(null, null, null);

        Assert.DoesNotThrow(() => partial.Apply(Color.red, Color.green, Color.blue));
        Assert.IsNull(partial.Get(XRayTone.Terrain));
    }
}
