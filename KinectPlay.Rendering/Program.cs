using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

const int PosLocation = 0;
const int ColorLocation = 1;
const int TransformationLocation = 2;
string VertexShaderSource = $$"""
    #version 430 core

    layout (location = {{PosLocation}}) in vec3 pos;
    layout (location = {{ColorLocation}}) in vec3 color;
    layout (location = {{TransformationLocation}}) uniform mat4 transformation;

    out vec4 outColor;

    void main()
    {
        gl_Position = transformation * vec4(pos, 1.0f);
        outColor = vec4(color, 1.0f);
    }
    """;
const string FragmentShaderSource = """
    #version 430 core

    in vec4 outColor;

    out vec4 FragColor;

    void main()
    {
        FragColor = outColor;
    }
    """;

Point[] points =
[
    new(new(0.0f, 0.0f, 0.0f), new(1.0f, 0.0f, 1.0f)),
    new(new(-0.5f, -0.5f, 0.0f), new(1.0f, 0.0f, 0.0f)),
    new(new(0.5f, -0.5f, 0.0f), new(0.0f, 1.0f, 0.0f)),
    new(new(0.0f, 0.707f, 0.0f), new(0.0f, 0.0f, 1.0f)),
];

var window = Window.Create(WindowOptions.Default with { Title = "Kinect Rendering", Size = new(1920, 1080) });
var glGetter = new Lazy<GL>(window.CreateOpenGL);

uint shader = 0;
uint vertexArray = 0;
uint vertexBuffer = 0;

var transformation = Matrix4x4.Identity;

window.Load += OnLoad;
window.Render += OnRender;
window.Resize += size => glGetter.Value.Viewport(size);

window.Run();

void OnLoad()
{
    var gl = glGetter.Value;

    gl.Enable(EnableCap.DepthTest);
    gl.PointSize(10.0f);

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

    gl.VertexAttribPointer(PosLocation, 3, VertexAttribPointerType.Float, false, (uint)sizeof(Point), 0);
    gl.EnableVertexAttribArray(PosLocation);
    gl.VertexAttribPointer(
        ColorLocation,
        3,
        VertexAttribPointerType.Float,
        false,
        (uint)sizeof(Point),
        sizeof(Vector3)
    );
    gl.EnableVertexAttribArray(ColorLocation);

    gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

    gl.BindVertexArray(0);

    return (vertexArray, vertexBuffer);
}

void OnRender(double time)
{
    var projection = Matrix4x4.CreateOrthographic(window.Size.X, window.Size.Y, 0.0f, 1.0f);
    transformation =
        projection
        * Matrix4x4.CreateRotationX((float)Math.PI / 4.0f)
        * Matrix4x4.CreateRotationY((float)window.Time)
        * Matrix4x4.CreateScale(100.0f);

    ClearScreen();

    BindVertexBufferData();

    RenderVertex();
}

void ClearScreen()
{
    var gl = glGetter.Value;

    gl.ClearColor(0.2f, 0.3f, 0.3f, 1.0f);
    gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
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
    UpdateTransformation();

    gl.BindVertexArray(vertexArray);
    gl.DrawArrays(PrimitiveType.Points, 0, (uint)points.Length);
}

void UpdateTransformation()
{
    var gl = glGetter.Value;

    gl.UniformMatrix4(TransformationLocation, 1, false, in transformation.M11);
}

[StructLayout(LayoutKind.Sequential, Pack = 0)]
readonly struct Point(Vector3 position, Vector3 color)
{
    public readonly Vector3 Position = position;
    public readonly Vector3 Color = color;
}
