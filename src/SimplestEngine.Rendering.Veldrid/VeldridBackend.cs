using System.Numerics;
using System.Runtime.InteropServices;
using SimplestEngine.Abi;
using SimplestEngine.Servers;
using Veldrid;
using Veldrid.SPIRV;

namespace SimplestEngine.Rendering.Veldrid;

/// <summary>
/// Veldrid 2D backend. Translates the engine's RenderCommand stream into
/// Veldrid draw calls. Batches by texture / blend mode.
/// </summary>
public sealed class VeldridBackend : IRenderingBackend, IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public Vector2 Position;
        public Vector2 UV;
        public RgbaFloat Color;
        public Vertex(Vector2 p, Vector2 uv, RgbaFloat c) { Position = p; UV = uv; Color = c; }
    }

    // Per-batch quad cap (a flush is forced when reached).
    public const int MaxQuadsPerBatch = 4096;
    // Total quads the VBO can hold for the whole frame. Each FlushBatch appends
    // into this buffer at an increasing offset; a typical UI-heavy scene with
    // bitmap-font text can spend 2-3k quads on labels alone, so we leave headroom.
    public const int MaxQuadsPerFrame = 16384;
    private const int VerticesPerQuad = 4;
    private const int IndicesPerQuad = 6;

    private readonly GraphicsDevice _gd;
    private readonly CommandList _cl;

    private readonly DeviceBuffer _vbo;
    private readonly DeviceBuffer _ibo;
    private readonly DeviceBuffer _projBuf;
    private readonly Pipeline _pipeline;
    private readonly Sampler _sampler;
    private readonly ResourceLayout _projLayout;
    private readonly ResourceLayout _texLayout;
    private readonly ResourceSet _projSet;

    private readonly RidAllocator<TextureEntry> _textures = new();
    private readonly Dictionary<Rid, ResourceSet> _textureSets = new();
    private readonly Texture _whiteTex;
    private readonly TextureView _whiteView;
    private readonly Rid _whiteRid;

    // CPU staging for the current pending batch only. Flush writes this into the
    // GPU-side VBO at offset = _quadsFlushedThisFrame * VerticesPerQuad.
    private readonly Vertex[] _vertices = new Vertex[MaxQuadsPerBatch * VerticesPerQuad];
    // IBO stays per-batch-relative (indices 0..MaxQuadsPerBatch*4-1); DrawIndexed's
    // vertexOffset shifts each batch onto its slice of the larger VBO.
    private readonly ushort[] _indices = new ushort[MaxQuadsPerBatch * IndicesPerQuad];

    private int _quadCount;
    private Rid _currentTex;
    // Total quads already written to the VBO this frame. Each FlushBatch appends
    // at this offset (instead of overwriting from 0) - otherwise GraphicsDevice
    // UpdateBuffer's deferred semantics let later batches stomp earlier ones, and
    // every DrawIndexed ends up reading the LAST batch's vertex data.
    private int _quadsFlushedThisFrame;

    public VeldridBackend(GraphicsDevice gd)
    {
        _gd = gd;
        _cl = gd.ResourceFactory.CreateCommandList();

        // Index buffer is constant for the whole frame: triangles 0,1,2 / 0,2,3
        // per quad. DrawIndexed uses vertexOffset to remap the relative indices
        // into the per-batch slice of the VBO, so the IBO is laid out as if every
        // batch always starts at vertex 0.
        for (int q = 0; q < MaxQuadsPerBatch; q++)
        {
            int vi = q * VerticesPerQuad;
            int ii = q * IndicesPerQuad;
            _indices[ii + 0] = (ushort)(vi + 0);
            _indices[ii + 1] = (ushort)(vi + 1);
            _indices[ii + 2] = (ushort)(vi + 2);
            _indices[ii + 3] = (ushort)(vi + 0);
            _indices[ii + 4] = (ushort)(vi + 2);
            _indices[ii + 5] = (ushort)(vi + 3);
        }

        var factory = gd.ResourceFactory;

        _vbo = factory.CreateBuffer(new BufferDescription(
            (uint)(MaxQuadsPerFrame * VerticesPerQuad * Marshal.SizeOf<Vertex>()),
            BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _ibo = factory.CreateBuffer(new BufferDescription(
            (uint)(_indices.Length * sizeof(ushort)), BufferUsage.IndexBuffer));
        gd.UpdateBuffer(_ibo, 0, _indices);

        _projBuf = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        // Veldrid.SPIRV: vertex element names must match GLSL `in` identifiers (see Veldrid
        // getting-started Part 2). All semantics use TextureCoordinate for SPIRV backends.
        var vertLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

        var shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(CanvasShaders.VertexCode), "main"),
            new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(CanvasShaders.FragmentCode), "main"));

        _projLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Projection", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
        _texLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("u_Texture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("u_Sampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, false, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _projLayout, _texLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertLayout }, shaders),
            Outputs = gd.MainSwapchain.Framebuffer.OutputDescription,
        });

        _sampler = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerFilter.MinPoint_MagPoint_MipPoint, null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));

        _projSet = factory.CreateResourceSet(new ResourceSetDescription(_projLayout, _projBuf));

        // pre-create 1x1 white texture used for non-textured rects
        _whiteTex = factory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        _gd.UpdateTexture(_whiteTex, new byte[] { 255, 255, 255, 255 }, 0, 0, 0, 1, 1, 1, 0, 0);
        _whiteView = factory.CreateTextureView(_whiteTex);
        _whiteRid = _textures.Allocate(new TextureEntry { Tex = _whiteTex, View = _whiteView, Width = 1, Height = 1 });
        _textureSets[_whiteRid] = factory.CreateResourceSet(new ResourceSetDescription(_texLayout, _whiteView, _sampler));
    }

    public Vector2i GetSize() => new((int)_gd.MainSwapchain.Framebuffer.Width, (int)_gd.MainSwapchain.Framebuffer.Height);

    public void Resize(int width, int height)
    {
        _gd.ResizeMainWindow((uint)width, (uint)height);
    }

    public void BeginFrame(global::SimplestEngine.Color clearColor)
    {
        _cl.Begin();
        _cl.SetFramebuffer(_gd.MainSwapchain.Framebuffer);
        _cl.ClearColorTarget(0, new RgbaFloat(clearColor.R, clearColor.G, clearColor.B, clearColor.A));

        // orthographic projection: top-left origin
        var size = GetSize();
        var proj = Matrix4x4.CreateOrthographicOffCenter(0, size.X, size.Y, 0, -1, 1);
        _gd.UpdateBuffer(_projBuf, 0, proj);

        _cl.SetPipeline(_pipeline);
        _cl.SetVertexBuffer(0, _vbo);
        _cl.SetIndexBuffer(_ibo, IndexFormat.UInt16);
        _cl.SetGraphicsResourceSet(0, _projSet);

        _quadCount = 0;
        _currentTex = default;
        _quadsFlushedThisFrame = 0;
    }

    public void Submit(IRenderingServer src)
    {
        if (src is not RenderingServerDefault srv) return;

        // Snapshot items, sort by z-index for back-to-front.
        var items = srv.EnumerateItems().OrderBy(x => x.data.ZIndex).ToList();
        foreach (var (_, data) in items)
        {
            foreach (var cmd in data.Commands)
            {
                switch (cmd.Type)
                {
                    case RenderCommandType.Rect:
                        AddTexturedQuadWorldXform(_whiteRid, cmd.Rect, new Rect2(0, 0, 1, 1), cmd.Color, data.Transform);
                        break;
                    case RenderCommandType.TextureRect:
                        AddTexturedQuadWorldXform(cmd.Texture, cmd.Rect, NormalizeSrc(cmd.Texture, cmd.SrcRect), cmd.Color, data.Transform);
                        break;
                    case RenderCommandType.Line:
                        AddLine(cmd.Rect.Position, cmd.Rect.Position + cmd.Rect.Size, cmd.Color, cmd.Width <= 0 ? 1f : cmd.Width, data.Transform);
                        break;
                    case RenderCommandType.Circle:
                        AddCircle(cmd.Rect.Position, cmd.Rect.Size.X, cmd.Color, data.Transform);
                        break;
                }
            }
        }
        FlushBatch();
    }

    private Rect2 NormalizeSrc(Rid texRid, Rect2 src)
    {
        if (!_textures.TryGet(texRid, out var te) || te is null) return src;
        // If src is normalized (<=1) leave it; otherwise convert pixels->uv.
        if (src.Position.X == 0 && src.Position.Y == 0 && src.Size.X <= 1.001f && src.Size.Y <= 1.001f)
            return src;
        return new Rect2(
            new global::SimplestEngine.Vector2(src.Position.X / te.Width, src.Position.Y / te.Height),
            new global::SimplestEngine.Vector2(src.Size.X / te.Width, src.Size.Y / te.Height));
    }

    private void AddTexturedQuadWorldXform(Rid tex, Rect2 dst, Rect2 uvNorm, global::SimplestEngine.Color color, Transform2D xform)
    {
        if (tex != _currentTex && _quadCount > 0) FlushBatch();
        _currentTex = tex.IsValid ? tex : _whiteRid;
        if (_quadCount >= MaxQuadsPerBatch) FlushBatch();

        Vector2 p0 = ToSys(xform.Xform(new global::SimplestEngine.Vector2(dst.Position.X, dst.Position.Y)));
        Vector2 p1 = ToSys(xform.Xform(new global::SimplestEngine.Vector2(dst.Position.X + dst.Size.X, dst.Position.Y)));
        Vector2 p2 = ToSys(xform.Xform(new global::SimplestEngine.Vector2(dst.Position.X + dst.Size.X, dst.Position.Y + dst.Size.Y)));
        Vector2 p3 = ToSys(xform.Xform(new global::SimplestEngine.Vector2(dst.Position.X, dst.Position.Y + dst.Size.Y)));
        Vector2 u0 = new(uvNorm.Position.X, uvNorm.Position.Y);
        Vector2 u1 = new(uvNorm.Position.X + uvNorm.Size.X, uvNorm.Position.Y);
        Vector2 u2 = new(uvNorm.Position.X + uvNorm.Size.X, uvNorm.Position.Y + uvNorm.Size.Y);
        Vector2 u3 = new(uvNorm.Position.X, uvNorm.Position.Y + uvNorm.Size.Y);
        var c = new RgbaFloat(color.R, color.G, color.B, color.A);

        int vi = _quadCount * VerticesPerQuad;
        _vertices[vi + 0] = new Vertex(p0, u0, c);
        _vertices[vi + 1] = new Vertex(p1, u1, c);
        _vertices[vi + 2] = new Vertex(p2, u2, c);
        _vertices[vi + 3] = new Vertex(p3, u3, c);
        _quadCount++;
    }

    private void AddLine(global::SimplestEngine.Vector2 a, global::SimplestEngine.Vector2 b, global::SimplestEngine.Color color, float width, Transform2D xform)
    {
        var dir = b - a;
        var len = dir.Length();
        if (len < 1e-5f) return;
        var perp = new global::SimplestEngine.Vector2(-dir.Y / len, dir.X / len) * (width * 0.5f);
        var dst = new Rect2(a, dir);
        // crude line as a thin quad
        AddTexturedQuadWorldXform(_whiteRid, new Rect2(a + perp * 0, dir + perp * 2), new Rect2(0, 0, 1, 1), color, xform);
    }

    private void AddCircle(global::SimplestEngine.Vector2 center, float r, global::SimplestEngine.Color color, Transform2D xform)
    {
        // crude: render bounding rect; proper polygon impl in M5.5
        AddTexturedQuadWorldXform(_whiteRid, new Rect2(center - new global::SimplestEngine.Vector2(r, r), new global::SimplestEngine.Vector2(r * 2, r * 2)), new Rect2(0, 0, 1, 1), color, xform);
    }

    private void FlushBatch()
    {
        if (_quadCount == 0) return;
        // Hard cap: if we'd write past the end of the frame VBO we silently drop
        // the overflow. M5+ will switch to dynamic VBO growth or ring-buffering.
        int firstVertex = _quadsFlushedThisFrame * VerticesPerQuad;
        if (_quadsFlushedThisFrame + _quadCount > MaxQuadsPerFrame)
        {
            _quadCount = 0;
            return;
        }
        uint vertexBytes = (uint)(_quadCount * VerticesPerQuad * Marshal.SizeOf<Vertex>());
        uint vboByteOffset = (uint)(firstVertex * Marshal.SizeOf<Vertex>());
        // GraphicsDevice.UpdateBuffer is deferred: all updates this frame are
        // applied before any DrawIndexed runs. Writing each batch at its own
        // offset (rather than offset 0 every time) keeps the regions distinct,
        // and DrawIndexed's vertexOffset maps the relative IBO indices onto the
        // correct slice.
        _gd.UpdateBuffer(_vbo, vboByteOffset, _vertices.AsSpan(0, _quadCount * VerticesPerQuad));
        if (!_textureSets.TryGetValue(_currentTex, out var set)) set = _textureSets[_whiteRid];
        _cl.SetGraphicsResourceSet(1, set);
        _cl.DrawIndexed((uint)(_quadCount * IndicesPerQuad), 1, 0, firstVertex, 0);
        _quadsFlushedThisFrame += _quadCount;
        _quadCount = 0;
    }

    public void EndFrame()
    {
        _cl.End();
        _gd.SubmitCommands(_cl);
        _gd.SwapBuffers();
    }

    public Rid CreateTexture(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var factory = _gd.ResourceFactory;
        var tex = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)width, (uint)height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        if (rgba.Length > 0)
            _gd.UpdateTexture(tex, rgba.ToArray(), 0, 0, 0, (uint)width, (uint)height, 1, 0, 0);
        var view = factory.CreateTextureView(tex);
        var rid = _textures.Allocate(new TextureEntry { Tex = tex, View = view, Width = width, Height = height });
        _textureSets[rid] = factory.CreateResourceSet(new ResourceSetDescription(_texLayout, view, _sampler));
        return rid;
    }

    public void DestroyTexture(Rid rid)
    {
        if (_textures.TryGet(rid, out var entry) && entry is not null)
        {
            entry.View?.Dispose();
            entry.Tex?.Dispose();
        }
        _textures.Free(rid);
        if (_textureSets.TryGetValue(rid, out var set))
        {
            set.Dispose();
            _textureSets.Remove(rid);
        }
    }

    public Vector2i GetTextureSize(Rid rid) =>
        _textures.TryGet(rid, out var e) && e is not null ? new Vector2i(e.Width, e.Height) : Vector2i.Zero;

    private static Vector2 ToSys(global::SimplestEngine.Vector2 v) => new(v.X, v.Y);

    public void Dispose()
    {
        foreach (var s in _textureSets.Values) s.Dispose();
        foreach (var (_, e) in _textures.Enumerate())
        {
            if (e is null) continue;
            e.View?.Dispose();
            e.Tex?.Dispose();
        }
        _pipeline.Dispose();
        _projSet.Dispose();
        _projLayout.Dispose();
        _texLayout.Dispose();
        _sampler.Dispose();
        _projBuf.Dispose();
        _vbo.Dispose();
        _ibo.Dispose();
        _cl.Dispose();
    }
}

internal sealed class TextureEntry
{
    public Texture? Tex;
    public TextureView? View;
    public int Width;
    public int Height;
}
