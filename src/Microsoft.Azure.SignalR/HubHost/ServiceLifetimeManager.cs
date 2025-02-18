﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.SignalR
{
    internal class ServiceLifetimeManager<THub> : ServiceLifetimeManagerBase<THub> where THub : Hub
    {
        private const string MarkerNotConfiguredError =
            "'AddAzureSignalR(...)' was called without a matching call to 'IApplicationBuilder.UseAzureSignalR(...)'.";

        private readonly ILogger<ServiceLifetimeManager<THub>> _logger;
        private readonly IReadOnlyList<IHubProtocol> _allProtocols;

        private readonly IServiceConnectionManager<THub> _serviceConnectionManager;
        private readonly IClientConnectionManager _clientConnectionManager;

        public ServiceLifetimeManager(IServiceConnectionManager<THub> serviceConnectionManager,
            IClientConnectionManager clientConnectionManager, IHubProtocolResolver protocolResolver,
            ILogger<ServiceLifetimeManager<THub>> logger, AzureSignalRMarkerService marker)
            : base(serviceConnectionManager, protocolResolver)
        {
            // after core 3.0 UseAzureSignalR() is not required.
#if NETSTANDARD2_0
            if (!marker.IsConfigured)
            {
                throw new InvalidOperationException(MarkerNotConfiguredError);
            }
#endif

            _serviceConnectionManager = serviceConnectionManager;
            _clientConnectionManager = clientConnectionManager;
            _allProtocols = protocolResolver.AllProtocols;
            _logger = logger;
        }

        public override Task OnConnectedAsync(HubConnectionContext connection)
        {
            if (_clientConnectionManager.ClientConnections.TryGetValue(connection.ConnectionId, out var serviceConnectionContext))
            {
                serviceConnectionContext.HubConnectionContext = connection;
            }

            return Task.CompletedTask;
        }

        public override Task SendConnectionAsync(string connectionId, string methodName, object[] args, CancellationToken cancellationToken = default)
        {
            if (IsInvalidArgument(connectionId))
            {
                throw new ArgumentException(NullOrEmptyStringErrorMessage, nameof(connectionId));
            }

            if (IsInvalidArgument(methodName))
            {
                throw new ArgumentException(NullOrEmptyStringErrorMessage, nameof(methodName));
            }

            if (_clientConnectionManager.ClientConnections.TryGetValue(connectionId, out var serviceConnectionContext))
            {
                var message = new InvocationMessage(methodName, args);

                // Write directly to this connection
                return serviceConnectionContext.HubConnectionContext.WriteAsync(message).AsTask();
            }

            return base.SendConnectionAsync(connectionId, methodName, args, cancellationToken);

            
        }
    }
}
