using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Kinect;
using Microsoft.Kinect.Face;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using ColorF = System.Numerics.Vector4;
using Vector4 = Microsoft.Kinect.Vector4;

PointF pointFZero = new() { X = 0, Y = 0 };

var sensor = KinectSensor.GetDefault();

using var frameReader = sensor.OpenMultiSourceFrameReader(
    FrameSourceTypes.Depth | FrameSourceTypes.Color | FrameSourceTypes.Body
);
var bodies = new Body[sensor.BodyFrameSource.BodyCount];
var color = new Color[sensor.ColorFrameSource.FrameDescription.Height, sensor.ColorFrameSource.FrameDescription.Width];

using var faceSource = new FaceFrameSource(sensor, 0, FaceFrameFeatures.RotationOrientation);
using var faceReader = faceSource.OpenReader();
FaceFrameResult? face = null;
faceReader.FrameArrived += (_, e) => face = e.FrameReference.AcquireFrame().FaceFrameResult;

var colorToCameraSpace = new CameraSpacePoint[
    sensor.ColorFrameSource.FrameDescription.Height,
    sensor.ColorFrameSource.FrameDescription.Width
];

Point[] points = [new(new(0.0f, 0.0f, 1.0f), new(1.0f, 0.0f, 1.0f, 1.0f))];
List<Point> backPoints = [];
var updatedPoints = true;
Vector3? head = null;
Quaternion? headRotation = null;

frameReader.MultiSourceFrameArrived += (_, e) =>
{
    var frame = e.FrameReference.AcquireFrame();
    unsafe
    {
        using var depthFrame = frame.DepthFrameReference.AcquireFrame();
        using var depthBuffer = depthFrame.LockImageBuffer();
        fixed (CameraSpacePoint* colorToCameraSpacePointer = colorToCameraSpace)
        {
            sensor.CoordinateMapper.MapColorFrameToCameraSpaceUsingIntPtr(
                depthBuffer.UnderlyingBuffer,
                depthBuffer.Size,
                (IntPtr)colorToCameraSpacePointer,
                (uint)colorToCameraSpace.Length * (uint)sizeof(CameraSpacePoint)
            );
        }
    }
    unsafe
    {
        using var colorFrame = frame.ColorFrameReference.AcquireFrame();
        fixed (Color* colorPointer = color)
        {
            colorFrame.CopyConvertedFrameDataToIntPtr(
                (IntPtr)colorPointer,
                (uint)color.Length * (uint)sizeof(Color),
                ColorImageFormat.Rgba
            );
        }
    }
    using var bodyFrame = frame.BodyFrameReference.AcquireFrame();

    bodyFrame.GetAndRefreshBodyData(bodies);
    for (int i = 0; i < bodies.Length; i++)
    {
        var body = bodies[i];
        if (!body.IsTracked)
        {
            continue;
        }

        head = ToVector(body.Joints[JointType.Head].Position);

        faceSource.TrackingId = body.TrackingId;
        headRotation = ToQuaternion(face?.FaceRotationQuaternion);

        backPoints.Clear();
        for (int y = 0; y < color.GetLength(0); y++)
        for (int x = 0; x < color.GetLength(1); x++)
        {
            var pos = ToVector(colorToCameraSpace[y, x]);
            if (Vector3.DistanceSquared(head.Value, pos) < 0.09)
            {
                var c = color[y, x];
                backPoints.Add(new(pos, new(c.R / 255.0f, c.G / 255.0f, c.B / 255.0f, c.A / 255.0f)));
            }
        }
        points = [.. backPoints];
        updatedPoints = true;

        break;
    }
};

sensor.Open();

const int PosLocation = 0;
const int ColorLocation = 1;
const int TransformationLocation = 2;
string VertexShaderSource = $$"""
    #version 430 core

    layout (location = {{PosLocation}}) in vec3 pos;
    layout (location = {{ColorLocation}}) in vec4 color;
    layout (location = {{TransformationLocation}}) uniform mat4 transformation;

    out vec4 outColor;

    void main()
    {
        gl_Position = transformation * vec4(pos, 1.0f);
        outColor = color;
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

var window = Window.Create(WindowOptions.Default with { Title = "KinectPlay", Size = new(1920, 1080), VSync = false, });
var glGetter = new Lazy<GL>(window.CreateOpenGL);

uint shader = 0;
uint vertexArray = 0;
uint vertexBuffer = 0;

var view = Matrix4x4.CreateLookAt(Vector3.Zero, new(0.0f, 0.0f, 1.0f), Vector3.UnitY);
var projection = Matrix4x4.CreatePerspectiveFieldOfView(
    (float)Math.PI * sensor.DepthFrameSource.FrameDescription.HorizontalFieldOfView / 180.0f,
    (float)window.Size.X / window.Size.Y,
    sensor.DepthFrameSource.DepthMinReliableDistance / 1000.0f,
    sensor.DepthFrameSource.DepthMaxReliableDistance / 1000.0f
);

window.Load += OnLoad;
window.Render += OnRender;
window.Resize += size =>
{
    glGetter.Value.Viewport(size);
    projection = Matrix4x4.CreatePerspectiveFieldOfView(
        (float)Math.PI * sensor.DepthFrameSource.FrameDescription.HorizontalFieldOfView / 180.0f,
        (float)window.Size.X / window.Size.Y,
        sensor.DepthFrameSource.DepthMinReliableDistance / 1000.0f,
        sensor.DepthFrameSource.DepthMaxReliableDistance / 1000.0f
    );
};

window.Run();

void OnLoad()
{
    var gl = glGetter.Value;

    gl.Enable(EnableCap.DepthTest);
    gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
    gl.PointSize(3.0f);

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
        4,
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
    ClearScreen();

    BindVertexBufferData();

    RenderVertex();
}

void ClearScreen()
{
    var gl = glGetter.Value;

    gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
}

unsafe void BindVertexBufferData()
{
    if (!updatedPoints)
    {
        return;
    }

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

    if (head is Vector3 hp)
    {
        var cameraPos = Vector3.Zero;
        var cameraUp = Vector3.UnitY;
        if (headRotation is Quaternion hr)
        {
            var rotation = Matrix4x4.CreateFromQuaternion(hr);
            cameraPos = hp - Vector3.Transform(0.5f * Vector3.UnitZ, rotation);
            cameraUp = Vector3.TransformNormal(cameraUp, rotation);
        }
        view = Matrix4x4.CreateLookAt(cameraPos - cameraUp * 0.1f, hp - cameraUp * 0.1f, cameraUp);
    }
    var transformation = view * projection;

    gl.UniformMatrix4(TransformationLocation, 1, false, in transformation.M11);
}

static Vector3 ToVector(CameraSpacePoint cameraSpacePoint) =>
    new(cameraSpacePoint.X, cameraSpacePoint.Y, cameraSpacePoint.Z);
static Quaternion? ToQuaternion(Vector4? q) => q is Vector4 qq ? new(qq.X, qq.Y, qq.Z, qq.W) : null;

[StructLayout(LayoutKind.Sequential, Pack = 0)]
readonly struct Point(Vector3 position, ColorF color)
{
    public readonly Vector3 Position = position;
    public readonly ColorF Color = color;
}

[StructLayout(LayoutKind.Sequential, Pack = 0)]
readonly struct Color(byte r, byte g, byte b, byte a)
{
    public readonly byte R = r;
    public readonly byte G = g;
    public readonly byte B = b;
    public readonly byte A = a;
}
