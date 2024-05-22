using System;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

const string VertexShaderSource = """
    #version 330 core

    layout (location = 0) in vec3 aPos;
    layout (location = 1) in vec3 aColor;

    out vec4 ourColor;

    void main()
    {
        gl_Position = vec4(aPos, 1.0f);
        ourColor = vec4(aColor, 1.0f);
    }
    """;
const string FragmentShaderSource = """
    #version 330 core

    in vec4 ourColor;

    out vec4 FragColor;

    void main()
    {
        FragColor = ourColor;
    }
    """;

Point[] points =
[
    new(new(-0.5f, -0.5f, 0.0f), new(1.0f, 0.0f, 0.0f)),
    new(new(0.5f, -0.5f, 0.0f), new(0.0f, 1.0f, 0.0f)),
    new(new(0.0f, 0.5f, 0.0f), new(0.0f, 0.0f, 1.0f)),
];

var window = Window.Create(WindowOptions.Default with { Title = "Kinect Rendering", Size = new(1920, 1080) });
var glGetter = new Lazy<GL>(window.CreateOpenGL);

uint shader = 0;
uint vertexArray = 0;
uint vertexBuffer = 0;

window.Load += OnLoad;
window.Render += OnRender;

window.Run();

void OnLoad()
{
    shader = BuildShader();

    (vertexArray, vertexBuffer) = BuildVertex();
}

uint BuildShader()
{
    var gl = glGetter.Value;

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

    var shader = gl.CreateProgram();
    gl.AttachShader(shader, vertexShader);
    gl.AttachShader(shader, fragmentShader);
    gl.LinkProgram(shader);
    if (gl.GetProgramInfoLog(shader) is string programError and not "")
    {
        throw new Exception($"Program error, {programError}");
    }

    gl.DeleteShader(vertexShader);
    gl.DeleteShader(fragmentShader);

    return shader;
}

unsafe (uint, uint) BuildVertex()
{
    var gl = glGetter.Value;

    var vertexArray = gl.GenVertexArray();
    gl.BindVertexArray(vertexArray);

    var vertexBuffer = gl.GenBuffer();
    gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexBuffer);

    gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(Point), 0);
    gl.EnableVertexAttribArray(0);
    gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)sizeof(Point), sizeof(Vector3));
    gl.EnableVertexAttribArray(1);

    gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

    gl.BindVertexArray(0);

    return (vertexArray, vertexBuffer);
}

void OnRender(double time)
{
    ClearScreen();

    BindVertexBufferData();

    RenderVertex();
}

void ClearScreen()
{
    var gl = glGetter.Value;

    gl.ClearColor(0.2f, 0.3f, 0.3f, 1.0f);
    gl.Clear(ClearBufferMask.ColorBufferBit);
}

unsafe void BindVertexBufferData()
{
    var gl = glGetter.Value;

    gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexBuffer);
    fixed (Point* p = points)
    {
        gl.BufferData(
            BufferTargetARB.ArrayBuffer,
            (nuint)(points.Length * sizeof(Point)),
            p,
            BufferUsageARB.StaticDraw
        );
    }
}

void RenderVertex()
{
    var gl = glGetter.Value;

    gl.UseProgram(shader);
    gl.BindVertexArray(vertexArray);
    gl.PointSize(10.0f);
    gl.DrawArrays(PrimitiveType.Points, 0, (uint)points.Length);
}

[StructLayout(LayoutKind.Sequential, Pack = 0)]
readonly struct Point(Vector3 position, Vector3 color)
{
    public readonly Vector3 Position = position;
    public readonly Vector3 Color = color;
}
