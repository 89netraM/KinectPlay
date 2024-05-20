using System;
using System.Numerics;
using Microsoft.Kinect;
using Microsoft.Kinect.Face;
using Raylib_cs;
using Vector4 = Microsoft.Kinect.Vector4;

PointF pointFZero = new() { X = 0, Y = 0 };

var sensor = KinectSensor.GetDefault();

Console.CancelKeyPress += (s, e) =>
{
    sensor.Close();
    Raylib.CloseWindow();
};

using var frameReader = sensor.OpenMultiSourceFrameReader(
    FrameSourceTypes.Depth | FrameSourceTypes.Color | FrameSourceTypes.Body
);
var bodies = new Body[sensor.BodyFrameSource.BodyCount];
var color = new Color[
    sensor.ColorFrameSource.FrameDescription.Height,
    sensor.ColorFrameSource.FrameDescription.Width
];

using var faceSource = new FaceFrameSource(sensor, 0, FaceFrameFeatures.RotationOrientation);
using var faceReader = faceSource.OpenReader();
FaceFrameResult? face = null;
faceReader.FrameArrived += (_, e) => face = e.FrameReference.AcquireFrame().FaceFrameResult;

var colorToCameraSpace = new CameraSpacePoint[
    sensor.ColorFrameSource.FrameDescription.Height,
    sensor.ColorFrameSource.FrameDescription.Width
];

Vector3? head = null;
Vector4? headRotation = null;

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
        headRotation = face?.FaceRotationQuaternion;

        break;
    }
};

sensor.Open();

Raylib.InitWindow(
    sensor.ColorFrameSource.FrameDescription.Width,
    sensor.ColorFrameSource.FrameDescription.Height,
    "KinectPlay"
);

var camera = new Camera3D(
    Vector3.Zero,
    Vector3.UnitZ,
    Vector3.UnitY,
    sensor.ColorFrameSource.FrameDescription.HorizontalFieldOfView,
    CameraType.CAMERA_PERSPECTIVE
);

while (!Raylib.WindowShouldClose())
{
    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.BLACK);

    if (head is Vector3 pos)
    {
        if (headRotation is Vector4 rot)
        {
            var q = new Quaternion(rot.X, rot.Y, rot.Z, rot.W);

            var headDir = Vector3.Transform(-Vector3.UnitZ, q);
            var upDir = Vector3.Transform(Vector3.UnitY, q);

            camera.position = pos + headDir * 0.5f;
            camera.target = pos;
            camera.up = upDir;
        }

        Raylib.BeginMode3D(camera);

        for (int y = 0; y < color.GetLength(0); y += 5)
        for (int x = 0; x < color.GetLength(1); x += 5)
        {
            var c = color[y, x];
            var p = ToVector(colorToCameraSpace[y, x]);
            if ((p - pos).LengthSquared() < 0.09)
            {
                Raylib.DrawSphere(p, 0.005f, c);
            }
        }
        Raylib.EndMode3D();
    }

    Raylib.EndDrawing();
}

sensor.Close();
Raylib.CloseWindow();

static Vector3 ToVector(CameraSpacePoint cameraSpacePoint) =>
    new(cameraSpacePoint.X, cameraSpacePoint.Y, cameraSpacePoint.Z);
