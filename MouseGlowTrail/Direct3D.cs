using System.Runtime.InteropServices;

namespace MouseGlowTrail;

// Direct3D 11 / DXGI / DirectComposition interop without the built-in COM marshaller (which trimming
// removes): interface pointers are plain void*, methods are called through their vtable slots.
// Slot numbers come from the Windows SDK 10.0.26100 headers (d3d11.h, dxgi.h, dcomp.h).

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_BUFFER_DESC
{
    public uint ByteWidth;
    public uint Usage;
    public uint BindFlags;
    public uint CpuAccessFlags;
    public uint MiscFlags;
    public uint StructureByteStride;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct D3D11_SUBRESOURCE_DATA
{
    public void* SysMem;
    public uint SysMemPitch;
    public uint SysMemSlicePitch;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_TEXTURE2D_DESC
{
    public uint Width;
    public uint Height;
    public uint MipLevels;
    public uint ArraySize;
    public uint Format;
    public uint SampleCount;
    public uint SampleQuality;
    public uint Usage;
    public uint BindFlags;
    public uint CpuAccessFlags;
    public uint MiscFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct D3D11_INPUT_ELEMENT_DESC
{
    public byte* SemanticName;
    public uint SemanticIndex;
    public uint Format;
    public uint InputSlot;
    public uint AlignedByteOffset;
    public uint InputSlotClass;
    public uint InstanceDataStepRate;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_DEPTH_STENCILOP_DESC
{
    public uint StencilFailOp;
    public uint StencilDepthFailOp;
    public uint StencilPassOp;
    public uint StencilFunc;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_DEPTH_STENCIL_DESC
{
    public int DepthEnable;
    public uint DepthWriteMask;
    public uint DepthFunc;
    public int StencilEnable;
    public byte StencilReadMask;
    public byte StencilWriteMask;
    public D3D11_DEPTH_STENCILOP_DESC FrontFace;
    public D3D11_DEPTH_STENCILOP_DESC BackFace;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_RASTERIZER_DESC
{
    public uint FillMode;
    public uint CullMode;
    public int FrontCounterClockwise;
    public int DepthBias;
    public float DepthBiasClamp;
    public float SlopeScaledDepthBias;
    public int DepthClipEnable;
    public int ScissorEnable;
    public int MultisampleEnable;
    public int AntialiasedLineEnable;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_VIEWPORT
{
    public float TopLeftX;
    public float TopLeftY;
    public float Width;
    public float Height;
    public float MinDepth;
    public float MaxDepth;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct D3D11_MAPPED_SUBRESOURCE
{
    public void* Data;
    public uint RowPitch;
    public uint DepthPitch;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_BOX
{
    public uint Left;
    public uint Top;
    public uint Front;
    public uint Right;
    public uint Bottom;
    public uint Back;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DXGI_ADAPTER_DESC
{
    public fixed char Description[128];
    public uint VendorId;
    public uint DeviceId;
    public uint SubSysId;
    public uint Revision;
    public nuint DedicatedVideoMemory;
    public nuint DedicatedSystemMemory;
    public nuint SharedSystemMemory;
    public long AdapterLuid;
}

internal static unsafe class D3D
{
    public const uint D3D_DRIVER_TYPE_HARDWARE = 1;
    public const uint D3D11_CREATE_DEVICE_PREVENT_INTERNAL_THREADING_OPTIMIZATIONS = 0x8;
    public const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    public const uint D3D11_SDK_VERSION = 7;
    public const uint D3D_FEATURE_LEVEL_10_0 = 0xA000;
    public const uint D3D_FEATURE_LEVEL_10_1 = 0xA100;
    public const uint D3D_FEATURE_LEVEL_11_0 = 0xB000;

    public const uint DXGI_FORMAT_R32G32B32A32_FLOAT = 2;
    public const uint DXGI_FORMAT_R32G32_FLOAT = 16;
    public const uint DXGI_FORMAT_D16_UNORM = 55;
    public const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    public const uint DXGI_ALPHA_MODE_PREMULTIPLIED = 1;

    public const uint D3D11_USAGE_DEFAULT = 0;
    public const uint D3D11_USAGE_IMMUTABLE = 1;
    public const uint D3D11_USAGE_DYNAMIC = 2;
    public const uint D3D11_USAGE_STAGING = 3;
    public const uint D3D11_BIND_VERTEX_BUFFER = 0x1;
    public const uint D3D11_BIND_CONSTANT_BUFFER = 0x4;
    public const uint D3D11_BIND_SHADER_RESOURCE = 0x8;
    public const uint D3D11_BIND_RENDER_TARGET = 0x20;
    public const uint D3D11_BIND_DEPTH_STENCIL = 0x40;
    public const uint D3D11_CPU_ACCESS_WRITE = 0x10000;
    public const uint D3D11_CPU_ACCESS_READ = 0x20000;
    public const uint D3D11_MAP_READ = 1;
    public const uint D3D11_MAP_WRITE_DISCARD = 4;
    public const uint D3D11_INPUT_PER_INSTANCE_DATA = 1;
    public const uint D3D11_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP = 5;
    public const uint D3D11_CLEAR_DEPTH = 0x1;
    public const uint D3D11_COMPARISON_GREATER = 5;
    public const uint D3D11_COMPARISON_ALWAYS = 8;
    public const uint D3D11_STENCIL_OP_KEEP = 1;
    public const uint D3D11_DEPTH_WRITE_MASK_ALL = 1;
    public const uint D3D11_FILL_SOLID = 3;
    public const uint D3D11_CULL_NONE = 1;

    public static readonly Guid IID_IDXGIDevice = new(0x54ec77fa, 0x1377, 0x44e6, 0x8c, 0x32, 0x88, 0xfd, 0x5f, 0x44, 0xc8, 0x4c);
    public static readonly Guid IID_ID3D11Texture2D = new(0x6f15aaf2, 0xd208, 0x4e89, 0x9a, 0xb4, 0x48, 0x95, 0x35, 0xd3, 0x4f, 0x9c);
    public static readonly Guid IID_IDCompositionDevice = new(0xC37EA93A, 0xE7AA, 0x450D, 0xB1, 0x6F, 0x97, 0x46, 0xCB, 0x04, 0x07, 0xF3);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    public static extern int D3D11CreateDevice(void* adapter, uint driverType, IntPtr software, uint flags,
        uint* featureLevels, uint featureLevelCount, uint sdkVersion, void** device, uint* featureLevel,
        void** context);

    [DllImport("dcomp.dll", ExactSpelling = true)]
    public static extern int DCompositionCreateDevice(void* dxgiDevice, Guid* iid, void** device);

    private static void* Slot(void* self, int index) => (*(void***)self)[index];

    // IUnknown
    public static int QueryInterface(void* self, Guid iid, void** result) =>
        ((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)Slot(self, 0))(self, &iid, result);

    public static void Release(ref void* self)
    {
        if (self != null)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint>)Slot(self, 2))(self);
            self = null;
        }
    }

    // ID3D11Device
    public static int CreateBuffer(void* device, D3D11_BUFFER_DESC* desc, D3D11_SUBRESOURCE_DATA* data, void** buffer) =>
        ((delegate* unmanaged[Stdcall]<void*, D3D11_BUFFER_DESC*, D3D11_SUBRESOURCE_DATA*, void**, int>)Slot(device, 3))(device, desc, data, buffer);

    public static int CreateTexture2D(void* device, D3D11_TEXTURE2D_DESC* desc, D3D11_SUBRESOURCE_DATA* data, void** texture) =>
        ((delegate* unmanaged[Stdcall]<void*, D3D11_TEXTURE2D_DESC*, D3D11_SUBRESOURCE_DATA*, void**, int>)Slot(device, 5))(device, desc, data, texture);

    public static int CreateShaderResourceView(void* device, void* resource, void** view) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, void**, int>)Slot(device, 7))(device, resource, null, view);

    public static int CreateRenderTargetView(void* device, void* resource, void** view) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, void**, int>)Slot(device, 9))(device, resource, null, view);

    public static int CreateDepthStencilView(void* device, void* resource, void** view) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, void**, int>)Slot(device, 10))(device, resource, null, view);

    public static int CreateInputLayout(void* device, D3D11_INPUT_ELEMENT_DESC* elements, uint count, byte* bytecode, nuint length, void** layout) =>
        ((delegate* unmanaged[Stdcall]<void*, D3D11_INPUT_ELEMENT_DESC*, uint, byte*, nuint, void**, int>)Slot(device, 11))(device, elements, count, bytecode, length, layout);

    public static int CreateVertexShader(void* device, byte* bytecode, nuint length, void** shader) =>
        ((delegate* unmanaged[Stdcall]<void*, byte*, nuint, void*, void**, int>)Slot(device, 12))(device, bytecode, length, null, shader);

    public static int CreatePixelShader(void* device, byte* bytecode, nuint length, void** shader) =>
        ((delegate* unmanaged[Stdcall]<void*, byte*, nuint, void*, void**, int>)Slot(device, 15))(device, bytecode, length, null, shader);

    public static int CreateDepthStencilState(void* device, D3D11_DEPTH_STENCIL_DESC* desc, void** state) =>
        ((delegate* unmanaged[Stdcall]<void*, D3D11_DEPTH_STENCIL_DESC*, void**, int>)Slot(device, 21))(device, desc, state);

    public static int CreateRasterizerState(void* device, D3D11_RASTERIZER_DESC* desc, void** state) =>
        ((delegate* unmanaged[Stdcall]<void*, D3D11_RASTERIZER_DESC*, void**, int>)Slot(device, 22))(device, desc, state);

    public static int GetDeviceRemovedReason(void* device) =>
        ((delegate* unmanaged[Stdcall]<void*, int>)Slot(device, 39))(device);

    // ID3D11DeviceContext
    public static void VSSetConstantBuffers(void* context, uint slot, void* buffer) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, uint, void**, void>)Slot(context, 7))(context, slot, 1, &buffer);

    public static void PSSetShaderResources(void* context, uint slot, void* view) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, uint, void**, void>)Slot(context, 8))(context, slot, 1, &view);

    public static void PSSetShader(void* context, void* shader) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void**, uint, void>)Slot(context, 9))(context, shader, null, 0);

    public static void VSSetShader(void* context, void* shader) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void**, uint, void>)Slot(context, 11))(context, shader, null, 0);

    public static int Map(void* context, void* resource, uint mapType, D3D11_MAPPED_SUBRESOURCE* mapped) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, uint, uint, uint, D3D11_MAPPED_SUBRESOURCE*, int>)Slot(context, 14))(context, resource, 0, mapType, 0, mapped);

    public static void Unmap(void* context, void* resource) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, uint, void>)Slot(context, 15))(context, resource, 0);

    public static void PSSetConstantBuffers(void* context, uint slot, void* buffer) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, uint, void**, void>)Slot(context, 16))(context, slot, 1, &buffer);

    public static void IASetInputLayout(void* context, void* layout) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void>)Slot(context, 17))(context, layout);

    public static void IASetVertexBuffer(void* context, void* buffer, uint stride)
    {
        uint offset = 0;
        ((delegate* unmanaged[Stdcall]<void*, uint, uint, void**, uint*, uint*, void>)Slot(context, 18))(context, 0, 1, &buffer, &stride, &offset);
    }

    public static void DrawInstanced(void* context, uint vertexCount, uint instanceCount) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, uint, uint, uint, void>)Slot(context, 21))(context, vertexCount, instanceCount, 0, 0);

    public static void IASetPrimitiveTopology(void* context, uint topology) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, void>)Slot(context, 24))(context, topology);

    public static void OMSetRenderTarget(void* context, void* renderTarget, void* depthStencil) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, void**, void*, void>)Slot(context, 33))(context, 1, &renderTarget, depthStencil);

    public static void OMSetDepthStencilState(void* context, void* state) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, uint, void>)Slot(context, 36))(context, state, 0);

    public static void RSSetState(void* context, void* state) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void>)Slot(context, 43))(context, state);

    public static void RSSetViewport(void* context, D3D11_VIEWPORT* viewport) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, D3D11_VIEWPORT*, void>)Slot(context, 44))(context, 1, viewport);

    public static void CopySubresourceRegion(void* context, void* destination, uint x, uint y, void* source, D3D11_BOX* box) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, uint, uint, uint, uint, void*, uint, D3D11_BOX*, void>)Slot(context, 46))(context, destination, 0, x, y, 0, source, 0, box);

    public static void CopyResource(void* context, void* destination, void* source) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, void>)Slot(context, 47))(context, destination, source);

    public static void ClearRenderTargetView(void* context, void* view, float* color) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, float*, void>)Slot(context, 50))(context, view, color);

    public static void ClearDepthStencilView(void* context, void* view, float depth) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, uint, float, byte, void>)Slot(context, 53))(context, view, D3D11_CLEAR_DEPTH, depth, 0);

    public static void ClearState(void* context) =>
        ((delegate* unmanaged[Stdcall]<void*, void>)Slot(context, 110))(context);

    public static void Flush(void* context) =>
        ((delegate* unmanaged[Stdcall]<void*, void>)Slot(context, 111))(context);

    // IDXGIDevice / IDXGIAdapter
    public static int GetAdapter(void* dxgiDevice, void** adapter) =>
        ((delegate* unmanaged[Stdcall]<void*, void**, int>)Slot(dxgiDevice, 7))(dxgiDevice, adapter);

    public static int GetDesc(void* adapter, DXGI_ADAPTER_DESC* desc) =>
        ((delegate* unmanaged[Stdcall]<void*, DXGI_ADAPTER_DESC*, int>)Slot(adapter, 8))(adapter, desc);

    // IDCompositionDevice
    public static int Commit(void* device) =>
        ((delegate* unmanaged[Stdcall]<void*, int>)Slot(device, 3))(device);

    public static int CreateTargetForHwnd(void* device, IntPtr hwnd, bool topmost, void** target) =>
        ((delegate* unmanaged[Stdcall]<void*, IntPtr, int, void**, int>)Slot(device, 6))(device, hwnd, topmost ? 1 : 0, target);

    public static int CreateVisual(void* device, void** visual) =>
        ((delegate* unmanaged[Stdcall]<void*, void**, int>)Slot(device, 7))(device, visual);

    public static int CreateVirtualSurface(void* device, uint width, uint height, uint format, uint alphaMode, void** surface) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, uint, uint, uint, void**, int>)Slot(device, 9))(device, width, height, format, alphaMode, surface);

    public static int CheckDeviceState(void* device, int* valid) =>
        ((delegate* unmanaged[Stdcall]<void*, int*, int>)Slot(device, 26))(device, valid);

    // IDCompositionTarget
    public static int SetRoot(void* target, void* visual) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, int>)Slot(target, 3))(target, visual);

    // IDCompositionVisual. SetOffsetX/Y, SetTransform and SetClip are overloaded, which MSVC lays out in
    // reverse declaration order; SetContent is the 13th method either way.
    public static int SetContent(void* visual, void* content) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, int>)Slot(visual, 15))(visual, content);

    // IDCompositionSurface / IDCompositionVirtualSurface
    public static int BeginDraw(void* surface, RECT* update, Guid iid, void** updateObject, POINT* offset) =>
        ((delegate* unmanaged[Stdcall]<void*, RECT*, Guid*, void**, POINT*, int>)Slot(surface, 3))(surface, update, &iid, updateObject, offset);

    public static int EndDraw(void* surface) =>
        ((delegate* unmanaged[Stdcall]<void*, int>)Slot(surface, 4))(surface);

    public static int Resize(void* surface, uint width, uint height) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, uint, int>)Slot(surface, 8))(surface, width, height);

    public static int Trim(void* surface, RECT* rectangles, uint count) =>
        ((delegate* unmanaged[Stdcall]<void*, RECT*, uint, int>)Slot(surface, 9))(surface, rectangles, count);
}
