using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.Kinect;
using Microsoft.Kinect.Face;
using Vector4 = Microsoft.Kinect.Vector4;

var sensor = KinectSensor.GetDefault();

Console.Write("\x1b[?25l");
Console.CancelKeyPress += (s, e) =>
{
    sensor.Close();
    Console.Write("\x1b[?25h");
};

using var bodyFrameReader = sensor.BodyFrameSource.OpenReader();
var bodies = new Body[sensor.BodyFrameSource.BodyCount];

(FaceFrameSource source, FaceFrameReader reader, FaceFrameResult? result)[] faces = [];
faces = Enumerable
    .Range(0, sensor.BodyFrameSource.BodyCount)
    .Select(i =>
    {
        var source = new FaceFrameSource(sensor, 0, FaceFrameFeatures.RotationOrientation);
        var reader = source.OpenReader();
        reader.FrameArrived += (_, e) =>
            faces[i].result = e.FrameReference.AcquireFrame().FaceFrameResult;
        return (source, reader, (FaceFrameResult?)null);
    })
    .ToArray();

bodyFrameReader.FrameArrived += (_, e) =>
{
    using var frame = e.FrameReference.AcquireFrame();

    frame.GetAndRefreshBodyData(bodies);
    for (int i = 0; i < bodies.Length; i++)
    {
        var body = bodies[i];
        if (!body.IsTracked)
        {
            continue;
        }

        faces[i].source.TrackingId = body.TrackingId;
        var face = faces[i].result;
        var rot = QuaternionToRotation(face?.FaceRotationQuaternion ?? new());

        var pos = body.Joints[JointType.Head].Position;
        Console.Write("\x1b[G\x1b[A");
        Console.WriteLine(
            $"Head Position: {pos.X, 6:0.000}, {pos.Y, 6:0.000}, {pos.Z, 6:0.000} (index {i}, id {body.TrackingId})"
        );
        Console.Write(
            $"Head Rotation: {rot.X, 6:0.000}, {rot.Y, 6:0.000}, {rot.Z, 6:0.000} (face {(face is not null ? "is" : "isn't")} available)"
        );
        break;
    }
};

sensor.Open();

Console.WriteLine("Sensor open, waiting for availability\n");

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
