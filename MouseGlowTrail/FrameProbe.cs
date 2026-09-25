namespace MouseGlowTrail;

/// <summary>One pass of the render loop, as seen by the live benchmark.</summary>
internal struct FrameRecord
{
    public double Time;
    public long UpdateTicks;
    public long DrawTicks;
    public long UploadTicks;
    public int Width;
    public int Height;
    public int UploadArea;
    public bool Presented;
    public bool Failed;
}

/// <summary>Collects <see cref="FrameRecord"/>s for the development harness; the product never creates one.</summary>
internal sealed class FrameProbe
{
    private readonly FrameRecord[] _records = new FrameRecord[1 << 16];
    private int _count;

    /// <summary>Native id of the render thread, so its CPU time can be told apart from driver threads.</summary>
    public int RenderThreadId { get; set; }

    public int Count => Volatile.Read(ref _count);

    public ref readonly FrameRecord this[int index] => ref _records[index];

    public void Add(in FrameRecord record)
    {
        if (_count < _records.Length)
        {
            _records[_count] = record;
            Volatile.Write(ref _count, _count + 1);
        }
    }
}
