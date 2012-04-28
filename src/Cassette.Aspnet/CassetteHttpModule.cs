﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace Cassette.Aspnet
{
    /// <summary>
    /// Serves as the entry point into Cassette for an ASP.NET web application.
    /// </summary>
    public class CassetteHttpModule : IHttpModule
    {
        static readonly object Lock = new object();
        static int _initializedModuleCount;
        static WebHost _host;
        internal static string StartUpTrace;

        /// <summary>
        /// Gets the <see cref="WebHost"/> for the web application.
        /// </summary>
        public static WebHost Host
        {
            get { return _host; }
        }

        public void Init(HttpApplication httpApplication)
        {
            lock (Lock)
            {
                var isFirstModuleInitForAppDomain = _initializedModuleCount == 0;
                _initializedModuleCount++;
                if (isFirstModuleInitForAppDomain)
                {
                    var startupTimer = Stopwatch.StartNew();
                    using (var recorder = new StartUpTraceRecorder())
                    {
                        _host = new WebHost();
                        _host.Initialize();

                        Trace.Source.TraceInformation("Total time elapsed: {0}ms", startupTimer.ElapsedMilliseconds);
                        StartUpTrace = recorder.TraceOutput;
                    }
                }
            }

            HandleHttpApplicationEvents(httpApplication);
        }

        void HandleHttpApplicationEvents(HttpApplication httpApplication)
        {
            var rewriter = _host.CreatePlaceholderRewriter();
            httpApplication.BeginRequest += (s, e) => Host.StoreRequestContainerInHttpContextItems();
            httpApplication.EndRequest += (s, e) => Host.RemoveRequestContainerFromHttpContextItems();

            if (IsRunningInCassini())
            {
                httpApplication.PostAuthorizeRequest += (s, e) => RewriteRequestsForCassini();
            }
            httpApplication.PostAuthorizeRequest += (s, e) => RewriteFileRequests();

            httpApplication.PostMapRequestHandler += (s, e) => rewriter.AddPlaceholderTrackerToHttpContextItems();
            httpApplication.PostRequestHandlerExecute += (s, e) => rewriter.RewriteOutput();
        }

        static bool IsRunningInCassini()
        {
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .Any(
                    a => a.FullName.StartsWith("WebDev.WebHost")
                );
        }

        void RewriteFileRequests()
        {
            var rewriter = Host.RequestContainer.Resolve<RawFileRequestRewriter>();
            rewriter.Rewrite();
        }

        void RewriteRequestsForCassini()
        {
            // The VS Web Development server (Cassini) does not handle Request.PathInfo correctly:
            // http://connect.microsoft.com/VisualStudio/feedback/details/221796/built-in-web-server-does-not-handle-an-ashx-handler-with-virtual-file-path
            // So we rewrite to the correct path.
            var path = HttpContext.Current.Request.AppRelativeCurrentExecutionFilePath;
            if (path.StartsWith("~/cassette.axd/", StringComparison.OrdinalIgnoreCase) && HttpContext.Current.Request.PathInfo == "")
            {
                var match = Regex.Match(path, @"^  ~/cassette\.axd  (?<pathInfo> [^?]* )  ( \? (?<queryString> .* ) )?  $", RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
                var pathInfo = match.Groups["pathInfo"].Value;
                var queryString = match.Groups["queryString"].Value;
                HttpContext.Current.RewritePath("~/cassette.axd", pathInfo, queryString);
            }
        }

        public void Dispose()
        {
            lock (Lock)
            {
                _initializedModuleCount--;
                var isFinalModule = _initializedModuleCount == 0;
                if (isFinalModule)
                {
                    _host.Dispose();
                }
            }
        }
    }
}