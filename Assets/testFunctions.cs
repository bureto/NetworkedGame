using FASTER.core;
using System;

public class testFunctions : IFunctions <int, int, int, int, Empty>
{
    public void SingleReader(ref int key, ref int input, ref int value, ref int dst) => dst = value;
    public void SingleWriter(ref int key, ref int src, ref int dst) => dst = src;
    public void ConcurrentReader(ref int key, ref int input, ref int value, ref int dst) => dst = value;
    public void ConcurrentWriter(ref int key, ref int src, ref int dst) => dst = src;
    public void InitialUpdater(ref int key, ref int input, ref int value) => value = input;
    public void CopyUpdater(ref int key, ref int input, ref int oldv, ref int newv) => newv = oldv + input;
    public void InPlaceUpdater(ref int key, ref int input, ref int value) => value += input;
    public void UpsertCompletionCallback(ref int key, ref int value, Empty ctx) { }
    public void ReadCompletionCallback(ref int key, ref int input, ref int output, Empty ctx, Status s) { }
    public void RMWCompletionCallback(ref int key, ref int input, Empty ctx, Status s) { }
    public void CheckpointCompletionCallback(Guid sessionId, long serialNum) { }
}
