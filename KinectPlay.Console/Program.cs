using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using KinectPlay.Console;
using Microsoft.Kinect;
using Microsoft.Kinect.Face;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using ColorF = System.Numerics.Vector4;
using Shader = KinectPlay.Console.Shader;
using Vector4 = Microsoft.Kinect.Vector4;
using VertexArray = KinectPlay.Console.VertexArray;

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

var window = Window.Create(WindowOptions.Default with { Title = "KinectPlay", Size = new(1920, 1080), VSync = false, });
var glGetter = new Lazy<GL>(window.CreateOpenGL);

Shader? shader = null;
VertexArray? vertexArray = null;
VertexBuffer? vertexBuffer = null;

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

    shader = new(gl);

    (vertexArray, vertexBuffer) = BuildVertex();
}

unsafe (VertexArray, VertexBuffer) BuildVertex()
{
    var gl = glGetter.Value;

    var vertexArray = new VertexArray(gl);
    using var a = vertexArray.Bind();

    var vertexBuffer = new VertexBuffer(gl);
    using var b = vertexBuffer.Bind();

    vertexArray.EnableAttributePointer(PosLocation, 3, VertexAttribPointerType.Float, false, sizeof(Point), 0);
    vertexArray.EnableAttributePointer(
        ColorLocation,
        4,
        VertexAttribPointerType.Float,
        false,
        sizeof(Point),
        sizeof(Vector3)
    );

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

    using var _ = vertexBuffer!.Bind();
    vertexBuffer!.BufferData(points);
}

void RenderVertex()
{
    var gl = glGetter.Value;

    using var s = shader!.Use();
    UpdateTransformation();

    using var a = vertexArray!.Bind();
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

    shader!.BindTransformation(transformation);
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
