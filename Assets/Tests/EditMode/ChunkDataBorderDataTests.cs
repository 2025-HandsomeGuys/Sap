using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

public class ChunkDataBorderDataTests
{
    [Test]
    public void SetBorderData_CopiesPixelsAndDimensions()
    {
        var data = new ChunkData(4, 4);
        var border = new Color32[]
        {
            new Color32(10, 0, 0, 255), new Color32(20, 0, 0, 255),
            new Color32(30, 0, 0, 255), new Color32(40, 0, 0, 255),
        };

        data.SetBorderData(border, 2, 2);

        Assert.AreEqual(4, data.BorderData.Length);
        Assert.AreEqual(2, data.BorderWidth);
        Assert.AreEqual(2, data.BorderHeight);
        Assert.AreEqual(30, data.BorderData[2].r);

        data.Dispose();
    }

    [Test]
    public void SetBorderData_SecondCall_ResizesWithoutLeak()
    {
        var data = new ChunkData(4, 4);
        data.SetBorderData(new Color32[4], 2, 2);
        data.SetBorderData(new Color32[9], 3, 3);   // 크기 변경

        Assert.AreEqual(9, data.BorderData.Length);
        Assert.AreEqual(3, data.BorderWidth);

        data.Dispose();
    }
}
