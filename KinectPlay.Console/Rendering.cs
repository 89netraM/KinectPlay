using System;
using System.Numerics;
using System.Threading.Channels;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace KinectPlay.Console;

internal class Rendering
{
    private static readonly Vector4 BackgroundColor = new(0.0f, 0.0f, 0.0f, 1.0f);
    private const float PointSize = 3.0f;
    private const float VerticalCameraOffset = -0.1f;
    private const float CameraDistance = 0.5f;

    private readonly ChannelReader<ReadOnlyMemory<Point>> pointsReader;
    private uint renderedPointsCount = 0;

    private Matrix4x4 view = Matrix4x4.CreateLookAt(Vector3.Zero, new(0.0f, 0.0f, 1.0f), Vector3.UnitY);
    private Matrix4x4 projection = Matrix4x4.Identity;

    private readonly GL gl;
    private readonly Shader shader;
    private readonly VertexArray vertexArray;
    private readonly VertexBuffer vertexBuffer;

    public unsafe Rendering(GL gl, ChannelReader<ReadOnlyMemory<Point>> pointsReader)
    {
        this.gl = gl;
        this.pointsReader = pointsReader;

        gl.Enable(EnableCap.DepthTest);
        gl.ClearColor(BackgroundColor.X, BackgroundColor.Y, BackgroundColor.Z, BackgroundColor.W);
        gl.PointSize(PointSize);

        shader = new Shader(gl);

        vertexArray = new VertexArray(gl);
        using var a = vertexArray.Bind();

        vertexBuffer = new VertexBuffer(gl);
        using var b = vertexBuffer.Bind();

        vertexArray.EnableAttributePointer(
            Shader.PositionLocation,
            3,
            VertexAttribPointerType.Float,
            false,
            sizeof(Point),
            0
        );
        vertexArray.EnableAttributePointer(
            Shader.ColorLocation,
            4,
            VertexAttribPointerType.Float,
            false,
            sizeof(Point),
            sizeof(Vector3)
        );
    }

    public void ResizeCamera(
        Vector2D<int> screenSize,
        float fovDegrees,
        float minDistanceMillimeters,
        float maxDistanceMilliliters
    )
    {
        gl.Viewport(screenSize);
        projection = Matrix4x4.CreatePerspectiveFieldOfView(
            (float)Math.PI * fovDegrees / 180.0f,
            (float)screenSize.X / screenSize.Y,
            minDistanceMillimeters / 1000.0f,
            maxDistanceMilliliters / 1000.0f
        );
    }

    public void Render(Head? head)
    {
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        BindVertexBufferData();

        RenderVertex(head);
    }

    private unsafe void BindVertexBufferData()
    {
        if (!pointsReader.TryRead(out var points))
        {
            return;
        }

        using var _ = vertexBuffer.Bind();
        vertexBuffer.BufferData(points);

        renderedPointsCount = (uint)points.Length;
    }

    private void RenderVertex(Head? head)
    {
        using var s = shader.Use();
        UpdateTransformation(head);

        using var a = vertexArray.Bind();
        gl.DrawArrays(PrimitiveType.Points, 0, renderedPointsCount);
    }

    private void UpdateTransformation(Head? head)
    {
        if (head is Head h)
        {
            var cameraPos = Vector3.Zero;
            var cameraUp = Vector3.UnitY;
            if (h.Rotation is Quaternion hRotation)
            {
                var rotation = Matrix4x4.CreateFromQuaternion(hRotation);
                cameraPos = h.Center - Vector3.Transform(CameraDistance * Vector3.UnitZ, rotation);
                cameraUp = Vector3.TransformNormal(cameraUp, rotation);
            }

            view = Matrix4x4.CreateLookAt(
                cameraPos + cameraUp * VerticalCameraOffset,
                h.Center + cameraUp * VerticalCameraOffset,
                cameraUp
            );
        }
        var transformation = view * projection;

        shader.BindTransformation(transformation);
    }
}
