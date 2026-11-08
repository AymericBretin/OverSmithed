namespace OverSmithed.UI
{
    // These enums are saved as numbers in prefabs and scenes: only add new values at the end,
    // with a new number, and never reorder or remove one.

    /// <summary>Named colors of the UI palette (values live in the <see cref="UiTheme"/> asset).</summary>
    public enum UiColor
    {
        White = 0,
        Wood = 1,
        WoodDark = 2,
        WoodLight = 3,
        Parchment = 4,
        ParchmentDark = 5,
        Gold = 6,
        GoldDark = 7,
        TextDark = 8,
        TextLight = 9,
        Danger = 10,
        Success = 11,
        Disabled = 12,
        Shadow = 13,
    }

    /// <summary>Text styles: font, size and font style come from the <see cref="UiTheme"/> asset.</summary>
    public enum UiTextStyle
    {
        Title = 0,
        Heading = 1,
        Body = 2,
        Small = 3,
        Button = 4,
    }

    /// <summary>Sprites of the UI kit. <see cref="None"/> means "keep the sprite set on the Image".</summary>
    public enum UiSprite
    {
        None = 0,
        PanelWood = 1,
        PanelParchment = 2,
        ButtonNormal = 3,
        ButtonPressed = 4,
        ButtonDisabled = 5,
        FocusFrame = 6,
        BarTrack = 7,
        BarFill = 8,
        SliderHandle = 9,
        Checkbox = 10,
        Checkmark = 11,
        InputField = 12,
        // 13 was "Tag" (removed: no tag above players). Don't reuse 13.
        DialogBox = 14,
        StarFull = 15,
        StarEmpty = 16,
        Coin = 17,
        Clock = 18,
        Lock = 19,
        ArrowLeft = 20,
        ArrowRight = 21,
        Close = 22,
        ArrowDown = 23,
    }
}
