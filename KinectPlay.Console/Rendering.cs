using System;
using System.Numerics;
using System.Threading.Channels;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace KinectPlay.Console;

internal class Rendering
{
    private static readonly Vector4 BackgroundColor = new(0.0f, 0.0f, 0.0f, 1.0f);
    private const float PointSize = 5.0f;
    private const float VerticalCameraOffset = -0.1f;
    private const float CameraDistance = 0.5f;
    private const float CameraTargetSpeedInMetersPerSecond = 0.5f;
    private const float CameraRotationSpeedInRadiansPerSecond = (float)Math.PI / 2.0f;

    private readonly ChannelReader<ReadOnlyMemory<Point>> pointsReader;
    private uint renderedPointsCount = 0;

    private Head? lastSeenHead = null;
    private Quaternion cameraRotation = Quaternion.Identity;
    private Vector3 cameraTarget = new(0.0f, 0.0f, 1.0f);
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

    public void ResizeCamera(Vector2D<int> screenSize, float fovDegrees)
    {
        gl.Viewport(screenSize);
        projection = Matrix4x4.CreatePerspectiveFieldOfView(
            (float)Math.PI * fovDegrees / 180.0f,
            (float)screenSize.X / screenSize.Y,
            0.1f,
            100.0f
        );
    }

    public void Render(float deltaTime, Head? head)
    {
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        BindVertexBufferData();

        RenderVertex(deltaTime, head);
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

    private void RenderVertex(float deltaTime, Head? head)
    {
        using var s = shader.Use();
        UpdateTransformation(deltaTime, head);

        using var a = vertexArray.Bind();
        gl.DrawArrays(PrimitiveType.Points, 0, renderedPointsCount);
    }

    private void UpdateTransformation(float deltaTime, Head? head)
    {
        if (head is Head newHead)
        {
            lastSeenHead = newHead;
        }

        if (lastSeenHead is Head h)
        {
            cameraTarget = Lerp(cameraTarget, h.Center, CameraTargetSpeedInMetersPerSecond * deltaTime);

            if (h.Rotation is Quaternion targetCameraRotation)
            {
                cameraRotation = Quaternion.Lerp(
                    cameraRotation,
                    targetCameraRotation,
                    CameraRotationSpeedInRadiansPerSecond * deltaTime
                );
            }
        }

        var rotationMatrix = Matrix4x4.CreateFromQuaternion(cameraRotation);
        var cameraUp = Vector3.TransformNormal(Vector3.UnitY, rotationMatrix);
        var cameraPosition = cameraTarget - Vector3.Transform(CameraDistance * Vector3.UnitZ, rotationMatrix);
        var view = Matrix4x4.CreateLookAt(
            cameraPosition + cameraUp * VerticalCameraOffset,
            cameraTarget + cameraUp * VerticalCameraOffset,
            cameraUp
        );
        var transformation = view * projection;

        shader.BindTransformation(transformation);
    }

    private static Vector3 Lerp(Vector3 from, Vector3 to, float step)
    {
        var diff = to - from;
        var distance = diff.Length();
        if (distance < step)
        {
            return to;
        }

        var direction = diff / distance;
        return from + direction * step;
    }
}
