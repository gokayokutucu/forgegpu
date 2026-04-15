namespace ForgeGPU.Core.InferenceMachines;

public enum MachineLivenessState
{
    Live = 0,
    Offline = 1,
    Stale = 2,
    Unavailable = 3
}
