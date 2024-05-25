using System.Numerics;

namespace KinectPlay.Console;

internal readonly struct Head(Vector3 center, Quaternion? rotation)
{
    public readonly Vector3 Center = center;
    public readonly Quaternion? Rotation = rotation;
}
