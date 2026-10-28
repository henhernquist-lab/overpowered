/// Read-only bridge from a power effect to the run's mechanical stacks. Returns 0 (and therefore exact legacy behaviour)
/// in every mode without a run, and in a run that has not picked that card.
public static class RunMechanics
{
    public static int Stacks(RunMechanic mechanic)
    {
        var world = WorldSession.Instance;
        var run = world != null ? world.Run : null;
        return run != null ? run.Mods.Mechanic(mechanic) : 0;
    }

    public static bool Has(RunMechanic mechanic) => Stacks(mechanic) > 0;
}
