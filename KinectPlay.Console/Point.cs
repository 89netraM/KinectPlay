using System.Numerics;
using System.Runtime.InteropServices;

namespace KinectPlay.Console;

[StructLayout(LayoutKind.Sequential, Pack = 0)]
readonly struct Point(Vector3 position, Vector4 color)
{
    public readonly Vector3 Position = position;
    public readonly Vector4 Color = color;
}
