using System;
using Silk.NET.OpenGL;

namespace KinectPlay.Console;

internal class VertexBuffer
{
    private readonly GL gl;
    private readonly uint handle;
    private readonly Lazy<IDisposable> unBinder;

    public VertexBuffer(GL gl)
    {
        this.gl = gl;
        unBinder = new(() => new UnBinder(this.gl));

        handle = gl.GenBuffer();
    }

    public IDisposable Bind()
    {
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, handle);
        return unBinder.Value;
    }

    public unsafe void BufferData<T>(ReadOnlyMemory<T> data)
        where T : unmanaged
    {
        fixed (T* pointer = data.Span)
        {
            gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(data.Length * sizeof(T)),
                pointer,
                BufferUsageARB.StaticDraw
            );
        }
    }

    private class UnBinder(GL gl) : IDisposable
    {
        public void Dispose() => gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }
}
