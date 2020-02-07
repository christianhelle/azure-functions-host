// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.Workers.ProcessManagement
{
    public class ProcessMonitor : IDisposable
    {
        private const int SampleHistorySize = 10;

        private readonly List<double> _cpuLoadHistory = new List<double>();
        private readonly int _effectiveCores;

        private Timer _timer;
        private Process _process;
        private TimeSpan? _lastProcessorTime;
        private DateTime _lastSampleTime;
        private bool _disposed = false;
        private TimeSpan _interval = TimeSpan.FromSeconds(1);
        private object _syncLock = new object();

        public ProcessMonitor(int processId, IEnvironment environment)
        {
            ProcessId = processId;
            _effectiveCores = environment.GetEffectiveCoresCount();
        }

        public int ProcessId { get; }

        public void Start()
        {
            _process = Process.GetProcessById(ProcessId);

            _timer = new Timer(OnTimer, null, TimeSpan.Zero, _interval);
        }

        public ProcessStats GetStats()
        {
            ProcessStats stats = null;
            lock (_syncLock)
            {
                stats = new ProcessStats
                {
                    CpuLoadHistory = _cpuLoadHistory.ToArray()
                };
            }
            return stats;
        }

        private void OnTimer(object state)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var currSampleTime = DateTime.UtcNow;

                SampleCPULoad(currSampleTime);

                _lastSampleTime = currSampleTime;
            }
            catch
            {
                // don't allow background exceptions to escape
            }
        }

        private void SampleCPULoad(DateTime currSampleTime)
        {
            var currProcessorTime = _process.TotalProcessorTime;

            if (_lastProcessorTime != null)
            {
                var currSampleDuration = currSampleTime - _lastSampleTime;
                var currSampleProcessorTime = (currProcessorTime - _lastProcessorTime.Value).TotalMilliseconds;
                var totalSampleProcessorTime = _effectiveCores * currSampleDuration.TotalMilliseconds;

                double cpuLoad = currSampleProcessorTime / totalSampleProcessorTime;
                cpuLoad = Math.Round(cpuLoad * 100);

                lock (_syncLock)
                {
                    if (_cpuLoadHistory.Count == SampleHistorySize)
                    {
                        _cpuLoadHistory.RemoveAt(0);
                    }
                    _cpuLoadHistory.Add(cpuLoad);
                }
            }

            _lastProcessorTime = currProcessorTime;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _timer?.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
