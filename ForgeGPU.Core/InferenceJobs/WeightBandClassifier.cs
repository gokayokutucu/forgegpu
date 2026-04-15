namespace ForgeGPU.Core.InferenceJobs;

public static class WeightBandClassifier
{
    public static WeightBand Classify(int weight)
    {
        return weight switch
        {
            >= 1 and <= 2 => WeightBand.W1_2,
            >= 3 and <= 5 => WeightBand.W3_5,
            >= 6 and <= 10 => WeightBand.W6_10,
            >= 11 and <= 20 => WeightBand.W11_20,
            >= 21 and <= 40 => WeightBand.W21_40,
            _ => WeightBand.W41Plus
        };
    }
}
