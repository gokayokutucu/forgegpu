using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.Infrastructure.Scheduling;

public sealed class DeficitRoundRobinBandScheduler
{
    private const int MaxSelectionRounds = 32;

    private readonly WeightBand[] _bandOrder = Enum.GetValues<WeightBand>();
    private readonly Dictionary<WeightBand, Queue<BufferedBandJob>> _bandBuffers;
    private readonly Dictionary<WeightBand, int> _bandCredits;
    private int _nextBandIndex;

    public DeficitRoundRobinBandScheduler()
    {
        _bandBuffers = _bandOrder.ToDictionary(x => x, _ => new Queue<BufferedBandJob>());
        _bandCredits = _bandOrder.ToDictionary(x => x, _ => 0);
    }

    public void Enqueue(Guid jobId, WeightBand weightBand, int exactWeight)
    {
        _bandBuffers[weightBand].Enqueue(new BufferedBandJob(jobId, weightBand, exactWeight));
    }

    public bool TrySelectNext(out BufferedBandJob bufferedJob, out int creditBeforeDebit, out int creditAfterDebit)
    {
        bufferedJob = default;
        creditBeforeDebit = 0;
        creditAfterDebit = 0;

        if (_bandBuffers.Values.All(queue => queue.Count == 0))
        {
            return false;
        }

        for (var round = 0; round < MaxSelectionRounds; round++)
        {
            for (var i = 0; i < _bandOrder.Length; i++)
            {
                var band = _bandOrder[_nextBandIndex];
                _nextBandIndex = (_nextBandIndex + 1) % _bandOrder.Length;

                var queue = _bandBuffers[band];
                if (queue.Count == 0)
                {
                    continue;
                }

                _bandCredits[band] += GetQuantum(band);
                var candidate = queue.Peek();

                if (_bandCredits[band] < candidate.ExactWeight)
                {
                    continue;
                }

                queue.Dequeue();
                creditBeforeDebit = _bandCredits[band];
                _bandCredits[band] -= candidate.ExactWeight;
                creditAfterDebit = _bandCredits[band];
                bufferedJob = candidate;
                return true;
            }
        }

        return false;
    }

    public int GetPendingBufferedCount()
    {
        return _bandBuffers.Values.Sum(queue => queue.Count);
    }

    public int GetBufferedCount(WeightBand band)
    {
        return _bandBuffers[band].Count;
    }

    public FairShareSnapshot GetSnapshot()
    {
        return new FairShareSnapshot(
            _bandOrder.ToDictionary(
                band => band.ToString(),
                band => _bandBuffers[band].Count,
                StringComparer.OrdinalIgnoreCase),
            _bandOrder.ToDictionary(
                band => band.ToString(),
                band => _bandCredits[band],
                StringComparer.OrdinalIgnoreCase));
    }

    public static int GetQuantum(WeightBand band)
    {
        return band switch
        {
            WeightBand.W1_2 => 2,
            WeightBand.W3_5 => 5,
            WeightBand.W6_10 => 10,
            WeightBand.W11_20 => 20,
            WeightBand.W21_40 => 40,
            WeightBand.W41Plus => 60,
            _ => 10
        };
    }

    public readonly record struct BufferedBandJob(Guid JobId, WeightBand WeightBand, int ExactWeight);

    public readonly record struct FairShareSnapshot(
        IReadOnlyDictionary<string, int> BandBufferDepths,
        IReadOnlyDictionary<string, int> BandCredits);
}
