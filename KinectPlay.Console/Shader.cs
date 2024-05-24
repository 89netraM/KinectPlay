using System;
using System.Numerics;
using Silk.NET.OpenGL;

namespace KinectPlay.Console;

internal class Shader
{
    private const int PositionLocation = 0;
    private const int ColorLocation = 1;
    private const int TransformationLocation = 2;
    private readonly string VertexShaderSource = $$"""
        #version 430 core

        layout (location = {{PositionLocation}}) in vec3 position;
        layout (location = {{ColorLocation}}) in vec4 color;
        layout (location = {{TransformationLocation}}) uniform mat4 transformation;

        out vec4 outColor;

        void main()
        {
            gl_Position = transformation * vec4(position, 1.0f);
            outColor = color;
        }
        """;
    private const string FragmentShaderSource = """
        #version 430 core

        in vec4 outColor;

        out vec4 FragColor;

        void main()
        {
            FragColor = outColor;
        }
        """;

    private readonly GL gl;
    private readonly uint handel;
    private readonly Lazy<IDisposable> unUser;

    public Shader(GL gl)
    {
        this.gl = gl;
        unUser = new(() => new UnUser(this.gl));

        var vertexShader = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vertexShader, VertexShaderSource);
        gl.CompileShader(vertexShader);
        if (gl.GetShaderInfoLog(vertexShader) is string vertexError and not "")
        {
            throw new Exception($"Vertex error, {vertexError}");
        }

        var fragmentShader = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(fragmentShader, FragmentShaderSource);
        gl.CompileShader(fragmentShader);
        if (gl.GetShaderInfoLog(fragmentShader) is string fragmentError and not "")
        {
            throw new Exception($"Fragment error, {fragmentError}");
        }

        handel = gl.CreateProgram();
        gl.AttachShader(handel, vertexShader);
        gl.AttachShader(handel, fragmentShader);
        gl.LinkProgram(handel);
        if (gl.GetProgramInfoLog(handel) is string programError and not "")
        {
            throw new Exception($"Program error, {programError}");
        }

        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);
    }

    public IDisposable Use()
    {
        gl.UseProgram(handel);
        return unUser.Value;
    }

    public void BindTransformation(in Matrix4x4 transform)
    {
        gl.UniformMatrix4(TransformationLocation, 1, false, in transform.M11);
    }

    private class UnUser(GL gl) : IDisposable
    {
        public void Dispose() => gl.UseProgram(0);
    }
}
