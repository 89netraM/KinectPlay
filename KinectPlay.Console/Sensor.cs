using System;
using System.Numerics;
using System.Threading.Channels;
using Microsoft.Kinect;
using Microsoft.Kinect.Face;
using Vector4 = Microsoft.Kinect.Vector4;

namespace KinectPlay.Console;

internal class Sensor : IDisposable
{
    private readonly KinectSensor sensor;
    private readonly MultiSourceFrameReader frameReader;
    private readonly FaceFrameSource faceSource;
    private readonly FaceFrameReader faceReader;
    private FaceFrameResult? faceResult = null;

    private readonly Body[] bodies;
    private readonly Color[,] pixelColors;
    private readonly CameraSpacePoint[,] pixelCoordinates;

    public readonly GrowingMemory<Point> points = new();
    private readonly ChannelWriter<ReadOnlyMemory<Point>> pointsWriter;
    public Head? Head { get; private set; } = null;

    public float HorizontalFov => sensor.ColorFrameSource.FrameDescription.HorizontalFieldOfView;

    public Sensor(ChannelWriter<ReadOnlyMemory<Point>> pointsWriter)
    {
        this.pointsWriter = pointsWriter;

        sensor = KinectSensor.GetDefault();
        frameReader = sensor.OpenMultiSourceFrameReader(
            FrameSourceTypes.Color | FrameSourceTypes.Depth | FrameSourceTypes.Body
        );
        frameReader.MultiSourceFrameArrived += OnMultiSourceFrameArrived;

        faceSource = new FaceFrameSource(sensor, 0, FaceFrameFeatures.RotationOrientation);
        faceReader = faceSource.OpenReader();
        faceReader.FrameArrived += OnFaceFrameArrived;

        bodies = new Body[sensor.BodyFrameSource.BodyCount];
        pixelColors = new Color[
            sensor.ColorFrameSource.FrameDescription.Height,
            sensor.ColorFrameSource.FrameDescription.Width
        ];
        pixelCoordinates = new CameraSpacePoint[
            sensor.ColorFrameSource.FrameDescription.Height,
            sensor.ColorFrameSource.FrameDescription.Width
        ];

        sensor.Open();
    }

    private void OnFaceFrameArrived(object sender, FaceFrameArrivedEventArgs e)
    {
        faceResult = e.FrameReference.AcquireFrame().FaceFrameResult;
    }

    private void OnMultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
    {
        var frame = e.FrameReference.AcquireFrame();

        LoadPixelColors(frame);
        LoadPixelCoordinates(frame);
        LoadBodies(frame);

        UpdateHead();
        WritePoints();
    }

    private unsafe void LoadPixelColors(MultiSourceFrame frame)
    {
        using var colorFrame = frame.ColorFrameReference.AcquireFrame();
        fixed (Color* colorPointer = pixelColors)
        {
            colorFrame.CopyConvertedFrameDataToIntPtr(
                (IntPtr)colorPointer,
                (uint)pixelColors.Length * (uint)sizeof(Color),
                ColorImageFormat.Rgba
            );
        }
    }

    private unsafe void LoadPixelCoordinates(MultiSourceFrame frame)
    {
        using var depthFrame = frame.DepthFrameReference.AcquireFrame();
        using var depthBuffer = depthFrame.LockImageBuffer();
        fixed (CameraSpacePoint* pixelCoordinatePointer = pixelCoordinates)
        {
            sensor.CoordinateMapper.MapColorFrameToCameraSpaceUsingIntPtr(
                depthBuffer.UnderlyingBuffer,
                depthBuffer.Size,
                (IntPtr)pixelCoordinatePointer,
                (uint)pixelCoordinates.Length * (uint)sizeof(CameraSpacePoint)
            );
        }
    }

    private void LoadBodies(MultiSourceFrame frame)
    {
        using var bodyFrame = frame.BodyFrameReference.AcquireFrame();
        bodyFrame.GetAndRefreshBodyData(bodies);
    }

    private void UpdateHead()
    {
        if (GetTrackedBody() is not Body body)
        {
            return;
        }

        faceSource.TrackingId = body.TrackingId;

        if (body.Joints[JointType.Head] is { TrackingState: not TrackingState.NotTracked, Position: var headPosition })
        {
            Head = new(ToVector(headPosition), ToQuaternion(faceResult?.FaceRotationQuaternion));
        }
    }

    private void WritePoints()
    {
        if (Head is not Head head)
        {
            return;
        }

        points.Clear();
        for (int y = 0; y < pixelColors.GetLength(0); y++)
        for (int x = 0; x < pixelColors.GetLength(1); x++)
        {
            var pos = ToVector(pixelCoordinates[y, x]);
            if (Vector3.DistanceSquared(head.Center, pos) < 0.09)
            {
                var c = pixelColors[y, x];
                points.Add(new(pos, new(c.R / 255.0f, c.G / 255.0f, c.B / 255.0f, c.A / 255.0f)));
            }
        }
        var didWrite = pointsWriter.TryWrite(points.Buffer);

        if (!didWrite)
        {
            System.Console.Error.WriteLine("Could not write point cloud to rendering system");
        }
    }

    private Body? GetTrackedBody() => Array.Find(bodies, b => b.IsTracked);

    public void Dispose()
    {
        points.Dispose();
        faceReader.Dispose();
        faceSource.Dispose();
        frameReader.Dispose();
        sensor.Close();
    }

    private static Vector3 ToVector(CameraSpacePoint cameraSpacePoint) =>
        new(cameraSpacePoint.X, cameraSpacePoint.Y, cameraSpacePoint.Z);

    private static Quaternion? ToQuaternion(Vector4? q) => q is Vector4 qq ? new(qq.X, qq.Y, qq.Z, qq.W) : null;
}
