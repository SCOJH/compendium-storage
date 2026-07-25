// -----------------------------------------------------------------------
// <copyright file="HttpResponseStreamTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using Compendium.Adapters.Supabase.Storage;

namespace Compendium.Adapters.Supabase.Tests.Storage;

public class HttpResponseStreamTests
{
    [Fact]
    public void Ctor_NullInner_Throws()
    {
        var act = () => new HttpResponseStream(null!, new HttpResponseMessage());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_NullResponse_Throws()
    {
        using var inner = new MemoryStream();

        var act = () => new HttpResponseStream(inner, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadSeekAndProbes_DelegateToInner()
    {
        var payload = Encoding.UTF8.GetBytes("compendium");
        var inner = new MemoryStream(payload);
        using var response = new HttpResponseMessage();
        var stream = new HttpResponseStream(inner, response);

        stream.CanRead.Should().BeTrue();
        stream.CanSeek.Should().BeTrue();
        stream.CanWrite.Should().BeFalse();
        stream.Length.Should().Be(payload.Length);

        var buffer = new byte[4];
        stream.Read(buffer, 0, 4).Should().Be(4);
        Encoding.UTF8.GetString(buffer).Should().Be("comp");

        stream.Seek(0, SeekOrigin.Begin).Should().Be(0);
        stream.Position.Should().Be(0);
        stream.Position = 2;
        stream.Position.Should().Be(2);
        stream.Flush(); // no-op, must not throw
    }

    [Fact]
    public void WriteAndSetLength_Throw()
    {
        using var inner = new MemoryStream();
        using var response = new HttpResponseMessage();
        var stream = new HttpResponseStream(inner, response);

        stream.Invoking(s => s.Write([1], 0, 1)).Should().Throw<NotSupportedException>();
        stream.Invoking(s => s.SetLength(1)).Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Dispose_DisposesInnerStream()
    {
        var inner = new TrackingStream();
        var stream = new HttpResponseStream(inner, new HttpResponseMessage());

        stream.Dispose();

        inner.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_DisposesInnerStream()
    {
        var inner = new TrackingStream();
        var stream = new HttpResponseStream(inner, new HttpResponseMessage());

        await stream.DisposeAsync();

        inner.IsDisposed.Should().BeTrue();
    }

    private sealed class TrackingStream : MemoryStream
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
