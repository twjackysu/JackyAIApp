namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// Holds every response through it to a size, for servers a reader named (their MCP server): a body longer than
    /// <paramref name="maxBytes"/> throws <see cref="ResponseTooLargeException"/> as soon as it says so (its
    /// <c>Content-Length</c>) or as soon as that much has been read, so one answer that never ends cannot fill the
    /// app's memory within the request's time. Read as it arrives, so a streamed answer (SSE) still streams.
    /// </summary>
    public sealed class ResponseSizeLimit(HttpMessageHandler inner, long maxBytes) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            var original = response.Content;
            if (original.Headers.ContentLength > maxBytes)
            {
                response.Dispose();
                throw new ResponseTooLargeException(maxBytes);
            }

            var limited = new StreamContent(new LimitedReadStream(await original.ReadAsStreamAsync(ct), maxBytes));
            foreach (var header in original.Headers)
                limited.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content = limited;
            return response;
        }

        /// <summary>A body read only up to a number of bytes; one more throws.</summary>
        internal sealed class LimitedReadStream(Stream inner, long maxBytes) : Stream
        {
            private long _read;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

            public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
                Count(await inner.ReadAsync(buffer.AsMemory(offset, count), ct));

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
                Count(await inner.ReadAsync(buffer, ct));

            private int Count(int read)
            {
                _read += read;
                if (_read > maxBytes) throw new ResponseTooLargeException(maxBytes);
                return read;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }

    /// <summary>A response was longer than <see cref="ResponseSizeLimit"/> lets through.</summary>
    public sealed class ResponseTooLargeException(long maxBytes)
        : IOException($"The response is longer than {maxBytes} bytes.")
    {
        public long MaxBytes { get; } = maxBytes;
    }
}
