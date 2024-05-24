using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;

namespace KinectPlay.Console;

internal class GrowingMemory<T> : IEnumerable<T>, IDisposable
    where T : unmanaged
{
    public int Count { get; private set; } = 0;

    private IMemoryOwner<T> owner = MemoryPool<T>.Shared.Rent();
    public ReadOnlyMemory<T> Buffer => owner.Memory.Slice(0, Count);

    public void Add(T item)
    {
        if (Count == owner.Memory.Length)
        {
            var nextOwner = MemoryPool<T>.Shared.Rent(owner.Memory.Length * 2);
            owner.Memory.CopyTo(nextOwner.Memory);
            owner.Dispose();
            owner = nextOwner;
        }
        owner.Memory.Span[Count++] = item;
    }

    public void Clear() => Count = 0;

    public void Dispose() => owner.Dispose();

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)[.. Buffer.Span]).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Buffer.ToArray().GetEnumerator();
}
