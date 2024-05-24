using System.Runtime.InteropServices;

namespace KinectPlay.Console;

[StructLayout(LayoutKind.Sequential, Pack = 0)]
readonly struct Color(byte r, byte g, byte b, byte a)
{
    public readonly byte R = r;
    public readonly byte G = g;
    public readonly byte B = b;
    public readonly byte A = a;
}
