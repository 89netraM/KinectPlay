using System;
using System.Threading.Channels;
using KinectPlay.Console;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

var pointsChannel = Channel.CreateBounded<ReadOnlyMemory<Point>>(
    new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest }
);
pointsChannel.Writer.TryWrite(new Point[] { new(new(0.0f, 0.0f, 1.0f), new(1.0f, 0.0f, 1.0f, 1.0f)) });
var sensor = new Sensor(pointsChannel.Writer);
Rendering? rendering = null;

var window = Window.Create(WindowOptions.Default with { Title = "KinectPlay", Size = new(1920, 1080), VSync = false, });

window.Load += () =>
{
    rendering = new Rendering(GL.GetApi(window), pointsChannel.Reader);
    rendering.ResizeCamera(window.Size, sensor.HorizontalFov);
};
window.Render += deltaTime => rendering?.Render((float)deltaTime, sensor.Head);
window.Resize += size => rendering?.ResizeCamera(size, sensor.HorizontalFov);

window.Run();
