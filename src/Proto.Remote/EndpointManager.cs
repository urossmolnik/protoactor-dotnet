﻿// -----------------------------------------------------------------------
//   <copyright file="EndpointManager.cs" company="Asynkron HB">
//       Copyright (C) 2015-2017 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public class Endpoint
    {
        public Endpoint(PID writer, PID watcher)
        {
            Writer = writer;
            Watcher = watcher;
        }

        public PID Writer { get; }
        public PID Watcher { get; }
    }

    public class EndpointManager : IActor, ISupervisorStrategy
    {
        private readonly Behavior _behavior;
        private readonly RemoteConfig _config;
        private readonly Dictionary<string, Endpoint> _connections = new Dictionary<string, Endpoint>();

        private readonly ILogger _logger = Log.CreateLogger<EndpointManager>();

        public EndpointManager(RemoteConfig config)
        {
            _config = config;
            _behavior = new Behavior(ActiveAsync);
        }

        public Task ReceiveAsync(IContext context)
        {
            return _behavior.ReceiveAsync(context);
        }

        public Task ActiveAsync(IContext context)
        {
            switch (context.Message)
            {
                case Started _:
                    {
                        _logger.LogDebug("Started EndpointManager");
                        return Actor.Done;
                    }
                case StopEndpointManager _:
                    {
                        foreach(var endpoint in _connections.Values)
                        {
                            endpoint.Watcher.Stop();
                            endpoint.Writer.Stop();
                        }
                        _connections.Clear();
                        _behavior.Become(TerminatedAsync);
                        _logger.LogDebug("Stopped EndpointManager");
                        return Actor.Done;
                    }
                case EndpointTerminatedEvent msg:
                    {
                        var endpoint = EnsureConnected(msg.Address, context);
                        endpoint.Watcher.Tell(msg);
                        return Actor.Done;
                    }
                case EndpointConnectedEvent msg:
                    {
                        var endpoint = EnsureConnected(msg.Address, context);
                        endpoint.Watcher.Tell(msg);
                        return Actor.Done;
                    }
                case RemoteTerminate msg:
                    {
                        var endpoint = EnsureConnected(msg.Watchee.Address, context);
                        endpoint.Watcher.Tell(msg);
                        return Actor.Done;
                    }
                case RemoteWatch msg:
                    {
                        var endpoint = EnsureConnected(msg.Watchee.Address, context);
                        endpoint.Watcher.Tell(msg);
                        return Actor.Done;
                    }
                case RemoteUnwatch msg:
                    {
                        var endpoint = EnsureConnected(msg.Watchee.Address, context);
                        endpoint.Watcher.Tell(msg);
                        return Actor.Done;
                    }
                case RemoteDeliver msg:
                    {
                        var endpoint = EnsureConnected(msg.Target.Address, context);
                        endpoint.Writer.Tell(msg);
                        return Actor.Done;
                    }
                default:
                    return Actor.Done;
            }
        }

        public Task TerminatedAsync(IContext context)
        {
            return Actor.Done;
        }
        
        public void HandleFailure(ISupervisor supervisor, PID child, RestartStatistics rs, Exception cause)
        {
            supervisor.RestartChildren(cause, child);
        }

        private Endpoint EnsureConnected(string address, IContext context)
        {
            var ok = _connections.TryGetValue(address, out var endpoint);
            if (!ok)
            {
                var writer = SpawnWriter(address, context);

                var watcher = SpawnWatcher(address, context);

                endpoint = new Endpoint(writer, watcher);
                _connections.Add(address, endpoint);
            }

            return endpoint;
        }

        private static PID SpawnWatcher(string address, IContext context)
        {
            var watcherProps = Actor.FromProducer(() => new EndpointWatcher(address));
            var watcher = context.Spawn(watcherProps);
            return watcher;
        }

        private PID SpawnWriter(string address, IContext context)
        {
            var writerProps =
                Actor.FromProducer(() => new EndpointWriter(address, _config.ChannelOptions, _config.CallOptions, _config.ChannelCredentials))
                    .WithMailbox(() => new EndpointWriterMailbox(_config.EndpointWriterBatchSize));
            var writer = context.Spawn(writerProps);
            return writer;
        }
    }
}
