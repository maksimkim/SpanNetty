using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Reflection.Metadata;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace HttpClient
{
    using System;
    using System.Text;
    using System.Threading.Tasks;
    using DotNetty.Buffers;
    using DotNetty.Codecs.Http;
    using DotNetty.Handlers.Tls;
    using DotNetty.Transport.Bootstrapping;
    using DotNetty.Transport.Channels;
    using DotNetty.Transport.Channels.Sockets;

    class Program
    {
        static async Task Main(string[] args)
        {
            IEventLoopGroup workGroup = new MultithreadEventLoopGroup(1);

            HelloClientHandler clientHandler = null;

            var cert = CreateSelfSignCert("foo");

            try
            {
                var bootstrap = new Bootstrap();
                bootstrap.Group(workGroup);
                bootstrap.Channel<TcpSocketChannel>();

                bootstrap
                    .Option(ChannelOption.SoBacklog, 8192)
                    .Handler(new ActionChannelInitializer<IChannel>(channel =>
                    {
                        IChannelPipeline pipeline = channel.Pipeline;

                        //pipeline.AddLast(TlsHandler.Client("httpbin.org"));
                        pipeline.AddLast(new TlsHandler(stream => new SslStream(
                            stream,
                            true,
                            (a,b,c,d) => true,
                            null,
                            EncryptionPolicy.RequireEncryption),
                            new ClientTlsSettings(true, new List<X509Certificate>(), "localhost")
                            {
                                OnAuthenticate = (ctx, settings, opts) =>
                                {
                                    opts.EnabledSslProtocols = SslProtocols.Tls12;
                                    opts.ClientCertificates.Add(cert);
                                }
                            }
                        ));
                        
                        pipeline.AddLast("encoder", new HttpRequestEncoder());
                        pipeline.AddLast("decoder", new HttpResponseDecoder(4096, 8192, 8192, false));
                        pipeline.AddLast("handler", clientHandler = new HelloClientHandler());
                    }));

                var cts = new CancellationTokenSource();
                
                Task.Run(async () =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        //var channel = await bootstrap.ConnectAsync("httpbin.org", 443);
                        //var channel = await bootstrap.ConnectAsync(IPAddress.Parse("3.226.68.17"), 443);
                        Console.WriteLine("Connecting...");
                        var channel = await bootstrap.ConnectAsync(IPAddress.Loopback, 9091);
                        Console.WriteLine($"Connected: {channel}");

                        var body = Encoding.UTF8.GetBytes("abcdef");

                        var headers = new DefaultHttpHeaders(true);

                        headers.Add(HttpHeaderNames.Host, "localhost:9091");
                        headers.Add(HttpHeaderNames.ContentLength, body.Length);

                        var req = new DefaultHttpRequest(HttpVersion.Http11, HttpMethod.Post, "/echo", headers);
                        await channel.WriteAndFlushAsync(req);
                        Console.WriteLine("Request headers sent");

                        for (var i = 0; i < body.Length; i++)
                        {
                            await channel.WriteAndFlushAsync(new DefaultHttpContent(Unpooled.WrappedBuffer(body, i, 1)));
                        }
                        await channel.WriteAndFlushAsync(EmptyLastHttpContent.Default);
                        Console.WriteLine("Request body sent");

                        await clientHandler.Completion;
                        await Task.Delay(500);
                    }
                });
                
                Console.ReadLine();
                cts.Cancel();
            }
            finally
            {
                workGroup.ShutdownGracefullyAsync().Wait();
            }
        }
        
        static X509Certificate2 CreateSelfSignCert(string name)
        {
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(name);

            using (var rsa = RSA.Create(2048))
            {
                var certRequest = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                // Explicitly not a CA.
                certRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));

                certRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));

                // TLS Server EKU
                certRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

                // Add the SubjectAlternativeName extension
                certRequest.CertificateExtensions.Add(sanBuilder.Build());

                var now = DateTimeOffset.UtcNow;
                var cert = certRequest.CreateSelfSigned(now, now.AddDays(365.25));
                var bytes = cert.Export(X509ContentType.Pfx, (string)null);
                cert = new X509Certificate2(bytes, (string)null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
                return cert;
            }
        }    
    }

    class HelloClientHandler : SimpleChannelInboundHandler<IHttpObject>
    {
        readonly TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
        public Task Completion => this.tcs.Task;

        protected override void ChannelRead0(IChannelHandlerContext ctx, IHttpObject msg)
        {
            if (msg is IHttpResponse req)
            {
                Console.WriteLine($"Response headers received: {msg}");  
            }
            else if (msg is IHttpContent content)
            {
                Console.WriteLine($"Response body received: {content.Content.ReadableBytes}");

                if (msg is ILastHttpContent)
                {
                    this.tcs.TrySetResult(null);
                }
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            Console.WriteLine($"Error: {exception}");
            this.tcs.TrySetException(exception);
            base.ExceptionCaught(context, exception);
        }

        public override void UserEventTriggered(IChannelHandlerContext context, object evt)
        {
            Console.WriteLine($"User Event: {evt}");
            if (evt is TlsCompletionEvent {IsSuccess: false} tlsEvt)
            {
                this.tcs.TrySetException(tlsEvt.Cause);
            }
            base.UserEventTriggered(context, evt);
        }
    }
}