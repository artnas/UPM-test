using System.Collections.Generic;
using System.Diagnostics;

namespace MeshSplit.Scripts.Utilities
{
    public static class PerformanceMonitor
    {
        private class Entry
        {
            public Stopwatch Stopwatch;
            public float TimeThreshold;
        }
        
        private static readonly Dictionary<string, Entry> _stopwatches = new Dictionary<string, Entry>();

        public static void Start(string textIdentifier, float timeThreshold = 0f)
        {
            if (_stopwatches.ContainsKey(textIdentifier))
            {
                _stopwatches[textIdentifier].Stopwatch.Restart();
            }
            else
            {
                var stopWatch = new Stopwatch();
                
                _stopwatches.Add(textIdentifier, new Entry
                {
                    Stopwatch = stopWatch,
                    TimeThreshold = timeThreshold
                });
                
                stopWatch.Start();
            }
        }
        
        public static void Stop(string textIdentifier, string additionalText = null)
        {
            if (_stopwatches.TryGetValue(textIdentifier, out var entry))
            {
                var stopwatch = entry.Stopwatch;
                stopwatch.Stop();
            
                var milliseconds = stopwatch.Elapsed.TotalMilliseconds;

                if (entry.TimeThreshold == 0 || milliseconds >= (entry.TimeThreshold * 1000))
                {
                    UnityEngine.Debug.Log($"{textIdentifier} {additionalText}\n\ttime: \t{milliseconds:n2} ms");
                }

                _stopwatches.Remove(textIdentifier);
            }
        }
        
        public static void Stop(string textIdentifier, out double milliseconds)
        {
            if (_stopwatches.TryGetValue(textIdentifier, out var entry))
            {
                var stopwatch = entry.Stopwatch;
                stopwatch.Stop();
            
                milliseconds = stopwatch.Elapsed.TotalMilliseconds;

                _stopwatches.Remove(textIdentifier);
            }
            else
            {
                milliseconds = 0;
            }
        }
    }
}
