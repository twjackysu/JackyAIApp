using System.Diagnostics.Metrics;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// Records what one <see cref="Meter"/> instance is told. Subscribes to that instance, not to the
    /// meter's name: other tests build their own meters of the same name in parallel, and a
    /// name-based listener would count theirs too.
    /// </summary>
    internal sealed class MeterCapture : IDisposable
    {
        internal sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, string?> Tags);

        internal sealed record InstrumentInfo(string Name, Type Type, string? Unit);

        private readonly MeterListener _listener = new();
        private readonly object _gate = new();
        private readonly List<Measurement> _measurements = [];
        private readonly List<InstrumentInfo> _instruments = [];

        public MeterCapture(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (!ReferenceEquals(instrument.Meter, meter)) return;
                lock (_gate) _instruments.Add(new InstrumentInfo(instrument.Name, instrument.GetType(), instrument.Unit));
                listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Add(i, v, tags));
            _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Add(i, v, tags));
            _listener.Start();
        }

        public IReadOnlyList<InstrumentInfo> Instruments
        {
            get { lock (_gate) return [.. _instruments]; }
        }

        public IReadOnlyList<Measurement> All
        {
            get { lock (_gate) return [.. _measurements]; }
        }

        public IReadOnlyList<Measurement> Of(string instrument) => [.. All.Where(m => m.Instrument == instrument)];

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var dict = new Dictionary<string, string?>();
            foreach (var tag in tags) dict[tag.Key] = tag.Value?.ToString();
            lock (_gate) _measurements.Add(new Measurement(instrument.Name, value, dict));
        }

        public void Dispose() => _listener.Dispose();
    }
}
