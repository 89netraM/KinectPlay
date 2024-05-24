using System;
using Silk.NET.OpenGL;

namespace KinectPlay.Console;

internal class VertexArray
{
    private readonly GL gl;
    private readonly uint handle;
    private readonly Lazy<IDisposable> unBinder;

    public VertexArray(GL gl)
    {
        this.gl = gl;
        unBinder = new(() => new UnBinder(this.gl));

        handle = gl.GenVertexArray();
    }

    public IDisposable Bind()
    {
        gl.BindVertexArray(handle);
        return unBinder.Value;
    }

    public void EnableAttributePointer(
        uint location,
        int byteSize,
        VertexAttribPointerType type,
        bool normalize,
        int stride,
        int offset
    )
    {
        gl.VertexAttribPointer(location, byteSize, type, normalize, (uint)stride, offset);
        gl.EnableVertexAttribArray(location);
    }

    private class UnBinder(GL gl) : IDisposable
    {
        public void Dispose() => gl.BindVertexArray(0);
    }
}
