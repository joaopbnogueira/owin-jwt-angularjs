﻿/*
The MIT License (MIT)

Copyright (c) 2016 Maksim Volkau

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

namespace DryIoc.SignalR
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Diagnostics.CodeAnalysis;
    using System.Reflection;
    using Microsoft.AspNet.SignalR;
    using Microsoft.AspNet.SignalR.Hubs;

    /// <summary>DryIoc extension to support SignalR.
    /// Provides DryIoc implementations of <see cref="IHubActivator"/> and <see cref="IDependencyResolver"/>.
    /// </summary>
    public static class DryIocSignalR
    {
        /// <summary>The method does: 
        ///  - registers <see cref="DryIocHubActivator"/> as <see cref="IHubActivator"/>,
        ///  - registers <see cref="DryIocDependencyResolver"/> as <see cref="IDependencyResolver"/>,
        ///  - optionally registers Hubs from given assemblies,
        ///  - sets GlobalHost.DependencyResolver to <see cref="DryIocDependencyResolver"/>.
        /// </summary>
        /// <param name="container">Container to use.</param>
        /// <param name="hubAssemblies">(optional) Assemblies with hubs.</param>
        /// <returns>Container for fluent API.</returns>
        /// <remarks>
        /// Related discussion: https://stackoverflow.com/questions/10555791/using-simple-injector-with-signalr
        /// </remarks>
        /// <example> <code lang="cs"><![CDATA[
        /// 
        ///      var hubAssemblies = new[] { Assembly.GetExecutingAssembly() };
        /// 
        ///      // - Approach 1: Full integration with HubActivator and setting-up the Resolver
        ///      container = new Container().WithSignalR(hubAssemblies);
        ///      RouteTable.Routes.MapHubs(); // should go before setting the resolver, check SO link above for reasoning
        /// 
        ///      // - Approach 2: Selective integration with DryIocHubActivator and default DependencyResolver 
        ///      container = new Container();
        ///      container.RegisterHubs(hubAssemblies);
        ///      GlobalHost.DependencyResolver.Register(typeof(IHubActivator), () => new DryIocHubActivator(container));
        ///      RouteTable.Routes.MapHubs();
        /// 
        ///   ]]></code></example>
        public static IContainer WithSignalR(this IContainer container, params Assembly[] hubAssemblies)
        {
            container.Register<IHubActivator, DryIocHubActivator>();
            container.Register<IDependencyResolver, DryIocDependencyResolver>(Reuse.Singleton);
            RegisterHubs(container, hubAssemblies);
            GlobalHost.DependencyResolver = container.Resolve<IDependencyResolver>();
            return container;
        }

        /// <summary>Helper method to register transient hubs from provided assemblies.</summary>
        /// <param name="container">Container to use for registration.</param>
        /// <param name="hubAssemblies">Array of hub types assemblies.</param>
        /// <remarks><see cref="Hub"/> implements <see cref="IDisposable"/> but supposed to be disposed
        /// outside of container by SignalR infrastructure, therefore the hubs registered with 
        /// <see cref="Setup.AllowDisposableTransient"/> and are not tracked by container. 
        /// This prevents possible memory leak when container will hold reference to disposable hub.
        /// In addition the hub will be registered once using <see cref="IfAlreadyRegistered.Keep"/> policy.</remarks>
        public static void RegisterHubs(this IContainer container, params Assembly[] hubAssemblies)
        {
            if (!hubAssemblies.IsNullOrEmpty())
                container.RegisterMany(hubAssemblies, IsHubType,
                    setup: Setup.With(allowDisposableTransient: true), 
                    ifAlreadyRegistered: IfAlreadyRegistered.Keep);
        }

        /// <summary>Helper method to register transient hubs from provided types.</summary>
        /// <param name="container">Container to use for registration.</param>
        /// <param name="hubTypes">Array of hub types.</param>
        /// <remarks><see cref="Hub"/> implements <see cref="IDisposable"/> but supposed to be disposed
        /// outside of container by SignalR infrastructure, therefore the hubs registered with 
        /// <see cref="Setup.AllowDisposableTransient"/> and are not tracked by container. 
        /// This prevents possible memory leak when container will hold reference to disposable hub.</remarks>
        public static void RegisterHubs(this IContainer container, params Type[] hubTypes)
        {
            if (!hubTypes.IsNullOrEmpty())
                container.RegisterMany(hubTypes, serviceTypeCondition: IsHubType, 
                    setup: Setup.With(allowDisposableTransient: true));
        }

        /// <summary>Helper method to found if the passed type is hub type:
        /// concrete type which implements <see cref="Hub"/> or <see cref="IHub"/>.</summary>
        /// <param name="type">Type to check.</param> <returns><c>True if hub type.</c></returns>
        public static bool IsHubType(Type type)
        {
            return !type.IsAbstract() && (
                type.BaseType == typeof(Hub) ||
                type.IsAssignableTo(typeof(IHub)));
        }
    }

    /// <summary>DryIoc implementation of <see cref="IDependencyResolver"/>.
    /// It uses <see cref="DefaultDependencyResolver"/> and combines directly registered services and
    /// default services on resolution.</summary>
    [SuppressMessage("Microsoft.Interoperability", "CA1405:ComVisibleTypeBaseTypesShouldBeComVisible",
        Justification = "Not available in PCL.")]
    public sealed class DryIocDependencyResolver : DefaultDependencyResolver
    {
        /// <summary>Created resolver given DryIoc resolver.</summary>
        public DryIocDependencyResolver(IResolver resolver)
        {
            _resolver = resolver;
        }

        /// <summary>Try to resolve service suing DryIoc container, 
        /// and if not resolved fallbacks to base <see cref="DefaultDependencyResolver"/>.</summary>
        public override object GetService(Type serviceType)
        {
            return _resolver.Resolve(serviceType, IfUnresolved.ReturnDefault)
                ?? base.GetService(serviceType);
        }

        /// <summary>Combines services from DryIoc container and base <see cref="DependencyResolverExtensions"/>
        /// and returns in a single collection.</summary>
        public override IEnumerable<object> GetServices(Type serviceType)
        {
            var services = _resolver.Resolve<object[]>(serviceType);
            var baseServices = base.GetServices(serviceType);

            return baseServices != null
                ? services.Concat(baseServices)
                : services.Length != 0 ? services
                : null;
        }

        /// <summary>Disposes DryIoc container at the end.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var disposable = _resolver as IDisposable;
                if (disposable != null)
                    disposable.Dispose();
            }
            base.Dispose(disposing);
        }

        private readonly IResolver _resolver;
    }

    /// <summary>Implements <see cref="IHubActivator"/>, 
    /// additionally before returning hub opens the scope to start Unit-of-Work,
    /// and disposes the scope together on the Hub dispose.</summary>
    public sealed class DryIocHubActivator : IHubActivator
    {
        /// <summary>Creates activator sing provided container.</summary> 
        /// <param name="container">Container to resolve hubs and open scope.</param>
        public DryIocHubActivator(IContainer container)
        {
            _container = container;
        }

        /// <summary>Creates hub by using <paramref name="descriptor"/> info.</summary>
        /// <returns>Created hub.</returns>
        public IHub Create(HubDescriptor descriptor)
        {
            return _container.Resolve<IHub>(descriptor.HubType);
        }

        private readonly IContainer _container;
    }
}
