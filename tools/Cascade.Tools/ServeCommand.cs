using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cascade.Tools
{
    /// <summary>
    /// <c>serve [folder]</c>: a small static web server for the WebGL build (TRD 13.12), so a phone or another PC on the LAN can open
    /// <c>http://&lt;this PC&gt;:28420/</c>. It does what a plain file server cannot: Unity's gzip build names its files
    /// <c>Cascade.wasm.gz</c>, <c>Cascade.data.gz</c> and so on, and the browser needs <c>Content-Encoding: gzip</c> plus the MIME type of the
    /// file inside (a wrong type makes <c>WebAssembly.instantiateStreaming</c> fail). Everything is sent with <c>Cache-Control: no-cache</c>
    /// (revalidate by ETag), so a rebuilt game shows up on reload.
    /// </summary>
    public static class ServeCommand
    {
        const int DefaultPort = 28420;

        public static int Run(Args args)
        {
            string root = Env.FindRepoRoot();
            string folder = args.Positional.Count > 0
                ? Path.GetFullPath(args.Positional[0], root)
                : Path.Combine(root, "Builds", "WebGL", "dev");
            if (!File.Exists(Path.Combine(folder, "index.html")))
                throw new UsageException("no WebGL build in " + folder + " (index.html is missing); build it first (.vscode/webgl.ps1, or BuildScript.BuildWebGL)");
            int port = args.Int("port", DefaultPort);
            if (port < 1 || port > 65535) throw new UsageException("--port expects 1-65535, got " + port);
            string bind = args.Get("bind") ?? "0.0.0.0";   // all interfaces: that is the point of a LAN server
            if (!IPAddress.TryParse(bind, out _) && bind != "localhost") throw new UsageException("--bind expects an IP address (0.0.0.0 = every interface, 127.0.0.1 = this PC only), got '" + bind + "'");

            var builder = WebApplication.CreateBuilder(Array.Empty<string>());
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://" + bind + ":" + port);
            var app = builder.Build();

            var files = new PhysicalFileProvider(folder);
            app.Use(async (ctx, next) =>
            {
                await next();
                Console.WriteLine("{0:HH:mm:ss} {1} {2} {3} {4}", DateTime.Now, ctx.Response.StatusCode, ctx.Request.Method, ctx.Request.Path, ctx.Connection.RemoteIpAddress);
            });
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = files,
                ContentTypeProvider = new UnityContentTypes(),
                ServeUnknownFileTypes = true,
                DefaultContentType = "application/octet-stream",
                OnPrepareResponse = ctx =>
                {
                    var headers = ctx.Context.Response.Headers;
                    string? encoding = UnityContentTypes.ContentEncodingFor(ctx.File.Name);
                    if (encoding != null) headers["Content-Encoding"] = encoding;
                    headers["Cache-Control"] = "no-cache";
                },
            });

            try { app.StartAsync().GetAwaiter().GetResult(); }
            catch (Exception e) when (e is IOException || e is InvalidOperationException || e is SocketException)
            {
                Console.Error.WriteLine("cannot listen on " + bind + ":" + port + ": " + (e.InnerException?.Message ?? e.Message));
                Console.Error.WriteLine("(is another server using the port? pass --port <n>)");
                return 1;
            }

            Console.WriteLine("Serving " + folder);
            Console.WriteLine("  this PC : http://localhost:" + port + "/");
            if (bind == "0.0.0.0")
            {
                var lan = LanAddresses().ToList();
                if (lan.Count == 0) Console.WriteLine("  LAN     : no network address found (is this PC connected?)");
                foreach (var a in lan) Console.WriteLine("  LAN     : http://" + a.Address + ":" + port + "/   (" + a.Name + (a.Likely ? "" : ", probably virtual") + ")");
                Console.WriteLine("Open a LAN address on the other device (same Wi-Fi/network). If it does not connect, allow this program through Windows Firewall");
                Console.WriteLine("(Private networks), or run once as admin:  netsh advfirewall firewall add rule name=\"Cascade WebGL\" dir=in action=allow protocol=TCP localport=" + port);
            }
            Console.WriteLine("Ctrl+C stops. Saves are per address: localhost and the LAN address are different origins.");
            app.WaitForShutdownAsync().GetAwaiter().GetResult();
            return 0;
        }

        sealed class LanAddress
        {
            public string Address = "";
            public string Name = "";
            public bool Likely;
        }

        /// <summary>IPv4 addresses of the connected network adapters, real ones (with a default gateway) first.</summary>
        static IEnumerable<LanAddress> LanAddresses()
        {
            var found = new List<LanAddress>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                var props = nic.GetIPProperties();
                bool gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = ua.Address.ToString();
                    if (ip.StartsWith("169.254.", StringComparison.Ordinal)) continue;   // no DHCP lease
                    found.Add(new LanAddress { Address = ip, Name = nic.Name, Likely = gateway });
                }
            }
            return found.OrderByDescending(a => a.Likely).ThenBy(a => a.Name, StringComparer.Ordinal);
        }
    }

    /// <summary>MIME type by the file's real extension, looking through Unity's <c>.gz</c> / <c>.br</c> suffix.</summary>
    public sealed class UnityContentTypes : IContentTypeProvider
    {
        readonly FileExtensionContentTypeProvider _inner = new FileExtensionContentTypeProvider();

        public UnityContentTypes()
        {
            _inner.Mappings[".wasm"] = "application/wasm";
            _inner.Mappings[".data"] = "application/octet-stream";
            _inner.Mappings[".symbols"] = "application/octet-stream";
            _inner.Mappings[".js"] = "application/javascript";
        }

        /// <summary>"gzip" or "br" for Unity's compressed build files, else null.</summary>
        public static string? ContentEncodingFor(string fileName)
        {
            if (fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) return "gzip";
            if (fileName.EndsWith(".br", StringComparison.OrdinalIgnoreCase)) return "br";
            return null;
        }

        public bool TryGetContentType(string subpath, out string contentType)
        {
            if (ContentEncodingFor(subpath) != null) subpath = subpath.Substring(0, subpath.LastIndexOf('.'));
            return _inner.TryGetContentType(subpath, out contentType!);
        }
    }
}
