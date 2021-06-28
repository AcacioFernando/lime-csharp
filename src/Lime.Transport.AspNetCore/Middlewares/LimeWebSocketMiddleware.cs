using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lime.Protocol.Serialization;
using Lime.Transport.WebSocket;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Lime.Transport.AspNetCore
{
    internal class LimeWebSocketMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IEnvelopeSerializer _envelopeSerializer;
        private readonly TransportListener _listener;
        private readonly int[] _wsPorts;

        public LimeWebSocketMiddleware(
            RequestDelegate next,
            IEnvelopeSerializer envelopeSerializer,
            TransportListener listener,
            IOptions<LimeOptions> options)
        {
            _next = next;
            _envelopeSerializer = envelopeSerializer;
            _listener = listener;
            _wsPorts = options
                .Value
                .EndPoints.Where(e => e.Transport == TransportType.WebSocket)
                .Select(e => e.EndPoint.Port)
                .ToArray();
        }

        public async Task Invoke(HttpContext context)
        {
            if (!_wsPorts.Contains(context.Connection.LocalPort))
            {
                await _next.Invoke(context);
                return;
            }
            
            if (!context.WebSockets.IsWebSocketRequest)
            {
                // Do not continue in the ASP.NET pipeline if this is not a websocket request.
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var webSocket = await context.WebSockets.AcceptWebSocketAsync("lime");
            using var transport = new ServerWebSocketTransport(
                context,
                webSocket,
                _envelopeSerializer);

            try
            {
                await transport.OpenAsync(null, context.RequestAborted);
                await _listener.ListenAsync(transport, context.RequestAborted);
            }
            finally
            {
                if (transport.IsConnected)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await transport.CloseAsync(cts.Token);
                }
            }
        }
    }
}