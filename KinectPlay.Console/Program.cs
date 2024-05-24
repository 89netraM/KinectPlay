using System;
using System.Numerics;
using KinectPlay.Console;
using Microsoft.Kinect;
using Microsoft.Kinect.Face;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Vector4 = Microsoft.Kinect.Vector4;

var sensor = KinectSensor.GetDefault();

Rendering? rendering = null;
Head? head = null;

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

frameReader.MultiSourceFrameArrived += (_, e) =>
{
    if (rendering is null)
    {
        return;
    }

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

        if (body.Joints[JointType.Head].TrackingState is not TrackingState.NotTracked)
        {
            var hCenter = ToVector(body.Joints[JointType.Head].Position);

            faceSource.TrackingId = body.TrackingId;
            var hRotation = ToQuaternion(face?.FaceRotationQuaternion);
            head = new(hCenter, hRotation);
        }

        if (head is Head h)
        {
            rendering.Points.Clear();
            for (int y = 0; y < color.GetLength(0); y++)
            for (int x = 0; x < color.GetLength(1); x++)
            {
                var pos = ToVector(colorToCameraSpace[y, x]);
                if (Vector3.DistanceSquared(h.Center, pos) < 0.09)
                {
                    var c = color[y, x];
                    rendering.Points.Add(new(pos, new(c.R / 255.0f, c.G / 255.0f, c.B / 255.0f, c.A / 255.0f)));
                }
            }
            rendering.ReloadPoints = true;
        }

        break;
    }
};

sensor.Open();

var window = Window.Create(WindowOptions.Default with { Title = "KinectPlay", Size = new(1920, 1080), VSync = false, });

window.Load += () =>
{
    rendering = new Rendering(GL.GetApi(window));
    rendering.ResizeCamera(
        window.Size,
        sensor.ColorFrameSource.FrameDescription.HorizontalFieldOfView,
        sensor.DepthFrameSource.DepthMinReliableDistance,
        sensor.DepthFrameSource.DepthMaxReliableDistance
    );
};
window.Render += deltaTime => rendering?.Render(head);
window.Resize += size =>
    rendering?.ResizeCamera(
        size,
        sensor.ColorFrameSource.FrameDescription.HorizontalFieldOfView,
        sensor.DepthFrameSource.DepthMinReliableDistance,
        sensor.DepthFrameSource.DepthMaxReliableDistance
    );

window.Run();

static Vector3 ToVector(CameraSpacePoint cameraSpacePoint) =>
    new(cameraSpacePoint.X, cameraSpacePoint.Y, cameraSpacePoint.Z);
static Quaternion? ToQuaternion(Vector4? q) => q is Vector4 qq ? new(qq.X, qq.Y, qq.Z, qq.W) : null;
