namespace adsb2mqtt
{
    using System;
    using System.Diagnostics;

    public class Tracing
    {
        private TraceLevel _traceLevel = TraceLevel.Warning;

        private static void InitListener()
        {
            var traceOut = new TextWriterTraceListener(System.Console.Out);
            Trace.Listeners.Add(traceOut);
        }

        public Tracing()
        {
            InitListener();
        }

        public TraceLevel TraceLevel
        {
            get => _traceLevel;
            set => _traceLevel = value;
        }

        public void Debug(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }

        public void Verbose(string message)
        {
            Trace.WriteLineIf(_traceLevel >= TraceLevel.Verbose,
                              $"VERBOSE:{message}");
        }

        public void Info(string message)
        {
            Trace.WriteLineIf(_traceLevel >= TraceLevel.Info,
                              $"INFO:{message}");
        }

        public void Warning(string message)
        {
            Trace.WriteLineIf(_traceLevel >= TraceLevel.Warning,
                              $"WARNING:{message}");
        }

        public void Error(string message)
        {
            Trace.WriteLineIf(_traceLevel >= TraceLevel.Error,
                              $"ERROR:{message}");
        }
    }
}