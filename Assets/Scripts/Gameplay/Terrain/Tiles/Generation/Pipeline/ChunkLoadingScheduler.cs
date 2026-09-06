// @tags: pipeline, chunk, generation, scheduler, async, loading
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

/// <summary>
/// 프레임 시간을 관리하면서 비동기 작업을 배치로 처리하는 스케줄러
/// 시간 예산(time budget)을 초과하지 않도록 작업을 분산시킴
/// </summary>
public class ChunkLoadingScheduler
{
    private Stopwatch stopwatch = new Stopwatch();
    
    // Configuration
    public int BatchSize { get; set; }
    public int MaxTimePerBatchMs { get; set; }
    public int MaxTimePerFrameMs { get; set; }
    
    /// <summary>
    /// 스케줄러 생성
    /// </summary>
    /// <param name="batchSize">배치당 처리할 최대 아이템 수</param>
    /// <param name="maxBatchMs">배치 처리 최대 시간 (밀리초)</param>
    /// <param name="maxFrameMs">프레임당 최대 시간 (밀리초)</param>
    public ChunkLoadingScheduler(int batchSize, int maxBatchMs, int maxFrameMs)
    {
        BatchSize = batchSize;
        MaxTimePerBatchMs = maxBatchMs;
        MaxTimePerFrameMs = maxFrameMs;
    }
    
    /// <summary>
    /// 배치 처리 시작 (타이머 재시작)
    /// </summary>
    public void StartBatch()
    {
        stopwatch.Restart();
    }
    
    /// <summary>
    /// 현재 배치 시간이 제한을 초과했는지 확인
    /// </summary>
    public bool ShouldBreakBatch()
    {
        return stopwatch.ElapsedMilliseconds > MaxTimePerBatchMs;
    }
    
    /// <summary>
    /// 현재 프레임 시간이 제한을 초과했는지 확인
    /// </summary>
    public bool ShouldYieldFrame()
    {
        return stopwatch.ElapsedMilliseconds > MaxTimePerFrameMs;
    }
    
    /// <summary>
    /// 다음 프레임으로 양보하고 타이머 재시작
    /// </summary>
    public IEnumerator YieldAndRestart()
    {
        yield return null;
        stopwatch.Restart();
    }
    

}
