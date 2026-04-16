using LethalCompanyInputUtils.Api;
using UnityEngine.InputSystem;

internal sealed class UniversalVolumeControllerInputActions : LcInputActions
{
    [InputAction("<Keyboard>/f10", Name = "Toggle Menu")]
    public InputAction ToggleMenuKey { get; set; } = null!;

    [InputAction("<Keyboard>/f9", Name = "Dump Active Audio")]
    public InputAction DumpAudioKey { get; set; } = null!;

    [InputAction("<Keyboard>/f5", Name = "Audio Debug")]
    public InputAction AudioDebugKey { get; set; } = null!;

    [InputAction("<Keyboard>/escape", Name = "Close Menu")]
    public InputAction CloseMenuKey { get; set; } = null!;
}