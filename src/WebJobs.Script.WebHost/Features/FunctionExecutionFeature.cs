﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Features
{
    internal class FunctionExecutionFeature : IFunctionExecutionFeature
    {
        private readonly IScriptJobHost _host;
        private readonly FunctionDescriptor _descriptor;
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;

        public FunctionExecutionFeature(IScriptJobHost host, FunctionDescriptor descriptor, IEnvironment environment, ILoggerFactory loggerFactory)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _descriptor = descriptor;
            _environment = environment;
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostMetrics);
        }

        public bool CanExecute => _descriptor != null;

        public FunctionDescriptor Descriptor => _descriptor;

        public async Task ExecuteAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            if (!CanExecute)
            {
                throw new InvalidOperationException("Unable to execute function without a target.");
            }

            JObject coldStartData = null;
            if (request.IsColdStart())
            {
                coldStartData = new JObject
                {
                    { "requestId", request.GetRequestId() },
                    { "language", Descriptor.Metadata.Language },
                    { "sku", _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku) }
                };

                var dispatchStopwatch = request.GetItemOrDefault<Stopwatch>(ScriptConstants.AzureFunctionsColdStartKey);
                if (dispatchStopwatch != null)
                {
                    dispatchStopwatch.Stop();
                    coldStartData.Add("dispatchDuration", dispatchStopwatch.ElapsedMilliseconds);
                }
            }

            var sw = Stopwatch.StartNew();
            var arguments = new Dictionary<string, object>()
            {
                { _descriptor.TriggerParameter.Name, request }
            };
            await _host.CallAsync(_descriptor.Name, arguments, cancellationToken);
            sw.Stop();

            if (coldStartData != null)
            {
                coldStartData.Add("functionDuration", sw.ElapsedMilliseconds);

                var logData = new Dictionary<string, object>
                {
                    [ScriptConstants.LogPropertyEventNameKey] = ScriptConstants.ColdStartEventName,
                    [ScriptConstants.LogPropertyActivityIdKey] = request.GetRequestId()
                };
                _logger.Log(LogLevel.Information, 0, logData, null, (s, e) => coldStartData.ToString(Formatting.None));
            }
        }
    }
}
