namespace RCListener.Model
{
    public class RcFrame
    {
        public RcFrame(ushort[] channels)
        {
            Channels = channels;
        }

        public ushort[] Channels { get; }
    }
}