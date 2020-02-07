// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;

namespace Microsoft.Azure.WebJobs.Script
{
    public class SystemEnvironment : IEnvironment
    {
        private static readonly Lazy<SystemEnvironment> _instance = new Lazy<SystemEnvironment>(CreateInstance);
        private static ConcurrentDictionary<string, string> _cache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private SystemEnvironment()
        {
        }

        public static SystemEnvironment Instance => _instance.Value;

        private static SystemEnvironment CreateInstance()
        {
            return new SystemEnvironment();
        }

        public string GetEnvironmentVariable(string name)
        {
            return _cache.GetOrAdd(name, (k) =>
            {
                return Environment.GetEnvironmentVariable(k);
            });
        }

        public void SetEnvironmentVariable(string name, string value)
        {
            _cache.AddOrUpdate(name, value, (k, v) =>
            {
                Environment.SetEnvironmentVariable(k, v);
                return v;
            });
        }

        public static void Reset()
        {
            _cache.Clear();
        }
    }
}
