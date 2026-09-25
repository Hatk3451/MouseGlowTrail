using System.Numerics;
using System.Runtime.InteropServices;
using static MouseGlowTrail.D3D;

namespace MouseGlowTrail;

/// <summary>
/// Direct3D 11 counterpart of <see cref="Canvas"/>: every primitive becomes one instanced quad and
/// Trail.hlsl evaluates the same distance fields, profile tables and dither. A frame covers a region
/// of the screen and is rendered into an offscreen target (with a depth buffer that implements
/// "highest alpha wins"); the presenter then copies it wherever it is displayed.
/// </summary>
internal sealed unsafe class GpuCanvas : IPrimitiveSink, IDisposable
{
    private const uint InstanceStride = 112;
    private const float QuadPadding = 1f;

    private void* _device;
    private void* _context;
    private void* _vertexShader;
    private void* _pixelShader;
    private void* _layout;
    private void* _instanceBuffer;
    private void* _frameBuffer;
    private void* _profiles;
    private void* _profilesView;
    private void* _depthState;
    private void* _rasterizer;
    private void* _target;
    private void* _targetView;
    private void* _depth;
    private void* _depthView;
    private int _instanceCapacity;
    private int _targetWidth;
    private int _targetHeight;
    private int _profileRows;
    private Instance[] _batch = new Instance[512];
    private int _count;
    private PixelBounds _region;

    private GpuCanvas()
    {
    }

    public void* Device => _device;

    public void* Context => _context;

    /// <summary>The offscreen target; the current region occupies its top-left corner.</summary>
    public void* Target => _target;

    public string AdapterName { get; private set; } = "";

    public static GpuCanvas? TryCreate(out string? error)
    {
        var canvas = new GpuCanvas();
        try
        {
            error = canvas.Initialize();
            if (error is null)
            {
                return canvas;
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        canvas.Dispose();
        return null;
    }

    private string? Initialize()
    {
        var levels = stackalloc uint[] { D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_10_0 };
        void* device;
        void* context;
        uint level;
        // A few hundred quads a frame: submitting them on this thread is cheaper than handing them to
        // a driver worker thread (measured: about a third less CPU in total).
        var hr = D3D11CreateDevice(null, D3D_DRIVER_TYPE_HARDWARE, 0,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_PREVENT_INTERNAL_THREADING_OPTIMIZATIONS, levels, 3,
            D3D11_SDK_VERSION, &device, &level, &context);
        if (hr < 0)
        {
            return $"D3D11CreateDevice 0x{hr:X8}";
        }

        _device = device;
        _context = context;

        // Software adapters (Basic Render Driver, remote sessions) rasterise on the CPU anyway, where
        // the dedicated CPU path is far cheaper.
        void* dxgiDevice = null;
        void* adapter = null;
        try
        {
            if (QueryInterface(device, IID_IDXGIDevice, &dxgiDevice) >= 0 && GetAdapter(dxgiDevice, &adapter) >= 0)
            {
                DXGI_ADAPTER_DESC desc;
                if (GetDesc(adapter, &desc) >= 0)
                {
                    AdapterName = new string(desc.Description);
                    if (desc.VendorId == 0x1414)
                    {
                        return $"software adapter ({AdapterName})";
                    }
                }
            }
        }
        finally
        {
            Release(ref adapter);
            Release(ref dxgiDevice);
        }

        fixed (byte* bytecode = Shaders.VertexShader)
        {
            void* shader;
            if ((hr = CreateVertexShader(device, bytecode, (nuint)Shaders.VertexShader.Length, &shader)) < 0)
            {
                return $"CreateVertexShader 0x{hr:X8}";
            }

            _vertexShader = shader;
            if ((hr = CreateLayout(bytecode, Shaders.VertexShader.Length)) < 0)
            {
                return $"CreateInputLayout 0x{hr:X8}";
            }
        }

        fixed (byte* bytecode = Shaders.PixelShader)
        {
            void* shader;
            if ((hr = CreatePixelShader(device, bytecode, (nuint)Shaders.PixelShader.Length, &shader)) < 0)
            {
                return $"CreatePixelShader 0x{hr:X8}";
            }

            _pixelShader = shader;
        }

        if ((hr = CreateProfiles()) < 0)
        {
            return $"profile texture 0x{hr:X8}";
        }

        var frameDesc = new D3D11_BUFFER_DESC
        {
            ByteWidth = 16,
            Usage = D3D11_USAGE_DYNAMIC,
            BindFlags = D3D11_BIND_CONSTANT_BUFFER,
            CpuAccessFlags = D3D11_CPU_ACCESS_WRITE
        };
        void* frame;
        if ((hr = CreateBuffer(device, &frameDesc, null, &frame)) < 0)
        {
            return $"constant buffer 0x{hr:X8}";
        }

        _frameBuffer = frame;

        // Depth holds each pixel's alpha; a fragment lands only when it is strictly brighter.
        var depthDesc = new D3D11_DEPTH_STENCIL_DESC
        {
            DepthEnable = 1,
            DepthWriteMask = D3D11_DEPTH_WRITE_MASK_ALL,
            DepthFunc = D3D11_COMPARISON_GREATER,
            FrontFace = new D3D11_DEPTH_STENCILOP_DESC
            {
                StencilFailOp = D3D11_STENCIL_OP_KEEP, StencilDepthFailOp = D3D11_STENCIL_OP_KEEP,
                StencilPassOp = D3D11_STENCIL_OP_KEEP, StencilFunc = D3D11_COMPARISON_ALWAYS
            },
            BackFace = new D3D11_DEPTH_STENCILOP_DESC
            {
                StencilFailOp = D3D11_STENCIL_OP_KEEP, StencilDepthFailOp = D3D11_STENCIL_OP_KEEP,
                StencilPassOp = D3D11_STENCIL_OP_KEEP, StencilFunc = D3D11_COMPARISON_ALWAYS
            }
        };
        void* depthState;
        if ((hr = CreateDepthStencilState(device, &depthDesc, &depthState)) < 0)
        {
            return $"depth state 0x{hr:X8}";
        }

        _depthState = depthState;

        var rasterizerDesc = new D3D11_RASTERIZER_DESC
        {
            FillMode = D3D11_FILL_SOLID,
            CullMode = D3D11_CULL_NONE,
            DepthClipEnable = 1
        };
        void* rasterizer;
        if ((hr = CreateRasterizerState(device, &rasterizerDesc, &rasterizer)) < 0)
        {
            return $"rasterizer state 0x{hr:X8}";
        }

        _rasterizer = rasterizer;
        return null;
    }

    private int CreateLayout(byte* bytecode, int length)
    {
        fixed (byte* quad = "QUAD\0"u8)
        fixed (byte* param = "PARAM\0"u8)
        fixed (byte* color = "COLOR\0"u8)
        {
            var elements = stackalloc D3D11_INPUT_ELEMENT_DESC[7];
            byte*[] names = [quad, quad, param, param, param, color, color];
            uint[] indices = [0, 1, 0, 1, 2, 0, 1];
            for (var i = 0; i < 7; i++)
            {
                elements[i] = new D3D11_INPUT_ELEMENT_DESC
                {
                    SemanticName = names[i],
                    SemanticIndex = indices[i],
                    Format = DXGI_FORMAT_R32G32B32A32_FLOAT,
                    AlignedByteOffset = (uint)(i * 16),
                    InputSlotClass = D3D11_INPUT_PER_INSTANCE_DATA,
                    InstanceDataStepRate = 1
                };
            }

            void* layout;
            var hr = CreateInputLayout(_device, elements, 7, bytecode, (nuint)length, &layout);
            _layout = layout;
            return hr;
        }
    }

    /// <summary>All profile tables as rows of one float2 texture (coverage, white).</summary>
    private int CreateProfiles()
    {
        // Make sure every table the trail can use exists before the texture is built.
        _ = TrailStyles.All;
        _ = Profiles.Particle;
        var tables = ProfileLut.All;
        _profileRows = tables.Length;
        var texels = new float[ProfileLut.Stride * 2 * tables.Length];
        foreach (var table in tables)
        {
            var row = table.Row * ProfileLut.Stride * 2;
            for (var i = 0; i < ProfileLut.Stride; i++)
            {
                texels[row + i * 2] = table.Alpha[i];
                texels[row + i * 2 + 1] = table.White[i];
            }
        }

        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = ProfileLut.Stride,
            Height = (uint)tables.Length,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT_R32G32_FLOAT,
            SampleCount = 1,
            Usage = D3D11_USAGE_IMMUTABLE,
            BindFlags = D3D11_BIND_SHADER_RESOURCE
        };
        fixed (float* data = texels)
        {
            var initial = new D3D11_SUBRESOURCE_DATA { SysMem = data, SysMemPitch = ProfileLut.Stride * 8 };
            void* texture;
            var hr = CreateTexture2D(_device, &desc, &initial, &texture);
            if (hr < 0)
            {
                return hr;
            }

            _profiles = texture;
            void* view;
            hr = CreateShaderResourceView(_device, texture, &view);
            _profilesView = view;
            return hr;
        }
    }

    /// <summary>Starts a frame covering <paramref name="region"/> (screen pixels).</summary>
    public void BeginFrame(in PixelBounds region)
    {
        if (ProfileLut.Count != _profileRows)
        {
            // A new look (custom colours, glow) brought new profile tables: rebuild the texture.
            Release(ref _profilesView);
            Release(ref _profiles);
            _profileRows = 0;
            CreateProfiles();
        }

        _region = region;
        _count = 0;
    }

    public int InstanceCount => _count;

    /// <summary>Renders the queued primitives into the top-left of <see cref="Target"/>. Negative on failure.</summary>
    public int Render()
    {
        var width = _region.Width;
        var height = _region.Height;
        var hr = EnsureTarget(width, height);
        if (hr < 0)
        {
            return hr;
        }

        var context = _context;
        D3D11_MAPPED_SUBRESOURCE mapped;
        if (_count > 0)
        {
            if ((hr = EnsureInstanceBuffer(_count)) < 0 ||
                (hr = Map(context, _instanceBuffer, D3D11_MAP_WRITE_DISCARD, &mapped)) < 0)
            {
                return hr;
            }

            fixed (Instance* source = _batch)
            {
                Buffer.MemoryCopy(source, mapped.Data, (long)_instanceCapacity * InstanceStride, (long)_count * InstanceStride);
            }

            Unmap(context, _instanceBuffer);
        }

        if ((hr = Map(context, _frameBuffer, D3D11_MAP_WRITE_DISCARD, &mapped)) < 0)
        {
            return hr;
        }

        var constants = (int*)mapped.Data;
        constants[0] = _region.Left;
        constants[1] = _region.Top;
        ((float*)constants)[2] = 2f / width;
        ((float*)constants)[3] = 2f / height;
        Unmap(context, _frameBuffer);

        var clear = stackalloc float[4];
        ClearRenderTargetView(context, _targetView, clear);
        ClearDepthStencilView(context, _depthView, 0f);
        if (_count > 0)
        {
            // State is set in full every frame: DirectComposition shares this device context.
            var viewport = new D3D11_VIEWPORT { Width = width, Height = height, MaxDepth = 1f };
            OMSetRenderTarget(context, _targetView, _depthView);
            OMSetDepthStencilState(context, _depthState);
            RSSetState(context, _rasterizer);
            RSSetViewport(context, &viewport);
            IASetInputLayout(context, _layout);
            IASetPrimitiveTopology(context, D3D11_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP);
            IASetVertexBuffer(context, _instanceBuffer, InstanceStride);
            VSSetShader(context, _vertexShader);
            VSSetConstantBuffers(context, 0, _frameBuffer);
            PSSetShader(context, _pixelShader);
            PSSetConstantBuffers(context, 0, _frameBuffer);
            PSSetShaderResources(context, 0, _profilesView);
            DrawInstanced(context, 4, (uint)_count);
            OMSetRenderTarget(context, null, null);
        }

        return 0;
    }

    /// <summary>Copies the rendered region into CPU memory (tests and previews only).</summary>
    public int ReadPixels(uint* destination)
    {
        var width = _region.Width;
        var height = _region.Height;
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)_targetWidth,
            Height = (uint)_targetHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleCount = 1,
            Usage = D3D11_USAGE_STAGING,
            CpuAccessFlags = D3D11_CPU_ACCESS_READ
        };
        void* staging;
        var hr = CreateTexture2D(_device, &desc, null, &staging);
        if (hr < 0)
        {
            return hr;
        }

        try
        {
            CopyResource(_context, staging, _target);
            D3D11_MAPPED_SUBRESOURCE mapped;
            if ((hr = Map(_context, staging, D3D11_MAP_READ, &mapped)) < 0)
            {
                return hr;
            }

            for (var y = 0; y < height; y++)
            {
                Buffer.MemoryCopy((byte*)mapped.Data + (long)y * mapped.RowPitch, destination + (long)y * width,
                    width * 4L, width * 4L);
            }

            Unmap(_context, staging);
            return 0;
        }
        finally
        {
            Release(ref staging);
        }
    }

    /// <summary>Frees the offscreen target; it is recreated at the next frame.</summary>
    public void ReleaseTarget()
    {
        Release(ref _depthView);
        Release(ref _depth);
        Release(ref _targetView);
        Release(ref _target);
        _targetWidth = _targetHeight = 0;
    }

    private int EnsureTarget(int width, int height)
    {
        if (_target != null && width <= _targetWidth && height <= _targetHeight)
        {
            return 0;
        }

        // Grow with headroom so a trail that is still getting longer does not reallocate every frame.
        width = Math.Max(_targetWidth, (width + width / 4 + 255) & ~255);
        height = Math.Max(_targetHeight, (height + height / 4 + 255) & ~255);
        ReleaseTarget();
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleCount = 1,
            Usage = D3D11_USAGE_DEFAULT,
            BindFlags = D3D11_BIND_RENDER_TARGET
        };
        void* texture;
        void* view;
        var hr = CreateTexture2D(_device, &desc, null, &texture);
        if (hr < 0)
        {
            return hr;
        }

        _target = texture;
        if ((hr = CreateRenderTargetView(_device, texture, &view)) < 0)
        {
            return hr;
        }

        _targetView = view;
        desc.Format = DXGI_FORMAT_D16_UNORM;
        desc.BindFlags = D3D11_BIND_DEPTH_STENCIL;
        if ((hr = CreateTexture2D(_device, &desc, null, &texture)) < 0)
        {
            return hr;
        }

        _depth = texture;
        if ((hr = CreateDepthStencilView(_device, texture, &view)) < 0)
        {
            return hr;
        }

        _depthView = view;
        _targetWidth = width;
        _targetHeight = height;
        return 0;
    }

    private int EnsureInstanceBuffer(int count)
    {
        if (_instanceBuffer != null && count <= _instanceCapacity)
        {
            return 0;
        }

        Release(ref _instanceBuffer);
        var capacity = Math.Max(1024, (int)BitOperations.RoundUpToPowerOf2((uint)count));
        var desc = new D3D11_BUFFER_DESC
        {
            ByteWidth = (uint)capacity * InstanceStride,
            Usage = D3D11_USAGE_DYNAMIC,
            BindFlags = D3D11_BIND_VERTEX_BUFFER,
            CpuAccessFlags = D3D11_CPU_ACCESS_WRITE
        };
        void* buffer;
        var hr = CreateBuffer(_device, &desc, null, &buffer);
        if (hr >= 0)
        {
            _instanceBuffer = buffer;
            _instanceCapacity = capacity;
        }

        return hr;
    }

    private ref Instance Append()
    {
        if (_count == _batch.Length)
        {
            Array.Resize(ref _batch, _batch.Length * 2);
        }

        return ref _batch[_count++];
    }

    public void Capsule(float ax, float ay, float bx, float by, float radius, float fade, uint rgb, ProfileLut lut)
    {
        if (fade <= 0.002f || radius <= 0.01f || lut.Row >= _profileRows)
        {
            return;
        }

        var reach = radius * lut.Extent;
        ref var instance = ref Append();
        var ex = bx - ax;
        var ey = by - ay;
        var lengthSquared = ex * ex + ey * ey;
        SegmentQuad(ref instance, ax, ay, ex, ey, lengthSquared, reach);
        instance.Kind = 0;
        instance.Row = lut.Row;
        instance.Param0 = new Float4(ax, ay, ex, ey);
        var indexScale = lut.IndexScale / (radius * radius);
        instance.Param1 = new Float4(lengthSquared > 1e-6f ? 1f / lengthSquared : 0f, reach * reach, indexScale, 0f);
        instance.Param2 = new Float4(fade * 255f, 0f, -RibbonLimits.Unbounded, RibbonLimits.Unbounded);
        instance.Color0 = Rgb(rgb);
        instance.Color1 = default;
    }

    public void Ribbon(float ax, float ay, float bx, float by, float radiusA, float radiusB, float fadeA, float fadeB,
        uint rgbA, uint rgbB, ProfileLut lut, bool capStart, bool capEnd, float nextX, float nextY)
    {
        var maxFade = MathF.Max(fadeA, fadeB);
        var maxRadius = MathF.Max(radiusA, radiusB);
        if (maxFade <= 0.002f || maxRadius <= 0.01f || lut.Row >= _profileRows)
        {
            return;
        }

        radiusA = MathF.Max(radiusA, 0.05f);
        radiusB = MathF.Max(radiusB, 0.05f);
        var reach = maxRadius * lut.ReachFor(maxFade);
        ref var instance = ref Append();
        var ex = bx - ax;
        var ey = by - ay;
        var lengthSquared = ex * ex + ey * ey;
        SegmentQuad(ref instance, ax, ay, ex, ey, lengthSquared, reach);
        instance.Kind = 0;
        instance.Row = lut.Row;
        instance.Param0 = new Float4(ax, ay, ex, ey);
        var indexScaleA = lut.IndexScale / (radiusA * radiusA);
        instance.Param1 = new Float4(lengthSquared > 1e-6f ? 1f / lengthSquared : 0f, reach * reach, indexScaleA,
            lut.IndexScale / (radiusB * radiusB) - indexScaleA);
        var limits = RibbonLimits.Compute(ex, ey, capStart, capEnd, nextX, nextY);
        instance.Param2 = new Float4(fadeA * 255f, (fadeB - fadeA) * 255f, limits.LowT, limits.HighT);
        var colorA = Rgb(rgbA);
        var colorB = Rgb(rgbB);
        instance.Color0 = new Float4(colorA.X, colorA.Y, colorA.Z, limits.NextX);
        instance.Color1 = new Float4(colorB.X - colorA.X, colorB.Y - colorA.Y, colorB.Z - colorA.Z, limits.NextY);
    }

    public void Ring(float cx, float cy, float radius, float thickness, float fade, uint rgb, float white)
    {
        if (fade <= 0.002f || thickness <= 0.05f)
        {
            return;
        }

        var reach = radius + thickness * 2.6f;
        var inner = MathF.Max(0f, radius - thickness * 2.6f);
        ref var instance = ref Append();
        SquareQuad(ref instance, cx, cy, reach);
        instance.Kind = 2;
        instance.Row = 0;
        instance.Param0 = new Float4(cx, cy, radius, 1f / thickness);
        instance.Param1 = new Float4(inner * inner, reach * reach, fade * 255f, 0f);
        var color = Rgb(rgb);
        instance.Color0 = new Float4(color.X + (255f - color.X) * white, color.Y + (255f - color.Y) * white,
            color.Z + (255f - color.Z) * white, 0f);
        instance.Param2 = default;
        instance.Color1 = default;
    }

    public void Sparkle(float cx, float cy, float size, float fade, uint rgb, float white)
    {
        if (fade <= 0.002f || size <= 0.05f)
        {
            return;
        }

        ref var instance = ref Append();
        SquareQuad(ref instance, cx, cy, size * 3.2f);
        instance.Kind = 1;
        instance.Row = 0;
        instance.Param0 = new Float4(cx, cy, 0f, 0f);
        instance.Param1 = new Float4(1f / size, fade * 255f, white, 0f);
        instance.Color0 = Rgb(rgb);
        instance.Param2 = default;
        instance.Color1 = default;
    }

    public void Shape(float cx, float cy, float size, float angle, float fade, uint rgb, float white, ParticleShape shape)
    {
        if (fade <= 0.002f || size <= 0.05f)
        {
            return;
        }

        ref var instance = ref Append();
        SquareQuad(ref instance, cx, cy, size * FrameComposer.ShapeReach);
        instance.Kind = 3;
        instance.Row = 0;
        var inverseSize = 1f / size;
        instance.Param0 = new Float4(cx, cy, MathF.Cos(angle) * inverseSize, MathF.Sin(angle) * inverseSize);
        instance.Param1 = new Float4(size, fade * 255f, white, (float)shape);
        instance.Color0 = Rgb(rgb);
        instance.Param2 = default;
        instance.Color1 = default;
    }

    /// <summary>An oriented box around a segment and its reach (a square for a dot).</summary>
    private static void SegmentQuad(ref Instance instance, float ax, float ay, float ex, float ey, float lengthSquared,
        float reach)
    {
        var pad = reach + QuadPadding;
        if (lengthSquared <= 1e-6f)
        {
            instance.Quad0 = new Float4(ax - pad, ay - pad, 2f * pad, 0f);
            instance.VX = 0f;
            instance.VY = 2f * pad;
            return;
        }

        var length = MathF.Sqrt(lengthSquared);
        var dx = ex / length;
        var dy = ey / length;
        instance.Quad0 = new Float4(ax - (dx - dy) * pad, ay - (dy + dx) * pad, dx * (length + 2f * pad),
            dy * (length + 2f * pad));
        instance.VX = -dy * 2f * pad;
        instance.VY = dx * 2f * pad;
    }

    /// <summary>The same whole-pixel square the CPU clips to: floor(c - reach) .. ceil(c + reach), inclusive.</summary>
    private static void SquareQuad(ref Instance instance, float cx, float cy, float reach)
    {
        var x0 = MathF.Floor(cx - reach);
        var y0 = MathF.Floor(cy - reach);
        var x1 = MathF.Ceiling(cx + reach) + 1f;
        var y1 = MathF.Ceiling(cy + reach) + 1f;
        instance.Quad0 = new Float4(x0, y0, x1 - x0, 0f);
        instance.VX = 0f;
        instance.VY = y1 - y0;
    }

    private static Float4 Rgb(uint rgb) => new((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255, 0f);

    public void Dispose()
    {
        if (_context != null)
        {
            ClearState(_context);
            Flush(_context);
        }

        ReleaseTarget();
        Release(ref _instanceBuffer);
        Release(ref _frameBuffer);
        Release(ref _profilesView);
        Release(ref _profiles);
        Release(ref _depthState);
        Release(ref _rasterizer);
        Release(ref _layout);
        Release(ref _pixelShader);
        Release(ref _vertexShader);
        Release(ref _context);
        Release(ref _device);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Float4(float x, float y, float z, float w)
    {
        public float X = x;
        public float Y = y;
        public float Z = z;
        public float W = w;
    }

    /// <summary>Matches the QUAD0..COLOR1 input layout in Trail.hlsl (seven float4, 112 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Instance
    {
        public Float4 Quad0;
        public float VX;
        public float VY;
        public float Kind;
        public float Row;
        public Float4 Param0;
        public Float4 Param1;
        public Float4 Param2;
        public Float4 Color0;
        public Float4 Color1;
    }
}
