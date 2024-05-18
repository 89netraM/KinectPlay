using System;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;
using Microsoft.Kinect.Face;
using Vector4 = Microsoft.Kinect.Vector4;

PointF pointFZero = new() { X = 0, Y = 0 };

var sensor = KinectSensor.GetDefault();

Console.Write("\x1b[?25l");
Console.CancelKeyPress += (s, e) =>
{
    sensor.Close();
    Console.Write("\x1b[?25h");
};

using var frameReader = sensor.OpenMultiSourceFrameReader(
    FrameSourceTypes.Depth | FrameSourceTypes.Body
);
var bodies = new Body[sensor.BodyFrameSource.BodyCount];

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
    using var bodyFrame = frame.BodyFrameReference.AcquireFrame();

    bodyFrame.GetAndRefreshBodyData(bodies);
    for (int i = 0; i < bodies.Length; i++)
    {
        var body = bodies[i];
        if (!body.IsTracked)
        {
            continue;
        }

        var sb = new StringBuilder("\x1b[G\x1b[K\x1b[A\x1b[K\x1b[A\x1b[K");

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
        }
        else
        {
            sb.AppendLine("Nose Position: Missing!");
        }

        faceSource.TrackingId = body.TrackingId;
        var rot = QuaternionToRotation(face?.FaceRotationQuaternion ?? new());
        sb.Append(
            $"Head Rotation: {rot.X, 6:0.000}, {rot.Y, 6:0.000}, {rot.Z, 6:0.000} (face {(face is not null ? "is" : "isn't")} available)"
        );

        Console.Write(sb);
        break;
    }
};

sensor.Open();

Console.WriteLine("Sensor open, waiting for availability\n\n");

await Task.Delay(-1);

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
