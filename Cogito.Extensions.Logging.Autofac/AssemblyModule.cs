﻿using System;
using System.Collections.Generic;
using System.Linq;

using Autofac;
using Autofac.Core;
using Autofac.Core.Activators.Delegate;
using Autofac.Core.Activators.Reflection;
using Autofac.Core.Lifetime;
using Autofac.Core.Registration;

using Cogito.Autofac;

using Microsoft.Extensions.Logging;

namespace Cogito.Extensions.Logging.Autofac
{

    /// <summary>
    /// Makes the Microsoft Extensions Logger available to the Autofac container. Implementation expects a registered
    /// <see cref="ILoggerFactory"/>.
    /// </summary>
    public class AssemblyModule : ModuleBase
    {

        protected override void Register(ContainerBuilder builder)
        {
            builder.RegisterFromAttributes(typeof(AssemblyModule).Assembly);
            builder.Register(ctx => ctx.Resolve<ILoggerFactory>().CreateLogger("")).SingleInstance();
            builder.RegisterSource(new LoggerRegistrationSource());
        }

        protected override void AttachToComponentRegistration(IComponentRegistryBuilder componentRegistry, IComponentRegistration registration)
        {
            // ignore components that provide loggers
            if (registration.Services.OfType<TypedService>().Any(ts => ts.ServiceType.IsAssignableTo<ILogger>()))
                return;

            if (registration.Activator is ReflectionActivator ra)
            {
                var parameters = ra.ConstructorFinder
                    .FindConstructors(ra.LimitType)
                    .SelectMany(ctor => ctor.GetParameters());

                if (parameters.Any(pi => pi.ParameterType == typeof(ILogger)))
                {
                    registration.PipelineBuilding += (sender, args) =>
                    {
                        args.Use(global::Autofac.Core.Resolving.Pipeline.PipelinePhase.ParameterSelection, (context, next) =>
                        {
                            var logger = context.Resolve<ILoggerFactory>().CreateLogger(registration.Activator.LimitType);
                            context.ChangeParameters(context.Parameters.Append(TypedParameter.From(logger)));
                            next(context);
                        });
                    };
                }
            }
        }

        class LoggerRegistrationSource : IRegistrationSource
        {

            public bool IsAdapterForIndividualComponents => false;

            public IEnumerable<IComponentRegistration> RegistrationsFor(Service service, Func<Service, IEnumerable<ServiceRegistration>> registrationAccessor)
            {
                if (service is IServiceWithType s)
                {
                    if (s.ServiceType == typeof(ILogger))
                        yield return new ComponentRegistration(
                            Guid.NewGuid(),
                            new DelegateActivator(s.ServiceType, (c, p) => c.Resolve<ILoggerFactory>().CreateLogger("")),
                            new CurrentScopeLifetime(),
                            InstanceSharing.None,
                            InstanceOwnership.ExternallyOwned,
                            new[] { service },
                            new Dictionary<string, object>());

                    if (s.ServiceType.IsGenericType &&
                        s.ServiceType.GetGenericTypeDefinition() == typeof(ILogger<>))
                        yield return new ComponentRegistration(
                            Guid.NewGuid(),
                            new DelegateActivator(s.ServiceType, (c, p) =>
                                Activator.CreateInstance(
                                    typeof(Logger<>).MakeGenericType(s.ServiceType.GetGenericArguments()[0]),
                                    c.Resolve<ILoggerFactory>())),
                            new CurrentScopeLifetime(),
                            InstanceSharing.None,
                            InstanceOwnership.ExternallyOwned,
                            new[] { service },
                            new Dictionary<string, object>());
                }
            }

        }

    }

}
