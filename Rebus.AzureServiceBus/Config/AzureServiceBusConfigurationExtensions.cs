﻿using System.Configuration;
using Rebus.AzureServiceBus;
using Rebus.Logging;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;
using Rebus.Subscriptions;
using Rebus.Threading;
using Rebus.Timeouts;
using Rebus.Transport;

// ReSharper disable once CheckNamespace
namespace Rebus.Config
{
    /// <summary>
    /// Configuration extensions for the Azure Service Bus transport
    /// </summary>
    public static class AzureServiceBusConfigurationExtensions
    {
        const string AsbSubStorageText = "The Azure Service Bus transport was inserted as the subscriptions storage because it has native support for pub/sub messaging";
        const string AsbTimeoutManagerText = "A disabled timeout manager was installed as part of the Azure Service Bus configuration, becuase the transport has native support for deferred messages";

        /// <summary>
        /// Configures Rebus to use Azure Service Bus to transport messages as a one-way client (i.e. will not be able to receive any messages)
        /// </summary>
        public static void UseAzureServiceBusAsOneWayClient(this StandardConfigurer<ITransport> configurer, string connectionStringNameOrConnectionString)
        {
            var connectionString = GetConnectionString(connectionStringNameOrConnectionString);

            configurer
                .OtherService<AzureServiceBusTransport>()
                .Register(c =>
                {
                    var rebusLoggerFactory = c.Get<IRebusLoggerFactory>();
                    var asyncTaskFactory = c.Get<IAsyncTaskFactory>();
                    return new AzureServiceBusTransport(connectionString, null, rebusLoggerFactory, asyncTaskFactory);
                });

            configurer
                .OtherService<ISubscriptionStorage>()
                .Register(c => c.Get<AzureServiceBusTransport>(), description: AsbSubStorageText);

            configurer.Register(c => c.Get<AzureServiceBusTransport>());

            configurer.OtherService<ITimeoutManager>().Register(c => new DisabledTimeoutManager(), description: AsbTimeoutManagerText);

            OneWayClientBackdoor.ConfigureOneWayClient(configurer);
        }

        /// <summary>
        /// Configures Rebus to use Azure Service Bus queues to transport messages, connecting to the service bus instance pointed to by the connection string
        /// (or the connection string with the specified name from the current app.config)
        /// </summary>
        public static AzureServiceBusTransportSettings UseAzureServiceBus(this StandardConfigurer<ITransport> configurer, string connectionStringNameOrConnectionString, string inputQueueAddress)
        {
            var connectionString = GetConnectionString(connectionStringNameOrConnectionString);
            var settingsBuilder = new AzureServiceBusTransportSettings();

            RegisterStandardTransport(configurer, inputQueueAddress, connectionString, settingsBuilder);

            return settingsBuilder;
        }

        static void RegisterStandardTransport(StandardConfigurer<ITransport> configurer, string inputQueueAddress, string connectionString, AzureServiceBusTransportSettings settings)
        {
            // register the actual transport as itself
            configurer
                .OtherService<AzureServiceBusTransport>()
                .Register(c =>
                {
                    var rebusLoggerFactory = c.Get<IRebusLoggerFactory>();
                    var asyncTaskFactory = c.Get<IAsyncTaskFactory>();
                    var transport = new AzureServiceBusTransport(connectionString, inputQueueAddress, rebusLoggerFactory, asyncTaskFactory);

                    if (settings.PrefetchingEnabled)
                    {
                        transport.PrefetchMessages(settings.NumberOfMessagesToPrefetch);
                    }

                    transport.AutomaticallyRenewPeekLock = settings.AutomaticPeekLockRenewalEnabled;

                    transport.PartitioningEnabled = settings.PartitioningEnabled;
                    transport.DoNotCreateQueuesEnabled = settings.DoNotCreateQueuesEnabled;
                    return transport;
                });

            // map subscription storage to transport
            configurer
                .OtherService<ISubscriptionStorage>()
                .Register(c => c.Get<AzureServiceBusTransport>(), description: AsbSubStorageText);

            // map ITransport to transport
            configurer.Register(c => c.Get<AzureServiceBusTransport>());

            // remove deferred messages step
            configurer.OtherService<IPipeline>().Decorate(c =>
            {
                var pipeline = c.Get<IPipeline>();

                return new PipelineStepRemover(pipeline)
                    .RemoveIncomingStep(s => s.GetType() == typeof(HandleDeferredMessagesStep));
            });

            // disable timeout manager
            configurer.OtherService<ITimeoutManager>()
                .Register(c => new DisabledTimeoutManager(), description: AsbTimeoutManagerText);
        }

        static string GetConnectionString(string connectionStringNameOrConnectionString)
        {
            var connectionStringSettings = ConfigurationManager.ConnectionStrings[connectionStringNameOrConnectionString];

            return connectionStringSettings?.ConnectionString ?? connectionStringNameOrConnectionString;
        }
    }
}