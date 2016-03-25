﻿using DryIoc;
using DryIoc.SignalR;
using DryIoc.WebApi;

using Owin;

using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Microsoft.Owin;
using Microsoft.Owin.Cors;
using Microsoft.Owin.Diagnostics;
using Microsoft.Owin.StaticFiles;

using Playground.Owin.Jwt.Infrastructure;
using Playground.Owin.Jwt.Infrastructure.QueryStrings;
using Playground.Owin.Jwt.Models.Abstractions;
using Playground.Owin.Jwt.Models.Implementations;

using System;
using System.Linq;
using System.Reflection;
using System.Web.Http;
using System.Web.Http.Cors;
using AppFunc = System.Func<
    System.Collections.Generic.IDictionary<string, object>,
    System.Threading.Tasks.Task
>;
using System.Collections.Generic;
using System.Threading.Tasks;

[assembly: OwinStartup(typeof(Playground.Owin.Jwt.Startup))]

namespace Playground.Owin.Jwt
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            var webApiConfig = new HttpConfiguration();
            var hubConfig = new HubConfiguration
            {
                EnableJSONP = true,
                EnableJavaScriptProxies = true
            };

            webApiConfig.MapHttpAttributeRoutes();
            webApiConfig.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
            webApiConfig.EnableCors(
                new EnableCorsAttribute("*", "*", "GET, POST, OPTIONS, PUT, DELETE, PATCH")
            );

            var options = new FileServerOptions
            {
                EnableDefaultFiles = true,
                FileSystem = new WebPhysicalFileSystem(".\\wwwroot")
            };

            var container = new Container()
                .WithWebApi(webApiConfig)
                .WithSignalR(Assembly.GetExecutingAssembly());

            container.Register<ITest, Test>();

            app.Use(new Func<AppFunc, AppFunc>(next => env => Invoke(next, env)))
                .UseErrorPage(ErrorPageOptions.ShowAll)
                .UseCors(CorsOptions.AllowAll)
                .Use(typeof(OwinMiddleWareQueryStringExtractor))
                .UseOAuthAuthorizationServer(new OAuthOptions())
                .UseJwtBearerAuthentication(new JwtOptions())
                .UseAngularServer("/", "/index.html")
                .UseFileServer(options)
                .UseWebApi(webApiConfig)
                .MapSignalR(hubConfig);
        }

        private static void RegisterHubActivatorAndHubs(IContainer container)
        {
            GlobalHost.DependencyResolver.Register(
                typeof (IHubActivator),
                () => new DryIocHubActivator(container));

            container.RegisterHubs(Assembly.GetExecutingAssembly());
        }

        private async Task Invoke(AppFunc next, IDictionary<string, object> env)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            await Console.Out.WriteLineAsync($"[BEGIN] :: #{env["owin.RequestPath"]}");
            
            await next.Invoke(env);

            Console.ForegroundColor = ConsoleColor.Green;
            await Console.Out.WriteLineAsync($"[-END-] :: #{env["owin.RequestPath"]}");
        }
    }   
}
