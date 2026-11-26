namespace OverSmithed.Crafting
{
    /// <summary>
    /// The kinds of crafting stations. Saved as numbers in assets: only add new values at the end,
    /// and never reorder or remove one.
    /// </summary>
    public enum StationKind
    {
        None = 0,
        Furnace = 1,
        Anvil = 2,
        Workbench = 3,
        /// <summary>Parts put together on a forge tray (CR-11). Was the assembly table: same value, assets unchanged.</summary>
        Tray = 4,
    }
}
