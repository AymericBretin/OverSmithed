namespace OverSmithed.UI
{
    /// <summary>A UI component that takes its look from a <see cref="UiTheme"/>.</summary>
    public interface IThemed
    {
        UiTheme Theme { get; }

        /// <summary>Copies the theme values (font, color, sprite...) onto the component.</summary>
        void ApplyTheme();
    }
}
