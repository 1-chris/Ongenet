namespace Ongenet.App.Controls.Engine3D
{
    /// <summary>A CPU pixel frame (BGRA8888, premultiplied) produced by the render loop's triple buffer.</summary>
    internal sealed class FrameBuffer
    {
        public byte[] Pixels = System.Array.Empty<byte>();
        public int Width;
        public int Height;
        public int Stride;

        public void EnsureCapacity(int bytes)
        {
            if (Pixels.Length < bytes) Pixels = new byte[bytes];
        }
    }
}
