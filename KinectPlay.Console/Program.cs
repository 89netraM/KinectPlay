using System;
using System.Numerics;
using System.Text;
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

var faceSource = new FaceFrameSource(
    sensor,
    0,
    FaceFrameFeatures.RotationOrientation | FaceFrameFeatures.PointsInColorSpace
);
var faceReader = faceSource.OpenReader();
FaceFrameResult? face = null;
faceReader.FrameArrived += (_, e) => face = e.FrameReference.AcquireFrame().FaceFrameResult;

var colorToCameraSpace = new CameraSpacePoint[
    sensor.ColorFrameSource.FrameDescription.Height,
    sensor.ColorFrameSource.FrameDescription.Width
];

var sb = new StringBuilder("Sensor open, waiting for availability");
(Vector3 pos, Color color)? nose = null;

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

        sb.Clear();

        var pos = body.Joints[JointType.Head].Position;
        sb.AppendLine(
            $"Head Position: {pos.X, 6:0.000}, {pos.Y, 6:0.000}, {pos.Z, 6:0.000} (index {i}, id {body.TrackingId})"
        );

        if (
            face?.FacePointsInColorSpace[FacePointType.Nose] is PointF nosePos
            && nosePos != pointFZero
        )
        {
            var nosePosCameraSpace = colorToCameraSpace[(int)nosePos.Y, (int)nosePos.X];
            sb.AppendLine(
                $"Nose Position: {nosePosCameraSpace.X, 6:0.000}, {nosePosCameraSpace.Y, 6:0.000}, {nosePosCameraSpace.Z, 6:0.000}"
            );
            nose = (
                new(nosePosCameraSpace.X, nosePosCameraSpace.Y, nosePosCameraSpace.Z),
                color[(int)nosePos.Y, (int)nosePos.X]
            );
        }
        else
        {
            sb.AppendLine("Nose Position: Missing!");
            if (nose is var (p, _))
            {
                nose = (p, Color.PINK);
            }
        }

        faceSource.TrackingId = body.TrackingId;
        var rot = QuaternionToRotation(face?.FaceRotationQuaternion ?? new());
        sb.Append(
            $"Head Rotation: {rot.X, 7:0.000}, {rot.Y, 7:0.000}, {rot.Z, 7:0.000} (face {(face is not null ? "is" : "isn't")} available)"
        );

        break;
    }
};

sensor.Open();

Raylib.InitWindow(
    sensor.ColorFrameSource.FrameDescription.Width,
    sensor.ColorFrameSource.FrameDescription.Height,
    "KinectPlay"
);

var font = Raylib.LoadFontEx(@"C:\Windows\Fonts\CascadiaMono.ttf", 20, null, 0);

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

    Raylib.BeginMode3D(camera);
    if (nose is var (p, c))
    {
        Raylib.DrawSphere(p, 0.01f, c);
    }
    Raylib.EndMode3D();

    Raylib.DrawTextEx(font, sb.ToString(), new(0, 0), 20, 0, Color.WHITE);

    Raylib.EndDrawing();
}

sensor.Close();
Raylib.CloseWindow();

// Borrowed from the SDK example code
static Vector3 QuaternionToRotation(Vector4 rotQuaternion)
{
    float x = rotQuaternion.X;
    float y = rotQuaternion.Y;
    float z = rotQuaternion.Z;
    float w = rotQuaternion.W;

    // convert face rotation quaternion to Euler angles in degrees
    float pitch =
        (float)Math.Atan2(2 * ((y * z) + (w * x)), (w * w) - (x * x) - (y * y) + (z * z))
        / (float)Math.PI
        * 180.0f;
    float yaw = (float)Math.Asin(2 * ((w * y) - (x * z))) / (float)Math.PI * 180.0f;
    float roll =
        (float)Math.Atan2(2 * ((x * y) + (w * z)), (w * w) + (x * x) - (y * y) - (z * z))
        / (float)Math.PI
        * 180.0f;
    return new(pitch, yaw, roll);
}
