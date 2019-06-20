﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.EventHandlers.EventArgs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class RpcInitializationService : IHostedService
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly IEnvironment _environment;
        private readonly IWebHostLanguageWorkerChannelManager _languageWorkerChannelManager;
        private readonly IRpcServer _rpcServer;
        private readonly ILogger _logger;
        private readonly string _workerRuntime;

        private Dictionary<OSPlatform, List<string>> _hostingOSToWhitelistedRuntimes = new Dictionary<OSPlatform, List<string>>()
        {
            {
                OSPlatform.Windows,
                new List<string>() { LanguageWorkerConstants.JavaLanguageWorkerName }
            },
            {
                OSPlatform.Linux,
                new List<string>() { LanguageWorkerConstants.PythonLanguageWorkerName }
            }
        };

        // _webHostLevelWhitelistedRuntimes are started at webhost level when running in Azure and locally
        private List<string> _webHostLevelWhitelistedRuntimes = new List<string>()
        {
            LanguageWorkerConstants.JavaLanguageWorkerName
        };

        public RpcInitializationService(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IEnvironment environment, IRpcServer rpcServer, IWebHostLanguageWorkerChannelManager languageWorkerChannelManager, IScriptHostManager scriptHostManager, ILogger<RpcInitializationService> logger)
        {
            _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
            _logger = logger;
            _rpcServer = rpcServer;
            _environment = environment;
            _languageWorkerChannelManager = languageWorkerChannelManager ?? throw new ArgumentNullException(nameof(languageWorkerChannelManager));
            _workerRuntime = _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName);
            scriptHostManager.ScriptHostStateChanged += KillRpcServer;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Utility.CheckAppOffline(_applicationHostOptions.CurrentValue.ScriptPath))
            {
                return;
            }
            _logger.LogDebug("Starting Rpc Initialization Service.");
            await InitializeRpcServerAsync();
            await InitializeChannelsAsync();
            _logger.LogDebug("Rpc Initialization Service started.");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Shuttingdown Rpc Channels Manager");
            _languageWorkerChannelManager.ShutdownChannels();
            return Task.CompletedTask;
        }

        internal async Task InitializeRpcServerAsync()
        {
            try
            {
                _logger.LogDebug("Initializing RpcServer");
                await _rpcServer.StartAsync();
                _logger.LogDebug("RpcServer initialized");
            }
            catch (Exception grpcInitEx)
            {
                var hostInitEx = new HostInitializationException($"Failed to start Rpc Server. Check if your app is hitting connection limits.", grpcInitEx);
            }
        }

        internal Task InitializeChannelsAsync()
        {
            if (ShouldStartInPlaceholderMode())
            {
                return InitializePlaceholderChannelsAsync();
            }

            return InitializeWebHostRuntimeChannelsAsync();
        }

        private Task InitializePlaceholderChannelsAsync()
        {
            if (_environment.IsLinuxHostingEnvironment())
            {
                return InitializePlaceholderChannelsAsync(OSPlatform.Linux);
            }

            return InitializePlaceholderChannelsAsync(OSPlatform.Windows);
        }

        private Task InitializePlaceholderChannelsAsync(OSPlatform os)
        {
            return Task.WhenAll(_hostingOSToWhitelistedRuntimes[os].Select(runtime =>
                _languageWorkerChannelManager.InitializeChannelAsync(runtime)));
        }

        private Task InitializeWebHostRuntimeChannelsAsync()
        {
            if (_webHostLevelWhitelistedRuntimes.Contains(_workerRuntime))
            {
                return _languageWorkerChannelManager.InitializeChannelAsync(_workerRuntime);
            }

            return Task.CompletedTask;
        }

        private bool ShouldStartInPlaceholderMode()
        {
            return string.IsNullOrEmpty(_workerRuntime) && _environment.IsPlaceholderModeEnabled();
        }

        private void KillRpcServer(object sender, ScriptHostStateChangedEventArgs e)
        {
            if (e.OldValue.Equals(ScriptHostState.Stopping) && e.NewValue.Equals(ScriptHostState.Stopped))
            {
                _logger.LogDebug($"Killing RPC server due to ScriptHostState change from {e.OldValue.ToString()} to {e.NewValue.ToString()}");
                _rpcServer.KillAsync().Wait();
            }
        }

        // To help with unit tests
        internal void AddSupportedWebHostLevelRuntime(string language) => _webHostLevelWhitelistedRuntimes.Add(language);
    }
}
